using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SteamOskKorean
{
    /// <summary>
    /// Sends the Windows Hangul/English toggle key and tracks physical presses.
    /// </summary>
    internal sealed class InputService
    {
        private long _suppressUntilMs;
        private Thread _trackingThread;
        private volatile bool _tracking;
        private Action<IntPtr> _onHangulPressed;

        public IntPtr ForegroundWindow
        {
            get { return NativeMethods.GetForegroundWindow(); }
        }

        public bool IsOskWindow(IntPtr window)
        {
            if (window == IntPtr.Zero)
            {
                return false;
            }

            var title = new StringBuilder(256);
            NativeMethods.GetWindowText(window, title, title.Capacity);
            return title.ToString().IndexOf(Config.VirtualKeyboardTitleMarker, StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Best-effort read of a window's IME conversion mode.
        /// Falls back to the last known value when the app does not expose it.
        /// </summary>
        public bool ReadImeState(IntPtr window)
        {
            if (window == IntPtr.Zero)
            {
                return true;
            }

            try
            {
                IntPtr imeWindow = NativeMethods.ImmGetDefaultIMEWnd(window);
                if (imeWindow == IntPtr.Zero)
                {
                    return true;
                }

                IntPtr result;
                IntPtr ok = NativeMethods.SendMessageTimeout(
                    imeWindow, 0x0283, (IntPtr)1, IntPtr.Zero, 2, 500, out result);

                return ok != IntPtr.Zero && (result.ToInt64() & 1) == 1;
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>Sends VK_HANGUL, exactly like pressing the 한/영 key.</summary>
        public void SendHangul()
        {
            _suppressUntilMs = NowMs() + Config.KeySuppressMs;

            var down = new NativeMethods.Input
            {
                type = NativeMethods.InputKeyboard
            };
            down.data.keyboard.virtualKey = NativeMethods.VkHangul;
            down.data.keyboard.flags = 0;

            var up = down;
            up.data.keyboard.flags = NativeMethods.KeyEventKeyUp;

            NativeMethods.SendInput(
                2,
                new[] { down, up },
                Marshal.SizeOf(typeof(NativeMethods.Input)));
        }

        /// <summary>
        /// Sends an ASCII letter as a real key event (Shift when uppercase).
        /// This is what lets the Windows IME compose double consonants.
        /// </summary>
        public void SendLetter(char letter)
        {
            if (letter < 'A' || letter > 'z')
            {
                return;
            }

            char upper = char.ToUpperInvariant(letter);
            if (upper < 'A' || upper > 'Z')
            {
                return;
            }

            bool shift = char.IsUpper(letter);
            ushort virtualKey = (ushort)(0x41 + (upper - 'A'));

            var inputs = new System.Collections.Generic.List<NativeMethods.Input>();
            if (shift)
            {
                inputs.Add(CreateKey(VkShift, false));
            }

            inputs.Add(CreateKey(virtualKey, false));
            inputs.Add(CreateKey(virtualKey, true));

            if (shift)
            {
                inputs.Add(CreateKey(VkShift, true));
            }

            uint sent = NativeMethods.SendInput(
                (uint)inputs.Count,
                inputs.ToArray(),
                Marshal.SizeOf(typeof(NativeMethods.Input)));

            if (sent == 0)
            {
                Log.Warn("SendInput(letter) failed err=" + Marshal.GetLastWin32Error());
            }
        }

        private const ushort VkShift = 0x10;

        private static NativeMethods.Input CreateKey(ushort virtualKey, bool keyUp)
        {
            var input = new NativeMethods.Input
            {
                type = NativeMethods.InputKeyboard
            };
            input.data.keyboard.virtualKey = virtualKey;
            input.data.keyboard.flags = keyUp ? NativeMethods.KeyEventKeyUp : 0;
            return input;
        }

        /// <summary>Starts polling the physical 한/영 key (Full mode only).</summary>
        public void StartTracking(Action<IntPtr> onHangulPressed)
        {
            StopTracking();

            _onHangulPressed = onHangulPressed;
            _tracking = true;
            _trackingThread = new Thread(TrackingLoop);
            _trackingThread.IsBackground = true;
            _trackingThread.Start();
        }

        public void StopTracking()
        {
            _tracking = false;
            _trackingThread = null;
            _onHangulPressed = null;
        }

        private void TrackingLoop()
        {
            bool wasDown = false;

            while (_tracking)
            {
                bool isDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VkHangul) & 0x8000) != 0;

                if (isDown && !wasDown && NowMs() > _suppressUntilMs)
                {
                    Action<IntPtr> handler = _onHangulPressed;
                    IntPtr foreground = NativeMethods.GetForegroundWindow();

                    if (handler != null && foreground != IntPtr.Zero && !IsOskWindow(foreground))
                    {
                        handler(foreground);
                    }
                }

                wasDown = isDown;
                Thread.Sleep(Config.KeyPollIntervalMs);
            }
        }

        private static long NowMs()
        {
            return DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
        }
    }
}
