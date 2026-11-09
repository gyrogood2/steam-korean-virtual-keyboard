using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SteamOskKorean
{
    /// <summary>
    /// Local HTTP control endpoint used by the injected OSK page.
    /// Routes: /state, /hangul, /flip, /key.
    /// </summary>
    internal sealed class ControlServer : IDisposable
    {
        private readonly Engine _engine;
        private readonly string _token;
        private TcpListener _listener;
        private volatile bool _running;

        public ControlServer(Engine engine, string token)
        {
            _engine = engine;
            _token = token;
        }

        public void Start()
        {
            _listener = new TcpListener(IPAddress.Loopback, Config.ControlPort);
            _listener.Start();

            _running = true;
            var thread = new Thread(AcceptLoop);
            thread.IsBackground = true;
            thread.Start();

            Log.Info("control server on " + Config.ControlPort);
        }

        public void Dispose()
        {
            _running = false;

            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                }
                catch (Exception)
                {
                }
            }
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(HandleClient, client);
                }
                catch (Exception)
                {
                    // Listener stopped.
                }
            }
        }

        private void HandleClient(object state)
        {
            var client = state as TcpClient;
            if (client == null)
            {
                return;
            }

            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    bool tooLarge;
                    string path = ReadRequestPath(stream, out tooLarge);
                    if (tooLarge)
                    {
                        WriteStatus(stream, "413 Payload Too Large");
                        return;
                    }

                    if (path == null)
                    {
                        return;
                    }

                    if (!IsAuthorized(path))
                    {
                        WriteStatus(stream, "403 Forbidden");
                        return;
                    }

                    string route = RouteOf(path);
                    if (!IsKnownRoute(route))
                    {
                        WriteStatus(stream, "404 Not Found");
                        return;
                    }

                    WriteJson(stream, Dispatch(route, path));
                }
            }
            catch (Exception exception)
            {
                Log.Error("control client", exception);
            }
        }

        /// <summary>Route part of the request target, without the query string.</summary>
        private static string RouteOf(string path)
        {
            int query = path.IndexOf('?');
            return query < 0 ? path : path.Substring(0, query);
        }

        private static bool IsKnownRoute(string route)
        {
            return route == "/state"
                || route == "/key"
                || route == "/hangul"
                || route == "/flip";
        }

        private bool Dispatch(string route, string path)
        {
            if (route == "/key")
            {
                char letter = GetQueryChar(path, "c");
                if (letter != '\0')
                {
                    _engine.SendLetter(letter);
                }

                return _engine.IsKorean;
            }

            if (route == "/hangul")
            {
                return _engine.Toggle(true);
            }

            if (route == "/flip")
            {
                return _engine.Toggle(false);
            }

            return _engine.IsKorean;
        }

        private static string ReadRequestPath(NetworkStream stream, out bool tooLarge)
        {
            tooLarge = false;
            var buffer = new List<byte>();
            var chunk = new byte[4096];
            int headerEnd = -1;

            while (headerEnd < 0)
            {
                int read = stream.Read(chunk, 0, chunk.Length);
                if (read <= 0)
                {
                    return null;
                }

                for (int i = 0; i < read; i++)
                {
                    buffer.Add(chunk[i]);
                }

                if (buffer.Count > Config.MaxRequestHeaderBytes)
                {
                    tooLarge = true;
                    return null;
                }

                headerEnd = IndexOf(buffer, new byte[] { 13, 10, 13, 10 });
            }

            string header = Encoding.ASCII.GetString(buffer.ToArray(), 0, headerEnd);
            string[] lines = header.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] parts = lines.Length > 0 ? lines[0].Split(' ') : new string[0];

            return parts.Length > 1 ? parts[1] : null;
        }

        private static void WriteJson(NetworkStream stream, bool korean)
        {
            byte[] body = Encoding.UTF8.GetBytes(
                "{\"korean\":" + (korean ? "true" : "false") + "}");

            string header = "HTTP/1.1 200 OK\r\n"
                          + "Access-Control-Allow-Origin: *\r\n"
                          + "Content-Type: application/json\r\n"
                          + "Content-Length: " + body.Length + "\r\n"
                          + "Connection: close\r\n\r\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(body, 0, body.Length);
        }

        private bool IsAuthorized(string path)
        {
            string value = GetQueryValue(path, "t");
            return value != null && string.Equals(value, _token, StringComparison.Ordinal);
        }

        private static void WriteStatus(NetworkStream stream, string status)
        {
            byte[] header = Encoding.ASCII.GetBytes(
                "HTTP/1.1 " + status + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            stream.Write(header, 0, header.Length);
        }

        private static string GetQueryValue(string path, string name)
        {
            int queryStart = path.IndexOf('?');
            if (queryStart < 0)
            {
                return null;
            }

            string query = path.Substring(queryStart + 1);
            foreach (string pair in query.Split('&'))
            {
                string[] parts = pair.Split('=');
                if (parts.Length == 2 && parts[0] == name)
                {
                    return Uri.UnescapeDataString(parts[1]);
                }
            }

            return null;
        }

        private static char GetQueryChar(string path, string name)
        {
            int queryStart = path.IndexOf('?');
            if (queryStart < 0)
            {
                return '\0';
            }

            string query = path.Substring(queryStart + 1);
            foreach (string pair in query.Split('&'))
            {
                string[] parts = pair.Split('=');
                if (parts.Length == 2 && parts[0] == name)
                {
                    string value = Uri.UnescapeDataString(parts[1]);
                    return value.Length > 0 ? value[0] : '\0';
                }
            }

            return '\0';
        }

        private static int IndexOf(List<byte> buffer, byte[] pattern)
        {
            for (int i = 0; i + pattern.Length <= buffer.Count; i++)
            {
                bool match = true;

                for (int j = 0; j < pattern.Length; j++)
                {
                    if (buffer[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
