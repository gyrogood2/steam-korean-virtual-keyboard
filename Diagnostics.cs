using System;
using System.Diagnostics;
using System.Text;

namespace SteamOskKorean
{
    /// <summary>Human-readable connection status for the tray menu.</summary>
    internal static class Diagnostics
    {
        public static string Describe(AppMode mode)
        {
            bool steamRunning = IsSteamRunning();
            bool marker = DebugSetup.IsReady();
            bool reachable = CdpClient.IsReachable();

            var text = new StringBuilder();
            text.AppendLine("Steam 실행: " + (steamRunning ? "예" : "아니오"));
            text.AppendLine("표시 파일: " + (marker ? "있음" : "없음"));
            text.AppendLine("디버그 포트: " + DescribeEndpoint(reachable));
            text.AppendLine("현재 모드: " + ModeLabel(mode));
            text.AppendLine();

            if (reachable)
            {
                text.AppendLine("정상입니다.");
                text.AppendLine("Steam에서 가상 키보드를 열어 확인하세요.");
                text.AppendLine();
                text.AppendLine("※ 일반 데스크톱 채팅창이 아니라");
                text.AppendLine("   Big Picture 모드나 게임패드 입력에서 뜹니다.");
            }
            else if (!steamRunning)
            {
                text.AppendLine("Steam이 실행되어 있지 않습니다.");
                text.AppendLine("Steam을 실행한 뒤 다시 확인해 주세요.");
            }
            else if (!marker)
            {
                text.AppendLine("Steam 표시 파일이 없습니다.");
                text.AppendLine("설치를 다시 실행하거나,");
                text.AppendLine("앱을 관리자 권한으로 한 번 실행해 주세요.");
            }
            else
            {
                text.AppendLine("Steam이 실행 중이지만 연결 통로가 닫혀 있습니다.");
                text.AppendLine("Steam을 트레이 메뉴에서 완전히 종료한 뒤");
                text.AppendLine("다시 실행해 주세요.");
                text.AppendLine();
                text.AppendLine("(Steam은 켤 때만 표시 파일을 읽습니다.)");
            }

            return text.ToString();
        }

        public static bool IsSteamRunning()
        {
            try
            {
                return Process.GetProcessesByName("steam").Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string DescribeEndpoint(bool reachable)
        {
            if (!reachable)
            {
                return "연결 안 됨";
            }

            string endpoint = CdpClient.LastEndpoint;
            if (string.IsNullOrEmpty(endpoint))
            {
                return "연결됨";
            }

            return "연결됨 (" + endpoint.Replace("http://", string.Empty).Replace("/json/list", string.Empty) + ")";
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
    }
}
