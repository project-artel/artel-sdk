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
            Assert.That(buttons, Has.Length.EqualTo(13));
            Assert.That(Array.Find(buttons, button => button.name == "취소 Button").gameObject.activeInHierarchy, Is.False);

            var serverInput = canvas.GetComponentInChildren<InputField>(true);
            Assert.That(serverInput, Is.Not.Null);
            Assert.That(serverInput.gameObject.name, Is.EqualTo("Server URL"));
            Assert.That(serverInput.text, Is.EqualTo(manager.Server.HttpBaseUri.AbsoluteUri));
            Assert.That(serverInput.gameObject.activeInHierarchy, Is.False);
            var settings = Array.Find(buttons, button => button.name == "고급 설정 Button");
            settings.onClick.Invoke();
            Assert.That(serverInput.gameObject.activeInHierarchy, Is.True);
            settings.onClick.Invoke();
            Assert.That(serverInput.gameObject.activeInHierarchy, Is.False);
            Assert.That(new Artel.Domain.Server().HttpBaseUri.AbsoluteUri, Is.EqualTo(Artel.Domain.Server.DefaultUrl));
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

        [UnityTest]
        public IEnumerator 주소_적용은_새_서버의_로그인_화면을_연다()
        {
            host = new GameObject("Artel server input test");
            var manager = host.AddComponent<ArtelManager>();
            yield return null;
            var canvas = host.transform.Find("Artel Overlay Canvas");
            var input = canvas.GetComponentInChildren<InputField>(true);
            var apply = Array.Find(canvas.GetComponentsInChildren<Button>(true), button => button.name == "적용 Button");
            input.text = "http://ui-test.example:8080";
            apply.onClick.Invoke();
            Assert.That(manager.Server.HttpBaseUri.AbsoluteUri, Is.EqualTo("http://ui-test.example:8080/"));
            Assert.That(ArtelSdkSession.LoadToken(), Is.Empty);
            Assert.That(canvas.Find("Artel Overlay Cover/Gate Content").gameObject.activeSelf, Is.True);
            PlayerPrefs.DeleteKey(ArtelOwnedPlayerPrefs.ServerUrl);
            ArtelSdkSession.UseServer(new Artel.Domain.Server());
        }

        [UnityTest]
        public IEnumerator 프로젝트_목록은_마지막_항목까지_스크롤하고_테마_변경_후에도_유지된다()
        {
            host = new GameObject("Artel project scroll test");
            host.AddComponent<ArtelManager>();
            yield return null;

            var controller = host.GetComponent<ArtelOverlayController>();
            var viewModel = (ArtelOverlayViewModel)typeof(ArtelOverlayController)
                .GetField("viewModel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
            var projects = (System.Collections.Generic.List<Artel.Protocol.Dto.SdkProjectDto>)viewModel.Projects;
            for (var index = 0; index < 10; index++)
            {
                projects.Add(new Artel.Protocol.Dto.SdkProjectDto { Id = index.ToString(), Name = "Project " + index });
            }
            typeof(ArtelOverlayViewModel).GetProperty("HasToken").SetValue(viewModel, true);
            typeof(ArtelOverlayController).GetMethod("RefreshView", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);

            for (var theme = 0; theme < 2; theme++)
            {
                var canvas = host.transform.Find("Artel Overlay Canvas");
                var scroll = canvas.GetComponentInChildren<ScrollRect>(true);
                Assert.That(scroll.gameObject.activeInHierarchy, Is.True);
                Assert.That(scroll.content.GetComponentsInChildren<Button>(), Has.Length.EqualTo(10));
                Canvas.ForceUpdateCanvases();
                scroll.verticalNormalizedPosition = 0f;
                yield return null;

                var last = scroll.content.GetChild(9).GetComponent<RectTransform>();
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, last);
                Assert.That(bounds.min.y, Is.GreaterThanOrEqualTo(scroll.viewport.rect.yMin - 1f));
                Assert.That(bounds.max.y, Is.LessThanOrEqualTo(scroll.viewport.rect.yMax + 1f));

                if (theme == 0)
                {
                    var toggle = Array.Find(canvas.GetComponentsInChildren<Toggle>(true), value => value.name == "다크 모드 Toggle");
                    toggle.isOn = !toggle.isOn;
                    yield return null;
                }
            }
        }

        [UnityTest]
        public IEnumerator 선택_프로젝트는_패널에_표시되고_테마를_바꿔도_유지된다()
        {
            host = new GameObject("Artel selected project test");
            host.AddComponent<ArtelManager>();
            yield return null;

            var controller = host.GetComponent<ArtelOverlayController>();
            var viewModel = (ArtelOverlayViewModel)typeof(ArtelOverlayController)
                .GetField("viewModel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
            var canvas = host.transform.Find("Artel Overlay Canvas");
            var label = canvas.Find("Artel Panel/Selected Project").GetComponent<Text>();
            Assert.That(label.text, Is.EqualTo("프로젝트 · 미선택"));

            const string projectName = "계몽 <테스트> 긴 프로젝트 이름으로 패널 줄바꿈과 상태 영역을 확인합니다";
            var projects = (System.Collections.Generic.List<Artel.Protocol.Dto.SdkProjectDto>)viewModel.Projects;
            projects.Add(new Artel.Protocol.Dto.SdkProjectDto { Id = "project-1", Name = projectName });
            typeof(ArtelOverlayViewModel).GetProperty("HasToken").SetValue(viewModel, true);
            typeof(ArtelOverlayViewModel).GetProperty("State").SetValue(viewModel, ArtelConnectionState.Connected);
            viewModel.SelectProject("project-1");

            for (var theme = 0; theme < 2; theme++)
            {
                canvas = host.transform.Find("Artel Overlay Canvas");
                label = canvas.Find("Artel Panel/Selected Project").GetComponent<Text>();
                Assert.That(label.gameObject.activeInHierarchy, Is.True);
                Assert.That(label.text, Is.EqualTo("프로젝트 · " + projectName));
                Assert.That(label.supportRichText, Is.False);
                Assert.That(label.horizontalOverflow, Is.EqualTo(HorizontalWrapMode.Wrap));
                Assert.That(label.verticalOverflow, Is.EqualTo(VerticalWrapMode.Truncate));
                var status = (Text)typeof(ArtelOverlayController)
                    .GetField("statusText", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
                var panel = canvas.Find("Artel Panel").GetComponent<RectTransform>();
                var labelBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(panel, label.rectTransform);
                var statusBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(panel, status.rectTransform);
                Assert.That(labelBounds.min.y, Is.GreaterThan(statusBounds.max.y));
                Assert.That(labelBounds.min.x, Is.GreaterThanOrEqualTo(panel.rect.xMin));
                Assert.That(labelBounds.max.x, Is.LessThanOrEqualTo(panel.rect.xMax));

                if (theme == 0)
                {
                    var toggle = Array.Find(canvas.GetComponentsInChildren<Toggle>(true), value => value.name == "다크 모드 Toggle");
                    toggle.isOn = true;
                    yield return null;
                }
            }

            viewModel.SelectProject("legacy-id");
            Assert.That(label.text, Is.EqualTo("프로젝트 · legacy-id"));
            viewModel.LogOut();
            Assert.That(label.text, Is.EqualTo("프로젝트 · 미선택"));
        }

        private static void ClearSession()
        {
            ArtelSdkSession.Clear();
            foreach (var key in SessionPlayerPrefsKeys)
            {
                PlayerPrefs.DeleteKey(key);
            }

            PlayerPrefs.Save();
        }
    }
}
