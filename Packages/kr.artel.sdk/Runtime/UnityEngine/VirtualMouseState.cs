using global::UnityEngine;

namespace Artel
{
    /// <summary>
    /// Where the agent's pointer is and which of its buttons are down. Unlike a key press, a button
    /// never expires on its own — a drag lasts as long as the agent needs, so only an explicit
    /// release ends it.
    /// </summary>
    internal sealed class VirtualMouseState
    {
        public const int ButtonCount = 3;

        private readonly ButtonPressState[] buttons = new ButtonPressState[ButtonCount];

        /// <summary>
        /// How far the real mouse may drift before the person is taken to have grabbed it back.
        /// A few pixels, because a resting mouse still reports jitter.
        /// </summary>
        private const float ReclaimPixels = 4f;

        private Vector2 physicalWhenClaimed;

        /// <summary>
        /// False until the agent moves the pointer for the first time, which is what lets the proxy
        /// keep reporting the real pointer in a session nobody is driving.
        /// </summary>
        public bool HasPosition { get; private set; }

        public Vector2 Position { get; private set; }

        public static bool IsButton(int button)
        {
            return button >= 0 && button < ButtonCount;
        }

        public void MoveTo(Vector2 screenPosition, Vector2 physicalPosition)
        {
            Position = screenPosition;
            physicalWhenClaimed = physicalPosition;
            HasPosition = true;
        }

        /// <summary>
        /// Whether the agent's pointer is the one to report. It stops being so the moment the real
        /// mouse moves: a person reaching for it means they want the game back, and a claim that
        /// outlives them leaves the game reading a cursor nobody is driving.
        /// </summary>
        /// <remarks>
        /// This gives the claim up as a side effect, because the read is the only moment the two
        /// positions can be compared.
        /// </remarks>
        public bool OwnsPointer(Vector2 physicalPosition)
        {
            if (!HasPosition)
            {
                return false;
            }

            if ((physicalPosition - physicalWhenClaimed).sqrMagnitude > ReclaimPixels * ReclaimPixels)
            {
                ReleasePointer();
                return false;
            }

            return true;
        }

        /// <summary>Hands the pointer back without disturbing the buttons, which have their own frame rules.</summary>
        public void ReleasePointer()
        {
            HasPosition = false;
        }

        public void Press(int button, int currentFrame)
        {
            if (!IsButton(button))
            {
                return;
            }

            // 이미 눌린 채인 버튼을 다시 누르면 StartFrame 이 새로 찍혀 GetButtonDown 이 한 번 더
            // 참이 된다. mouse_down 과 KeyCode.Mouse0 을 실은 key_down 이 같은 버튼을 가리키므로
            // 둘이 겹쳐 들어올 수 있고, 그때 폴링하는 게임이 클릭을 두 번으로 세면 안 된다.
            // 놓기를 예약해 둔 버튼은 다시 누를 수 있다 — 그것은 이미 끝난 누름의 다음 누름이다.
            var held = buttons[button];
            if (held != null && !held.ReleaseFrame.HasValue)
            {
                return;
            }

            // The frame after the request, matching the virtual keyboard: the action is handled in
            // the manager's Update, and a consumer polling in its own Update must not miss it
            // because of script execution order.
            buttons[button] = new ButtonPressState(currentFrame + 1);
        }

        public void Release(int button, int currentFrame)
        {
            if (!IsButton(button))
            {
                return;
            }

            Release(buttons[button], currentFrame);
        }

        public void ReleaseAll(int currentFrame)
        {
            foreach (var state in buttons)
            {
                Release(state, currentFrame);
            }
        }

        public bool GetButtonDown(int button, int frame)
        {
            var state = StateOf(button);
            return state != null && state.StartFrame == frame && IsHeldOn(state, frame);
        }

        public bool GetButton(int button, int frame)
        {
            var state = StateOf(button);
            return state != null && frame >= state.StartFrame && IsHeldOn(state, frame);
        }

        public bool GetButtonUp(int button, int frame)
        {
            var state = StateOf(button);
            return state != null && state.ReleaseFrame == frame;
        }

        public bool IsAnyButtonHeld(int frame)
        {
            for (var button = 0; button < ButtonCount; button++)
            {
                if (GetButton(button, frame))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Unity 의 <c>Input.anyKeyDown</c> 은 마우스 버튼도 센다. 가상 쪽만 그러지 않으면 에이전트가
        /// 누른 버튼이 <c>anyKeyDown</c> 으로 넘길 화면에서만 조용해진다.
        /// </summary>
        public bool IsAnyButtonDown(int frame)
        {
            for (var button = 0; button < ButtonCount; button++)
            {
                if (GetButtonDown(button, frame))
                {
                    return true;
                }
            }

            return false;
        }

        public void Refresh(int frame)
        {
            for (var button = 0; button < ButtonCount; button++)
            {
                var state = buttons[button];
                if (state != null && state.ReleaseFrame.HasValue && state.ReleaseFrame.Value < frame)
                {
                    buttons[button] = null;
                }
            }
        }

        public void Clear()
        {
            for (var button = 0; button < ButtonCount; button++)
            {
                buttons[button] = null;
            }

            Position = Vector2.zero;
            HasPosition = false;
        }

        /// <summary>
        /// 놓기는 **누름보다 최소 한 프레임 뒤**다.
        /// </summary>
        /// <remarks>
        /// 누름은 요청한 프레임의 다음에 시작한다(<see cref="Press"/>). 놓기가 같은 규칙만
        /// 따르면, 한 프레임 안에 들어온 누름과 놓기가 같은 프레임을 가리켜 <c>GetButton</c> 의
        /// <c>frame &lt; ReleaseFrame</c> 이 어디서도 참이 되지 않는다 — 눌린 프레임이 0개가 되고,
        /// 프레임마다 폴링하는 쪽은 그 누름을 통째로 못 본다.
        /// <para>
        /// 실제로 <c>click_at</c> 이 그랬다. <c>mouse_down</c> 과 <c>mouse_up</c> 이 프레임을
        /// 양보하지 않아 같은 프레임에 처리되고, <c>VirtualMouseMessenger</c> 가 매 프레임 받는
        /// <c>buttonHeld</c> 가 한 번도 참이 아니어서 <c>OnMouseDown</c> 이 아예 안 나갔다.
        /// 좌표는 맞았고 액션은 <c>ok</c> 를 돌려주었으므로 실패가 조용했다(ARTEL-766).
        /// </para>
        /// <para>
        /// 눌린 채 프레임이 지나간 뒤의 놓기는 이 하한에 걸리지 않는다 — 드래그는 그대로다.
        /// </para>
        /// </remarks>
        private static void Release(ButtonPressState state, int currentFrame)
        {
            if (state == null || state.ReleaseFrame.HasValue)
            {
                return;
            }

            var earliest = state.StartFrame + 1;
            var asked = currentFrame + 1;
            state.ReleaseFrame = asked > earliest ? asked : earliest;
        }

        private static bool IsHeldOn(ButtonPressState state, int frame)
        {
            return !state.ReleaseFrame.HasValue || frame < state.ReleaseFrame.Value;
        }

        private ButtonPressState StateOf(int button)
        {
            return IsButton(button) ? buttons[button] : null;
        }

        private sealed class ButtonPressState
        {
            public ButtonPressState(int startFrame)
            {
                StartFrame = startFrame;
            }

            public int StartFrame { get; }
            public int? ReleaseFrame { get; set; }
        }
    }
}
