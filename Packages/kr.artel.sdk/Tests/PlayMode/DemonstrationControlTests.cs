using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Artel.Protocol.Dto;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Artel.Tests
{
    public sealed class DemonstrationControlTests
    {
        private ArtelManager displaced;
        private GameObject host;
        private ArtelManager manager;
        private RecordingTransport transport;

        [SetUp] public void SetUp()
        {
            displaced = ArtelManagerSlot.Clear();
            ArtelInput.ResetVirtualKeyboard();
            host = new GameObject("Demonstration control test");
            manager = host.AddComponent<ArtelManager>();
            transport = new RecordingTransport();
            manager.SetWebSocketTransport(transport, false);
        }

        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(host);
            ArtelManagerSlot.Restore(displaced);
            ArtelInput.ReleaseAllVirtualInput();
        }

        [UnityTest] public IEnumerator Release_StopsRunningAndQueuedInputs()
        {
            Assert.That(Control("ACQUIRE"), Is.True);
            Assert.That(manager.Reading, Is.True);
            SendActions(11, "move_mouse", new List<object> { 0.5f, 0.5f });
            SendActions(12, "key_down", new List<object> { "Space" });
            Assert.That(Control("RELEASE"), Is.True);
            Assert.That(manager.Reading, Is.False);
            Assert.That(ArtelInput.PointerHeldForRun, Is.False);
            yield return null;
            yield return null;
            yield return null;
            Assert.That(ArtelInput.GetKey(KeyCode.Space), Is.False);
            Assert.That(ArtelInput.HasVirtualMousePosition, Is.False);
            var answers = transport.Sent.Select(JObject.Parse)
                .Where(message => (string)message["type"] == "ACTION_RESULT").ToList();
            Assert.That(answers.Select(answer => (long)answer["requestId"]), Is.EquivalentTo(new long[] { 11, 12 }));
            // 직접 이동은 RELEASE 전에 끝날 수 있지만 해제 후 입력을 다시 적용하지 않는다.
        }

        [UnityTest] public IEnumerator ConnectionEpochChange_ReleasesHeldInputAndRejectsOldGeneration()
        {
            Assert.That(Control("ACQUIRE"), Is.True);
            SendActions(20, "key_down", new List<object> { "Space" });
            yield return null;
            yield return null;
            Assert.That(ArtelInput.GetKey(KeyCode.Space), Is.True);
            transport.ConnectionEpoch++;
            yield return null;
            yield return null;
            Assert.That(ArtelInput.GetKey(KeyCode.Space), Is.False);
            Assert.That(Control("RENEW"), Is.False);
            Assert.That(Control("ACQUIRE"), Is.False);
            Assert.That(Control("ACQUIRE", 2), Is.True);
        }

        [UnityTest] public IEnumerator ActiveLease_RejectsLegacyActionsAndUnsupportedScan()
        {
            Assert.That(Control("ACQUIRE"), Is.True);
            SendActions(30, "key_down", new List<object> { "Space" }, legacy: true);
            SendActions(31, "scan_all_scenes", new List<object>());
            yield return null;
            yield return null;
            Assert.That(ArtelInput.GetKey(KeyCode.Space), Is.False);
            var answers = transport.Sent.Select(JObject.Parse)
                .Where(message => (string)message["type"] == "ACTION_RESULT").ToList();
            Assert.That(answers.Count, Is.EqualTo(2));
            Assert.That(answers.All(answer => !(bool)answer["results"][0]["success"]), Is.True);
        }

        [UnityTest] public IEnumerator Acquire_RejectsRunningLegacyAction()
        {
            SendActions(40, "move_mouse", new List<object> { 100f, 100f }, legacy: true);
            Assert.That(Control("ACQUIRE"), Is.False);
            yield return null;
        }

        [UnityTest] public IEnumerator Expiry_ReleasesHeldKeyWithoutAnotherMessage()
        {
            double now = 10;
            typeof(ArtelManager).GetField("demonstrationLease", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, new DemonstrationLease(() => now));
            Assert.That(Control("ACQUIRE"), Is.True);
            SendActions(50, "key_down", new List<object> { "Space" });
            yield return null;
            yield return null;
            Assert.That(ArtelInput.GetKey(KeyCode.Space), Is.True);
            now = 18;
            yield return null;
            yield return null;
            Assert.That(ArtelInput.GetKey(KeyCode.Space), Is.False);
            Assert.That(ArtelInput.PointerHeldForRun, Is.False);
            Assert.That(manager.Reading, Is.False);
            Assert.That(Control("RENEW"), Is.False);
        }

        [Test] public void Release_PreservesReadingsStartedBeforeDemonstration()
        {
            Assert.That(manager.StartReadings(), Is.True);
            Assert.That(Control("ACQUIRE"), Is.True);
            Assert.That(Control("RELEASE"), Is.True);
            Assert.That(manager.Reading, Is.True);
        }

        [UnityTest] public IEnumerator DemonstrationPointer_UsesGameResolutionInsteadOfStreamResolution()
        {
            Assert.That(Control("ACQUIRE"), Is.True);
            var cursor = (CursorController)typeof(ArtelManager)
                .GetField("cursorController", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
            typeof(CursorController).GetField("smoothMovement", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(cursor, true);
            typeof(CursorController).GetField("movementDurationSeconds", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(cursor, 10f);
            SendActions(60, "move_mouse", new List<object> { 0.25, 0.75 });
            yield return null;
            yield return null;
            Assert.That(ArtelInput.mousePosition.x, Is.EqualTo(Screen.width * 0.25f).Within(0.01));
            Assert.That(ArtelInput.mousePosition.y, Is.EqualTo(Screen.height * 0.25f).Within(0.01));
        }

        [UnityTest] public IEnumerator DemonstrationPointer_BurstCompletesWithoutGlideBacklog()
        {
            Assert.That(Control("ACQUIRE"), Is.True);
            for (var index = 0; index < 35; index++)
            {
                SendActions(100 + index, "move_mouse", new List<object> { index / 34d, 0.25 });
                yield return null;
            }
            for (var frame = 0; frame < 4; frame++) yield return null;
            var answers = transport.Sent.Select(JObject.Parse)
                .Where(message => (string)message["type"] == "ACTION_RESULT").ToList();
            Assert.That(answers.Count, Is.EqualTo(35));
            Assert.That(answers.All(answer => (bool)answer["results"][0]["success"]), Is.True);
            Assert.That(ArtelInput.mousePosition.x, Is.EqualTo(Screen.width).Within(0.01));
            Assert.That(ArtelInput.mousePosition.y, Is.EqualTo(Screen.height * 0.75f).Within(0.01));
        }

        private bool Control(string operation, long generation = 1)
        {
            string answer = null;
            Handle(new ArtelWebSocketMessage(JsonConvert.SerializeObject(new
            {
                type = "DEMONSTRATION_CONTROL", sessionId = "one", generation,
                operation, leaseMilliseconds = 8000
            }), text => answer = text));
            return (bool)JObject.Parse(answer)["accepted"];
        }

        private void SendActions(long id, string method, List<object> parameters, bool legacy = false)
        {
            Handle(new ArtelWebSocketMessage(JsonConvert.SerializeObject(new ArtelRequestDto
            {
                Type = "ACTION", Id = id,
                Demonstration = legacy ? null : new DemonstrationReferenceDto { SessionId = "one", Generation = 1 },
                Actions = new List<ActionRequestDto> { new ActionRequestDto { Id = 1, Method = method, Parameters = parameters } }
            }), transport.Send));
        }

        private void Handle(ArtelWebSocketMessage message)
        {
            typeof(ArtelManager).GetMethod("HandleMessage", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(manager, new object[] { message });
        }

        private sealed class RecordingTransport : IArtelWebSocketTransport, IArtelConnectionEpoch
        {
            public List<string> Sent { get; } = new List<string>();
            public bool IsConnected => true;
            public long ConnectionEpoch { get; set; } = 1;
            public ArtelTransportPhase Phase => ArtelTransportPhase.Connected;
            public void Start() { }
            public void Stop() { }
            public void Dispose() { }
            public void Send(string text) { Sent.Add(text); }
            public bool TryDequeueMessage(out ArtelWebSocketMessage message) { message = null; return false; }
        }
    }
}
