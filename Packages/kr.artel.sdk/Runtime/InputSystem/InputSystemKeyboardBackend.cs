using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Users;

namespace Artel
{
    // 누름/놓음은 VirtualKeyboardState 가 정한다. 이 adapter 는 input update 마다 그 상태를 가상 Keyboard 에 전달한다.
    // 구조는 InputSystemMouseBackend 와 같다.
    internal sealed class InputSystemKeyboardBackend : INewInputKeyboardBackend
    {
        private static InputSystemKeyboardBackend registered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            registered = new InputSystemKeyboardBackend();
            NewInputKeyboard.Register(registered);
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InstallEditorCleanup()
        {
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) NewInputKeyboard.Dispose();
        }
#endif

        /// <summary>QA 가 넣는 장치. 사람의 키보드와 가르는 데 쓴다(제어권 회수 키).</summary>
        internal static Keyboard VirtualDevice => registered?.keyboard;

        private readonly List<KeyCode> heldKeyCodes = new List<KeyCode>();
        private readonly List<Key> heldKeys = new List<Key>();
        private readonly List<Key> deliveredKeys = new List<Key>();
        private Keyboard keyboard;
        private bool releasing;
        private int releaseFrame = -1;
        private int processedFrame = -1;
        private bool queued;
        private bool consumed;
        private bool deliveredAnyKey;
        private double queuedTime;
        private NewInputKeyboardLifetime lifetime;
        private bool holdsSettings;

        public bool IsActive => keyboard != null && keyboard.added;

        public string ValidationError(KeyCode key)
        {
            if (!KeyCodeToKey.TryConvert(key, out _))
                return "KeyCode " + key + " has no key on the Input System keyboard.";
            if (InputSystem.settings.updateMode == InputSettings.UpdateMode.ProcessEventsManually)
                return "QA key actions require automatic Input System updates.";
            if (InputSystem.settings.updateMode == InputSettings.UpdateMode.ProcessEventsInFixedUpdate && Time.timeScale == 0f)
                return "QA key actions cannot run while Fixed Update input is paused.";
            return KeyboardUserCount() > 1 ? "QA key actions require at most one keyboard InputUser." : null;
        }

        public void Activate()
        {
            if (!Application.isPlaying) return;
            if (IsActive)
            {
                releasing = false;
                releaseFrame = -1;
                return;
            }

            QaInputSettings.Acquire();
            holdsSettings = true;
            keyboard = InputSystem.AddDevice<Keyboard>("Artel QA Keyboard");
            var lifetimeObject = new GameObject("Artel QA Keyboard lifetime");
            lifetimeObject.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(lifetimeObject);
            lifetime = lifetimeObject.AddComponent<NewInputKeyboardLifetime>();
            lifetime.Backend = this;
            processedFrame = -1;
            releaseFrame = -1;
            releasing = false;
            deliveredAnyKey = false;
            deliveredKeys.Clear();
            InputSystem.onBeforeUpdate += BeforeUpdate;
            InputSystem.onAfterUpdate += AfterUpdate;
            InputSystem.onEvent += OnEvent;
            PairKeyboardUser();
        }

        public bool HasProcessed(int frame) => IsActive && processedFrame >= frame;

        public void Release()
        {
            if (!IsActive || releasing) return;
            releasing = true;
            // ResetDevice 는 input update 가 멈춰 있어도 action 을 취소하고 UI 에 놓음을 알린다.
            InputSystem.ResetDevice(keyboard);
            deliveredKeys.Clear();
            deliveredAnyKey = false;
            releaseFrame = Time.frameCount;
        }

        public void FinishRelease()
        {
            if (releasing && Time.frameCount > releaseFrame) Dispose();
        }

        public void LifetimeDestroyed(NewInputKeyboardLifetime destroyed)
        {
            if (lifetime != destroyed) return;
            lifetime = null;
            Dispose();
        }

        public void Dispose()
        {
            InputSystem.onBeforeUpdate -= BeforeUpdate;
            InputSystem.onAfterUpdate -= AfterUpdate;
            InputSystem.onEvent -= OnEvent;
            var removed = keyboard;
            var removedLifetime = lifetime;
            lifetime = null;
            keyboard = null;
            releasing = false;
            queued = false;
            deliveredAnyKey = false;
            deliveredKeys.Clear();
            processedFrame = -1;
            releaseFrame = -1;
            if (removed != null && removed.added)
            {
                InputSystem.ResetDevice(removed);
                InputSystem.RemoveDevice(removed);
            }
            if (removedLifetime != null) QaInputSettings.DestroyOwnedObject(removedLifetime.gameObject);
            if (holdsSettings)
            {
                holdsSettings = false;
                QaInputSettings.Release();
            }
        }

        private bool IsGameUpdate => InputState.currentUpdateType == InputUpdateType.Dynamic ||
            InputState.currentUpdateType == InputUpdateType.Fixed || InputState.currentUpdateType == InputUpdateType.Manual;

        private void BeforeUpdate()
        {
            if (!IsGameUpdate) return;
            if (!IsActive) { Dispose(); return; }
            PairKeyboardUser();
            heldKeys.Clear();
            if (!releasing)
            {
                ArtelInput.ReadVirtualKeys(heldKeyCodes);
                foreach (var keyCode in heldKeyCodes)
                    if (KeyCodeToKey.TryConvert(keyCode, out var key)) heldKeys.Add(key);
            }
            // 상태가 바뀔 때만 넣는다. 같은 상태를 매번 넣으면 Input System 이 이 장치를 계속 current 로 만들어, 키를 놓은 뒤에도
            // Keyboard.current 를 읽는 게임이 사람의 키보드를 못 본다.
            // 앞에 넣은 이벤트가 아직 처리되지 않았으면(FixedUpdate 보류) queued 를 그대로 둔다. AfterUpdate 가 처리를 확인한 뒤 내린다.
            if (SameKeys(heldKeys, deliveredKeys)) return;
            // FixedUpdate 는 해당 fixed time 보다 늦은 이벤트를 다음 tick 까지 보류한다.
            queuedTime = InputState.currentUpdateType == InputUpdateType.Fixed
                ? System.Math.Max(0, Time.fixedUnscaledTime - 0.00001) : InputState.currentTime;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(heldKeys.ToArray()), queuedTime);
            deliveredKeys.Clear();
            deliveredKeys.AddRange(heldKeys);
            deliveredAnyKey = heldKeys.Count > 0;
            queued = true;
            consumed = false;
        }

        private static bool SameKeys(List<Key> left, List<Key> right)
        {
            if (left.Count != right.Count) return false;
            foreach (var key in left)
                if (!right.Contains(key)) return false;
            return true;
        }

        private void AfterUpdate()
        {
            if (!IsGameUpdate || !IsActive) return;
            // 넣은 상태가 아직 처리되지 않았으면(FixedUpdate 가 보류) 기다린다. 넣을 것이 없었던 update 는 이미 최신이다.
            if (queued && !consumed) return;
            queued = false;
            // 같은 rendered frame 의 여러 FixedUpdate 가 누름/놓음을 합치지 않는다.
            processedFrame = Time.frameCount;
            if (releasing && releaseFrame < 0) releaseFrame = Time.frameCount;
            // Keyboard.current 를 직접 읽는 게임은 가상 장치가 current 여야 키를 본다. 키를 쥔 동안만 current 로 둔다.
            if (!releasing && deliveredAnyKey) keyboard.MakeCurrent();
        }

        private void OnEvent(InputEventPtr inputEvent, InputDevice device)
        {
            if (!IsGameUpdate || device != keyboard) return;
            consumed = System.Math.Abs(inputEvent.time - queuedTime) < 0.000001;
        }

        private int KeyboardUserCount()
        {
            var count = 0;
            foreach (var user in InputUser.all)
                foreach (var device in user.pairedDevices)
                    if (device is Keyboard && device != keyboard) { count++; break; }
            return count;
        }

        private void PairKeyboardUser()
        {
            if (KeyboardUserCount() > 1) { Release(); return; }
            foreach (var user in InputUser.all)
            {
                var hasPhysicalKeyboard = false;
                var hasVirtualKeyboard = false;
                foreach (var device in user.pairedDevices)
                {
                    if (device == keyboard) hasVirtualKeyboard = true;
                    else if (device is Keyboard) hasPhysicalKeyboard = true;
                }
                if (hasPhysicalKeyboard && !hasVirtualKeyboard) InputUser.PerformPairingWithDevice(keyboard, user);
                else if (!hasPhysicalKeyboard && hasVirtualKeyboard) user.UnpairDevice(keyboard);
            }
        }
    }
}
