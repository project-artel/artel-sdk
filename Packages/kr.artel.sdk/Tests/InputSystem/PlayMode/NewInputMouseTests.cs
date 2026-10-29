#if ENABLE_INPUT_SYSTEM
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Artel.Protocol.Dto;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.Users;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Artel.Tests
{
    public sealed class NewInputMouseTests
    {
        private GameObject host;
        private GameObject[] pausedManagers;
        private GameObject eventSystemObject;
        private GameObject canvasObject;
        private Mouse physicalMouse;
        private ActionExecutor executor;
        private InputSettings.UpdateMode previousUpdateMode;
        private InputSettings.BackgroundBehavior previousBackgroundBehavior;
        private float previousFixedDeltaTime;
        private readonly List<InputUser> users = new List<InputUser>();

        [SetUp]
        public void SetUp()
        {
            // 다른 assembly의 bootstrap 검증을 위해 기존 manager는 파괴하지 않는다.
            pausedManagers = Object.FindObjectsOfType<ArtelManager>(true)
                .Select(manager => manager.gameObject).Where(target => target.activeSelf).ToArray();
            foreach (var target in pausedManagers) target.SetActive(false);
            ArtelInput.ResetVirtualKeyboard();
            previousUpdateMode = InputSystem.settings.updateMode;
            previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            previousFixedDeltaTime = Time.fixedDeltaTime;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            physicalMouse = InputSystem.AddDevice<Mouse>("Test physical mouse");
            host = new GameObject("QA mouse test", typeof(CursorController));
            executor = new ActionExecutor(null, host.GetComponent<CursorController>(), new PointerEventDispatcher());
            ArtelInput.PointerHeldForRun = true;
        }

        [TearDown]
        public void TearDown()
        {
            ArtelInput.ResetVirtualKeyboard();
            foreach (var user in users) if (user.valid) user.UnpairDevicesAndRemoveUser();
            users.Clear();
            if (physicalMouse != null && physicalMouse.added) InputSystem.RemoveDevice(physicalMouse);
            foreach (var target in new[] { host, eventSystemObject, canvasObject })
                if (target != null) Object.DestroyImmediate(target);
            InputSystem.settings.updateMode = previousUpdateMode;
            InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = previousFixedDeltaTime;
            foreach (var target in pausedManagers) if (target != null) target.SetActive(true);
        }

        [UnityTest]
        public IEnumerator PositionButtonsAndAliases_ReachActionsWithoutDuplicatePresses()
        {
            var position = new Vector2(100, 100);
            yield return Move(position);
            var mouse = Mouse.current;
            Assert.That(mouse, Is.Not.SameAs(physicalMouse));
            Assert.That(mouse.position.ReadValue(), Is.EqualTo(position));
            using (var click = new InputAction(type: InputActionType.Button, binding: "<Mouse>/leftButton"))
            {
                var presses = 0;
                click.performed += _ => presses++;
                click.Enable();
                yield return Run("mouse_down", 0);
                yield return Run("key_down", "Mouse0");
                Assert.That(presses, Is.EqualTo(1));
                yield return Run("key_up", "Mouse0");
                Assert.That(mouse.leftButton.isPressed, Is.False);
                yield return Run("key_click", "Mouse0", 0.01);
                Assert.That(presses, Is.EqualTo(2));
                Assert.That(mouse.leftButton.isPressed, Is.False);
            }
            yield return Run("key_down", "Mouse1");
            yield return Run("key_down", "Mouse2");
            yield return Move(new Vector2(150, 120));
            Assert.That(mouse.rightButton.isPressed && mouse.middleButton.isPressed, Is.True);
            yield return Run("mouse_up", 1);
            yield return Run("mouse_up", 2);
            Assert.That(mouse.rightButton.isPressed || mouse.middleButton.isPressed, Is.False);
        }

        [UnityTest]
        public IEnumerator NativeUi_ClickAndDragAreDeliveredOnce()
        {
            var source = BuildUi();
            yield return null;
            yield return Move(new Vector2(100, 100));
            ActionResultDto press = null;
            yield return executor.Execute(1, "mouse_down", new List<object>(), value => press = value);
            Assert.That(press.IsSuccess, Is.True, press.Error);
            var outcome = (Dictionary<string, object>)press.ReturnValue;
            Assert.That(outcome["reached"], Is.Null);
            Assert.That(outcome["pointerHeldByPerson"], Is.False);
            yield return Run("mouse_up");
            Assert.That(source.Events.Count(value => value == "down"), Is.EqualTo(1));
            Assert.That(source.Events.Count(value => value == "click"), Is.EqualTo(1));
            source.Events.Clear();
            yield return Run("mouse_down");
            yield return Move(new Vector2(250, 150));
            ArtelInput.ReleaseAllVirtualInput();
            yield return WaitFrames(4);
            Assert.That(source.Events.Count(value => value == "beginDrag"), Is.EqualTo(1));
            Assert.That(source.Events.Count(value => value == "endDrag"), Is.EqualTo(1));
            Assert.That(source.Events, Does.Not.Contain("click"));
            Assert.That(NewInputMouse.IsActive, Is.False);
        }

        [UnityTest]
        public IEnumerator Movement_PreservesHeldButtonsAndReportsDelta()
        {
            yield return Move(new Vector2(100, 100));
            yield return Run("mouse_down");
            var deltas = new List<Vector2>();
            using (var motion = new InputAction(type: InputActionType.PassThrough, binding: "<Mouse>/delta"))
            {
                motion.performed += context => deltas.Add(context.ReadValue<Vector2>());
                motion.Enable();
                ArtelInput.MoveMouse(new Vector2(130, 120));
                yield return WaitFrames(2);
                Assert.That(deltas, Does.Contain(new Vector2(30, 20)));
                Assert.That(Mouse.current.leftButton.isPressed, Is.True);
                Assert.That(ArtelInput.GetMouseButton(0), Is.True);
            }
        }

        [UnityTest]
        public IEnumerator PhysicalEvents_DoNotStealRunButCanReclaimOutsideRun()
        {
            yield return Move(new Vector2(100, 100));
            var virtualMouse = Mouse.current;
            InputSystem.QueueStateEvent(physicalMouse, new MouseState { position = new Vector2(400, 400) }.WithButton(MouseButton.Left));
            yield return WaitFrames(2);
            Assert.That(Mouse.current, Is.SameAs(virtualMouse));
            Assert.That(virtualMouse.position.ReadValue(), Is.EqualTo(new Vector2(100, 100)));
            Assert.That(physicalMouse.leftButton.isPressed, Is.False);
            ArtelInput.PointerHeldForRun = false;
            yield return WaitFrames(4);
            Assert.That(NewInputMouse.IsActive, Is.False);
            Assert.That(ArtelInput.HasVirtualMousePosition, Is.False);
        }

        [UnityTest]
        public IEnumerator ReleaseAndImmediateRestart_KeepNewInputAlive()
        {
            var originalSettings = InputSystem.settings;
            var originalRunInBackground = Application.runInBackground;
            yield return Move(new Vector2(100, 100));
            yield return Run("mouse_down");
            ArtelInput.ReleaseAllVirtualInput();
            ArtelInput.ReleaseAllVirtualInput();
            ArtelInput.MoveMouse(new Vector2(200, 200));
            yield return WaitFrames(3);
            Assert.That(NewInputMouse.IsActive, Is.True);
            Assert.That(Mouse.current.leftButton.isPressed, Is.False);
            Assert.That(Mouse.current.position.ReadValue(), Is.EqualTo(new Vector2(200, 200)));
            ArtelInput.ReleaseAllVirtualInput();
            yield return WaitFrames(4);
            Assert.That(NewInputMouse.IsActive, Is.False);
            Assert.That(InputSystem.settings, Is.SameAs(originalSettings));
            Assert.That(Application.runInBackground, Is.EqualTo(originalRunInBackground));
        }

        [UnityTest]
        public IEnumerator SingleMouseUser_ReceivesAndReleasesVirtualPairing()
        {
            var user = InputUser.PerformPairingWithDevice(physicalMouse);
            users.Add(user);
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            try
            {
                var map = actions.AddActionMap("game");
                var click = map.AddAction("click", InputActionType.Button, "<Mouse>/leftButton");
                user.AssociateActionsWithUser(actions);
                actions.Enable();
                yield return Move(new Vector2(100, 100));
                var virtualMouse = Mouse.current;
                Assert.That(user.pairedDevices.Contains(virtualMouse), Is.True);
                yield return Run("mouse_down");
                Assert.That(click.IsPressed(), Is.True);
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInFixedUpdate;
                Time.timeScale = 0f;
                ArtelInput.ReleaseAllVirtualInput();
                yield return WaitFrames(4);
                Assert.That(click.IsPressed(), Is.False);
                Assert.That(user.pairedDevices.Contains(virtualMouse), Is.False);
                Assert.That(user.pairedDevices.Contains(physicalMouse), Is.True);
            }
            finally { Object.DestroyImmediate(actions); }
        }

        [UnityTest]
        public IEnumerator ShortClick_WaitsForFixedInputAndUiBeforeRelease()
        {
            var source = BuildUi();
            yield return null;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInFixedUpdate;
            Time.fixedDeltaTime = 0.5f;
            yield return Move(new Vector2(100, 100));
            using (var click = new InputAction(type: InputActionType.Button, binding: "<Mouse>/leftButton"))
            {
                var presses = 0;
                click.performed += _ => presses++;
                click.Enable();
                yield return Run("key_click", "Mouse0", 0.01);
                Assert.That(presses, Is.EqualTo(1));
                Assert.That(source.Events.Count(value => value == "click"), Is.EqualTo(1));
            }
        }

        [UnityTest]
        public IEnumerator MultipleMouseUsers_AreRejectedWithoutChangingPairing()
        {
            users.Add(InputUser.PerformPairingWithDevice(physicalMouse));
            users.Add(InputUser.PerformPairingWithDevice(physicalMouse));
            yield return ExpectFailure("move_mouse", 100, 100);
            Assert.That(NewInputMouse.IsActive, Is.False);
            Assert.That(users[0].pairedDevices.Count, Is.EqualTo(1));
            Assert.That(users[1].pairedDevices.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator FixedUpdate_ProcessesButtonsAndRejectsPausedInput()
        {
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInFixedUpdate;
            yield return Move(new Vector2(100, 100));
            yield return Run("mouse_down");
            Assert.That(Mouse.current.leftButton.isPressed, Is.True);
            yield return Run("mouse_up");
            Assert.That(Mouse.current.leftButton.isPressed, Is.False);
            Time.timeScale = 0f;
            yield return ExpectFailure("mouse_down");
            Time.timeScale = 1f;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            yield return ExpectFailure("move_mouse", 100, 100);
        }

        [UnityTest]
        public IEnumerator PausedFixedUpdate_MouseReleaseClearsExistingHold()
        {
            yield return VerifyPausedRelease("mouse_up");
        }

        [UnityTest]
        public IEnumerator PausedFixedUpdate_MouseKeyReleaseClearsExistingHold()
        {
            yield return VerifyPausedRelease("key_up", "Mouse0");
        }

        private IEnumerator VerifyPausedRelease(string method, params object[] arguments)
        {
            yield return Move(new Vector2(100, 100));
            using (var click = new InputAction(type: InputActionType.Button, binding: "<Mouse>/leftButton"))
            {
                click.Enable();
                yield return Run("mouse_down");
                var mouse = Mouse.current;
                Assert.That(click.IsPressed(), Is.True);
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInFixedUpdate;
                Time.timeScale = 0f;
                yield return ExpectFailure(method, arguments);
                Assert.That(click.IsPressed(), Is.False);
                Assert.That(mouse.leftButton.isPressed, Is.False);
                Assert.That(ArtelInput.GetMouseButton(0), Is.False);
                Time.timeScale = 1f;
                yield return WaitFrames(4);
                Assert.That(click.IsPressed(), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator DynamicUpdate_ProcessesClickWhilePaused()
        {
            Time.timeScale = 0f;
            yield return Move(new Vector2(100, 100));
            yield return Run("mouse_down");
            Assert.That(Mouse.current.leftButton.isPressed, Is.True);
            yield return Run("mouse_up");
            Assert.That(Mouse.current.leftButton.isPressed, Is.False);
        }

        private IEnumerator Move(Vector2 position) => Run("move_mouse", position.x, Screen.height - position.y);

        private IEnumerator Run(string method, params object[] arguments)
        {
            ActionResultDto result = null;
            yield return executor.Execute(1, method, new List<object>(arguments), value => result = value);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.IsSuccess, Is.True, result.Error);
        }

        private IEnumerator ExpectFailure(string method, params object[] arguments)
        {
            ActionResultDto result = null;
            yield return executor.Execute(1, method, new List<object>(arguments), value => result = value);
            Assert.That(result.IsSuccess, Is.False);
        }

        private static IEnumerator WaitFrames(int count)
        {
            for (var frame = 0; frame < count; frame++)
            {
                yield return null;
                ArtelInput.AdvanceFrame();
            }
        }

        private PointerFixtureBehaviour BuildUi()
        {
            eventSystemObject = new GameObject("event system", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystemObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            canvasObject = new GameObject("canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var target = new GameObject("target", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(PointerFixtureBehaviour));
            target.transform.SetParent(canvasObject.transform, false);
            var rect = target.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(80, 80);
            rect.anchoredPosition = new Vector2(100, 100);
            return target.GetComponent<PointerFixtureBehaviour>();
        }
    }
}
#endif
