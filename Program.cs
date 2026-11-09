using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SteamOskKorean
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (HasFlag(args, "--enable-debug"))
            {
                DebugSetup.Run();
                return;
            }

            bool createdNew;
            using (var mutex = new Mutex(true, "SteamKoreanKeyboard_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                if (!DebugSetup.IsReady())
                {
                    if (!DebugSetup.TryStartElevated())
                    {
                        Log.Warn("elevation cancelled; debug marker was not created");
                    }
                }

                string payload = LoadPayload();
                string token = CreateToken();
                if (payload.IndexOf(Config.TokenPlaceholder, StringComparison.Ordinal) >= 0)
                {
                    payload = payload.Replace(Config.TokenPlaceholder, token);
                }
                else if (payload.Length > 0)
                {
                    Log.Warn("payload has no token placeholder");
                }

                var cdp = new CdpClient();
                var input = new InputService();
                var injector = new VirtualKeyboardInjector(cdp, payload, token);
                var engine = new Engine(input, injector);

                using (var server = new ControlServer(engine, token))
                {
                    server.Start();

                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new TrayContext(engine, ParseInitialMode(args)));
                }
            }
        }

        private static string CreateToken()
        {
            var bytes = new byte[16];
            using (var random = new System.Security.Cryptography.RNGCryptoServiceProvider())
            {
                random.GetBytes(bytes);
            }

            var text = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
            {
                text.Append(value.ToString("x2"));
            }

            return text.ToString();
        }

        private static bool HasFlag(string[] args, string flag)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static AppMode ParseInitialMode(string[] args)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, "--full", StringComparison.OrdinalIgnoreCase))
                {
                    return AppMode.Full;
                }

                if (string.Equals(arg, "--pure", StringComparison.OrdinalIgnoreCase))
                {
                    return AppMode.Pure;
                }
            }

            // Start in the useful mode even when the exe is launched directly.
            // The tray menu can still switch to 수동 or Off.
            return AppMode.Full;
        }

        private static string LoadPayload()
        {
            try
            {
                if (File.Exists(Config.PayloadPath))
                {
                    Log.Info("payload: file");
                    return File.ReadAllText(Config.PayloadPath, Encoding.UTF8);
                }

                using (Stream stream = typeof(Program).Assembly.GetManifestResourceStream("SteamOskKorean.payload.js"))
                {
                    if (stream != null)
                    {
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            Log.Info("payload: embedded");
                            return reader.ReadToEnd();
                        }
                    }
                }

                Log.Error("payload", new FileNotFoundException(Config.PayloadPath));
            }
            catch (Exception exception)
            {
                Log.Error("load payload", exception);
            }

            return string.Empty;
        }
    }
}
