#if UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using global::UnityEngine;

namespace Artel.NativeInput
{
    internal sealed class MacOsInputBackend : IOsInputBackend
    {
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string ObjectiveC = "/usr/lib/libobjc.A.dylib";
        private readonly HashSet<KeyCode> heldModifiers = new HashSet<KeyCode>();
        private readonly bool[] heldButtons = new bool[3];
        private ulong managedModifierFlags;
        private bool disposed;

        public void Validate(bool requireFocus = false)
        {
            if (disposed) throw new ObjectDisposedException(nameof(MacOsInputBackend));
            if (Application.isBatchMode)
                throw new InvalidOperationException("OS input requires an interactive macOS desktop; Unity is running in batchmode.");
            if (!AXIsProcessTrusted())
                throw new InvalidOperationException("macOS OS input requires Accessibility permission for this Unity player or Editor in System Settings. Grant permission and retry.");
            if (requireFocus) FocusedWindow();
        }

        public void SetKey(KeyCode key, bool down)
        {
            Validate();
            var nativeKey = OsKeyCodes.Mac(key);
            if (down) FocusedWindow();
            var modifierFlag = ModifierFlag(key);
            var input = CGEventCreateKeyboardEvent(IntPtr.Zero, nativeKey, down);
            if (input == IntPtr.Zero) throw new InvalidOperationException("macOS could not create the keyboard event.");
            try
            {
                if (modifierFlag != 0)
                {
                    managedModifierFlags |= modifierFlag;
                    if (down) heldModifiers.Add(key);
                    else heldModifiers.Remove(key);
                }
                CGEventSetFlags(input, EventFlags());
                CGEventPost(0, input);
            }
            finally { CFRelease(input); }
        }

        public void SetMouseButton(int button, bool down)
        {
            Validate();
            OsInputCoordinates.ValidateButton(button);
            var point = CurrentPointer();
            if (down)
            {
                OsInputCoordinates.RequirePlayerPointer();
                RequirePointerOverPlayer(FocusedWindow(), point);
            }
            var eventType = button == 0 ? (down ? 1u : 2u) :
                button == 1 ? (down ? 3u : 4u) : (down ? 25u : 26u);
            PostMouse(eventType, point, (uint)button, true);
            heldButtons[button] = down;
        }

        public void MoveMouse(Vector2 gameScreenPosition)
        {
            Validate();
            OsInputCoordinates.Validate(gameScreenPosition);
            var window = FocusedWindow();
            var screenFrame = ContentScreenFrame(window);
            var primaryDisplay = CGDisplayBounds(CGMainDisplayID());
            var point = new NativePoint
            {
                X = screenFrame.Origin.X + gameScreenPosition.x * screenFrame.Size.Width / Screen.width,
                Y = primaryDisplay.Size.Height - screenFrame.Origin.Y - screenFrame.Size.Height +
                    gameScreenPosition.y * screenFrame.Size.Height / Screen.height
            };
            var button = heldButtons[0] ? 0 : heldButtons[1] ? 1 : heldButtons[2] ? 2 : -1;
            var eventType = button == 0 ? 6u : button == 1 ? 7u : button == 2 ? 27u : 5u;
            PostMouse(eventType, point, (uint)Math.Max(0, button), false);
        }

        public void Dispose() { disposed = true; }

        private ulong EventFlags()
        {
            var flags = CGEventSourceFlagsState(0) & ~managedModifierFlags;
            foreach (var key in heldModifiers) flags |= ModifierFlag(key);
            return flags;
        }

        private static ulong ModifierFlag(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftShift: case KeyCode.RightShift: return 1UL << 17;
                case KeyCode.LeftControl: case KeyCode.RightControl: return 1UL << 18;
                case KeyCode.LeftAlt: case KeyCode.RightAlt: case KeyCode.AltGr: return 1UL << 19;
                case KeyCode.LeftCommand: case KeyCode.RightCommand:
                case KeyCode.LeftWindows: case KeyCode.RightWindows: return 1UL << 20;
                default: return 0;
            }
        }

        private static IntPtr FocusedWindow()
        {
            OsInputCoordinates.RequireFocus();
            var application = SendPointer(objc_getClass("NSApplication"), Selector("sharedApplication"));
            if (application == IntPtr.Zero || SendBoolean(application, Selector("isActive")) == 0)
                throw new InvalidOperationException("The active macOS application is not this Unity player. Focus the game and retry.");
            var window = SendPointer(application, Selector("keyWindow"));
            var mainWindow = SendPointer(application, Selector("mainWindow"));
            if (window == IntPtr.Zero || mainWindow == IntPtr.Zero || window != mainWindow)
                throw new InvalidOperationException("The Unity game window is not the key window. Close modal dialogs and focus the game.");
            return window;
        }

        private static NativeRect ContentScreenFrame(IntPtr window)
        {
            var contentView = SendPointer(window, Selector("contentView"));
            if (contentView == IntPtr.Zero)
                throw new InvalidOperationException("The Unity player content view is unavailable.");
            var frame = GetRectangle(contentView, Selector("frame"));
            var screenFrame = ConvertRectangle(window, Selector("convertRectToScreen:"), frame);
            if (screenFrame.Size.Width <= 0 || screenFrame.Size.Height <= 0)
                throw new InvalidOperationException("The Unity player content rectangle is empty.");
            return screenFrame;
        }

        private static void RequirePointerOverPlayer(IntPtr window, NativePoint quartzPoint)
        {
            var frame = ContentScreenFrame(window);
            // AppKit의 화면 좌표는 주 화면 왼쪽 아래가 원점이다.
            var point = new NativePoint
            {
                X = quartzPoint.X,
                Y = CGDisplayBounds(CGMainDisplayID()).Size.Height - quartzPoint.Y
            };
            var windowNumber = SendPointer(window, Selector("windowNumber"));
            var hitWindowNumber = WindowNumberAtPoint(objc_getClass("NSWindow"),
                Selector("windowNumberAtPoint:belowWindowWithWindowNumber:"), point, IntPtr.Zero);
            if (point.X < frame.Origin.X || point.X >= frame.Origin.X + frame.Size.Width ||
                point.Y <= frame.Origin.Y || point.Y > frame.Origin.Y + frame.Size.Height ||
                windowNumber.ToInt64() <= 0 || hitWindowNumber != windowNumber)
                throw new InvalidOperationException("The OS pointer must be over the visible game client area before pressing a mouse button.");
        }

        private void PostMouse(uint eventType, NativePoint position, uint button, bool click)
        {
            var input = CGEventCreateMouseEvent(IntPtr.Zero, eventType, position, button);
            if (input == IntPtr.Zero) throw new InvalidOperationException("macOS could not create the mouse event.");
            try
            {
                CGEventSetFlags(input, EventFlags());
                if (click) CGEventSetIntegerValueField(input, 1, 1);
                CGEventPost(0, input);
            }
            finally { CFRelease(input); }
        }

        private static NativePoint CurrentPointer()
        {
            var input = CGEventCreate(IntPtr.Zero);
            if (input == IntPtr.Zero) throw new InvalidOperationException("macOS could not read the current pointer position.");
            try { return CGEventGetLocation(input); }
            finally { CFRelease(input); }
        }

        private static IntPtr Selector(string name) { return sel_registerName(name); }

        private static NativeRect GetRectangle(IntPtr receiver, IntPtr selector)
        {
            // Intel의 objc_msgSend는 32-byte CGRect를 stret로 반환하고 Apple Silicon은 일반 ABI를 쓴다.
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64) return SendRectangle(receiver, selector);
            SendRectangleStret(out var rectangle, receiver, selector);
            return rectangle;
        }

        private static NativeRect ConvertRectangle(IntPtr receiver, IntPtr selector, NativeRect input)
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64) return SendConvertedRectangle(receiver, selector, input);
            SendConvertedRectangleStret(out var rectangle, receiver, selector, input);
            return rectangle;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { internal double X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize { internal double Width, Height; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { internal NativePoint Origin; internal NativeSize Size; }

        [DllImport(ApplicationServices)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool AXIsProcessTrusted();
        [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, [MarshalAs(UnmanagedType.I1)] bool down);
        [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreateMouseEvent(IntPtr source, uint eventType, NativePoint position, uint button);
        [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreate(IntPtr source);
        [DllImport(CoreGraphics)] private static extern NativePoint CGEventGetLocation(IntPtr input);
        [DllImport(CoreGraphics)] private static extern void CGEventPost(uint location, IntPtr input);
        [DllImport(CoreGraphics)] private static extern void CGEventSetFlags(IntPtr input, ulong flags);
        [DllImport(CoreGraphics)] private static extern ulong CGEventSourceFlagsState(int state);
        [DllImport(CoreGraphics)] private static extern void CGEventSetIntegerValueField(IntPtr input, uint field, long value);
        [DllImport(CoreGraphics)] private static extern uint CGMainDisplayID();
        [DllImport(CoreGraphics)] private static extern NativeRect CGDisplayBounds(uint display);
        [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);
        [DllImport(ObjectiveC)] private static extern IntPtr objc_getClass(string name);
        [DllImport(ObjectiveC)] private static extern IntPtr sel_registerName(string name);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPointer(IntPtr receiver, IntPtr selector);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")] private static extern IntPtr WindowNumberAtPoint(
            IntPtr receiver, IntPtr selector, NativePoint point, IntPtr belowWindowNumber);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")] private static extern byte SendBoolean(IntPtr receiver, IntPtr selector);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")] private static extern NativeRect SendRectangle(IntPtr receiver, IntPtr selector);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend_stret")] private static extern void SendRectangleStret(out NativeRect result, IntPtr receiver, IntPtr selector);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")] private static extern NativeRect SendConvertedRectangle(IntPtr receiver, IntPtr selector, NativeRect rectangle);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend_stret")] private static extern void SendConvertedRectangleStret(out NativeRect result, IntPtr receiver, IntPtr selector, NativeRect rectangle);
    }
}
#endif
