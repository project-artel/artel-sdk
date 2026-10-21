using System.Collections;
using Artel.Auth;
using Artel.Domain;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Artel.Tests
{
    /// <summary>매니저가 다른 서버를 선택하면 이전 인증을 재사용하지 않는다.</summary>
    public sealed class LaunchSessionBootstrapTests
    {
        private ArtelManager displacedInstance;
        private GameObject host;

        [SetUp]
        public void SetUp()
        {
            // 갈아끼우지 않으면 테스트가 개발자의 실제 키체인을 읽는다.
            ArtelSecretStore.Current = new PlayerPrefsSecretStore();
            ArtelSdkSession.Clear();

            // 개발 빌드용 AfterSceneLoad 훅이 플레이 모드 진입 때 하나를 띄워 두었다. 비켜 두지
            // 않으면 여기서 붙인 매니저를 Awake 가 중복으로 보고 그 자리에서 파괴한다.
            displacedInstance = ArtelManagerSlot.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null)
            {
                Object.DestroyImmediate(host);
            }

            ArtelManagerSlot.Restore(displacedInstance);
            ArtelSdkSession.Clear();
            ArtelSecretStore.Current = null;
        }

        [UnityTest]
        public IEnumerator 서버를_바꾸면_이전_서버의_주입된_로그인을_쓰지_않는다()
        {
            ArtelLaunchArguments.Parse(new[] { "-artel-project", "42" }, "sdk-token-value")
                .InstallSession();
            host = new GameObject("Artel scene manager");
            var manager = host.AddComponent<ArtelManager>();
            manager.SetServer(new Server("http://other.example:8080"));
            yield return null;

            var canvas = host.transform.Find("Artel Overlay Canvas");
            var gate = canvas.Find("Artel Overlay Cover/Gate Content");
            Assert.That(gate.gameObject.activeSelf, Is.True);
            Assert.That(ArtelSdkSession.LoadToken(), Is.Empty);
            Assert.That(ArtelSdkSession.TryLoadProjectId(out _), Is.False);
            ArtelSdkSession.UseServer(new Server());
            Assert.That(ArtelSdkSession.LoadToken(), Is.EqualTo("sdk-token-value"));
        }
    }
}
