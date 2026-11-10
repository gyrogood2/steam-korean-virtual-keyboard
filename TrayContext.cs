using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace SteamOskKorean
{
    /// <summary>Tray-only shell. No window; the mode is chosen from the tray menu.</summary>
    internal sealed class TrayContext : ApplicationContext
    {
        private readonly Engine _engine;
        private readonly NotifyIcon _icon;
        private readonly ToolStripMenuItem _fullItem;
        private readonly ToolStripMenuItem _pureItem;
        private readonly ToolStripMenuItem _offItem;
        private readonly ToolStripMenuItem _startupItem;
        private readonly Timer _startupCheck;
        private readonly SetupResult _setup;
        private readonly bool _startupLaunch;
        private Timer _noticeTimer;

        public TrayContext(Engine engine, AppMode initialMode, SetupResult setup, bool startupLaunch)
        {
            _engine = engine;
            _setup = setup;
            _startupLaunch = startupLaunch;

            _fullItem = CreateModeItem(
                "자동",
                "창별 한/영 상태를 자동으로 추적합니다.",
                AppMode.Full);
            _pureItem = CreateModeItem(
                "수동",
                "상태를 추적하지 않고 전역 1개로 동작합니다.",
                AppMode.Pure);
            _offItem = CreateModeItem(
                "Off",
                "주입을 해제하고 원래 가상 키보드로 돌아갑니다.",
                AppMode.Off);

            var menu = new ContextMenuStrip();
            menu.Items.Add(_fullItem);
            menu.Items.Add(_pureItem);
            menu.Items.Add(_offItem);
            menu.Items.Add(new ToolStripSeparator());

            _startupItem = new ToolStripMenuItem("Windows 시작 시 실행");
            _startupItem.Checked = Startup.IsEnabled;
            _startupItem.Click += delegate { ToggleStartup(); };
            menu.Items.Add(_startupItem);

            var statusItem = new ToolStripMenuItem("연결 상태 확인");
            statusItem.Click += delegate { ShowStatus(); };
            menu.Items.Add(statusItem);

            var logItem = new ToolStripMenuItem("로그 열기");
            logItem.Click += delegate { OpenLog(); };
            menu.Items.Add(logItem);

            menu.Items.Add(new ToolStripSeparator());
            var exitItem = new ToolStripMenuItem("종료");
            exitItem.Click += delegate { Exit(); };
            menu.Items.Add(exitItem);

            _icon = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "Steam 가상 키보드",
                Visible = true,
                ContextMenuStrip = menu
            };

            if (initialMode != AppMode.Off)
            {
                ApplyMode(initialMode);
            }
            else
            {
                RefreshChecks();
            }

            ShowSetupNotice();

            _startupCheck = new Timer { Interval = 10000 };
            _startupCheck.Tick += delegate
            {
                _startupCheck.Stop();
                CheckConnectionOnce();
            };
            _startupCheck.Start();
        }

        private void ShowSetupNotice()
        {
            if (_setup == null || string.IsNullOrEmpty(_setup.Message))
            {
                return;
            }

            const string title = "Steam 한글 가상 키보드";
            bool success = _setup.Outcome == SetupOutcome.Created || _setup.Outcome == SetupOutcome.Ready;

            // At sign-in a message box would block the desktop; use a balloon.
            if (_startupLaunch)
            {
                if (success)
                {
                    return;
                }

                _icon.ShowBalloonTip(
                    10000,
                    "Steam 연결 준비가 필요합니다",
                    "트레이 메뉴 → 연결 상태 확인을 눌러 자세한 내용을 보세요.",
                    ToolTipIcon.Warning);
                return;
            }

            _noticeTimer = new Timer { Interval = 800 };
            _noticeTimer.Tick += delegate
            {
                _noticeTimer.Stop();
                _noticeTimer.Dispose();
                _noticeTimer = null;
                MessageBox.Show(
                    _setup.Message,
                    title,
                    MessageBoxButtons.OK,
                    success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            };
            _noticeTimer.Start();
        }

        private static Icon LoadIcon()
        {
            try
            {
                int size = SystemInformation.SmallIconSize.Width;
                var handles = new IntPtr[1];
                var ids = new uint[1];

                uint count = NativeMethods.PrivateExtractIcons(
                    Application.ExecutablePath, 0, size, size, handles, ids, 1, 0);

                if (count > 0 && handles[0] != IntPtr.Zero)
                {
                    using (Icon temporary = Icon.FromHandle(handles[0]))
                    {
                        Icon icon = (Icon)temporary.Clone();
                        NativeMethods.DestroyIcon(handles[0]);
                        return icon;
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Error("load icon", exception);
            }

            return SystemIcons.Application;
        }

        private ToolStripMenuItem CreateModeItem(string text, string description, AppMode mode)
        {
            var item = new ToolStripMenuItem(text);
            item.ToolTipText = description;
            item.Click += delegate { ApplyMode(mode); };
            return item;
        }

        private void ApplyMode(AppMode mode)
        {
            try
            {
                if (mode == AppMode.Off)
                {
                    _engine.Stop();
                }
                else
                {
                    _engine.Start(mode);
                }
            }
            catch (Exception exception)
            {
                Log.Error("apply mode " + mode, exception);
            }

            RefreshChecks();
        }

        private void RefreshChecks()
        {
            AppMode mode = _engine.Mode;

            _fullItem.Checked = mode == AppMode.Full;
            _pureItem.Checked = mode == AppMode.Pure;
            _offItem.Checked = mode == AppMode.Off;

            _icon.Text = "Steam 가상 키보드: " + ModeLabel(mode);
        }

        private static string ModeLabel(AppMode mode)
        {
            if (mode == AppMode.Full)
            {
                return "자동";
            }

            if (mode == AppMode.Pure)
            {
                return "수동";
            }

            return "Off";
        }

        private void CheckConnectionOnce()
        {
            if (_engine.Mode == AppMode.Off)
            {
                return;
            }

            if (!Diagnostics.IsSteamRunning() || CdpClient.IsReachable())
            {
                return;
            }

            _icon.ShowBalloonTip(
                10000,
                "Steam 연결 대기 중",
                "Steam이 실행 중이지만 연결 통로가 닫혀 있습니다.\n" +
                "Steam을 완전히 종료했다가 다시 실행해 주세요.",
                ToolTipIcon.Warning);
        }

        private void ShowStatus()
        {
            MessageBox.Show(
                Diagnostics.Describe(_engine.Mode) + Environment.NewLine +
                "로그: " + Config.LogPath,
                "Steam 한글 가상 키보드",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void OpenLog()
        {
            try
            {
                string path = Config.LogPath;
                if (!System.IO.File.Exists(path) && System.IO.File.Exists(Config.LegacyLogPath))
                {
                    // Log written by 1.0.6 or older, next to the exe.
                    path = Config.LegacyLogPath;
                }

                if (!System.IO.File.Exists(path))
                {
                    MessageBox.Show(
                        "로그 파일이 아직 없습니다." + Environment.NewLine + path,
                        "Steam 한글 가상 키보드",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                Process.Start("notepad.exe", "\"" + path + "\"");
            }
            catch (Exception exception)
            {
                Log.Error("open log", exception);
                MessageBox.Show(
                    "로그 파일을 열지 못했습니다." + Environment.NewLine + Config.LogPath,
                    "Steam 한글 가상 키보드",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void ToggleStartup()
        {
            try
            {
                Startup.SetEnabled(!_startupItem.Checked);
            }
            catch (Exception exception)
            {
                Log.Error("startup toggle", exception);
            }

            _startupItem.Checked = Startup.IsEnabled;
        }

        private void Exit()
        {
            if (_startupCheck != null)
            {
                _startupCheck.Stop();
                _startupCheck.Dispose();
            }

            if (_noticeTimer != null)
            {
                _noticeTimer.Stop();
                _noticeTimer.Dispose();
                _noticeTimer = null;
            }

            _engine.Stop();
            _icon.Visible = false;
            _icon.Dispose();
            ExitThread();
        }
    }
}
