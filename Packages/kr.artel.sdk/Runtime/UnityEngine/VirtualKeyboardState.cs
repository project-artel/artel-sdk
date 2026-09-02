using System.Collections.Generic;
using global::UnityEngine;

namespace Artel
{
    internal sealed class VirtualKeyboardState
    {
        private readonly Dictionary<KeyCode, KeyPressState> presses =
            new Dictionary<KeyCode, KeyPressState>();

        public void Click(KeyCode key, float durationSeconds, int currentFrame)
        {
            presses[key] = new KeyPressState(currentFrame + 1, durationSeconds);
        }

        /// <summary>
        /// Holds a key until <see cref="Release"/> asks for it back. A duration cannot express this:
        /// the agent decides when to let go, often several actions later.
        /// </summary>
        public void Press(KeyCode key, int currentFrame)
        {
            presses[key] = new KeyPressState(currentFrame + 1, null);
        }

        /// <summary>
        /// Releases on the frame after the request, matching where a press starts, so a consumer
        /// polling in its own Update sees the release regardless of script execution order.
        /// </summary>
        public void Release(KeyCode key, int currentFrame)
        {
            if (presses.TryGetValue(key, out var state))
            {
                Release(state, currentFrame);
            }
        }

        public void ReleaseAll(int currentFrame)
        {
            foreach (var state in presses.Values)
            {
                Release(state, currentFrame);
            }
        }

        public bool GetKeyDown(KeyCode key, int frame, float time)
        {
            return TryGetState(key, frame, time, out var state) &&
                   state.StartFrame == frame &&
                   IsHeldOn(state, frame);
        }

        public bool GetKey(KeyCode key, int frame, float time)
        {
            return TryGetState(key, frame, time, out var state) &&
                   state.StartTime.HasValue &&
                   IsHeldOn(state, frame);
        }

        public bool GetKeyUp(KeyCode key, int frame, float time)
        {
            return TryGetState(key, frame, time, out var state) &&
                   state.ReleaseFrame == frame;
        }

        public bool AnyKey(int frame, float time)
        {
            Refresh(frame, time);
            foreach (var state in presses.Values)
            {
                if (state.StartTime.HasValue && IsHeldOn(state, frame))
                {
                    return true;
                }
            }

            return false;
        }

        public bool AnyKeyDown(int frame, float time)
        {
            Refresh(frame, time);
            foreach (var state in presses.Values)
            {
                if (state.StartFrame == frame && IsHeldOn(state, frame))
                {
                    return true;
                }
            }

            return false;
        }

        public void Refresh(int frame, float time)
        {
            var expiredKeys = new List<KeyCode>();
            foreach (var pair in presses)
            {
                Refresh(pair.Value, frame, time);
                if (pair.Value.ReleaseFrame.HasValue && pair.Value.ReleaseFrame.Value < frame)
                {
                    expiredKeys.Add(pair.Key);
                }
            }

            foreach (var key in expiredKeys)
            {
                presses.Remove(key);
            }
        }

        public void Clear()
        {
            presses.Clear();
        }

        /// <summary>
        /// 놓기는 누름보다 최소 한 프레임 뒤다. <c>VirtualMouseState.Release</c> 와 같은 규칙이고,
        /// 이유도 같다 — 같은 프레임에 들어온 누름과 놓기가 눌린 프레임을 0개로 만들면 프레임마다
        /// 폴링하는 쪽이 그 누름을 통째로 못 본다.
        /// </summary>
        /// <remarks>
        /// 마우스에서 실제로 그 일이 났다(ARTEL-766). 키보드는 <c>key_click</c> 이 누른 채
        /// 시간을 보내고 <c>key_down</c>/<c>key_up</c> 은 별개 메시지로 와서 지금 이 구멍을 밟는
        /// 경로가 없지만, 두 곳의 규칙이 달라야 할 이유도 없다.
        /// </remarks>
        private static void Release(KeyPressState state, int currentFrame)
        {
            if (state.ReleaseFrame.HasValue)
            {
                return;
            }

            var earliest = state.StartFrame + 1;
            var asked = currentFrame + 1;
            state.ReleaseFrame = asked > earliest ? asked : earliest;
        }

        private bool TryGetState(KeyCode key, int frame, float time, out KeyPressState state)
        {
            if (!presses.TryGetValue(key, out state))
            {
                return false;
            }

            Refresh(state, frame, time);
            return true;
        }

        /// <summary>
        /// A release scheduled for a later frame leaves the key down until that frame arrives. An
        /// expired duration schedules the release on the current frame, so the same test reports
        /// that key as already up.
        /// </summary>
        private static bool IsHeldOn(KeyPressState state, int frame)
        {
            return !state.ReleaseFrame.HasValue || frame < state.ReleaseFrame.Value;
        }

        private static void Refresh(KeyPressState state, int frame, float time)
        {
            if (frame < state.StartFrame || state.ReleaseFrame.HasValue)
            {
                return;
            }

            if (!state.StartTime.HasValue)
            {
                state.StartTime = time;
            }

            if (!state.DurationSeconds.HasValue)
            {
                return;
            }

            if (time >= state.StartTime.Value + state.DurationSeconds.Value)
            {
                state.ReleaseFrame = frame;
            }
        }

        private sealed class KeyPressState
        {
            public KeyPressState(int startFrame, float? durationSeconds)
            {
                StartFrame = startFrame;
                DurationSeconds = durationSeconds;
            }

            public int StartFrame { get; }

            /// <summary>Null for a hold, which only an explicit release ends.</summary>
            public float? DurationSeconds { get; }

            public float? StartTime { get; set; }
            public int? ReleaseFrame { get; set; }
        }
    }
}
