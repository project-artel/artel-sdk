#if UNITY_EDITOR_LINUX || (UNITY_STANDALONE_LINUX && !UNITY_EDITOR)
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using global::UnityEngine;

namespace Artel.NativeInput
{
    internal sealed class LinuxOsInputBackend : IOsInputBackend
    {
        private const string X11 = "libX11.so.6";
        private const string XTest = "libXtst.so.6";
        private readonly Dictionary<KeyCode, uint> heldKeyCodes = new Dictionary<KeyCode, uint>();
        private IntPtr display;
        private bool disposed;

        // Xlib error handler는 프로세스 전체 설정이다. 검사 중 다른 display의 오류는 기존 handler로 보낸다.
        private static readonly object ErrorHandlerLock = new object();
        private static readonly XErrorHandler CheckedErrorHandler = HandleXError;
        private static IntPtr checkedDisplay;
        private static IntPtr previousErrorHandler;
        private static byte checkedError;

        public void Validate(bool requireFocus = false)
        {
            if (disposed) throw new ObjectDisposedException(nameof(LinuxOsInputBackend));
            if (Application.isBatchMode)
                throw new InvalidOperationException("OS input requires an interactive Linux desktop; Unity is running in batchmode.");
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ||
                string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase))
                throw new PlatformNotSupportedException("Native Wayland OS input requires a RemoteDesktop portal/libei backend, which this SDK does not provide. Use an X11 desktop session or virtual input mode.");
            try
            {
                if (display == IntPtr.Zero) display = XOpenDisplay(IntPtr.Zero);
                if (display == IntPtr.Zero)
                    throw new InvalidOperationException("The X11 display is unavailable. Set DISPLAY and authorize this Unity player to access the X server.");
                if (XTestQueryExtension(display, out _, out _, out _, out _) == 0)
                    throw new PlatformNotSupportedException("The X11 server does not provide the XTEST extension required for OS input.");
            }
            catch (DllNotFoundException exception)
            {
                throw new InvalidOperationException("Linux OS input requires libX11.so.6 and libXtst.so.6. Install the X11 and XTest runtime libraries.", exception);
            }
            if (requireFocus) Checked(() => FocusedPlayerWindow());
        }

        public void SetKey(KeyCode key, bool down)
        {
            Validate();
            var symbol = OsKeyCodes.X11(key);
            Checked(() =>
            {
                if (down) FocusedPlayerWindow();
                var keyCode = !down && heldKeyCodes.TryGetValue(key, out var heldKeyCode)
                    ? heldKeyCode : XKeysymToKeycode(display, symbol);
                if (keyCode == 0)
                    throw new InvalidOperationException("The current X11 keymap has no keycode for KeyCode: " + key);
                if (XTestFakeKeyEvent(display, keyCode, down ? 1 : 0, UIntPtr.Zero) == 0)
                    throw new InvalidOperationException("XTestFakeKeyEvent rejected the OS keyboard event.");
                if (down) heldKeyCodes[key] = keyCode;
                else heldKeyCodes.Remove(key);
            });
        }

        public void SetMouseButton(int button, bool down)
        {
            Validate();
            OsInputCoordinates.ValidateButton(button);
            Checked(() =>
            {
                if (down)
                {
                    OsInputCoordinates.RequirePlayerPointer();
                    RequirePointerOverPlayer(FocusedPlayerWindow());
                }
                var nativeButton = button == 0 ? 1u : button == 1 ? 3u : 2u;
                if (XTestFakeButtonEvent(display, nativeButton, down ? 1 : 0, UIntPtr.Zero) == 0)
                    throw new InvalidOperationException("XTestFakeButtonEvent rejected the OS mouse event.");
            });
        }

        public void MoveMouse(Vector2 gameScreenPosition)
        {
            Validate();
            OsInputCoordinates.Validate(gameScreenPosition);
            Checked(() =>
            {
                var window = FocusedPlayerWindow();
                if (XGetGeometry(display, window, out var root, out _, out _, out var width, out var height, out _, out _) == 0 ||
                    width == 0 || height == 0)
                    throw new InvalidOperationException("The Unity player X11 client rectangle is unavailable.");
                if (XTranslateCoordinates(display, window, root, 0, 0, out var originX, out var originY, out _) == 0)
                    throw new InvalidOperationException("X11 could not map the Unity player to desktop coordinates.");
                var desktopX = originX + (int)(gameScreenPosition.x * width / Screen.width);
                var desktopY = originY + (int)(gameScreenPosition.y * height / Screen.height);
                if (XGetGeometry(display, root, out _, out _, out _, out var rootWidth, out var rootHeight, out _, out _) == 0 ||
                    desktopX < 0 || desktopY < 0 || desktopX >= rootWidth || desktopY >= rootHeight)
                    throw new InvalidOperationException("The requested game position is outside the Unity player's X11 screen.");
                var screen = ScreenForRoot(root);
                if (XTestFakeMotionEvent(display, screen, desktopX, desktopY, UIntPtr.Zero) == 0)
                    throw new InvalidOperationException("XTestFakeMotionEvent rejected the OS pointer event.");
            });
        }

        private int ScreenForRoot(UIntPtr root)
        {
            // -1은 게임 창이 아니라 현재 커서가 있는 X11 screen을 선택한다.
            var screenCount = XScreenCount(display);
            for (var screen = 0; screen < screenCount; screen++)
                if (XRootWindow(display, screen) == root) return screen;
            throw new InvalidOperationException("X11 could not locate the Unity player's root screen.");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (display != IntPtr.Zero)
            {
                XCloseDisplay(display);
                display = IntPtr.Zero;
            }
        }

        private UIntPtr FocusedPlayerWindow()
        {
            OsInputCoordinates.RequireFocus();
            XGetInputFocus(display, out var window, out _);
            var pidProperty = XInternAtom(display, "_NET_WM_PID", 1);
            if (pidProperty == UIntPtr.Zero)
                throw new InvalidOperationException("X11 cannot identify the focused Unity window (_NET_WM_PID is missing).");
            // focus가 자식 window에 있으면 PID가 붙은 client window까지 올라간다.
            for (var depth = 0; depth < 64 && window.ToUInt64() > 1; depth++)
            {
                var processId = WindowProcessId(window, pidProperty);
                if (processId.HasValue)
                {
                    if (processId.Value != getpid())
                        throw new InvalidOperationException("The focused X11 window does not belong to this Unity process. Focus the game and retry.");
                    return window;
                }
                if (XQueryTree(display, window, out var root, out var parent, out var children, out _) == 0)
                    break;
                if (children != IntPtr.Zero) XFree(children);
                if (parent == window || window == root) break;
                window = parent;
            }
            throw new InvalidOperationException("X11 could not identify a focused window owned by this Unity player.");
        }

        private void RequirePointerOverPlayer(UIntPtr window)
        {
            if (XGetGeometry(display, window, out var root, out _, out _, out var width, out var height, out _, out _) != 0)
            {
                // root부터 포인터 바로 아래 자식 창을 따라가므로 다른 창이 Unity를 덮은 경우도 거절한다.
                var candidate = root;
                for (var depth = 0; depth < 64 && candidate != UIntPtr.Zero; depth++)
                {
                    if (XQueryPointer(display, candidate, out var pointerRoot, out var child, out _, out _,
                            out var clientX, out var clientY, out _) == 0 || pointerRoot != root)
                        break;
                    if (candidate == window)
                    {
                        if (clientX >= 0 && clientY >= 0 && clientX < width && clientY < height) return;
                        break;
                    }
                    candidate = child;
                }
            }
            throw new InvalidOperationException("The OS pointer must be over the visible game client area before pressing a mouse button.");
        }

        private int? WindowProcessId(UIntPtr window, UIntPtr property)
        {
            var status = XGetWindowProperty(display, window, property, IntPtr.Zero, new IntPtr(1), 0,
                new UIntPtr(6), out _, out var format, out var count, out _, out var value);
            try
            {
                // format이 32여도 Xlib는 native unsigned long 크기로 항목을 저장한다.
                if (status != 0 || format != 32 || count == UIntPtr.Zero || value == IntPtr.Zero) return null;
                return Marshal.ReadInt32(value);
            }
            finally { if (value != IntPtr.Zero) XFree(value); }
        }

        private void Checked(Action operation)
        {
            lock (ErrorHandlerLock)
            {
                checkedDisplay = display;
                checkedError = 0;
                previousErrorHandler = XSetErrorHandler(CheckedErrorHandler);
                try
                {
                    operation();
                    XSync(display, 0);
                    if (checkedError != 0)
                        throw new InvalidOperationException("The X11 server rejected OS input (X error " + checkedError + "). Check the game window and X server permissions.");
                }
                finally
                {
                    // 예외 전에 큐에 들어간 요청의 오류도 기존 handler로 복구하기 전에 읽는다.
                    XSync(display, 0);
                    RestoreErrorHandler(previousErrorHandler);
                    checkedDisplay = IntPtr.Zero;
                    previousErrorHandler = IntPtr.Zero;
                    GC.KeepAlive(CheckedErrorHandler);
                }
            }
        }

        [AOT.MonoPInvokeCallback(typeof(XErrorHandler))]
        private static int HandleXError(IntPtr errorDisplay, ref XErrorEvent error)
        {
            if (errorDisplay == checkedDisplay)
            {
                checkedError = error.ErrorCode;
                return 0;
            }
            return previousErrorHandler == IntPtr.Zero ? 0 :
                Marshal.GetDelegateForFunctionPointer<XErrorHandler>(previousErrorHandler)(errorDisplay, ref error);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int XErrorHandler(IntPtr errorDisplay, ref XErrorEvent error);
        [StructLayout(LayoutKind.Sequential)]
        private struct XErrorEvent
        {
            internal int Type;
            internal IntPtr Display;
            internal UIntPtr ResourceId, Serial;
            internal byte ErrorCode, RequestCode, MinorCode;
        }

        [DllImport(X11)] private static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport(X11)] private static extern int XCloseDisplay(IntPtr connection);
        [DllImport(X11)] private static extern int XSync(IntPtr connection, int discard);
        [DllImport(X11)] private static extern IntPtr XSetErrorHandler(XErrorHandler handler);
        [DllImport(X11, EntryPoint = "XSetErrorHandler")] private static extern IntPtr RestoreErrorHandler(IntPtr handler);
        [DllImport(X11)] private static extern int XGetInputFocus(IntPtr connection, out UIntPtr window, out int revertTo);
        [DllImport(X11)] private static extern UIntPtr XInternAtom(IntPtr connection, string name, int onlyIfExists);
        [DllImport(X11)] private static extern int XGetWindowProperty(IntPtr connection, UIntPtr window, UIntPtr property,
            IntPtr offset, IntPtr length, int delete, UIntPtr requestedType, out UIntPtr actualType,
            out int format, out UIntPtr itemCount, out UIntPtr remainingBytes, out IntPtr value);
        [DllImport(X11)] private static extern int XQueryTree(IntPtr connection, UIntPtr window, out UIntPtr root,
            out UIntPtr parent, out IntPtr children, out uint childCount);
        [DllImport(X11)] private static extern int XQueryPointer(IntPtr connection, UIntPtr window, out UIntPtr root,
            out UIntPtr child, out int rootX, out int rootY, out int windowX, out int windowY, out uint mask);
        [DllImport(X11)] private static extern int XGetGeometry(IntPtr connection, UIntPtr window, out UIntPtr root,
            out int x, out int y, out uint width, out uint height, out uint borderWidth, out uint depth);
        [DllImport(X11)] private static extern int XTranslateCoordinates(IntPtr connection, UIntPtr source,
            UIntPtr destination, int sourceX, int sourceY, out int destinationX, out int destinationY, out UIntPtr child);
        [DllImport(X11)] private static extern int XFree(IntPtr pointer);
        [DllImport(X11)] private static extern byte XKeysymToKeycode(IntPtr connection, UIntPtr keySymbol);
        [DllImport(XTest)] private static extern int XTestQueryExtension(IntPtr connection, out int eventBase,
            out int errorBase, out int majorVersion, out int minorVersion);
        [DllImport(XTest)] private static extern int XTestFakeKeyEvent(IntPtr connection, uint keycode, int pressed, UIntPtr delay);
        [DllImport(XTest)] private static extern int XTestFakeButtonEvent(IntPtr connection, uint button, int pressed, UIntPtr delay);
        [DllImport(X11)] private static extern int XScreenCount(IntPtr connection);
        [DllImport(X11)] private static extern UIntPtr XRootWindow(IntPtr connection, int screen);
        [DllImport(XTest)] private static extern int XTestFakeMotionEvent(IntPtr connection, int screen, int x, int y, UIntPtr delay);
        [DllImport("libc")] private static extern int getpid();
    }
}
#endif
