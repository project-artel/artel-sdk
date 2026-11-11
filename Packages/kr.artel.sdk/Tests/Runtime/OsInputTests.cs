using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Artel.NativeInput;
using Artel.Protocol.Dto;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Artel.Tests
{
    public sealed class OsInputTests
    {
        private FakeBackend backend;
        private ActionExecutor executor;

        [SetUp]
        public void SetUp()
        {
            ArtelInput.ResetVirtualKeyboard();
            backend = new FakeBackend();
            executor = new ActionExecutor(null, null, new PointerEventDispatcher(),
                osInput: new OsInputSession(() => backend));
        }

        [TearDown]
        public void TearDown()
        {
            backend.FailRelease = false;
            executor.ReleaseOsInput();
            ArtelInput.ReleaseAllVirtualInput();
        }

        [Test]
        public void 기존_ACTION은_virtual을_기본값으로_사용()
        {
            var request = JsonConvert.DeserializeObject<ArtelRequestDto>("{\"type\":\"ACTION\",\"actions\":[]}");
            Assert.That(request.InputMode, Is.EqualTo("virtual"));
            Assert.That(Run("key_down", new List<object> { "A" }).IsSuccess, Is.True);
            Assert.That(VirtualKeyOnNextFrame(KeyCode.A), Is.True);
            Assert.That(backend.Events, Is.Empty);
        }

        [TestCase("auto")]
        [TestCase("")]
        [TestCase("OS")]
        public void 알_수_없는_모드는_입력을_보내지_않고_실패(string mode)
        {
            Assert.That(Run("key_down", new List<object> { "A" }, mode).IsSuccess, Is.False);
            Assert.That(backend.Events, Is.Empty);
        }

        [Test]
        public void preflight는_권한과_포커스를_검사하고_키를_누르지_않음()
        {
            var parameters = JsonConvert.DeserializeObject<List<object>>("[{\"mode\":\"os\"}]");
            Assert.That(Run("configure_input", parameters).IsSuccess, Is.True);
            Assert.That(backend.Validations, Is.EqualTo(1));
            Assert.That(backend.RequiredFocus, Is.True);
            Assert.That(backend.Events, Is.Empty);
            Run("key_down", new List<object> { "A" });
            Assert.That(VirtualKeyOnNextFrame(KeyCode.A), Is.True);
        }

        [Test]
        public void 권한_오류는_가상_입력으로_전환하지_않고_실패()
        {
            backend.ValidationError = "permission denied";
            var result = Run("key_down", new List<object> { "A" }, "os");
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("permission denied"));
            Assert.That(VirtualKeyOnNextFrame(KeyCode.A), Is.False);
            Assert.That(backend.Events, Is.Empty);
        }

        [Test]
        public void OS_키와_마우스는_native_backend에만_전달()
        {
            Assert.That(Run("key_down", new List<object> { "A" }, "os").IsSuccess, Is.True);
            Assert.That(VirtualKeyOnNextFrame(KeyCode.A), Is.False);
            Run("key_up", new List<object> { "A" }, "os");
            Run("key_down", new List<object> { "Mouse0" }, "os");
            Run("mouse_up", new List<object>(), "os");
            Assert.That(backend.Events, Is.EqualTo(new[] { "A:True", "A:False", "mouse0:True", "mouse0:False" }));
        }

        [Test]
        public void OS_마우스_이동은_기존_게임_화면_좌표를_전달()
        {
            var result = Run("move_mouse", new List<object> { 120L, 80L }, "os");
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(backend.Position, Is.EqualTo(new Vector2(120, 80)));
            Assert.That(ArtelInput.HasVirtualMousePosition, Is.False);
        }

        [TestCase("set_axis")]
        [TestCase("set_button")]
        [TestCase("button_click")]
        [TestCase("enter_text")]
        public void OS_모드는_Unity_직접_조작을_거절(string method)
        {
            var result = Run(method, new List<object>(), "os");
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("not available in OS input mode"));
            Assert.That(backend.Events, Is.Empty);
        }

        [Test]
        public void 키_click은_native_down과_up을_모두_전달()
        {
            var result = Run("key_click", new List<object> { "Space", 0.01f }, "os");
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(backend.Events, Is.EqualTo(new[] { "Space:True", "Space:False" }));
        }

        [Test]
        public void 입력_실패시_기존_hold도_해제()
        {
            Run("key_down", new List<object> { "A" }, "os");
            backend.MoveError = true;
            Assert.That(Run("move_mouse", new List<object> { 120L, 80L }, "os").IsSuccess, Is.False);
            Assert.That(backend.Events, Does.Contain("A:False"));
            Assert.That(backend.Disposed, Is.True);
        }

        [Test]
        public void 모드_변경시_OS_hold_해제후_가상_입력_사용()
        {
            Run("key_down", new List<object> { "A" }, "os");
            Run("key_down", new List<object> { "B" });
            Assert.That(backend.Events, Is.EqualTo(new[] { "A:True", "A:False" }));
            Assert.That(VirtualKeyOnNextFrame(KeyCode.B), Is.True);
        }

        [Test]
        public void mode가_없는_비입력_요청은_OS_hold를_유지()
        {
            Run("key_down", new List<object> { "A" }, "os");
            Run("resume_time", new List<object>());
            var parameters = JsonConvert.DeserializeObject<List<object>>("[{\"mode\":\"os\"}]");
            Assert.That(Run("configure_input", parameters).IsSuccess, Is.True);
            Assert.That(backend.Events, Is.EqualTo(new[] { "A:True" }));
        }

        [Test]
        public void 해제로_중단된_key_click은_실패()
        {
            ActionResultDto result = null;
            var action = executor.Execute(1, "key_click", new List<object> { "Space", 1f },
                value => result = value, inputMode: "os");
            Assert.That(action.MoveNext(), Is.True);
            var click = (IEnumerator)action.Current;
            Assert.That(click.MoveNext(), Is.True);
            Assert.That(backend.Events, Is.EqualTo(new[] { "Space:True" }));
            Assert.That(executor.ReleaseOsInput(), Is.Null);
            Drain(click);
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("interrupted"));
            Assert.That(backend.Events, Is.EqualTo(new[] { "Space:True", "Space:False" }));
        }

        [Test]
        public void 해제_실패도_다른_hold를_해제하고_다음_해제에서_재시도()
        {
            Run("key_down", new List<object> { "A" }, "os");
            Run("mouse_down", new List<object>(), "os");
            backend.FailRelease = true;
            Assert.That(executor.ReleaseOsInput(), Does.Contain("release denied"));
            Assert.That(backend.Events, Does.Contain("mouse0:False"));
            backend.FailRelease = false;
            Assert.That(executor.ReleaseOsInput(), Is.Null);
            Assert.That(backend.Disposed, Is.True);
        }

        [TestCase("ReleaseAgentInput", null)]
        [TestCase("OnApplicationFocus", false)]
        public void manager_입력_정리와_포커스_해제에서_OS_hold_해제(string method, bool? focused)
        {
            Run("key_down", new List<object> { "A" }, "os");
            var host = new GameObject("inactive OS input test");
            host.SetActive(false);
            try
            {
                var manager = host.AddComponent<ArtelManager>();
                typeof(ArtelManager).GetField("actionExecutor", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(manager, executor);
                typeof(ArtelManager).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(manager, focused.HasValue ? new object[] { focused.Value } : null);
                Assert.That(backend.Events, Does.Contain("A:False"));
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void 누르지_않은_키의_up은_사람의_키를_해제하지_않음()
        {
            Assert.That(Run("key_up", new List<object> { "A" }, "os").IsSuccess, Is.True);
            Assert.That(backend.Events, Is.Empty);
        }

        [Test]
        public void 지원하지_않는_키는_hold로_기록하지_않음()
        {
            backend.UnsupportedKey = KeyCode.JoystickButton0;
            Assert.That(Run("key_down", new List<object> { "JoystickButton0" }, "os").IsSuccess, Is.False);
            Assert.That(executor.ReleaseOsInput(), Is.Null);
            Assert.That(backend.Events, Is.Empty);
        }

        private static bool VirtualKeyOnNextFrame(KeyCode key)
        {
            var keyboard = (VirtualKeyboardState)typeof(ArtelInput)
                .GetField("VirtualKeyboard", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            return keyboard.GetKey(key, Time.frameCount + 1, Time.unscaledTime);
        }

        private ActionResultDto Run(string method, List<object> parameters, string mode = "virtual")
        {
            ActionResultDto result = null;
            Drain(executor.Execute(1, method, parameters, value => result = value, inputMode: mode));
            return result;
        }

        private static void Drain(IEnumerator routine)
        {
            while (routine.MoveNext())
                if (routine.Current is IEnumerator nested) Drain(nested);
        }

        private sealed class FakeBackend : IOsInputBackend
        {
            internal readonly List<string> Events = new List<string>();
            internal int Validations;
            internal string ValidationError;
            internal bool MoveError;
            internal bool FailRelease;
            internal bool Disposed;
            internal Vector2 Position;
            internal KeyCode UnsupportedKey;
            internal bool RequiredFocus;
            public void Validate(bool requireFocus = false)
            {
                RequiredFocus = requireFocus;
                Validations++;
                if (ValidationError != null) throw new InvalidOperationException(ValidationError);
            }
            public void SetKey(KeyCode key, bool down)
            {
                if (key == UnsupportedKey) throw new InvalidOperationException("unsupported key");
                Events.Add(key + ":" + down);
                if (!down && FailRelease) throw new InvalidOperationException("release denied");
            }
            public void SetMouseButton(int button, bool down) => Events.Add("mouse" + button + ":" + down);
            public void MoveMouse(Vector2 gameScreenPosition)
            {
                if (MoveError) throw new InvalidOperationException("window unavailable");
                Position = gameScreenPosition;
            }
            public void Dispose() => Disposed = true;
        }
    }
}
