using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SteamOskKorean
{
    internal enum SetupOutcome
    {
        Ready,
        Created,
        Cancelled,
        AccessDenied,
        SteamNotFound,
        Failed
    }

    internal sealed class SetupResult
    {
        public SetupOutcome Outcome;
        public string SteamPath;
        public string Message;
        public bool NeedsSteamRestart;
    }

    /// <summary>
    /// Creates Steam's debug-port marker file. The Steam folder is usually
    /// read-only, so the write is tried without elevation first, then once
    /// through UAC with the Steam path found by the non-elevated process.
    /// </summary>
    internal static class DebugSetup
    {
        public const string MarkerName = ".cef-enable-remote-debugging";

        private const int ChildWaitMs = 90000;

        private const int ExitOk = 0;
        private const int ExitAccessDenied = 2;
        private const int ExitNoSteam = 3;
        private const int ExitCancelled = 4;
        private const int ExitOther = 5;

        /// <summary>Entry point for the elevated child process (--enable-debug).</summary>
        public static void Run(string[] args)
        {
            bool quiet = HasArg(args, "--quiet");
            string steam = FindSteamPath(ValueOf(args, "--steam"));
            SetupResult result = CreateMarker(steam);

            Log.Info("setup child: " + result.Outcome + " (" + steam + ")");

            if (!quiet)
            {
                if (result.Outcome == SetupOutcome.Created || result.Outcome == SetupOutcome.Ready)
                {
                    MessageBox.Show(
                        "Steam 연결 준비가 끝났습니다.\n\nSteam을 완전히 종료한 뒤 다시 실행해 주세요.",
                        "Steam 한글 가상 키보드",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(
                        result.Message,
                        "Steam 한글 가상 키보드",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            Environment.ExitCode = ExitCodeFor(result.Outcome);
        }

        /// <summary>
        /// Makes sure the marker exists. Never throws and never blocks forever.
        /// The app keeps working even when the marker cannot be created.
        /// </summary>
        public static SetupResult EnsureReady(string[] args)
        {
            // Sign-in launches must never raise a UAC prompt.
            bool allowElevation = !HasArg(args, "--startup");
            return EnsureReady(args, allowElevation);
        }

        public static SetupResult EnsureReady(string[] args, bool allowElevation)
        {
            string steam = FindSteamPath(ValueOf(args, "--steam"));

            if (!LooksLikeSteam(steam))
            {
                SetupResult missing = Fail(
                    SetupOutcome.SteamNotFound,
                    steam,
                    "Steam 설치 경로를 찾지 못했습니다.\n" + steam +
                    "\n\nSteam을 한 번 실행한 뒤 다시 시도해 주세요.");
                Log.Warn("setup: " + missing.Outcome + " (" + steam + ")");
                return missing;
            }

            SetupResult result = CreateMarker(steam);
            if (result.Outcome == SetupOutcome.Created || result.Outcome == SetupOutcome.Ready)
            {
                Log.Info("setup: " + result.Outcome + " (" + steam + ")");
                return result;
            }

            // The marker exists but this process cannot write it. Ask for
            // elevation once, passing the path we already found: after UAC the
            // child may run as a different account with a different HKCU.
            int code = allowElevation ? ElevateAndWait(steam) : ExitCancelled;

            if (IsReadyAt(steam))
            {
                result = new SetupResult
                {
                    Outcome = SetupOutcome.Created,
                    SteamPath = steam,
                    NeedsSteamRestart = true,
                    Message = "Steam 연결 준비가 끝났습니다.\n\n" +
                              "Steam이 이미 실행 중이라면 트레이 메뉴에서 완전히 종료한 뒤\n" +
                              "다시 실행해 주세요.\n" +
                              "(Steam은 켤 때만 표시 파일을 읽습니다.)"
                };
                Log.Info("setup: created by elevated child (" + steam + ")");
                return result;
            }

            if (code == ExitCancelled)
            {
                string message = allowElevation
                    ? "관리자 권한 요청이 취소되었습니다.\n\n" +
                      "앱은 계속 실행되지만, Steam이 켜질 때 연결 통로를 열려면\n" +
                      "표시 파일을 만들어야 합니다.\n\n" +
                      "해결 방법:\n" +
                      "1) 트레이 메뉴 → 종료\n" +
                      "2) 바탕화면의 \"Steam (Korean)\" 바로가기 실행\n" +
                      "   (Steam이 꺼져 있으면 디버그 옵션으로 함께 켜집니다.)"
                    : "Steam 표시 파일이 아직 없습니다.\n\n" +
                      "Windows 시작 시에는 관리자 권한을 요청하지 않습니다.\n" +
                      "Steam을 완전히 종료한 뒤 바탕화면의\n" +
                      "\"Steam (Korean)\" 바로가기로 실행해 주세요.\n" +
                      "(관리자 권한 없이 디버그 옵션으로 Steam을 켭니다.)";

                result = Fail(SetupOutcome.Cancelled, steam, message);
                Log.Warn("setup: elevation skipped or cancelled (" + steam + ")");
                return result;
            }

            result = Fail(
                SetupOutcome.AccessDenied,
                steam,
                "Steam 표시 파일을 만들지 못했습니다.\n\n" +
                "경로: " + Path.Combine(steam, MarkerName) + "\n" +
                "원인: 이 폴더에 파일을 쓸 권한이 없습니다.\n" +
                (code == ExitOther ? "관리자 권한으로 실행했지만 실패했습니다.\n" : string.Empty) +
                "\n해결 방법:\n" +
                "1) Steam을 완전히 종료한 뒤 바탕화면의\n" +
                "   \"Steam (Korean)\" 바로가기로 실행하세요.\n" +
                "   (관리자 권한 없이 디버그 옵션으로 Steam을 켭니다.)\n" +
                "2) 또는 이 앱을 한 번만 \"관리자 권한으로 실행\"하세요.\n\n" +
                "앱은 계속 실행됩니다.");
            Log.Warn("setup: marker not created, exit=" + code + " (" + steam + ")");
            return result;
        }

        public static bool IsReady()
        {
            try
            {
                return IsReadyAt(ReadSteamPath());
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsReadyAt(string steam)
        {
            return !string.IsNullOrEmpty(steam) && File.Exists(Path.Combine(steam, MarkerName));
        }

        /// <summary>Checks the Windows principal, not a mutable token.</summary>
        public static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static SetupResult CreateMarker(string steam)
        {
            if (!LooksLikeSteam(steam))
            {
                return Fail(
                    SetupOutcome.SteamNotFound,
                    steam,
                    "Steam 설치 경로를 찾지 못했습니다.\n" + steam +
                    "\n\nSteam을 한 번 실행한 뒤 다시 시도해 주세요.");
            }

            string flag = Path.Combine(steam, MarkerName);

            if (File.Exists(flag))
            {
                return new SetupResult
                {
                    Outcome = SetupOutcome.Ready,
                    SteamPath = steam
                };
            }

            if (Directory.Exists(flag))
            {
                // File.Create on an existing directory fails with "access is
                // denied", which used to be reported as a permission problem.
                return Fail(
                    SetupOutcome.AccessDenied,
                    steam,
                    "같은 이름의 폴더가 있어 표시 파일을 만들지 못했습니다.\n\n" +
                    flag + "\n\n" +
                    "Steam 폴더에서 \"" + MarkerName + "\" 이름의 폴더를 지운 뒤 다시 시도해 주세요.");
            }

            try
            {
                using (File.Create(flag))
                {
                }

                return new SetupResult
                {
                    Outcome = SetupOutcome.Created,
                    SteamPath = steam,
                    NeedsSteamRestart = true
                };
            }
            catch (UnauthorizedAccessException exception)
            {
                Log.Warn("setup: access denied " + flag + " (" + exception.GetType().Name + ")");
                return Fail(
                    SetupOutcome.AccessDenied,
                    steam,
                    "Steam 표시 파일을 만들 권한이 없습니다.\n\n" + flag);
            }
            catch (DirectoryNotFoundException exception)
            {
                Log.Warn("setup: folder missing " + flag + " (" + exception.GetType().Name + ")");
                return Fail(
                    SetupOutcome.SteamNotFound,
                    steam,
                    "Steam 폴더가 없습니다.\n\n" + steam);
            }
            catch (Exception exception)
            {
                Log.Error("setup: create marker", exception);
                return Fail(
                    SetupOutcome.Failed,
                    steam,
                    "Steam 표시 파일을 만들지 못했습니다.\n\n" + flag +
                    "\n\n" + exception.GetType().Name + ": " + exception.Message);
            }
        }

        /// <summary>
        /// Relaunches this exe elevated and waits for it. Returns the child exit
        /// code, or ExitCancelled when UAC was dismissed or could not launch.
        /// </summary>
        private static int ElevateAndWait(string steam)
        {
            try
            {
                string arguments = "--enable-debug --quiet --steam \"" + steam + "\"";
                var info = new ProcessStartInfo(Application.ExecutablePath, arguments);
                info.UseShellExecute = true;
                info.Verb = "runas";

                Log.Info("setup: requesting elevation");

                using (Process child = Process.Start(info))
                {
                    if (child == null)
                    {
                        return ExitCancelled;
                    }

                    if (!child.WaitForExit(ChildWaitMs))
                    {
                        Log.Warn("setup: elevated child did not finish in time");
                        return ExitOther;
                    }

                    return child.ExitCode;
                }
            }
            catch (Exception exception)
            {
                // Win32Exception 1223 = the user dismissed the UAC prompt.
                Log.Warn("setup: elevation not started (" + exception.Message + ")");
                return ExitCancelled;
            }
        }

        private static int ExitCodeFor(SetupOutcome outcome)
        {
            switch (outcome)
            {
                case SetupOutcome.Ready:
                case SetupOutcome.Created:
                    return ExitOk;
                case SetupOutcome.AccessDenied:
                    return ExitAccessDenied;
                case SetupOutcome.SteamNotFound:
                    return ExitNoSteam;
                case SetupOutcome.Cancelled:
                    return ExitCancelled;
                default:
                    return ExitOther;
            }
        }

        private static SetupResult Fail(SetupOutcome outcome, string steam, string message)
        {
            return new SetupResult
            {
                Outcome = outcome,
                SteamPath = steam,
                Message = message
            };
        }

        private static bool HasArg(string[] args, string name)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ValueOf(string[] args, string name)
        {
            if (args == null)
            {
                return null;
            }

            for (int index = 0; index < args.Length - 1; index++)
            {
                if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[index + 1];
                }
            }

            return null;
        }

        public static string FindSteamPath(string hint)
        {
            // An explicit hint wins even when it is wrong: the caller already
            // resolved it as the signed-in user, and silently falling back to
            // another account's HKCU is what broke elevation before.
            if (!string.IsNullOrEmpty(hint))
            {
                return Normalize(hint);
            }

            string path = RegistryValue(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            if (LooksLikeSteam(path))
            {
                return Normalize(path);
            }

            path = RegistryValue(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
            if (LooksLikeSteam(path))
            {
                return Normalize(path);
            }

            path = RegistryValue(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
            if (LooksLikeSteam(path))
            {
                return Normalize(path);
            }

            string[] common =
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam"
            };

            foreach (string candidate in common)
            {
                if (LooksLikeSteam(candidate))
                {
                    return candidate;
                }
            }

            return Normalize(path);
        }

        private static string ReadSteamPath()
        {
            return FindSteamPath(null);
        }

        private static string RegistryValue(RegistryKey root, string subKey, string name)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(subKey))
                {
                    if (key == null)
                    {
                        return null;
                    }

                    string value = key.GetValue(name) as string;
                    return string.IsNullOrEmpty(value) ? null : value;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool LooksLikeSteam(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                return File.Exists(Path.Combine(Normalize(path), "steam.exe"));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            return path.Replace('/', '\\').TrimEnd('\\');
        }
    }
}
