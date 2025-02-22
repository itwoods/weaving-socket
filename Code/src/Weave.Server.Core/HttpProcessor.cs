﻿using System;
using System.Collections;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Weave.Server
{
    /// <summary>
    /// 连接到HTTP服务器的客户端封装类，，，用于处理数据，，
    /// </summary>
    public class HttpProcessor
    {
        public TcpClient socket;
        public HttpServer srv;
        private Stream inputStream;
        public StreamWriter outputStream;
        public string http_method;
        public string http_url;
        public string http_protocol_versionstring;
        public Hashtable httpHeaders = new Hashtable();
        public string retrunData = "";
        public DateTime updatetime;
        private static readonly int MAX_POST_SIZE = 10 * 1024 * 1024; // 10MB

        public HttpProcessor(TcpClient s, HttpServer srv)
        {
            this.socket = s;
            this.srv = srv;
        }

        private string StreamReadLine(Stream inputStream)
        {
            int next_char;
            string data = "";
            while (true)
            {
                next_char = inputStream.ReadByte();
                if (next_char == '\n') { break; }
                if (next_char == '\r') { continue; }
                if (next_char == -1) { Thread.Sleep(1); continue; };
                data += Convert.ToChar(next_char);
            }
            return data;
        }

        public void Process(object p)
        {
            // we can't use a StreamReader for input, because it buffers up extra data on us inside it's
            // "processed" view of the world, and we want the data raw after the headers
            inputStream = new BufferedStream(socket.GetStream());
            // we probably shouldn't be using a streamwriter for all output from handlers either
            outputStream = new StreamWriter(new BufferedStream(socket.GetStream()));
            try
            {
                ParseRequest();
                ReadHeaders();
                if (http_method.Equals("GET"))
                {
                    HandleGETRequest();
                }
                else if (http_method.Equals("POST"))
                {
                    HandlePOSTRequest();
                }
            }
            catch
            {
                WriteFailure();
            }
            try
            {
                outputStream.Flush();
                inputStream = null; outputStream = null; // bs = null;            
                socket.Close();
            }
            catch { }
        }

        public void ParseRequest()
        {
            string request = StreamReadLine(inputStream);
            string[] tokens = request.Split(' ');
            if (tokens.Length != 3)
            {
                // throw new Exception("invalid http request line");
                return;
            }
            http_method = tokens[0].ToUpper();
            http_url = tokens[1];
            http_protocol_versionstring = tokens[2];
            //Console.WriteLine("starting: " + request);
        }

        public void ReadHeaders()
        {
            string line;
            while ((line = StreamReadLine(inputStream)) != null)
            {
                if (line.Equals(""))
                {
                    return;
                }
                int separator = line.IndexOf(':');
                if (separator == -1)
                {
                    throw new Exception("invalid http header line: " + line);
                }
                string name = line.Substring(0, separator);
                int pos = separator + 1;
                while ((pos < line.Length) && (line[pos] == ' '))
                {
                    pos++; // strip any spaces
                }
                string value = line.Substring(pos, line.Length - pos);
                httpHeaders[name] = value;
            }
            httpHeaders.Add("Access-Control-Allow-Origin", "*");
        }

        public void HandleGETRequest()
        {
            srv.HandleGETRequest(this);
        }

        private const int BUF_SIZE = 4096;
        public void HandlePOSTRequest()
        {
            // this post data processing just reads everything into a memory stream.
            // this is fine for smallish things, but for large stuff we should really
            // hand an input stream to the request processor. However, the input stream 
            // we hand him needs to let him see the "end of the stream" at this content 
            // length, because otherwise he won't know when he's seen it all! 
            ////Console.WriteLine("get post data start");
            int content_len = 0;
            MemoryStream ms = new MemoryStream();
            if (this.httpHeaders.ContainsKey("Content-Length"))
            {
                content_len = Convert.ToInt32(this.httpHeaders["Content-Length"]);
                if (content_len > MAX_POST_SIZE)
                {
                    throw new Exception(
                        string.Format("POST Content-Length({0}) too big for this simple server",
                          content_len));
                }
                byte[] buf = new byte[BUF_SIZE];
                int to_read = content_len;
                while (to_read > 0)
                {
                    int numread = this.inputStream.Read(buf, 0, Math.Min(BUF_SIZE, to_read));
                    if (numread == 0)
                    {
                        if (to_read == 0)
                        {
                            break;
                        }
                        else
                        {
                            throw new Exception("client disconnected during post");
                        }
                    }
                    to_read -= numread;
                    ms.Write(buf, 0, numread);
                }
                ms.Seek(0, SeekOrigin.Begin);
            }
            srv.HandlePOSTRequest(this, new StreamReader(ms));
        }

        public void WriteSuccess()
        {
            outputStream.WriteLine("HTTP/1.0 200 OK");
            outputStream.WriteLine("Content-Type: text/html");
            outputStream.WriteLine("Access-Control-Allow-Origin:*");
            outputStream.WriteLine("Connection: close");
            outputStream.WriteLine("");
        }

        public void WriteFailure()
        {
            outputStream.WriteLine("HTTP/1.0 404 File not found");
            outputStream.WriteLine("Connection: close");
            outputStream.WriteLine("");
        }
    }
}
