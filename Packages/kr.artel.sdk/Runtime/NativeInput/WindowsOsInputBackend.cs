#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
using System;
using System.Runtime.InteropServices;
using global::UnityEngine;

namespace Artel.NativeInput
{
    internal sealed class WindowsOsInputBackend : IOsInputBackend
    {
        private bool disposed;

        public void Validate(bool requireFocus = false)
        {
            if (disposed) throw new ObjectDisposedException(nameof(WindowsOsInputBackend));
            if (Application.isBatchMode)
                throw new InvalidOperationException("OS input requires an interactive desktop; Unity is running in batchmode.");
            var desktop = OpenInputDesktop(0, false, 0x0100);
            if (desktop == IntPtr.Zero)
                throw new InvalidOperationException("The interactive Windows input desktop is unavailable. Unlock the desktop and retry.");
            CloseDesktop(desktop);
            if (requireFocus) FocusedWindow();
        }

        public void SetKey(KeyCode key, bool down)
        {
            Validate();
            var virtualKey = OsKeyCodes.Windows(key);
            if (down) FocusedWindow();
            var scanCode = MapVirtualKey(virtualKey, 4);
            var flags = down ? 0u : 0x0002u;
            var keyboard = new KeyboardInput();
            if (key == KeyCode.Pause)
            {
                // Pause의 E1 시퀀스는 KEYEVENTF_EXTENDEDKEY로 표현할 수 없다.
                keyboard.VirtualKey = virtualKey;
            }
            else
            {
                if (scanCode == 0) throw new InvalidOperationException("Windows has no scan code for KeyCode: " + key);
                keyboard.ScanCode = (ushort)(scanCode & 0xff);
                flags |= 0x0008;
                if ((scanCode & 0xff00) == 0xe000 || key == KeyCode.KeypadEnter) flags |= 0x0001;
            }
            keyboard.Flags = flags;
            Send(new NativeInput { Type = 1, Payload = new InputUnion { Keyboard = keyboard } });
        }

        public void SetMouseButton(int button, bool down)
        {
            Validate();
            OsInputCoordinates.ValidateButton(button);
            var downFlag = button == 0 ? 0x0002u : button == 1 ? 0x0008u : 0x0020u;
            if (!down)
            {
                SendMouse(downFlag << 1);
                return;
            }
            OsInputCoordinates.RequirePlayerPointer();
            var window = FocusedWindow();
            var previousAwareness = SetThreadDpiAwarenessContext(new IntPtr(-4));
            if (previousAwareness == IntPtr.Zero)
                throw new InvalidOperationException("Windows could not establish a DPI-aware coordinate context.");
            try
            {
                if (!GetCursorPos(out var point) || GetAncestor(WindowFromPoint(point), 2) != window ||
                    !ScreenToClient(window, ref point) || !GetClientRect(window, out var client) ||
                    point.X < client.Left || point.X >= client.Right || point.Y < client.Top || point.Y >= client.Bottom)
                    throw new InvalidOperationException("The OS pointer must be over the visible game client area before pressing a mouse button.");
                SendMouse(downFlag);
            }
            finally { SetThreadDpiAwarenessContext(previousAwareness); }
        }

        public void MoveMouse(Vector2 gameScreenPosition)
        {
            Validate();
            OsInputCoordinates.Validate(gameScreenPosition);
            var window = FocusedWindow();
            // Unity의 DPI 설정과 관계없이 client rect와 virtual desktop을 같은 물리 pixel 좌표로 읽는다.
            var previousAwareness = SetThreadDpiAwarenessContext(new IntPtr(-4));
            if (previousAwareness == IntPtr.Zero)
                throw new InvalidOperationException("Windows could not establish a DPI-aware coordinate context.");
            try
            {
                if (!GetClientRect(window, out var client) || client.Right <= client.Left || client.Bottom <= client.Top)
                    throw new InvalidOperationException("The Unity player client rectangle is unavailable.");
                var origin = new NativePoint();
                if (!ClientToScreen(window, ref origin))
                    throw new InvalidOperationException("Windows could not map the Unity player to desktop coordinates.");
                var desktopLeft = GetSystemMetrics(76);
                var desktopTop = GetSystemMetrics(77);
                var desktopWidth = GetSystemMetrics(78);
                var desktopHeight = GetSystemMetrics(79);
                if (desktopWidth <= 1 || desktopHeight <= 1)
                    throw new InvalidOperationException("Windows virtual desktop bounds are unavailable.");
                var desktopX = origin.X + Math.Floor(gameScreenPosition.x * (double)(client.Right - client.Left) / Screen.width);
                var desktopY = origin.Y + Math.Floor(gameScreenPosition.y * (double)(client.Bottom - client.Top) / Screen.height);
                if (desktopX < desktopLeft || desktopX >= desktopLeft + desktopWidth ||
                    desktopY < desktopTop || desktopY >= desktopTop + desktopHeight)
                    throw new InvalidOperationException("The requested game position is outside the Windows virtual desktop.");
                var mouse = new MouseInput
                {
                    X = (int)Math.Round((desktopX - desktopLeft) * 65535.0 / (desktopWidth - 1)),
                    Y = (int)Math.Round((desktopY - desktopTop) * 65535.0 / (desktopHeight - 1)),
                    Flags = 0x0001 | 0x4000 | 0x8000
                };
                Send(new NativeInput { Payload = new InputUnion { Mouse = mouse } });
            }
            finally { SetThreadDpiAwarenessContext(previousAwareness); }
        }

        public void Dispose() { disposed = true; }

        private static IntPtr FocusedWindow()
        {
            OsInputCoordinates.RequireFocus();
            var window = GetForegroundWindow();
            GetWindowThreadProcessId(window, out var processId);
            if (window == IntPtr.Zero || processId != GetCurrentProcessId())
                throw new InvalidOperationException("The foreground window does not belong to this Unity process. Focus the game and retry.");
            return window;
        }

        private static void SendMouse(uint flags)
        {
            Send(new NativeInput { Payload = new InputUnion { Mouse = new MouseInput { Flags = flags } } });
        }

        private static void Send(NativeInput input)
        {
            if (SendInput(1, new[] { input }, Marshal.SizeOf<NativeInput>()) != 1)
                throw new InvalidOperationException("Windows SendInput failed (error " + Marshal.GetLastWin32Error() +
                    "). Check desktop access and run the SDK at the same integrity level as the game.");
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeInput { internal uint Type; internal InputUnion Payload; }
        // union의 alignment를 유지해야 INPUT 크기가 x86에서 28, x64에서 40이 된다.
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] internal MouseInput Mouse;
            [FieldOffset(0)] internal KeyboardInput Keyboard;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            internal int X, Y;
            internal uint MouseData, Flags, Time;
            internal UIntPtr ExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            internal ushort VirtualKey, ScanCode;
            internal uint Flags, Time;
            internal UIntPtr ExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { internal int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
        [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")] private static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr window, out NativeRect rectangle);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ScreenToClient(IntPtr window, ref NativePoint point);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    }
}
#endif
