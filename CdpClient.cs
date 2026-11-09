using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace SteamOskKorean
{
    /// <summary>A Chrome DevTools Protocol page target exposed by Steam's CEF.</summary>
    internal sealed class CdpTarget
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Type { get; set; }
        public string WebSocketUrl { get; set; }
    }

    /// <summary>
    /// Minimal CDP client: lists Steam's CEF targets and evaluates JavaScript
    /// in a target through its WebSocket endpoint.
    /// </summary>
    internal sealed class CdpClient
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public IList<CdpTarget> GetTargets()
        {
            var targets = new List<CdpTarget>();

            var request = (HttpWebRequest)WebRequest.Create(Config.CdpTargetListUrl);
            request.Timeout = Config.CdpConnectTimeoutMs;
            request.Proxy = null;

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                var items = Serializer.DeserializeObject(reader.ReadToEnd()) as object[];
                if (items == null)
                {
                    return targets;
                }

                foreach (var item in items)
                {
                    var fields = item as Dictionary<string, object>;
                    if (fields == null)
                    {
                        continue;
                    }

                    targets.Add(new CdpTarget
                    {
                        Id = GetString(fields, "id"),
                        Title = GetString(fields, "title"),
                        Type = GetString(fields, "type"),
                        WebSocketUrl = GetString(fields, "webSocketDebuggerUrl")
                    });
                }
            }

            return targets;
        }

        /// <summary>Evaluates an expression and returns its string value.</summary>
        public string Evaluate(CdpTarget target, string expression)
        {
            if (string.IsNullOrEmpty(target.WebSocketUrl))
            {
                return null;
            }

            using (var socket = new ClientWebSocket())
            {
                var connectToken = new CancellationTokenSource(Config.CdpConnectTimeoutMs);
                socket.ConnectAsync(new Uri(target.WebSocketUrl), connectToken.Token)
                      .Wait(Config.CdpConnectTimeoutMs);

                Send(socket, expression);
                string response = Receive(socket);
                TryClose(socket);

                return ExtractValue(response);
            }
        }

        private static void Send(ClientWebSocket socket, string expression)
        {
            var parameters = new Dictionary<string, object>
            {
                { "expression", expression },
                { "returnByValue", true },
                { "awaitPromise", true }
            };

            var message = new Dictionary<string, object>
            {
                { "id", 1 },
                { "method", "Runtime.evaluate" },
                { "params", parameters }
            };

            byte[] payload = Encoding.UTF8.GetBytes(Serializer.Serialize(message));
            socket.SendAsync(
                new ArraySegment<byte>(payload),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None)
                .Wait(Config.CdpSendTimeoutMs);
        }

        private static string Receive(ClientWebSocket socket)
        {
            var buffer = new byte[131072];
            var builder = new StringBuilder();

            while (true)
            {
                var receive = socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (!receive.Wait(Config.CdpReceiveTimeoutMs))
                {
                    break;
                }

                builder.Append(Encoding.UTF8.GetString(buffer, 0, receive.Result.Count));
                if (receive.Result.EndOfMessage)
                {
                    break;
                }
            }

            return builder.ToString();
        }

        private static void TryClose(ClientWebSocket socket)
        {
            try
            {
                socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None)
                      .Wait(400);
            }
            catch (Exception)
            {
                // The connection may already be gone; nothing to do.
            }
        }

        private static string ExtractValue(string response)
        {
            var message = Serializer.DeserializeObject(response) as Dictionary<string, object>;
            if (message == null)
            {
                return null;
            }

            var result = GetMap(message, "result");
            if (result == null)
            {
                return null;
            }

            var value = GetMap(result, "result");
            if (value == null)
            {
                return null;
            }

            object raw;
            return value.TryGetValue("value", out raw) ? Convert.ToString(raw) : null;
        }

        private static Dictionary<string, object> GetMap(Dictionary<string, object> source, string key)
        {
            object value;
            return source.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static string GetString(Dictionary<string, object> source, string key)
        {
            object value;
            return source.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value)
                : string.Empty;
        }
    }
}
