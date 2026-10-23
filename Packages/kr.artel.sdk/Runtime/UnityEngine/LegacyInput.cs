using global::UnityEngine;

namespace Artel
{
    /// <summary>
    /// 구 <c>UnityEngine.Input</c> 을 읽는 SDK 의 유일한 자리(ARTEL-1233).
    /// </summary>
    /// <remarks>
    /// Player Settings 의 Active Input Handling 이 "Input System Package (New)" 하나뿐이면 Unity 는 구 <c>Input</c> 의 모든
    /// 호출에 <c>InvalidOperationException</c> 을 던진다. SDK 는 매 frame 마우스 위치를 읽으므로 그런 프로젝트에서는 매
    /// frame 예외가 났다(2022.3.34f1 에서 확인). 그때 Unity 는 <c>ENABLE_LEGACY_INPUT_MANAGER</c> 를 정의하지 않으므로, 그
    /// 정의가 없으면 여기서 아무것도 누르지 않은 값(false, 0, <c>Vector3.zero</c>)을 돌려준다.
    ///
    /// 그런 프로젝트의 게임 코드는 구 <c>Input</c> 을 부르지 않으므로 SDK 가 넣는 가상 입력도 게임에 닿지 않는다. 그래서
    /// 지원하지 않는다고 overlay 에 알린다(<see cref="IsAvailable"/>).
    /// </remarks>
    internal static class LegacyInput
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        public static bool IsAvailable => true;

        public static Vector3 mousePosition => Input.mousePosition;
        public static bool anyKey => Input.anyKey;
        public static bool anyKeyDown => Input.anyKeyDown;
        public static bool GetKey(KeyCode key) => Input.GetKey(key);
        public static bool GetKey(string name) => Input.GetKey(name);
        public static bool GetKeyDown(KeyCode key) => Input.GetKeyDown(key);
        public static bool GetKeyDown(string name) => Input.GetKeyDown(name);
        public static bool GetKeyUp(KeyCode key) => Input.GetKeyUp(key);
        public static bool GetKeyUp(string name) => Input.GetKeyUp(name);
        public static bool GetMouseButton(int button) => Input.GetMouseButton(button);
        public static bool GetMouseButtonDown(int button) => Input.GetMouseButtonDown(button);
        public static bool GetMouseButtonUp(int button) => Input.GetMouseButtonUp(button);
        public static float GetAxis(string axisName) => Input.GetAxis(axisName);
        public static float GetAxisRaw(string axisName) => Input.GetAxisRaw(axisName);
        public static bool GetButton(string axisName) => Input.GetButton(axisName);
        public static bool GetButtonDown(string axisName) => Input.GetButtonDown(axisName);
        public static bool GetButtonUp(string axisName) => Input.GetButtonUp(axisName);
#else
        public static bool IsAvailable => false;

        public static Vector3 mousePosition => Vector3.zero;
        public static bool anyKey => false;
        public static bool anyKeyDown => false;
        public static bool GetKey(KeyCode key) => false;
        public static bool GetKey(string name) => false;
        public static bool GetKeyDown(KeyCode key) => false;
        public static bool GetKeyDown(string name) => false;
        public static bool GetKeyUp(KeyCode key) => false;
        public static bool GetKeyUp(string name) => false;
        public static bool GetMouseButton(int button) => false;
        public static bool GetMouseButtonDown(int button) => false;
        public static bool GetMouseButtonUp(int button) => false;
        public static float GetAxis(string axisName) => 0f;
        public static float GetAxisRaw(string axisName) => 0f;
        public static bool GetButton(string axisName) => false;
        public static bool GetButtonDown(string axisName) => false;
        public static bool GetButtonUp(string axisName) => false;
#endif
    }
}
