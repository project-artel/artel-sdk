using System.Collections;
using System.Reflection;
using Artel.Auth;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Artel.Tests
{
    /// <summary>
    /// <c>RUN_STATUS</c> decides whether the real mouse can take the agent's pointer. While a run
    /// is <c>RUNNING</c> it cannot, so a person working in another window does not make drags fail
    /// at random. Any other state, and the person pressing Ctrl+Shift+M, gives it back.
    /// </summary>
    public sealed class PointerHeldForRunTests
    {
        private ArtelManager displacedInstance;
        private GameObject host;
        private ArtelManager manager;

        [SetUp]
        public void SetUp()
        {
            ArtelSecretStore.Current = new PlayerPrefsSecretStore();
            displacedInstance = ArtelManagerSlot.Clear();
            ArtelInput.ResetVirtualKeyboard();
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null)
            {
                Object.DestroyImmediate(host);
            }

            ArtelManagerSlot.Restore(displacedInstance);
            ArtelSecretStore.Current = null;
            ArtelInput.ResetVirtualKeyboard();
        }

        [UnityTest]
        public IEnumerator Running_HoldsThePointer()
        {
            yield return StartManager();

            SendRunStatus("RUNNING");

            Assert.That(ArtelInput.PointerHeldForRun, Is.True);
        }

        [UnityTest]
        public IEnumerator Finished_HandsThePointerAndHeldButtonsBack()
        {
            yield return StartManager();
            SendRunStatus("RUNNING");
            ArtelInput.MoveMouse(new Vector2(100f, 100f));
            ArtelInput.PressMouseButton(0);

            SendRunStatus("FINISHED");

            Assert.That(ArtelInput.PointerHeldForRun, Is.False);
            Assert.That(ArtelInput.HasVirtualMousePosition, Is.False);
            yield return null;
            yield return null;
            Assert.That(ArtelInput.IsMouseButtonHeld(0), Is.False);
        }

        [UnityTest]
        public IEnumerator AStateThisSdkDoesNotKnow_LetsThePointerGo()
        {
            // A pointer held when no run needs it is the ARTEL-154 failure: the person cannot use
            // the mouse in the game at all.
            yield return StartManager();
            SendRunStatus("RUNNING");

            SendRunStatus("PAUSED");

            Assert.That(ArtelInput.PointerHeldForRun, Is.False);
        }

        [UnityTest]
        public IEnumerator TakePointerBack_EndsTheHoldForTheRestOfTheRun()
        {
            yield return StartManager();
            SendRunStatus("RUNNING");
            ArtelInput.MoveMouse(new Vector2(100f, 100f));

            manager.TakePointerBack();

            Assert.That(ArtelInput.PointerHeldForRun, Is.False);
            Assert.That(ArtelInput.HasVirtualMousePosition, Is.False);
        }

        private IEnumerator StartManager()
        {
            host = new GameObject("Artel pointer hold test");
            manager = host.AddComponent<ArtelManager>();
            yield return null;
        }

        private void SendRunStatus(string state)
        {
            var json =
                "{\"type\":\"RUN_STATUS\",\"state\":\"" + state + "\",\"projectName\":\"WordVenture\"," +
                "\"testRunName\":\"타이틀에서 전투까지\",\"qaRunId\":41,\"qaTryId\":77," +
                "\"label\":null,\"outcome\":null,\"at\":\"2026-10-07T09:00:00Z\"}";

            // HandleMessage is private; OverlayGuiBootstrapTests reaches it the same way.
            typeof(ArtelManager)
                .GetMethod("HandleMessage", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(manager, new object[] { new ArtelWebSocketMessage(json, _ => { }) });
        }
    }
}
