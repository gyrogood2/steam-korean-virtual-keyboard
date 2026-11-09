using System;
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

        public TrayContext(Engine engine, AppMode initialMode)
        {
            _engine = engine;

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
            _engine.Stop();
            _icon.Visible = false;
            _icon.Dispose();
            ExitThread();
        }
    }
}
