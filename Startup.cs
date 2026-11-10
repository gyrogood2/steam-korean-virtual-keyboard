using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SteamOskKorean
{
    /// <summary>Per-user "run at sign-in" registration (HKCU Run key).</summary>
    internal static class Startup
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "SteamKoreanKeyboard";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                    {
                        return key != null && key.GetValue(ValueName) != null;
                    }
                }
                catch (Exception exception)
                {
                    Log.Error("startup read", exception);
                    return false;
                }
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null)
                {
                    return;
                }

                if (enabled)
                {
                    // --startup keeps sign-in launches quiet (no UAC/message box).
                    key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\" --full --startup");
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }

            Log.Info("startup " + (enabled ? "on" : "off"));
        }
    }
}
