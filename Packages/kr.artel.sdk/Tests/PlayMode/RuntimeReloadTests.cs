using System;
using System.Collections;
using System.Reflection;
using Artel.Auth;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Artel.Tests
{
    public sealed class RuntimeReloadTests
    {
        private GameObject host;
        private ArtelManager displacedInstance;

        [SetUp]
        public void SetUp()
        {
            displacedInstance = ArtelManagerSlot.Clear();
            ArtelSecretStore.Current = new PlayerPrefsSecretStore();
            ArtelSdkSession.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null) Object.DestroyImmediate(host);
            ArtelManagerSlot.Restore(displacedInstance);
            ArtelSdkSession.Clear();
            ArtelSecretStore.Current = null;
        }

        [UnityTest]
        public IEnumerator Runtime객체가_사라진_뒤_재활성화하면_프레임과_종료가_동작한다()
        {
            host = new GameObject("Artel reload test");
            var manager = host.AddComponent<ArtelManager>();
            var overlay = host.GetComponent<ArtelOverlayController>();
            yield return null;

            var originalCanvas = host.transform.Find("Artel Overlay Canvas");
            Assert.That(originalCanvas, Is.Not.Null);
            host.SetActive(false);

            // Unity reload가 보존하지 않는 일반 C# 객체를 제거한다.
            foreach (var fieldName in new[] { "scanner", "allSceneScanner", "actionExecutor",
                         "gameFunctions", "jsonCodec", "frameTimeRecorder", "frameTimingSampler", "streamHost" })
            {
                Field(manager, fieldName).SetValue(manager, null);
            }
            var ownsRuntime = Field(manager, "ownsRuntime");
            ownsRuntime.SetValue(manager, false);
            Field(overlay, "viewModel").SetValue(overlay, null);

            host.SetActive(true);
            yield return null;
            yield return null;

            Assert.That(Attribute.IsDefined(ownsRuntime, typeof(NonSerializedAttribute)), Is.True,
                "runtime 소유 표시도 runtime 객체와 함께 초기화되어야 한다.");
            Assert.That(manager.SdkId, Is.Not.Null.And.Not.Empty);
            Assert.That(Field(manager, "streamHost").GetValue(manager), Is.Not.Null);
            Assert.That(Field(overlay, "viewModel").GetValue(overlay), Is.Not.Null);
            Assert.That(originalCanvas == null, Is.True);
            Assert.That(host.transform.Find("Artel Overlay Canvas"), Is.Not.Null);
            Assert.DoesNotThrow(manager.StopTransport);
            Assert.DoesNotThrow(manager.StopTransport);
            host.SetActive(false);
            host.SetActive(true);
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator 중복_매니저가_제거되어도_기존_매니저가_유지된다()
        {
            host = new GameObject("Artel surviving manager");
            var manager = host.AddComponent<ArtelManager>();
            var duplicateHost = new GameObject("Artel duplicate manager");
            duplicateHost.AddComponent<ArtelManager>();
            yield return null;
            yield return null;

            Assert.That(duplicateHost == null, Is.True);
            Assert.That(manager != null && manager.isActiveAndEnabled, Is.True);
            Assert.DoesNotThrow(manager.StopTransport);
            LogAssert.NoUnexpectedReceived();
        }

        private static FieldInfo Field(object target, string name)
        {
            return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        }
    }
}
