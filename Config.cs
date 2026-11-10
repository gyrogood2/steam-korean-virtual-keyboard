using System;
using System.Collections.Generic;
using System.IO;

namespace SteamOskKorean
{
    /// <summary>Central configuration values for the application.</summary>
    internal static class Config
    {
        public const string Version = "1.0.7";

        public const int ControlPort = 8793;
        public const int CdpPort = 8080;

        /// <summary>Upper bound for an HTTP request header; anything larger is refused.</summary>
        public const int MaxRequestHeaderBytes = 8192;

        public const int InjectIntervalMs = 1000;
        public const int KeyPollIntervalMs = 15;
        public const int KeySuppressMs = 150;

        public const int CdpConnectTimeoutMs = 3000;
        public const int CdpListTimeoutMs = 1500;
        public const int CdpSendTimeoutMs = 4000;
        public const int CdpReceiveTimeoutMs = 5000;

        /// <summary>First characters of the Steam virtual keyboard window title ("가상").</summary>
        public const string VirtualKeyboardTitleMarker = "\uAC00\uC0C1";

        public const string PayloadFileName = "payload.js";
        public const string LogFileName = "SteamKoreanKeyboard.log";
        public const string DataFolderName = "SteamKoreanKeyboard";

        /// <summary>Replaced with a per-run random token before injection.</summary>
        public const string TokenPlaceholder = "__SIX_TOKEN__";

        public static readonly string BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;

        private static readonly object LogSync = new object();
        private static string _logPath;

        public static string PayloadPath
        {
            get { return Path.Combine(BaseDirectory, PayloadFileName); }
        }

        /// <summary>
        /// Log file in a per-user writable folder. The app may live under
        /// Program Files, where writing next to the exe fails silently.
        /// </summary>
        public static string LogPath
        {
            get
            {
                lock (LogSync)
                {
                    if (_logPath == null)
                    {
                        _logPath = Path.Combine(ResolveDataDirectory(), LogFileName);
                    }

                    return _logPath;
                }
            }
        }

        /// <summary>Old location, only used to open a log written by 1.0.6 or older.</summary>
        public static string LegacyLogPath
        {
            get { return Path.Combine(BaseDirectory, LogFileName); }
        }

        /// <summary>Folder offered by the tray "로그 열기" menu.</summary>
        public static string LogDirectory
        {
            get { return Path.GetDirectoryName(LogPath); }
        }

        private static string ResolveDataDirectory()
        {
            var candidates = new List<string>();

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local))
            {
                candidates.Add(Path.Combine(local, DataFolderName));
            }

            string temp = Path.GetTempPath();
            if (!string.IsNullOrEmpty(temp))
            {
                candidates.Add(Path.Combine(temp, DataFolderName));
            }

            candidates.Add(BaseDirectory);

            foreach (string directory in candidates)
            {
                if (IsWritable(directory))
                {
                    return directory;
                }
            }

            return BaseDirectory;
        }

        private static bool IsWritable(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string probe = Path.Combine(directory, ".write-test-" + Guid.NewGuid().ToString("N"));
                using (File.Create(probe))
                {
                }

                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Steam's CEF debug port is usually IPv6-only, but some setups expose
        /// it on IPv4. Try IPv4 first, then fall back to IPv6.
        /// </summary>
        public static string[] CdpTargetListUrls
        {
            get
            {
                return new[]
                {
                    "http://127.0.0.1:" + CdpPort + "/json/list",
                    "http://[::1]:" + CdpPort + "/json/list"
                };
            }
        }
    }
}
