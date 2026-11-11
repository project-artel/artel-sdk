#if ENABLE_INPUT_SYSTEM
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Artel.Protocol.Dto;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Users;
using UnityEngine.TestTools;

namespace Artel.Tests
{
    /// <summary>
    /// QA 키 명령이 Input System 의 가상 Keyboard 로 게임에 닿는지(ARTEL-1412). 구조는 <see cref="NewInputMouseTests"/> 와 같다.
    /// </summary>
    public sealed class NewInputKeyboardTests
    {
        private GameObject host;
        private GameObject[] pausedManagers;
        private Keyboard physicalKeyboard;
        private ActionExecutor executor;
        private InputSettings.UpdateMode previousUpdateMode;
        private InputSettings.BackgroundBehavior previousBackgroundBehavior;
        private float previousFixedDeltaTime;
        private readonly List<InputUser> users = new List<InputUser>();

        [SetUp]
        public void SetUp()
        {
            pausedManagers = UnityEngine.Object.FindObjectsOfType<ArtelManager>(true)
                .Select(manager => manager.gameObject).Where(target => target.activeSelf).ToArray();
            foreach (var target in pausedManagers) target.SetActive(false);
            ArtelInput.ResetVirtualKeyboard();
            InputReadManifest.ClearForTests();
            previousUpdateMode = InputSystem.settings.updateMode;
            previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            previousFixedDeltaTime = Time.fixedDeltaTime;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            physicalKeyboard = InputSystem.AddDevice<Keyboard>("Test physical keyboard");
            host = new GameObject("QA keyboard test", typeof(CursorController));
            executor = new ActionExecutor(null, host.GetComponent<CursorController>(), new PointerEventDispatcher());
        }

        [TearDown]
        public void TearDown()
        {
            ArtelInput.ResetVirtualKeyboard();
            InputReadManifest.ClearForTests();
            foreach (var user in users) if (user.valid) user.UnpairDevicesAndRemoveUser();
            users.Clear();
            if (physicalKeyboard != null && physicalKeyboard.added) InputSystem.RemoveDevice(physicalKeyboard);
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            InputSystem.settings.updateMode = previousUpdateMode;
            InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = previousFixedDeltaTime;
            foreach (var target in pausedManagers) if (target != null) target.SetActive(true);
        }

        [UnityTest]
        public IEnumerator KeyDownAndUp_ReachKeyboardCurrent()
        {
            yield return Run("key_down", "Space");
            Assert.That(Keyboard.current, Is.Not.SameAs(physicalKeyboard), "QA 키를 쥔 동안 Keyboard.current 는 가상 장치다.");
            Assert.That(Keyboard.current.spaceKey.isPressed, Is.True);
            Assert.That(physicalKeyboard.spaceKey.isPressed, Is.False);

            yield return Run("key_up", "Space");
            Assert.That(InputSystemKeyboardBackend.VirtualDevice.spaceKey.isPressed, Is.False);
        }

        [UnityTest]
        public IEnumerator KeyClick_PerformsAnActionOnce()
        {
            using (var jump = new InputAction(type: InputActionType.Button, binding: "<Keyboard>/space"))
            {
                var presses = 0;
                jump.performed += _ => presses++;
                jump.Enable();
                yield return Run("key_click", "Space", 0.05);
                yield return WaitFrames(2);
                Assert.That(presses, Is.EqualTo(1));
                Assert.That(jump.IsPressed(), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator RenamedKeyCodes_ReachTheirInputSystemKeys()
        {
            using (var enter = new InputAction(type: InputActionType.Button, binding: "<Keyboard>/enter"))
            using (var digit = new InputAction(type: InputActionType.Button, binding: "<Keyboard>/1"))
            {
                enter.Enable();
                digit.Enable();
                yield return Run("key_down", "Return");
                yield return Run("key_down", "Alpha1");
                Assert.That(enter.IsPressed(), Is.True);
                Assert.That(digit.IsPressed(), Is.True);
                yield return Run("key_up", "Return");
                yield return Run("key_up", "Alpha1");
            }
        }

        /// <summary>
        /// FixedUpdate 가 없는 frame 이 끼어도 짧은 누름이 한 번은 실린다. 누름이 처리된 뒤에 시간을 재기 때문이다.
        /// </summary>
        [UnityTest]
        public IEnumerator ShortClick_UnderFixedUpdate_IsSeenOnce()
        {
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInFixedUpdate;
            Time.fixedDeltaTime = 0.5f;
            using (var jump = new InputAction(type: InputActionType.Button, binding: "<Keyboard>/space"))
            {
                var presses = 0;
                jump.performed += _ => presses++;
                jump.Enable();
                yield return Run("key_click", "Space", 0.01);
                Assert.That(presses, Is.EqualTo(1));
            }
        }

        [UnityTest]
        public IEnumerator KeyboardUser_IsPairedWithTheVirtualKeyboard_AndUnpairedOnRelease()
        {
            var user = InputUser.PerformPairingWithDevice(physicalKeyboard);
            users.Add(user);
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            try
            {
                var map = actions.AddActionMap("game");
                var undo = map.AddAction("undo", InputActionType.Button, "<Keyboard>/u");
                user.AssociateActionsWithUser(actions);
                actions.Enable();
                yield return Run("key_down", "U");
                var virtualKeyboard = InputSystemKeyboardBackend.VirtualDevice;
                Assert.That(user.pairedDevices.Contains(virtualKeyboard), Is.True);
                Assert.That(undo.IsPressed(), Is.True);

                ArtelInput.ReleaseAllVirtualInput();
                yield return WaitFrames(4);
                Assert.That(undo.IsPressed(), Is.False);
                Assert.That(NewInputKeyboard.IsActive, Is.False);
                Assert.That(user.pairedDevices.Contains(physicalKeyboard), Is.True);
                Assert.That(user.pairedDevices.Count, Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(actions); }
        }

        /// <summary>마우스와 키보드가 설정 복제본을 함께 쓴다. 둘 다 놓은 뒤에만 원래대로 돌아온다.</summary>
        [UnityTest]
        public IEnumerator SettingsAreRestored_OnlyAfterBothMouseAndKeyboardLetGo()
        {
            var originalSettings = InputSystem.settings;
            var originalRunInBackground = Application.runInBackground;
            var physicalMouse = InputSystem.AddDevice<Mouse>("Test physical mouse");
            try
            {
                yield return Run("key_down", "A");
                ArtelInput.MoveMouse(new Vector2(50, 50));
                yield return WaitFrames(2);
                Assert.That(InputSystem.settings, Is.Not.SameAs(originalSettings));

                NewInputKeyboard.Dispose();
                Assert.That(InputSystem.settings, Is.Not.SameAs(originalSettings), "마우스가 아직 쓰고 있다.");

                ArtelInput.ReleaseAllVirtualInput();
                yield return WaitFrames(4);
                Assert.That(NewInputMouse.IsActive, Is.False);
                Assert.That(InputSystem.settings, Is.SameAs(originalSettings));
                Assert.That(Application.runInBackground, Is.EqualTo(originalRunInBackground));
            }
            finally
            {
                if (physicalMouse.added) InputSystem.RemoveDevice(physicalMouse);
            }
        }

        /// <summary>제어권 회수 키는 사람의 키보드에서만 읽는다.</summary>
        [UnityTest]
        public IEnumerator TakeoverKey_IsReadFromThePhysicalKeyboardOnly()
        {
            yield return Run("key_down", "LeftControl");
            yield return Run("key_down", "LeftShift");
            yield return Run("key_down", "M");
            Assert.That(NewInputMouse.TakeoverPressed, Is.False, "에이전트가 누른 키는 회수가 아니다.");
            yield return Run("key_up", "LeftControl");
            yield return Run("key_up", "LeftShift");
            yield return Run("key_up", "M");

            // 회수 키는 QA 가 입력을 쥔 동안 읽는다. 그동안은 QA 설정 복제본이 editor 의 game view focus 와 상관없이 실제 장치
            // 이벤트를 받게 한다. 가상 키보드는 여전히 활성이다.
            Assert.That(NewInputKeyboard.IsActive, Is.True);
            InputSystem.QueueStateEvent(physicalKeyboard, new KeyboardState(Key.LeftCtrl, Key.LeftShift));
            yield return null;
            InputSystem.QueueStateEvent(physicalKeyboard, new KeyboardState(Key.LeftCtrl, Key.LeftShift, Key.M));
            // 이 frame 의 dynamic input update 가 이벤트를 처리한 뒤, 같은 frame 에서 읽는다.
            yield return null;
            Assert.That(physicalKeyboard.mKey.wasPressedThisFrame, Is.True, "전제: 실제 키보드의 M 이 이 frame 에 눌렸다.");
            Assert.That(NewInputMouse.TakeoverPressed, Is.True);
        }

        /// <summary>New 전용에서는 넣을 수 없는 키를 실패로 알린다. Both 는 legacy 경로가 받으므로 성공이다.</summary>
        [UnityTest]
        public IEnumerator AKeyWithNoInputSystemKey_FailsOnlyWithoutTheLegacyPath()
        {
            if (LegacyInput.IsAvailable)
            {
                yield return Run("key_click", "Exclaim", 0.01);
            }
            else
            {
                var error = string.Empty;
                yield return Failure("key_click", value => error = value, "Exclaim", 0.01);
                Assert.That(error, Does.Contain("Exclaim"));
            }
        }

        [UnityTest]
        public IEnumerator ManualUpdates_FailOnlyWithoutTheLegacyPath()
        {
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            if (LegacyInput.IsAvailable)
            {
                Assert.That(NewInputKeyboard.ValidationError(KeyCode.Space), Is.Null);
            }
            else
            {
                var error = string.Empty;
                yield return Failure("key_down", value => error = value, "Space");
                Assert.That(error, Does.Contain("automatic Input System updates"));
            }
            yield return null;
        }

        /// <summary>
        /// 일시 정지한 Fixed Update 에서는 Input System update 가 돌지 않는다. Both 는 legacy proxy 로 보내 성공하고, New 전용은
        /// 실패로 알린다. 예전처럼 2초를 기다린 뒤 쥔 입력을 모두 놓으면 안 된다.
        /// </summary>
        [UnityTest]
        public IEnumerator PausedFixedUpdate_FallsBackToLegacyOnlyInBoth()
        {
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInFixedUpdate;
            Time.timeScale = 0f;
            if (LegacyInput.IsAvailable)
            {
                var started = Time.realtimeSinceStartup;
                yield return Run("key_down", "Space");
                yield return Run("key_click", "A", 0.01);
                Assert.That(Time.realtimeSinceStartup - started, Is.LessThan(1f), "Input System update 를 기다리지 않는다.");
                yield return null;
                Assert.That(ArtelInput.GetKey(KeyCode.Space), Is.True, "legacy proxy 가 키를 쥐고 있다.");
                yield return Run("key_up", "Space");
            }
            else
            {
                var error = string.Empty;
                yield return Failure("key_down", value => error = value, "Space");
                Assert.That(error, Does.Contain("Fixed Update"));
            }
        }

        /// <summary>놓기는 언제나 받는다. 쥔 키를 놓지 못하게 하면 게임에 키가 눌린 채 남는다.</summary>
        [UnityTest]
        public IEnumerator KeyUp_IsNeverRefused()
        {
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            ActionResultDto result = null;
            yield return executor.Execute(1, "key_up", new List<object> { "Exclaim" }, value => result = value);
            Assert.That(result.IsSuccess, Is.True, result.Error);
        }

        /// <summary>`list_inputs` 가 지금 켜진 action 의 키보드 키를 KeyCode 이름으로 답한다(ARTEL-1431).</summary>
        [UnityTest]
        public IEnumerator ListInputs_NamesTheKeysOfEnabledActions()
        {
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            try
            {
                var map = actions.AddActionMap("Player");
                var undo = map.AddAction("Undo", InputActionType.Button);
                undo.AddBinding("<Keyboard>/u");
                undo.AddBinding("<Gamepad>/buttonWest");
                var confirm = map.AddAction("Confirm", InputActionType.Button);
                confirm.AddBinding("<Keyboard>/enter");
                confirm.AddBinding("<Keyboard>/1");
                var move = map.AddAction("Move");
                move.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                    .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
                var sprint = map.AddAction("Sprint", InputActionType.Button);
                sprint.AddBinding("<Keyboard>/shift");
                map.AddAction("Disabled").AddBinding("<Keyboard>/space");
                undo.Enable();
                confirm.Enable();
                move.Enable();
                sprint.Enable();

                ActionResultDto result = null;
                yield return executor.Execute(1, "list_inputs", new List<object>(), value => result = value);

                Assert.That(result.IsSuccess, Is.True, result.Error);
                var inputs = (Artel.Protocol.Dto.InputListDto)result.ReturnValue;
                var byName = inputs.Actions.ToDictionary(action => action.Name, action => action.Keys);
                Assert.That(byName["Player/Undo"], Is.EqualTo(new[] { "U" }));
                Assert.That(byName["Player/Confirm"], Is.EqualTo(new[] { "Return", "Alpha1" }));
                Assert.That(byName["Player/Move"], Is.EqualTo(new[] { "W", "S", "A", "D" }));
                Assert.That(byName["Player/Sprint"], Is.EqualTo(new[] { "LeftShift" }), "양쪽 Shift 를 묶은 control 은 왼쪽 키로 읽는다.");
                Assert.That(byName.ContainsKey("Player/Disabled"), Is.False, "꺼진 action 은 지금 누를 수 없다.");
                Assert.That(inputs.Actions.Select(action => action.Name), Is.Ordered.Using((System.Collections.Generic.IComparer<string>)System.StringComparer.Ordinal));
            }
            finally
            {
                actions.Disable();
                UnityEngine.Object.DestroyImmediate(actions);
            }
        }

        /// <summary>키보드 장치가 없을 때(CI, headless)는 경로 끝 이름으로 읽는다.</summary>
        [Test]
        public void KeyCodeNameOfPath_ReadsTheControlNameWithoutADevice()
        {
            Assert.That(KeyCodeToKey.TryKeyCodeNameOfPath("<Keyboard>/upArrow", null, out var up), Is.True);
            Assert.That(up, Is.EqualTo("UpArrow"));
            Assert.That(KeyCodeToKey.TryKeyCodeNameOfPath("<Keyboard>/1", null, out var digit), Is.True);
            Assert.That(digit, Is.EqualTo("Alpha1"));
            Assert.That(KeyCodeToKey.TryKeyCodeNameOfPath("<Keyboard>/enter", null, out var enter), Is.True);
            Assert.That(enter, Is.EqualTo("Return"));
            Assert.That(KeyCodeToKey.TryKeyCodeNameOfPath("<Keyboard>/shift", null, out var shift), Is.True);
            Assert.That(shift, Is.EqualTo("LeftShift"));
            Assert.That(KeyCodeToKey.TryKeyCodeNameOfPath("<Keyboard>/ctrl", null, out var ctrl), Is.True);
            Assert.That(ctrl, Is.EqualTo("LeftControl"));
            Assert.That(KeyCodeToKey.TryKeyCodeNameOfPath("<Gamepad>/buttonSouth", null, out _), Is.False);
            Assert.That(KeyCodeToKey.TryKeyCodeNameOfPath("<Keyboard>/anyKey", null, out _), Is.False);
        }

        [Test]
        public void KeyCodeToKey_MapsRenamedKeysAndRefusesNonKeys()
        {
            Assert.That(KeyCodeToKey.TryConvert(KeyCode.Return, out var enter), Is.True);
            Assert.That(enter, Is.EqualTo(Key.Enter));
            Assert.That(KeyCodeToKey.TryConvert(KeyCode.Alpha1, out var digit), Is.True);
            Assert.That(digit, Is.EqualTo(Key.Digit1));
            Assert.That(KeyCodeToKey.TryConvert(KeyCode.Keypad5, out var numpad), Is.True);
            Assert.That(numpad, Is.EqualTo(Key.Numpad5));
            Assert.That(KeyCodeToKey.TryConvert(KeyCode.LeftControl, out var control), Is.True);
            Assert.That(control, Is.EqualTo(Key.LeftCtrl));
            Assert.That(KeyCodeToKey.TryConvert(KeyCode.UpArrow, out var up), Is.True);
            Assert.That(up, Is.EqualTo(Key.UpArrow));
            Assert.That(KeyCodeToKey.TryConvert(KeyCode.W, out var w), Is.True);
            Assert.That(w, Is.EqualTo(Key.W));
            Assert.That(KeyCodeToKey.TryConvert(KeyCode.LeftCommand, out var command), Is.True);
            Assert.That(command, Is.EqualTo(Key.LeftMeta));
            Assert.That(KeyCodeToKey.TryConvert(KeyCode.AltGr, out var altGr), Is.True);
            Assert.That(altGr, Is.EqualTo(Key.RightAlt));
            Assert.That(KeyCodeToKey.TryConvert(KeyCode.Numlock, out var numLock), Is.True);
            Assert.That(numLock, Is.EqualTo(Key.NumLock));

            Assert.That(KeyCodeToKey.TryConvert(KeyCode.None, out _), Is.False);
            Assert.That(KeyCodeToKey.TryConvert(KeyCode.Mouse0, out _), Is.False);
            Assert.That(KeyCodeToKey.TryConvert(KeyCode.Exclaim, out _), Is.False);
            Assert.That(KeyCodeToKey.TryConvert(KeyCode.JoystickButton0, out _), Is.False);
        }

        /// <summary>대응된 값은 모두 실제 Key 다. 미대응 목록은 고정하지 않는다(패키지 버전마다 Key 가 늘어난다).</summary>
        [Test]
        public void KeyCodeToKey_OnlyProducesDefinedKeys()
        {
            foreach (KeyCode keyCode in Enum.GetValues(typeof(KeyCode)))
            {
                if (KeyCodeToKey.TryConvert(keyCode, out var key))
                    Assert.That(Enum.IsDefined(typeof(Key), key) && key != Key.None, Is.True, keyCode.ToString());
            }
        }

        private IEnumerator Run(string method, params object[] arguments)
        {
            ActionResultDto result = null;
            yield return executor.Execute(1, method, new List<object>(arguments), value => result = value);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.IsSuccess, Is.True, result.Error);
        }

        private IEnumerator Failure(string method, Action<string> error, params object[] arguments)
        {
            ActionResultDto result = null;
            yield return executor.Execute(1, method, new List<object>(arguments), value => result = value);
            Assert.That(result.IsSuccess, Is.False);
            error(result.Error);
        }

        private static IEnumerator WaitFrames(int count)
        {
            for (var frame = 0; frame < count; frame++)
            {
                yield return null;
                ArtelInput.AdvanceFrame();
            }
        }
    }
}
#endif
