using System;
using System.Collections.Generic;
using System.Threading;

namespace SteamOskKorean
{
    internal enum AppMode
    {
        Off,
        Full,
        Pure
    }

    /// <summary>
    /// Owns the injection loop, the per-window Korean state, and the public
    /// operations used by the control server and the UI.
    /// </summary>
    internal sealed class Engine
    {
        private readonly InputService _input;
        private readonly VirtualKeyboardInjector _injector;
        private readonly object _sync = new object();
        private readonly Dictionary<IntPtr, bool> _states = new Dictionary<IntPtr, bool>();

        private bool _lastKnown = true;
        private bool _globalKorean = true;
        private AppMode _mode = AppMode.Off;
        private Thread _injectThread;
        private volatile bool _running;

        public Engine(InputService input, VirtualKeyboardInjector injector)
        {
            _input = input;
            _injector = injector;
        }

        public AppMode Mode
        {
            get { lock (_sync) { return _mode; } }
        }

        /// <summary>Current Korean/English state for the focused window.</summary>
        public bool IsKorean
        {
            get
            {
                if (Mode == AppMode.Full)
                {
                    IntPtr foreground = GetForegroundTarget();
                    return foreground != IntPtr.Zero ? GetState(foreground) : _lastKnown;
                }

                lock (_sync)
                {
                    return _globalKorean;
                }
            }
        }

        public void Start(AppMode mode)
        {
            Stop();

            lock (_sync)
            {
                _mode = mode;
            }

            _running = true;
            _injectThread = new Thread(InjectLoop);
            _injectThread.IsBackground = true;
            _injectThread.Start();

            if (mode == AppMode.Full)
            {
                _input.StartTracking(OnPhysicalHangul);
            }

            Log.Info("engine start mode=" + mode);
        }

        public void Stop()
        {
            _running = false;
            _input.StopTracking();
            _injector.RestoreAll();

            lock (_sync)
            {
                _mode = AppMode.Off;
            }

            Log.Info("engine stop");
        }

        /// <summary>
        /// Toggles the state. When <paramref name="sendKey"/> is true the actual
        /// Windows 한/영 key is sent; otherwise only the label state changes.
        /// </summary>
        public bool Toggle(bool sendKey)
        {
            IntPtr foreground = GetForegroundTarget();
            bool korean;

            if (Mode == AppMode.Full)
            {
                korean = !(foreground != IntPtr.Zero ? GetState(foreground) : _lastKnown);
                SetState(foreground, korean);
            }
            else
            {
                lock (_sync)
                {
                    _globalKorean = !_globalKorean;
                    korean = _globalKorean;
                }
            }

            if (sendKey)
            {
                _input.SendHangul();
            }

            Log.Info((sendKey ? "hangul" : "flip") + " -> " + (korean ? "ko" : "en"));
            return korean;
        }

        public void SendLetter(char letter)
        {
            _input.SendLetter(letter);
        }

        private void InjectLoop()
        {
            while (_running)
            {
                try
                {
                    _injector.InjectAll();
                }
                catch (Exception exception)
                {
                    Log.Error("inject loop", exception);
                }

                Thread.Sleep(Config.InjectIntervalMs);
            }
        }

        private void OnPhysicalHangul(IntPtr window)
        {
            SetState(window, !GetState(window));
        }

        private IntPtr GetForegroundTarget()
        {
            IntPtr foreground = _input.ForegroundWindow;
            return _input.IsOskWindow(foreground) ? IntPtr.Zero : foreground;
        }

        private bool GetState(IntPtr window)
        {
            lock (_sync)
            {
                bool value;
                if (_states.TryGetValue(window, out value))
                {
                    return value;
                }

                value = _input.ReadImeState(window);
                _states[window] = value;
                _lastKnown = value;
                return value;
            }
        }

        private void SetState(IntPtr window, bool value)
        {
            lock (_sync)
            {
                if (window != IntPtr.Zero)
                {
                    _states[window] = value;
                }

                _lastKnown = value;
            }
        }
    }
}
