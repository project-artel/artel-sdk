using UnityEngine;

namespace Artel
{
    // optional assembly가 등록되지 않으면 기존 Legacy 입력 경로만 사용한다.
    internal static class NewInputMouse
    {
        private static INewInputMouseBackend backend;

        internal static void Register(INewInputMouseBackend replacement)
        {
            backend?.Dispose();
            backend = replacement;
        }

        internal static bool IsActive => backend?.IsActive ?? false;
        internal static bool UsesNativeUi => backend?.UsesNativeUi ?? false;
        internal static Vector2 PhysicalPosition => backend?.PhysicalPosition ?? LegacyInput.mousePosition;
        internal static bool TakeoverPressed => backend?.TakeoverPressed ?? false;
        internal static string ValidationError
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                if (Application.isPlaying && backend == null)
                    return "QA mouse actions require Input System 1.7.0 or newer and the Artel.InputSystem adapter.";
#endif
                return backend?.ValidationError;
            }
        }
        internal static void Activate() => backend?.Activate();
        internal static bool HasProcessed(int frame) => backend?.HasProcessed(frame) ?? true;
        internal static void Release() => backend?.Release();
        // reset과 SubsystemRegistration의 실행 순서가 달라도 등록은 유지한다.
        internal static void Dispose() => backend?.Dispose();
    }

    internal interface INewInputMouseBackend
    {
        bool IsActive { get; }
        bool UsesNativeUi { get; }
        Vector2 PhysicalPosition { get; }
        bool TakeoverPressed { get; }
        string ValidationError { get; }
        void Activate();
        bool HasProcessed(int frame);
        void Release();
        void Dispose();
    }
}
