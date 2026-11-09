using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SteamOskKorean
{
    /// <summary>
    /// Creates Steam's debug-port marker file so the app can attach on a normal
    /// Steam launch. Runs elevated (the Steam folder is usually read-only).
    /// </summary>
    internal static class DebugSetup
    {
        public static void Run()
        {
            try
            {
                string steam = ReadSteamPath();
                if (!File.Exists(Path.Combine(steam, "steam.exe")))
                {
                    MessageBox.Show(
                        "Steam 설치 경로를 찾지 못했습니다.\n" + steam + "\n\nSteam을 한 번 실행한 뒤 다시 시도해 주세요.",
                        "Steam 한글 가상 키보드",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                string flag = Path.Combine(steam, ".cef-enable-remote-debugging");
                if (!File.Exists(flag))
                {
                    File.Create(flag).Dispose();
                }

                MessageBox.Show(
                    "Steam 연결 준비가 끝났습니다.\n\nSteam을 완전히 종료한 뒤 다시 실행해 주세요.",
                    "Steam 한글 가상 키보드",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "설정 파일을 만들지 못했습니다.\n" + exception.Message,
                    "Steam 한글 가상 키보드",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        public static bool IsReady()
        {
            try
            {
                return File.Exists(Path.Combine(ReadSteamPath(), ".cef-enable-remote-debugging"));
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Relaunches this exe elevated. Returns false when UAC is cancelled.</summary>
        public static bool TryStartElevated()
        {
            try
            {
                var info = new ProcessStartInfo(Application.ExecutablePath, "--enable-debug");
                info.UseShellExecute = true;
                info.Verb = "runas";
                Process.Start(info);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ReadSteamPath()
        {
            string path = null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    path = key == null ? null : key.GetValue("SteamPath") as string;
                }
            }
            catch (Exception)
            {
            }

            if (!string.IsNullOrEmpty(path))
            {
                path = path.Replace('/', '\\');
                if (File.Exists(Path.Combine(path, "steam.exe")))
                {
                    return path;
                }
            }

            const string fallback = @"C:\Program Files (x86)\Steam";
            if (File.Exists(Path.Combine(fallback, "steam.exe")))
            {
                return fallback;
            }

            return path ?? fallback;
        }
    }
}
