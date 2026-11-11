using System;
using global::UnityEngine;

namespace Artel.NativeInput
{
    internal interface IOsInputBackend : IDisposable
    {
        void Validate(bool requireFocus = false);
        void SetKey(KeyCode key, bool down);
        void SetMouseButton(int button, bool down);
        void MoveMouse(Vector2 gameScreenPosition);
    }

    internal static class OsInputBackendFactory
    {
        internal static IOsInputBackend Create()
        {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            return new WindowsOsInputBackend();
#elif UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
            return new MacOsInputBackend();
#elif UNITY_EDITOR_LINUX || (UNITY_STANDALONE_LINUX && !UNITY_EDITOR)
            return new LinuxOsInputBackend();
#else
            throw new PlatformNotSupportedException("OS input requires a Windows, macOS, or Linux desktop player.");
#endif
        }
    }

    internal static class OsInputCoordinates
    {
        internal static void Validate(Vector2 position)
        {
            if (float.IsNaN(position.x) || float.IsNaN(position.y) ||
                float.IsInfinity(position.x) || float.IsInfinity(position.y) ||
                Screen.width <= 0 || Screen.height <= 0 || position.x < 0 || position.y < 0 ||
                position.x >= Screen.width || position.y >= Screen.height)
                throw new InvalidOperationException("OS pointer coordinates must be inside the game screen.");
            if (Cursor.lockState == CursorLockMode.Locked)
                throw new InvalidOperationException("Absolute OS pointer movement is unavailable while the game locks the cursor.");
            RequirePlayerPointer();
        }

        internal static void RequirePlayerPointer()
        {
            if (Application.isEditor)
                throw new PlatformNotSupportedException("OS pointer input requires a standalone player; the Editor Game view bounds are not available.");
        }

        internal static void RequireFocus()
        {
            if (!Application.isFocused)
                throw new InvalidOperationException("OS input requires the game window to be focused. Focus the game or start the run in virtual input mode.");
        }

        internal static void ValidateButton(int button)
        {
            if (button < 0 || button > 2)
                throw new InvalidOperationException("OS mouse button must be 0 (left), 1 (right), or 2 (middle).");
        }
    }
}
