using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.Users;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace Artel
{
    // 버튼/소유권은 VirtualMouseState가 관리한다. 이 adapter는 input update에서 그 상태를 전달한다.
    internal sealed class InputSystemMouseBackend : INewInputMouseBackend
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register() => NewInputMouse.Register(new InputSystemMouseBackend());

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InstallEditorCleanup()
        {
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) NewInputMouse.Dispose();
        }
#endif

        private Mouse mouse;
        private Vector2 physicalPosition;
        private Vector2 deliveredPosition;
        private bool releasing;
        private int releaseFrame = -1;
        private int processedFrame = -1;
        private bool queued;
        private bool consumed;
        private double queuedTime;
        private bool claimedPosition;
        private NewInputMouseLifetime lifetime;
        private InputSettings hostSettings;
        private InputSettings qaSettings;
        private bool hostRunInBackground;

        public bool IsActive => mouse != null && mouse.added;
        public bool UsesNativeUi => EventSystem.current != null &&
            EventSystem.current.currentInputModule is InputSystemUIInputModule;
        public Vector2 PhysicalPosition => IsActive ? physicalPosition : ReadPhysicalPosition();
        public bool TakeoverPressed => Keyboard.current != null &&
            Keyboard.current.ctrlKey.isPressed && Keyboard.current.shiftKey.isPressed &&
            Keyboard.current.mKey.wasPressedThisFrame;

        public string ValidationError
        {
            get
            {
                if (InputSystem.settings.updateMode == InputSettings.UpdateMode.ProcessEventsManually)
                    return "QA mouse actions require automatic Input System updates.";
                if (InputSystem.settings.updateMode == InputSettings.UpdateMode.ProcessEventsInFixedUpdate && Time.timeScale == 0f)
                    return "QA mouse actions cannot run while Fixed Update input is paused.";
                return MouseUserCount() > 1 ? "QA mouse actions require at most one mouse InputUser." : null;
            }
        }

        public void Activate()
        {
            if (!Application.isPlaying) return;
            if (IsActive)
            {
                releasing = false;
                releaseFrame = -1;
                claimedPosition = ArtelInput.HasVirtualMousePosition;
                return;
            }

            physicalPosition = ReadPhysicalPosition();
            deliveredPosition = ArtelInput.mousePosition;
            hostSettings = InputSystem.settings;
            qaSettings = Object.Instantiate(hostSettings);
            qaSettings.hideFlags = HideFlags.HideAndDontSave;
            qaSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            qaSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            hostRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            InputSystem.settings = qaSettings;
            // 이미 눌린 실제 버튼이 action의 active control로 남지 않게 한다. 위치는 soft reset이 유지한다.
            foreach (var device in InputSystem.devices)
                if (device is Mouse) InputSystem.ResetDevice(device);
            mouse = InputSystem.AddDevice<Mouse>("Artel QA Mouse");
            var lifetimeObject = new GameObject("Artel QA Mouse lifetime");
            lifetimeObject.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(lifetimeObject);
            lifetime = lifetimeObject.AddComponent<NewInputMouseLifetime>();
            lifetime.Backend = this;
            processedFrame = -1;
            releaseFrame = -1;
            releasing = false;
            claimedPosition = ArtelInput.HasVirtualMousePosition;
            InputSystem.onBeforeUpdate += BeforeUpdate;
            InputSystem.onAfterUpdate += AfterUpdate;
            InputSystem.onEvent += OnEvent;
            PairMouseUser();
        }

        public bool HasProcessed(int frame) => IsActive && processedFrame >= frame;

        public void Release()
        {
            if (!IsActive || releasing) return;
            releasing = true;
            // ResetDevice는 input update가 멈춰 있어도 action을 취소하고 UI에 release를 알린다.
            InputSystem.ResetDevice(mouse);
            releaseFrame = Time.frameCount;
        }

        public void FinishRelease()
        {
            if (releasing && Time.frameCount > releaseFrame) Dispose();
        }

        public void LifetimeDestroyed(NewInputMouseLifetime destroyed)
        {
            if (lifetime != destroyed) return;
            // OnDestroy 중인 객체를 Dispose가 다시 파괴하지 않게 한다.
            lifetime = null;
            Dispose();
        }

        public void Dispose()
        {
            InputSystem.onBeforeUpdate -= BeforeUpdate;
            InputSystem.onAfterUpdate -= AfterUpdate;
            InputSystem.onEvent -= OnEvent;
            var removed = mouse;
            var removedLifetime = lifetime;
            lifetime = null;
            mouse = null;
            releasing = false;
            queued = false;
            processedFrame = -1;
            releaseFrame = -1;
            if (removed != null && removed.added)
            {
                InputSystem.ResetDevice(removed);
                InputSystem.RemoveDevice(removed);
            }
            if (removedLifetime != null) DestroyOwnedObject(removedLifetime.gameObject);
            if (qaSettings != null)
            {
                if (InputSystem.settings == qaSettings) InputSystem.settings = hostSettings;
                DestroyOwnedObject(qaSettings);
                qaSettings = null;
                hostSettings = null;
                if (!hostRunInBackground) Application.runInBackground = false;
            }
            foreach (var device in InputSystem.devices)
                if (device is Mouse) InputSystem.TrySyncDevice(device);
        }

        private static void DestroyOwnedObject(Object target)
        {
            // Play Mode 종료 뒤에도 남은 HideAndDontSave 객체는 즉시 정리한다.
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }

        private bool IsGameUpdate => InputState.currentUpdateType == InputUpdateType.Dynamic ||
            InputState.currentUpdateType == InputUpdateType.Fixed || InputState.currentUpdateType == InputUpdateType.Manual;

        private void BeforeUpdate()
        {
            if (!IsGameUpdate) return;
            if (!IsActive) { Dispose(); return; }
            if (claimedPosition && !ArtelInput.OwnsVirtualPointer) Release();
            PairMouseUser();
            ArtelInput.ReadVirtualMouse(out var position, out var buttons);
            if (!claimedPosition) position = physicalPosition;
            if (releasing) { position = deliveredPosition; buttons = 0; }
            var state = new MouseState
            {
                position = position,
                delta = position - deliveredPosition,
                buttons = buttons
            };
            deliveredPosition = position;
            // FixedUpdate는 해당 fixed time보다 늦은 이벤트를 다음 tick까지 보류한다.
            queuedTime = InputState.currentUpdateType == InputUpdateType.Fixed
                ? System.Math.Max(0, Time.fixedUnscaledTime - 0.00001) : InputState.currentTime;
            InputSystem.QueueStateEvent(mouse, state, queuedTime);
            queued = true;
            consumed = false;
        }

        private void AfterUpdate()
        {
            if (!IsGameUpdate || !queued || !consumed || !IsActive) return;
            queued = false;
            // 같은 rendered frame의 여러 FixedUpdate가 press/release를 합치지 않는다.
            processedFrame = Time.frameCount;
            if (releasing && releaseFrame < 0) releaseFrame = Time.frameCount;
            if (!releasing) mouse.MakeCurrent();
        }

        private void OnEvent(InputEventPtr inputEvent, InputDevice device)
        {
            if (!IsGameUpdate) return;
            if (device == mouse)
            {
                consumed = System.Math.Abs(inputEvent.time - queuedTime) < 0.000001;
                return;
            }
            if (!(device is Mouse physicalMouse)) return;
            if (!inputEvent.IsA<StateEvent>() && !inputEvent.IsA<DeltaStateEvent>()) return;
            if (physicalMouse.position.ReadValueFromEvent(inputEvent, out var position)) physicalPosition = position;
            if (!releasing && ArtelInput.OwnsVirtualPointer) inputEvent.handled = true;
        }

        private Vector2 ReadPhysicalPosition()
        {
            if (Mouse.current != null && Mouse.current != mouse) return Mouse.current.position.ReadValue();
            foreach (var device in InputSystem.devices)
                if (device is Mouse physicalMouse && device != mouse) return physicalMouse.position.ReadValue();
            return Vector2.zero;
        }

        private int MouseUserCount()
        {
            var count = 0;
            foreach (var user in InputUser.all)
                foreach (var device in user.pairedDevices)
                    if (device is Mouse && device != mouse) { count++; break; }
            return count;
        }

        private void PairMouseUser()
        {
            if (MouseUserCount() > 1) { Release(); return; }
            foreach (var user in InputUser.all)
            {
                var hasPhysicalMouse = false;
                var hasVirtualMouse = false;
                foreach (var device in user.pairedDevices)
                {
                    if (device == mouse) hasVirtualMouse = true;
                    else if (device is Mouse) hasPhysicalMouse = true;
                }
                if (hasPhysicalMouse && !hasVirtualMouse) InputUser.PerformPairingWithDevice(mouse, user);
                else if (!hasPhysicalMouse && hasVirtualMouse) user.UnpairDevice(mouse);
            }
        }
    }
}
