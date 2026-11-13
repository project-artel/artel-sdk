using UnityEngine;
using UnityEngine.InputSystem;

namespace Artel
{
    // QA 가상 장치가 있는 동안 쓰는 InputSettings 복제본. 마우스와 키보드 backend 가 함께 쓰고, 마지막 holder 가 놓을 때만
    // 원래 설정과 runInBackground 를 되돌린다.
    internal static class QaInputSettings
    {
        private static int holders;
        private static InputSettings hostSettings;
        private static InputSettings qaSettings;
        private static bool hostRunInBackground;

        internal static void Acquire()
        {
            if (holders++ > 0) return;
            hostSettings = InputSystem.settings;
            qaSettings = Object.Instantiate(hostSettings);
            qaSettings.hideFlags = HideFlags.HideAndDontSave;
            // 창이 focus 를 잃어도 가상 장치를 끄지 않는다. 여러 게임을 나란히 돌리면 focus 는 하나에만 있다.
            qaSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            qaSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            hostRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            InputSystem.settings = qaSettings;
        }

        internal static void Release()
        {
            if (holders == 0) return;
            if (--holders > 0) return;
            if (qaSettings == null) return;
            if (InputSystem.settings == qaSettings) InputSystem.settings = hostSettings;
            DestroyOwnedObject(qaSettings);
            qaSettings = null;
            hostSettings = null;
            if (!hostRunInBackground) Application.runInBackground = false;
        }

        internal static void DestroyOwnedObject(Object target)
        {
            // Play Mode 종료 뒤에도 남은 HideAndDontSave 객체는 즉시 정리한다.
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
