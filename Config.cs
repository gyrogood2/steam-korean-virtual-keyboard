using System;
using System.IO;

namespace SteamOskKorean
{
    /// <summary>Central configuration values for the application.</summary>
    internal static class Config
    {
        public const string Version = "1.0.0";

        public const int ControlPort = 8793;
        public const int CdpPort = 8080;

        public const int InjectIntervalMs = 1000;
        public const int KeyPollIntervalMs = 15;
        public const int KeySuppressMs = 150;

        public const int CdpConnectTimeoutMs = 3000;
        public const int CdpSendTimeoutMs = 4000;
        public const int CdpReceiveTimeoutMs = 5000;

        /// <summary>First characters of the Steam virtual keyboard window title ("가상").</summary>
        public const string VirtualKeyboardTitleMarker = "\uAC00\uC0C1";

        public const string PayloadFileName = "payload.js";
        public const string LogFileName = "SteamKoreanKeyboard.log";

        /// <summary>Replaced with a per-run random token before injection.</summary>
        public const string TokenPlaceholder = "__SIX_TOKEN__";

        public static readonly string BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;

        public static string PayloadPath
        {
            get { return Path.Combine(BaseDirectory, PayloadFileName); }
        }

        public static string LogPath
        {
            get { return Path.Combine(BaseDirectory, LogFileName); }
        }

        public static string CdpTargetListUrl
        {
            get { return "http://[::1]:" + CdpPort + "/json/list"; }
        }
    }
}
