using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SteamOskKorean
{
    /// <summary>Win32 interop used by the input and IME services.</summary>
    internal static class NativeMethods
    {
        public const int VkHangul = 0x15;
        public const uint InputKeyboard = 1;
        public const uint KeyEventKeyUp = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        public struct MouseInput
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr extraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KeyboardInput
        {
            public ushort virtualKey;
            public ushort scanCode;
            public uint flags;
            public uint time;
            public IntPtr extraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HardwareInput
        {
            public uint message;
            public ushort parameterLow;
            public ushort parameterHigh;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MouseInput mouse;
            [FieldOffset(0)] public KeyboardInput keyboard;
            [FieldOffset(0)] public HardwareInput hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Input
        {
            public uint type;
            public InputUnion data;
        }

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint count, Input[] inputs, int size);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

        [DllImport("imm32.dll")]
        public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr window);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessageTimeout(
            IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint PrivateExtractIcons(
            string file, int index, int width, int height,
            IntPtr[] icons, uint[] iconIds, uint count, uint flags);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr icon);
    }
}
