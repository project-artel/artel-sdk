using System.Collections;
using Artel.Auth;
using Artel.Domain;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Artel.Tests
{
    /// <summary>
    /// 씬이 들고 온 매니저도 주입된 세션을 쓰는지 본다 (ARTEL-787).
    /// </summary>
    /// <remarks>
    /// 플레이 모드에서만 돌 수 있다. 매니저의 <c>Awake</c> 가 <c>DontDestroyOnLoad</c> 를 부르고,
    /// 오버레이가 세션을 읽는 것은 컨트롤러의 <c>Start</c> 다 — 둘 다 에디터 스크립트에서는 돌지
    /// 않는다.
    ///
    /// 실제 <c>BeforeSceneLoad</c> 훅을 돌리지는 못한다. 그 훅은 에디터의 명령행을 읽는데 테스트가
    /// 거기에 <c>-artel-project</c> 를 실을 방법이 없고, 훅은 플레이 모드 진입 때 이미 한 번
    /// 돌았다. 그래서 훅과 같은 입구인 <c>InstallSession</c> 을 부른 뒤 매니저를 세운다. 이
    /// 테스트가 지키는 것은 <b>세션이 먼저 들어가 있으면 씬이 들고 온 매니저가 그것을 쓴다</b>는
    /// 것이고, 훅이 그 "먼저" 를 보장하는 것은 <c>RuntimeInitializeLoadType.BeforeSceneLoad</c> 다.
    /// </remarks>
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
        public IEnumerator 씬이_들고_온_매니저는_로그인을_묻지_않는다()
        {
            ArtelLaunchArguments.Parse(new[] { "-artel-logout", "-artel-project", "42" }, "sdk-token-value")
                .InstallSession();

            host = new GameObject("Artel scene manager");
            var manager = host.AddComponent<ArtelManager>();

            // host 가 빈 Server 는 요청을 만들다 던지므로 등록이 네트워크에 나가지 않는다.
            // 씬이 들고 온 매니저에는 -artel-server 가 닿지 않는다는 사실을 이 줄이 그대로
            // 보여 준다 — 서버를 정하는 것은 언제나 이 매니저 자신이다.
            manager.SetServer(new Server(true, string.Empty, 443));

            // 오버레이는 컨트롤러의 Start 에서 세션을 읽고, Start 는 다음 프레임에 돈다.
            yield return null;

            var canvas = host.transform.Find("Artel Overlay Canvas");
            Assert.That(canvas, Is.Not.Null);

            // 로그인 게이트가 뜨지 않는다. 세션 없이 뜬 매니저는 OverlayGuiBootstrapTests 에서
            // 이 게이트로 로그인부터 묻는다.
            var gate = canvas.Find("Artel Overlay Cover/Gate Content");
            Assert.That(gate, Is.Not.Null);
            Assert.That(gate.gameObject.activeSelf, Is.False);

            // 코너 패널도 닫힌 채로 시작한다. 사람이 고를 것이 남아 있지 않다는 뜻이다.
            var panel = canvas.Find("Artel Panel");
            Assert.That(panel, Is.Not.Null);
            Assert.That(panel.gameObject.activeSelf, Is.False);

            // -artel-logout 이 앞선다. 지우는 것이 뒤였다면 여기 남는 것이 없다.
            Assert.That(ArtelSdkSession.TryLoadToken(out var token), Is.True);
            Assert.That(token, Is.EqualTo("sdk-token-value"));
            Assert.That(ArtelSdkSession.TryLoadProjectId(out var projectId), Is.True);
            Assert.That(projectId, Is.EqualTo("42"));
        }
    }
}
