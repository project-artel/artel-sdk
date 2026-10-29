using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Artel.Auth;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Artel.Tests
{
    public sealed class ConnectionRecoveryTests
    {
        private GameObject host;
        private ArtelManager manager;
        private ArtelManager displacedInstance;
        private RecoveringTransport transport;
        private float originalTimeScale;

        [SetUp]
        public void SetUp()
        {
            displacedInstance = ArtelManagerSlot.Clear();
            ArtelSecretStore.Current = new PlayerPrefsSecretStore();
            ArtelSdkSession.Clear();
            originalTimeScale = Time.timeScale;
            host = new GameObject("Artel connection recovery");
            manager = host.AddComponent<ArtelManager>();
            transport = new RecoveringTransport();
            manager.SetWebSocketTransport(transport, false);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
            Time.timeScale = originalTimeScale;
            ArtelManagerSlot.Restore(displacedInstance);
            ArtelSdkSession.Clear();
            ArtelSecretStore.Current = null;
        }

        [UnityTest]
        public IEnumerator 연결이_끊기면_입력과_일시정지를_해제하고_새_요청을_처리한다()
        {
            yield return null;
            transport.Enqueue(1, "pause_time", "[]");
            transport.Enqueue(2, "key_down", "[\"Space\"]");
            yield return WaitForResult(transport, 2);
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(ArtelInput.GetKey(KeyCode.Space), Is.True);

            transport.Connected = false;
            transport.ConnectionEpoch++;
            yield return null;
            yield return null;
            Assert.That(Time.timeScale, Is.EqualTo(originalTimeScale));
            Assert.That(ArtelInput.GetKey(KeyCode.Space), Is.False);

            transport.Connected = true;
            transport.ConnectionEpoch++;
            transport.Enqueue(3, "pause_time", "[]");
            yield return WaitForResult(transport, 3);
            Assert.That(transport.HasResult(3), Is.True);
            Assert.That(Time.timeScale, Is.Zero);
        }

        [UnityTest]
        public IEnumerator 프레임_사이_재연결도_이전_action과_큐를_폐기한다()
        {
            yield return null;
            transport.Enqueue(10, "key_click", "[\"Mouse0\",60]");
            transport.Enqueue(11, "pause_time", "[]");
            yield return null;
            Assert.That(transport.HasResult(10), Is.False);

            // IsConnected는 그대로 true여도 socket 세대가 바뀌면 이전 실행을 취소해야 한다.
            transport.ConnectionEpoch++;
            transport.Enqueue(12, "key_up", "[\"Mouse0\"]");
            yield return WaitForResult(transport, 12);
            yield return null;
            Assert.That(transport.HasResult(10), Is.False);
            Assert.That(transport.HasResult(11), Is.False);
            Assert.That(transport.HasResult(12), Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(originalTimeScale));
            Assert.That(ArtelInput.IsMouseButtonHeld(0), Is.False);
        }

        [UnityTest]
        public IEnumerator action_결과_전송이_실패해도_다음_요청을_처리한다()
        {
            yield return null;
            transport.FailNextResult = true;
            LogAssert.Expect(LogType.Warning, new Regex("Action execution failed: injected send failure"));
            transport.Enqueue(20, "pause_time", "[]");
            for (var frame = 0; frame < 12; frame++) yield return null;
            Assert.That(Time.timeScale, Is.EqualTo(originalTimeScale));

            transport.Enqueue(21, "pause_time", "[]");
            yield return WaitForResult(transport, 21);
            Assert.That(transport.HasResult(21), Is.True);
            Assert.That(Time.timeScale, Is.Zero);
        }

        [UnityTest]
        public IEnumerator 종료가_실패해도_transport를_교체할_수_있다()
        {
            yield return null;
            manager.ClearWebSocketTransport(transport);
            var runInBackground = Application.runInBackground;
            Application.runInBackground = true;
            manager.SetWebSocketTransport(transport, true);
            transport.FailStop = true;
            LogAssert.Expect(LogType.Warning, new Regex("transport stop cleanup failed"));
            Assert.DoesNotThrow(manager.StopTransport);
            Assert.That(transport.Disposed, Is.True);
            Assert.That(manager.HasWebSocketTransport, Is.False);
            Assert.That(Application.runInBackground, Is.True);
            Application.runInBackground = runInBackground;
            var replacement = new RecoveringTransport();
            manager.SetWebSocketTransport(replacement, false);
            replacement.Enqueue(30, "pause_time", "[]");
            yield return WaitForResult(replacement, 30);
            Assert.That(replacement.HasResult(30), Is.True);
        }

        [UnityTest]
        public IEnumerator Update에서_runtime이_유실되면_정리하고_재초기화한다()
        {
            yield return null;
            transport.Enqueue(50, "pause_time", "[]");
            yield return WaitForResult(transport, 50);
            Assert.That(Time.timeScale, Is.Zero);
            typeof(ArtelManager).GetField("frameTimeRecorder", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, null);
            LogAssert.Expect(LogType.Warning, new Regex("Runtime update failed; rebuilding in 5s"));
            yield return null;
            yield return null;
            Assert.That(Time.timeScale, Is.EqualTo(originalTimeScale));
            // 실제 시간 대기 대신 재시도 시각을 앞당겨 복원 경로를 실행한다.
            typeof(ArtelManager).GetField("nextRuntimeTickTime", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, 0f);
            transport.Enqueue(51, "pause_time", "[]");
            yield return WaitForResult(transport, 51);
            Assert.That(Time.timeScale, Is.Zero);
        }

        [UnityTest]
        public IEnumerator overlay를_다시_켜면_남은_진행_표시를_해제한다()
        {
            yield return null;
            var overlay = host.GetComponent<ArtelOverlayController>();
            var registration = typeof(ArtelOverlayController).GetField("registrationRunning",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var login = typeof(ArtelOverlayController).GetField("loginRunning",
                BindingFlags.Instance | BindingFlags.NonPublic);
            registration.SetValue(overlay, true);
            login.SetValue(overlay, true);
            overlay.enabled = false;
            overlay.enabled = true;
            yield return null;
            Assert.That(registration.GetValue(overlay), Is.False);
            Assert.That(login.GetValue(overlay), Is.False);
            Assert.That(host.transform.Find("Artel Overlay Canvas"), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator 초기화_표시가_남아도_유실된_runtime을_복원한다()
        {
            yield return null;
            manager.enabled = false;
            typeof(ArtelManager).GetField("frameTimeRecorder", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, null);
            manager.enabled = true;
            transport.Enqueue(40, "pause_time", "[]");
            yield return WaitForResult(transport, 40);
            Assert.That(transport.HasResult(40), Is.True);
        }

        private static IEnumerator WaitForResult(RecoveringTransport recoveringTransport, int id)
        {
            for (var frame = 0; frame < 120 && !recoveringTransport.HasResult(id); frame++) yield return null;
            Assert.That(recoveringTransport.HasResult(id), Is.True, "Request " + id);
            yield return null;
        }

        private sealed class RecoveringTransport : IArtelWebSocketTransport, IArtelConnectionEpochSource
        {
            private readonly Queue<ArtelWebSocketMessage> messages = new Queue<ArtelWebSocketMessage>();
            private readonly List<string> results = new List<string>();
            public bool Connected = true;
            public bool FailNextResult;
            public bool FailStop;
            public bool Disposed;
            public long ConnectionEpoch { get; set; }
            public bool IsConnected => Connected;
            public ArtelTransportPhase Phase => Connected ? ArtelTransportPhase.Connected : ArtelTransportPhase.Connecting;
            public void Start() { }
            public void Stop() { if (FailStop) throw new InvalidOperationException("injected stop failure"); }
            public void Dispose() { Disposed = true; }
            public bool TryDequeueMessage(out ArtelWebSocketMessage message)
            {
                message = messages.Count > 0 ? messages.Dequeue() : null;
                return message != null;
            }
            public void Send(string text)
            {
                if (!text.Contains("ACTION_RESULT")) return;
                if (FailNextResult)
                {
                    FailNextResult = false;
                    throw new InvalidOperationException("injected send failure");
                }
                results.Add(text);
            }
            public void Enqueue(int id, string method, string parameters)
            {
                var epoch = ConnectionEpoch;
                messages.Enqueue(new ArtelWebSocketMessage(
                    "{\"type\":\"ACTION\",\"id\":" + id + ",\"actions\":[{\"id\":1,\"method\":\"" + method +
                    "\",\"params\":" + parameters + "}]}", Send, () => Connected && ConnectionEpoch == epoch));
            }
            public bool HasResult(int id)
            {
                return results.Exists(text => text.Contains("\"requestId\":" + id + ","));
            }
        }
    }
}
