using System;
using System.IO;
using System.Text;

namespace SteamOskKorean
{
    /// <summary>Minimal thread-safe file logger.</summary>
    internal static class Log
    {
        private static readonly object Sync = new object();

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string context, Exception exception)
        {
            Write("ERROR", context + ": " + exception.Message);
        }

        private static void Write(string level, string message)
        {
            lock (Sync)
            {
                try
                {
                    string line = DateTime.Now.ToString("HH:mm:ss.fff")
                                + " [" + level + "] " + message + Environment.NewLine;
                    File.AppendAllText(Config.LogPath, line, new UTF8Encoding(false));
                }
                catch (IOException)
                {
                    // Logging must never break the application.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
