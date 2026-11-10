using System;
using global::UnityEngine;

namespace Artel
{
    public static partial class ArtelInput
    {
        private static readonly VirtualKeyboardState VirtualKeyboard = new VirtualKeyboardState();
        private static readonly VirtualMouseState VirtualMouse = new VirtualMouseState();
        private static readonly VirtualAxisState VirtualAxes = new VirtualAxisState();
        private static readonly VirtualMouseMessenger MouseMessenger = new VirtualMouseMessenger();

        /// <summary>
        /// 키로 물어도 마우스 버튼은 마우스 상태가 답한다. <c>KeyCode.Mouse0</c> 은 왼쪽 버튼
        /// 그 자체이므로, 가상 키보드만 보면 <c>mouse_down</c> 으로 들어온 클릭을 폴링하는 게임이
        /// 보지 못한다. 실제 입력과 마찬가지로 둘을 OR 로 합친다 — 물리 클릭은 엔진이 이미 양쪽으로
        /// 답하고, bool 의 OR 은 같은 사실을 두 번 세지 않는다.
        /// </summary>
        public static bool GetKeyDown(KeyCode key)
        {
            return LegacyInput.GetKeyDown(key) ||
                   VirtualKeyboard.GetKeyDown(key, Time.frameCount, Time.unscaledTime) ||
                   MouseButtonKeyCode.TryGetButton(key, out var button) &&
                   VirtualMouse.GetButtonDown(button, Time.frameCount);
        }

        /// <inheritdoc cref="GetKeyDown(KeyCode)"/>
        public static bool GetKeyDown(string name)
        {
            return LegacyInput.GetKeyDown(name) ||
                   TryParseKeyCode(name, out var key) && GetKeyDown(key);
        }

        /// <inheritdoc cref="GetKeyDown(KeyCode)"/>
        public static bool GetKey(KeyCode key)
        {
            return LegacyInput.GetKey(key) ||
                   VirtualKeyboard.GetKey(key, Time.frameCount, Time.unscaledTime) ||
                   MouseButtonKeyCode.TryGetButton(key, out var button) &&
                   VirtualMouse.GetButton(button, Time.frameCount);
        }

        /// <inheritdoc cref="GetKeyDown(KeyCode)"/>
        public static bool GetKey(string name)
        {
            return LegacyInput.GetKey(name) ||
                   TryParseKeyCode(name, out var key) && GetKey(key);
        }

        /// <inheritdoc cref="GetKeyDown(KeyCode)"/>
        public static bool GetKeyUp(KeyCode key)
        {
            return LegacyInput.GetKeyUp(key) ||
                   VirtualKeyboard.GetKeyUp(key, Time.frameCount, Time.unscaledTime) ||
                   MouseButtonKeyCode.TryGetButton(key, out var button) &&
                   VirtualMouse.GetButtonUp(button, Time.frameCount);
        }

        /// <inheritdoc cref="GetKeyDown(KeyCode)"/>
        public static bool GetKeyUp(string name)
        {
            return LegacyInput.GetKeyUp(name) ||
                   TryParseKeyCode(name, out var key) && GetKeyUp(key);
        }

        public static bool anyKey
        {
            get
            {
                return LegacyInput.anyKey ||
                       VirtualKeyboard.AnyKey(Time.frameCount, Time.unscaledTime) ||
                       VirtualMouse.IsAnyButtonHeld(Time.frameCount);
            }
        }

        public static bool anyKeyDown
        {
            get
            {
                return LegacyInput.anyKeyDown ||
                       VirtualKeyboard.AnyKeyDown(Time.frameCount, Time.unscaledTime) ||
                       VirtualMouse.IsAnyButtonDown(Time.frameCount);
            }
        }

        /// <summary>
        /// The agent's pointer once it has been moved, and the real one until then. There is no
        /// combining the two the way the key calls do: a position is one value, not a vote.
        /// </summary>
        public static Vector3 mousePosition
        {
            get
            {
                var physical = PhysicalMousePosition;
                if (!VirtualMouse.OwnsPointer(physical))
                {
                    return physical;
                }

                var position = VirtualMouse.Position;
                return new Vector3(position.x, position.y, 0f);
            }
        }

        private static readonly System.Collections.Generic.List<global::UnityEngine.EventSystems.RaycastResult> UiHits =
            new System.Collections.Generic.List<global::UnityEngine.EventSystems.RaycastResult>();

        /// <summary>
        /// <c>EventSystem.IsPointerOverGameObject()</c> 를 에이전트의 포인터로 답한다.
        /// </summary>
        /// <remarks>
        /// 원래 호출은 입력 모듈이 실제 마우스로 만든 포인터를 본다. 그래서 에이전트가 UI 를 누르는 순간 게임이
        /// "UI 위가 아니다"로 읽고 빈 곳을 누른 것처럼 군다 — 캐릭터를 눌러 연 차트를 동사를 끌기 시작하자마자 닫는
        /// 게임이 그랬다. 사람이 하면 실제 마우스가 UI 위에 있으니 멀쩡하다. 에이전트가 포인터를 쥐고 있을 때만 그
        /// 위치로 UI 를 레이캐스트하고, 아니면 원래 호출에 맡긴다.
        /// </remarks>
        public static bool IsPointerOverGameObject(global::UnityEngine.EventSystems.EventSystem eventSystem)
        {
            return IsPointerOverGameObject(eventSystem, global::UnityEngine.EventSystems.PointerInputModule.kMouseLeftId);
        }

        /// <summary>id 를 받는 overload. 마우스 id(음수)만 에이전트가 답하고, 터치 id 는 원래 호출에 맡긴다.</summary>
        public static bool IsPointerOverGameObject(global::UnityEngine.EventSystems.EventSystem eventSystem, int pointerId)
        {
            // null 이면 원래 호출처럼 NullReferenceException 이다. 게임이 겪던 실패를 바꾸지 않는다.
            if (eventSystem == null || pointerId >= 0 || !VirtualMouse.OwnsPointer(PhysicalMousePosition))
            {
                return eventSystem.IsPointerOverGameObject(pointerId);
            }

            var data = new global::UnityEngine.EventSystems.PointerEventData(eventSystem) { position = VirtualMouse.Position };
            UiHits.Clear();
            eventSystem.RaycastAll(data, UiHits);
            return UiHits.Count > 0;
        }

        public static bool GetMouseButton(int button)
        {
            return LegacyInput.GetMouseButton(button) ||
                   VirtualMouse.GetButton(button, Time.frameCount);
        }

        public static bool GetMouseButtonDown(int button)
        {
            return LegacyInput.GetMouseButtonDown(button) ||
                   VirtualMouse.GetButtonDown(button, Time.frameCount);
        }

        public static bool GetMouseButtonUp(int button)
        {
            return LegacyInput.GetMouseButtonUp(button) ||
                   VirtualMouse.GetButtonUp(button, Time.frameCount);
        }

        /// <summary>
        /// A held axis replaces the real one outright instead of combining with it. The key calls
        /// can OR their two answers because a bool has an OR; a float does not, and letting the
        /// larger magnitude win would put a stray real input above what the agent asked for.
        /// </summary>
        public static float GetAxis(string axisName)
        {
            if (VirtualAxes.TryGetValue(axisName, Time.frameCount, out var value))
            {
                return value;
            }

            return LegacyInput.GetAxis(axisName);
        }

        /// <summary>
        /// The held value as given, not snapped to -1/0/1. The agent named the value it wanted, and
        /// rounding 0.5 up to 1 would quietly hand back something it did not ask for. A caller
        /// driving the axis with -1/0/1 reads -1/0/1.
        /// </summary>
        /// <remarks>
        /// Identical to <see cref="GetAxis"/> apart from the real call it falls through to. Both
        /// have to exist because the weaver matches call sites by signature.
        /// </remarks>
        public static float GetAxisRaw(string axisName)
        {
            if (VirtualAxes.TryGetValue(axisName, Time.frameCount, out var value))
            {
                return value;
            }

            return LegacyInput.GetAxisRaw(axisName);
        }

        public static bool GetButton(string axisName)
        {
            return LegacyInput.GetButton(axisName) ||
                   VirtualAxes.GetButton(axisName, Time.frameCount);
        }

        public static bool GetButtonDown(string axisName)
        {
            return LegacyInput.GetButtonDown(axisName) ||
                   VirtualAxes.GetButtonDown(axisName, Time.frameCount);
        }

        public static bool GetButtonUp(string axisName)
        {
            return LegacyInput.GetButtonUp(axisName) ||
                   VirtualAxes.GetButtonUp(axisName, Time.frameCount);
        }

        internal static void SetAxis(string axisName, float value)
        {
            VirtualAxes.Set(axisName, value, Time.frameCount);
        }

        internal static void ReleaseAxis(string axisName)
        {
            VirtualAxes.Release(axisName, Time.frameCount);
        }

        internal static void ClickKey(KeyCode key, float durationSeconds)
        {
            VirtualKeyboard.Click(key, durationSeconds, Time.frameCount);
        }

        internal static void PressKey(KeyCode key)
        {
            VirtualKeyboard.Press(key, Time.frameCount);
        }

        internal static void ReleaseKey(KeyCode key)
        {
            VirtualKeyboard.Release(key, Time.frameCount);
        }

        internal static Vector2 PhysicalMousePosition => NewInputMouse.PhysicalPosition;

        internal static bool OwnsVirtualPointer => VirtualMouse.OwnsPointer(PhysicalMousePosition);

        internal static void ReadVirtualMouse(out Vector2 position, out ushort buttons)
        {
            position = VirtualMouse.Position;
            buttons = 0;
            for (var button = 0; button < VirtualMouseState.ButtonCount; button++)
                if (VirtualMouse.GetButton(button, Time.frameCount)) buttons |= (ushort)(1 << button);
        }

        internal static void MoveMouse(Vector2 screenPosition)
        {
            VirtualMouse.MoveTo(screenPosition, PhysicalMousePosition);
            NewInputMouse.Activate();
        }

        internal static void PressMouseButton(int button)
        {
            if (button == 0 && !VirtualMouse.GetButton(button, Time.frameCount))
                CancelGuiPress();
            VirtualMouse.Press(button, Time.frameCount);
            NewInputMouse.Activate();
        }

        internal static void ReleaseMouseButton(int button)
        {
            VirtualMouse.Release(button, Time.frameCount);
        }

        internal static bool IsMouseButtonHeld(int button)
        {
            return VirtualMouse.GetButton(button, Time.frameCount);
        }

        internal static bool HasVirtualMousePosition
        {
            get { return VirtualMouse.HasPosition; }
        }

        /// <summary>
        /// Whether a QA run keeps the agent's pointer no matter what the real mouse does. Set from
        /// <c>RUN_STATUS</c>; see <c>VirtualMouseState.HeldForRun</c> for why.
        /// </summary>
        internal static bool PointerHeldForRun
        {
            get { return VirtualMouse.HeldForRun; }
            set { VirtualMouse.HeldForRun = value; }
        }

        /// <summary>
        /// Lets go of everything the agent was holding. A run that ends mid-drag would otherwise
        /// leave the game with a key or a button held down for the rest of the session — and with
        /// a pointer position frozen where the agent left it, which is worse: the game keeps
        /// reading it forever and the person at the machine cannot move the mouse anywhere.
        /// </summary>
        internal static void ReleaseAllVirtualInput()
        {
            VirtualKeyboard.ReleaseAll(Time.frameCount);
            VirtualMouse.ReleaseAll(Time.frameCount);
            VirtualMouse.ReleasePointer();
            NewInputMouse.Release();
            VirtualAxes.ReleaseAll(Time.frameCount);
            MouseMessenger.Clear();
            CancelGuiPress();
        }

        /// <summary>
        /// 마지막 누름이 무엇에 닿았나. 닿은 것이 없거나 아직 프레임이 안 지났으면 빈 문자열이다.
        /// </summary>
        internal static string LastPressReceiver => MouseMessenger.LastPressReceiver;

        /// <summary>누름이 메신저를 지나갔나. 안 지나갔으면 사람이 포인터를 도로 가져간 것이다.</summary>
        internal static bool SawPress => MouseMessenger.SawPress;

        /// <summary>다음 누름을 재기 전에 지난 것을 지운다.</summary>
        internal static void ForgetLastPress()
        {
            MouseMessenger.ForgetLastPress();
            LastGuiPressReceiver = null;
        }

        internal static void AdvanceFrame()
        {
            VirtualKeyboard.Refresh(Time.frameCount, Time.unscaledTime);
            VirtualMouse.Refresh(Time.frameCount);
            VirtualAxes.Refresh(Time.frameCount);

            // Only while the agent holds the pointer. The engine keeps sending its own OnMouse*
            // from the real cursor, so driving ours at the same time would deliver everything twice.
            if (VirtualMouse.OwnsPointer(PhysicalMousePosition))
            {
                MouseMessenger.Tick(VirtualMouse.Position, VirtualMouse.GetButton(0, Time.frameCount));
            }
            else
            {
                MouseMessenger.Clear();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetVirtualKeyboard()
        {
            NewInputMouse.Dispose();
            VirtualKeyboard.Clear();
            VirtualMouse.Clear();
            VirtualAxes.Clear();
            MouseMessenger.Clear();
            CancelGuiPress();
            guiRepaintFrame = -1;
        }

        private static bool TryParseKeyCode(string value, out KeyCode key)
        {
            return Enum.TryParse(value, true, out key) && key != KeyCode.None;
        }
    }
}
