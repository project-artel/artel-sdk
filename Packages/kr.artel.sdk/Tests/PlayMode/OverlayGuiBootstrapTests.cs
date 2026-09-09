using System;
using System.Collections;
using System.Reflection;
using Artel.Affordances.Scan;
using Artel.Auth;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Artel.Tests
{
    /// <summary>
    /// 매니저를 붙이기만 하면 오버레이가 따라오는지 본다. 플레이 모드에서만 돌 수 있다:
    /// 매니저의 <c>Awake</c>가 <c>DontDestroyOnLoad</c>를 부르고 — 에디터 스크립트에서는
    /// 부를 수 없다 — GUI는 컨트롤러의 <c>Start</c>가 만든다.
    /// </summary>
    public sealed class OverlayGuiBootstrapTests
    {
        private const string DarkThemePlayerPrefsKey = "Artel.DarkTheme";

        // 세션은 여러 키에 흩어져 있다. 하나라도 흘리면 로그인된 화면으로 시작한다.
        private static readonly string[] SessionPlayerPrefsKeys =
        {
            "Artel.SdkToken",
            "Artel.SdkTokenExpiresAt",
            "Artel.SdkDisplayName",
            "Artel.ProjectId",
            "Artel.InstanceId"
        };

        private ArtelManager displacedInstance;
        private GameObject host;

        [SetUp]
        public void SetUp()
        {
            // 토큰은 보안 저장소로 간다. 갈아끼우지 않으면 테스트가 개발자의 실제 키체인을 읽어
            // 로그인 화면 대신 등록 화면부터 시작한다.
            ArtelSecretStore.Current = new PlayerPrefsSecretStore();
            ClearSession();
            PlayerPrefs.DeleteKey(DarkThemePlayerPrefsKey);
            displacedInstance = ArtelManagerSlot.Clear();
            // -artel-window-label 을 실은 실행이 있었는지가 다음 테스트로 새면 안 된다.
            ArtelWindowLabel.Value = null;
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null)
            {
                UnityEngine.Object.DestroyImmediate(host);
            }

            ArtelManagerSlot.Restore(displacedInstance);
            ClearSession();
            PlayerPrefs.DeleteKey(DarkThemePlayerPrefsKey);
            PlayerPrefs.Save();
            ArtelSecretStore.Current = null;
            ArtelWindowLabel.Value = null;
        }

        [UnityTest]
        public IEnumerator ArtelManager_CreatesOverlayGuiAutomatically()
        {
            host = new GameObject("Artel overlay test");
            var manager = host.AddComponent<ArtelManager>();

            var controller = host.GetComponent<ArtelOverlayController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(host.GetComponent<KeyboardStatusController>(), Is.Not.Null);

            // GUI는 컨트롤러의 Start가 만들고, Start는 다음 프레임에 돈다.
            yield return null;

            // 이름으로 찾지 않는다. 개발 빌드용 훅이 띄운 매니저의 오버레이도 같은 이름이라
            // 어느 쪽을 봤는지 알 수 없다. 캔버스는 컨트롤러의 자식이므로 host 아래에서 찾는다.
            var canvas = host.transform.Find("Artel Overlay Canvas");
            Assert.That(canvas, Is.Not.Null);

            var buttons = canvas.GetComponentsInChildren<Button>(true);
            var toggles = canvas.GetComponentsInChildren<Toggle>(true);
            var smoothCursorToggle = Array.Find(toggles, toggle => toggle.name == "부드러운 커서 Toggle");
            var loginButton = Array.Find(buttons, button => button.name == "로그인 Button");
            var connectButton = Array.Find(buttons, button => button.name == "연결 Button");

            Assert.That(manager.SdkId, Is.Not.Empty);
            Assert.That(manager.InstanceName, Is.Not.Empty);

            // Artel 토글, 고급, 연결, 로그아웃, 로그인, 다시 시도, 게이트 로그아웃, 나중에.
            Assert.That(buttons, Has.Length.EqualTo(8));

            // 키 입력창은 사라졌다. 남아 있으면 로그인 흐름과 두 입구가 공존한다.
            Assert.That(canvas.GetComponentInChildren<InputField>(true), Is.Null);
            Assert.That(loginButton, Is.Not.Null);
            Assert.That(loginButton.interactable, Is.True);
            Assert.That(connectButton, Is.Not.Null);
            Assert.That(connectButton.interactable, Is.False);
            Assert.That(smoothCursorToggle, Is.Not.Null);
            Assert.That(smoothCursorToggle.isOn, Is.False);
            Assert.That(Array.Find(toggles, toggle => toggle.name == "다크 모드 Toggle"), Is.Not.Null);
            Assert.That(canvas.GetComponentsInChildren<ArtelLogoGraphic>(true), Has.Length.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator ArtelManager_DrawsWindowLabelTopLeft_WhenArgumentGiven()
        {
            ArtelWindowLabel.Value = "TC 9139";

            host = new GameObject("Artel overlay test");
            host.AddComponent<ArtelManager>();

            // GUI는 컨트롤러의 Start가 만들고, Start는 다음 프레임에 돈다.
            yield return null;

            var canvas = host.transform.Find("Artel Overlay Canvas");
            Assert.That(canvas, Is.Not.Null);

            var label = canvas.Find("Artel Window Label");
            Assert.That(label, Is.Not.Null);
            Assert.That(label.GetComponent<Instrument>(), Is.Not.Null);
            Assert.That(label.GetComponentInChildren<Text>(true).text, Is.EqualTo("TC 9139"));

            var labelRect = label.GetComponent<RectTransform>();
            Assert.That(labelRect.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(labelRect.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(labelRect.pivot, Is.EqualTo(new Vector2(0f, 1f)));
        }

        /// <summary>
        /// `artel qa matrix` 가 창마다 만드는 문구를 그대로 넣어, 판이 그 길이를 담는지 본다.
        /// 판이 고정 폭이면 이 문구는 뒤가 잘리고, 잘린 라벨은 어느 조합의 창인지 말해 주지
        /// 못한다.
        /// </summary>
        [UnityTest]
        public IEnumerator ArtelManager_FitsWindowLabelPlateToText_ForMatrixLabel()
        {
            const string MatrixLabel = "slot 0 testRun=1 contentMap=off knowledge=server default";
            ArtelWindowLabel.Value = MatrixLabel;

            host = new GameObject("Artel overlay test");
            host.AddComponent<ArtelManager>();

            yield return null;

            var label = host.transform.Find("Artel Overlay Canvas/Artel Window Label");
            Assert.That(label, Is.Not.Null);

            var text = label.GetComponentInChildren<Text>(true);
            Assert.That(text.text, Is.EqualTo(MatrixLabel));
            Assert.That(text.horizontalOverflow, Is.EqualTo(HorizontalWrapMode.Overflow));
            Assert.That(
                label.GetComponent<RectTransform>().sizeDelta.x,
                Is.GreaterThanOrEqualTo(text.preferredWidth));

            // 세로로 여백을 두면 남는 높이가 줄 높이보다 낮아지고, Truncate 가 줄을 통째로
            // 지워 판만 까맣게 남는다. 여백 0 과 Overflow 둘 다 그것을 막는다.
            Assert.That(text.verticalOverflow, Is.EqualTo(VerticalWrapMode.Overflow));
            Assert.That(text.rectTransform.offsetMin.y, Is.EqualTo(0f));
            Assert.That(text.rectTransform.offsetMax.y, Is.EqualTo(0f));
        }

        [UnityTest]
        public IEnumerator ArtelManager_DoesNotDrawWindowLabel_WhenArgumentMissing()
        {
            host = new GameObject("Artel overlay test");
            host.AddComponent<ArtelManager>();

            yield return null;

            var canvas = host.transform.Find("Artel Overlay Canvas");
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.Find("Artel Window Label"), Is.Null);
        }

        /// <summary>
        /// 라벨이 있으면 그 아래에 상태 줄도 뜬다. 아직 RUN_STATUS 를 받은 적이 없으므로
        /// 문구는 RunStatusLine.NoRunYet 이어야 한다 — 빈 문자열이면 그리다가 실패한 줄과
        /// 가려지지 않는다 (ARTEL-835).
        /// </summary>
        [UnityTest]
        public IEnumerator ArtelManager_DrawsRunStatusLineBelowLabel_WhenLabelPresent()
        {
            ArtelWindowLabel.Value = "TC 9139";

            host = new GameObject("Artel overlay test");
            host.AddComponent<ArtelManager>();

            yield return null;

            var canvas = host.transform.Find("Artel Overlay Canvas");
            var statusLine = canvas.Find("Artel Run Status");
            Assert.That(statusLine, Is.Not.Null);
            Assert.That(statusLine.GetComponent<Instrument>(), Is.Not.Null);
            Assert.That(
                statusLine.GetComponentInChildren<Text>(true).text,
                Is.EqualTo(RunStatusLine.NoRunYet));
        }

        /// <summary>
        /// 라벨이 없으면 화면은 지금과 똑같이 남는다 — 상태 줄의 픽셀도 모든 화면 캡처에
        /// 실리므로, 이 줄이 라벨 없이 켜지면 라벨 없이 뜨는 모든 QA 런의 화면이 달라진다
        /// (ARTEL-835).
        /// </summary>
        [UnityTest]
        public IEnumerator ArtelManager_DoesNotDrawRunStatusLine_WhenLabelMissing()
        {
            host = new GameObject("Artel overlay test");
            host.AddComponent<ArtelManager>();

            yield return null;

            var canvas = host.transform.Find("Artel Overlay Canvas");
            Assert.That(canvas.Find("Artel Run Status"), Is.Null);
        }

        /// <summary>
        /// RUN_STATUS 가 도착하면 그 자리에서 문구가 바뀐다. HandleMessage 는 private 이라
        /// GameStateSwitchTests 와 같은 방법으로 리플렉션을 거쳐 부른다.
        /// </summary>
        [UnityTest]
        public IEnumerator ArtelManager_UpdatesRunStatusLine_WhenRunStatusArrives()
        {
            ArtelWindowLabel.Value = "TC 9139";

            host = new GameObject("Artel overlay test");
            var manager = host.AddComponent<ArtelManager>();

            yield return null;

            const string RunStatusJson =
                "{\"type\":\"RUN_STATUS\",\"state\":\"WAITING_AGENT\",\"projectName\":\"WordVenture\"," +
                "\"testRunName\":\"타이틀에서 전투까지\",\"qaRunId\":41,\"qaTryId\":77," +
                "\"label\":null,\"outcome\":null,\"at\":\"2026-09-04T16:30:00Z\"}";
            var message = new ArtelWebSocketMessage(RunStatusJson, _ => { });

            typeof(ArtelManager)
                .GetMethod("HandleMessage", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(manager, new object[] { message });

            var statusLine = host.transform.Find("Artel Overlay Canvas/Artel Run Status");
            var text = statusLine.GetComponentInChildren<Text>(true);
            Assert.That(
                text.text,
                Is.EqualTo("project WordVenture · test run 타이틀에서 전투까지 · agent session 기다리는 중"));
        }

        private static void ClearSession()
        {
            foreach (var key in SessionPlayerPrefsKeys)
            {
                PlayerPrefs.DeleteKey(key);
            }

            PlayerPrefs.Save();
        }
    }
}
