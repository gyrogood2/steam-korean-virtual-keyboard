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
        public string Url { get; set; }
        public string WebSocketUrl { get; set; }
    }

    /// <summary>
    /// Minimal CDP client: lists Steam's CEF targets and evaluates JavaScript
    /// in a target through its WebSocket endpoint.
    /// </summary>
    internal sealed class CdpClient
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();
        private static string _lastEndpoint;

        /// <summary>The address that last answered with a real CDP target list.</summary>
        public static string LastEndpoint
        {
            get { return _lastEndpoint; }
        }

        public IList<CdpTarget> GetTargets()
        {
            Exception last = null;
            var failures = new StringBuilder();

            foreach (string url in Config.CdpTargetListUrls)
            {
                try
                {
                    IList<CdpTarget> targets = GetTargets(url);
                    if (!string.Equals(_lastEndpoint, url, StringComparison.Ordinal))
                    {
                        _lastEndpoint = url;
                        Log.Info("cdp " + url);
                    }

                    return targets;
                }
                catch (Exception exception)
                {
                    last = exception;
                    if (failures.Length > 0)
                    {
                        failures.Append("; ");
                    }

                    failures.Append(url).Append(" -> ").Append(exception.Message);
                }
            }

            throw new InvalidOperationException(
                "Steam debug port is not reachable. " + failures,
                last);
        }

        /// <summary>
        /// Checks whether Steam's CDP answers on IPv4 or IPv6. A plain HTTP 200
        /// is not enough: port 8080 may belong to an unrelated local service,
        /// so the body has to look like a CDP version document.
        /// </summary>
        public static bool IsReachable()
        {
            var client = new CdpClient();
            try
            {
                client.GetTargets();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static IList<CdpTarget> GetTargets(string url)
        {
            var targets = new List<CdpTarget>();

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Timeout = Config.CdpListTimeoutMs;
            request.Proxy = null;

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                string body = reader.ReadToEnd();
                object[] items;

                try
                {
                    items = Serializer.DeserializeObject(body) as object[];
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        "Port 8080 answered, but not with CDP JSON.",
                        exception);
                }

                // Another program may own port 8080. Anything that is not a
                // CDP target list must fail so the next address is tried.
                if (items == null || !LooksLikeCdpList(items))
                {
                    throw new InvalidOperationException(
                        "Port 8080 answered, but not with a CDP target list.");
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
                        Url = GetString(fields, "url"),
                        WebSocketUrl = GetString(fields, "webSocketDebuggerUrl")
                    });
                }
            }

            // Chrome, Edge, Electron and other CEF apps also answer with a
            // valid CDP list. Without this check the app would attach to that
            // instance instead of Steam (port 8080 is a common choice).
            if (!LooksLikeSteam(targets))
            {
                throw new InvalidOperationException(
                    "Port 8080 answered with CDP, but not Steam's target list.");
            }

            return targets;
        }

        private static bool LooksLikeSteam(IList<CdpTarget> targets)
        {
            foreach (CdpTarget target in targets)
            {
                if (string.Equals(target.Title, "SharedJSContext", StringComparison.Ordinal))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(target.Url)
                    && target.Url.IndexOf("steamloopback.host", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(target.Title)
                    && target.Title.IndexOf("\uAC00\uC0C1", StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LooksLikeCdpList(object[] items)
        {
            // An empty array is not proof of Steam: another service can answer
            // "[]" on port 8080. Steam always exposes at least one target
            // (SharedJSContext), so retry the next address and probe again.
            if (items.Length == 0)
            {
                return false;
            }

            foreach (var item in items)
            {
                var fields = item as Dictionary<string, object>;
                if (fields != null && (fields.ContainsKey("type") || fields.ContainsKey("webSocketDebuggerUrl")))
                {
                    return true;
                }
            }

            return false;
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
