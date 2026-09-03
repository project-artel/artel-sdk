using System;
using Artel.Auth;
using Artel.Domain;
using Artel.Serialization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Artel.Tests
{
    /// <summary>
    /// 실행 인자와 <c>ARTEL_SDK_TOKEN</c> 이 세션을 채우는지 지킨다 (ARTEL-787).
    /// </summary>
    /// <remarks>
    /// <c>ArtelLaunchArguments.Parse</c> 는 Unity API 를 부르지 않으므로 player 없이 EditMode 에서
    /// 그대로 돈다. 저장을 확인하는 몇 개만 <see cref="ArtelSdkSession"/> 을 거치고, 그때도 실제
    /// 키체인 대신 <see cref="PlayerPrefsSecretStore"/> 로 갈아 끼운다.
    /// </remarks>
    public sealed class LaunchArgumentsTests
    {
        [SetUp]
        public void SetUp()
        {
            ArtelSecretStore.Current = new PlayerPrefsSecretStore();
            ArtelSdkSession.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ArtelSdkSession.Clear();
            ArtelSecretStore.Current = null;
        }

        [Test]
        public void 인자가_없으면_아무것도_받지_않는다()
        {
            // 오늘의 동작이 그대로 남는 자리다. 인자를 주지 않은 실행은 오버레이가 로그인부터 묻는다.
            var parsed = ArtelLaunchArguments.Parse(new string[0], null);

            Assert.That(parsed.ClearsSession, Is.False);
            Assert.That(parsed.Token, Is.Null);
            Assert.That(parsed.ProjectId, Is.Null);
            Assert.That(parsed.Secure, Is.Null);
            Assert.That(parsed.Host, Is.Null);
            Assert.That(parsed.Port, Is.Null);
            Assert.That(parsed.FrontendOrigin, Is.Null);
            Assert.That(parsed.Errors, Is.Empty);
        }

        [Test]
        public void 실행_파일_경로가_첫_항목이어도_읽는다()
        {
            // Environment.GetCommandLineArgs() 가 주는 그대로 넘긴다. 첫 항목은 실행 파일 경로다.
            var parsed = ArtelLaunchArguments.Parse(
                new[] { "/games/WordVenture.x86_64", "-batchmode", "-artel-project", "42" },
                null);

            Assert.That(parsed.ProjectId, Is.EqualTo("42"));
            Assert.That(parsed.Errors, Is.Empty);
        }

        [Test]
        public void server_인자를_host_와_port_로_나눈다()
        {
            var parsed = ArtelLaunchArguments.Parse(
                new[] { "-artel-server", "stage-orch.artel.kr:443" }, null);

            Assert.That(parsed.Host, Is.EqualTo("stage-orch.artel.kr"));
            Assert.That(parsed.Port, Is.EqualTo(443));
            Assert.That(parsed.Errors, Is.Empty);
        }

        [Test]
        public void port_가_숫자가_아니면_서버를_비워_두고_오류를_남긴다()
        {
            // host 만 받아 두면 어느 포트로 붙는지가 인자와 기본값에 반씩 걸린다.
            var parsed = ArtelLaunchArguments.Parse(
                new[] { "-artel-server", "localhost:eight" }, null);

            Assert.That(parsed.Host, Is.Null);
            Assert.That(parsed.Port, Is.Null);
            Assert.That(parsed.Errors.Count, Is.EqualTo(1));
            Assert.That(parsed.Errors[0], Does.Contain("-artel-server").And.Contains("eight"));
        }

        [Test]
        public void port_가_범위를_벗어나면_오류를_남긴다()
        {
            var parsed = ArtelLaunchArguments.Parse(
                new[] { "-artel-server", "localhost:70000" }, null);

            Assert.That(parsed.Port, Is.Null);
            Assert.That(parsed.Errors.Count, Is.EqualTo(1));
            Assert.That(parsed.Errors[0], Does.Contain("65535"));
        }

        [Test]
        public void server_인자가_host_port_형식이_아니면_오류를_남긴다()
        {
            var parsed = ArtelLaunchArguments.Parse(
                new[] { "-artel-server", "localhost" }, null);

            Assert.That(parsed.Host, Is.Null);
            Assert.That(parsed.Errors.Count, Is.EqualTo(1));
            Assert.That(parsed.Errors[0], Does.Contain("host:port"));
        }

        [Test]
        public void secure_는_true_와_false_를_받는다()
        {
            Assert.That(
                ArtelLaunchArguments.Parse(new[] { "-artel-secure", "true" }, null).Secure,
                Is.True);
            Assert.That(
                ArtelLaunchArguments.Parse(new[] { "-artel-secure", "False" }, null).Secure,
                Is.False);
        }

        [Test]
        public void secure_가_true_도_false_도_아니면_오류를_남긴다()
        {
            var parsed = ArtelLaunchArguments.Parse(new[] { "-artel-secure", "yes" }, null);

            Assert.That(parsed.Secure, Is.Null);
            Assert.That(parsed.Errors.Count, Is.EqualTo(1));
            Assert.That(parsed.Errors[0], Does.Contain("-artel-secure"));
        }

        [Test]
        public void frontend_가_절대_주소가_아니면_오류를_남긴다()
        {
            var parsed = ArtelLaunchArguments.Parse(
                new[] { "-artel-frontend", "home.stage.artel.kr" }, null);

            Assert.That(parsed.FrontendOrigin, Is.Null);
            Assert.That(parsed.Errors.Count, Is.EqualTo(1));
            Assert.That(parsed.Errors[0], Does.Contain("-artel-frontend"));
        }

        [Test]
        public void 값이_빠진_인자는_오류를_남긴다()
        {
            // `-artel-project -batchmode` 는 프로젝트 id 를 빠뜨린 것이지 `-batchmode` 라는
            // 프로젝트를 고른 것이 아니다.
            var parsed = ArtelLaunchArguments.Parse(
                new[] { "-artel-project", "-batchmode", "-artel-server" }, null);

            Assert.That(parsed.ProjectId, Is.Null);
            Assert.That(parsed.Host, Is.Null);
            Assert.That(parsed.Errors.Count, Is.EqualTo(2));
            Assert.That(parsed.Errors[0], Does.Contain("-artel-project").And.Contains("값이 없습니다"));
            Assert.That(parsed.Errors[1], Does.Contain("-artel-server").And.Contains("값이 없습니다"));
        }

        [Test]
        public void 인자_이름은_대소문자를_가리지_않는다()
        {
            // 이름이 조금 달라 조용히 무시되는 것이 이 기능의 가장 나쁜 실패다.
            var parsed = ArtelLaunchArguments.Parse(new[] { "-Artel-Logout" }, null);

            Assert.That(parsed.ClearsSession, Is.True);
        }

        [Test]
        public void 토큰은_환경_변수에서만_온다()
        {
            // 명령행에 실은 토큰은 같은 기계의 다른 사용자가 프로세스 목록에서 그대로 읽는다.
            // 그래서 `-artel-token` 같은 인자는 아예 없다.
            var fromCommandLine = ArtelLaunchArguments.Parse(
                new[] { "-artel-token", "sdk-token-value" }, null);

            Assert.That(fromCommandLine.Token, Is.Null);

            var fromEnvironment = ArtelLaunchArguments.Parse(new string[0], "  sdk-token-value  ");

            Assert.That(fromEnvironment.Token, Is.EqualTo("sdk-token-value"));
        }

        [Test]
        public void 환경_변수가_비어_있으면_오류를_남긴다()
        {
            // 변수가 없는 것은 인자를 주지 않은 실행이고, 비어 있는 것은 토큰을 실으려다 실패한
            // 실행이다. 뒤쪽은 말해 주지 않으면 알 길이 없다.
            var parsed = ArtelLaunchArguments.Parse(new string[0], "   ");

            Assert.That(parsed.Token, Is.Null);
            Assert.That(parsed.Errors.Count, Is.EqualTo(1));
            Assert.That(parsed.Errors[0], Does.Contain("ARTEL_SDK_TOKEN"));
        }

        [Test]
        public void 오류_문구에_토큰_원문이_들어가지_않는다()
        {
            var parsed = ArtelLaunchArguments.Parse(
                new[] { "-artel-secure", "yes" }, "sdk-token-value");

            Assert.That(parsed.Errors.Count, Is.EqualTo(1));
            Assert.That(parsed.Errors[0], Does.Not.Contain("sdk-token-value"));
        }

        [Test]
        public void 잘못된_값은_Debug_LogError_로_남는다()
        {
            // 무인 실행에서는 이 로그가 유일한 단서다.
            LogAssert.Expect(LogType.Error, "[Artel] -artel-secure 는 true 나 false 여야 합니다: yes");

            ArtelLaunchArguments.Parse(new[] { "-artel-secure", "yes" }, null).LogErrors();
        }

        [Test]
        public void 준_값만_서버에_덮어쓴다()
        {
            var server = new Server();

            ArtelLaunchArguments.Parse(
                    new[] { "-artel-server", "localhost:8080", "-artel-secure", "false" }, null)
                .ConfigureServer(server);

            Assert.That(server.HttpBaseUri.Host, Is.EqualTo("localhost"));
            Assert.That(server.HttpBaseUri.Port, Is.EqualTo(8080));
            Assert.That(server.HttpBaseUri.Scheme, Is.EqualTo("http"));
            Assert.That(server.WebSocketBaseUri.Scheme, Is.EqualTo("ws"));

            // -artel-frontend 를 주지 않았으므로 로그인 중계 주소는 기본값이 남는다.
            Assert.That(server.FrontendBaseUri, Is.Not.Null);
        }

        [Test]
        public void frontend_만_주면_오케스트레이션_주소는_기본값이_남는다()
        {
            var defaultServer = new Server();
            var server = new Server();

            ArtelLaunchArguments.Parse(
                    new[] { "-artel-frontend", "http://localhost:5173" }, null)
                .ConfigureServer(server);

            Assert.That(server.FrontendBaseUri, Is.EqualTo(new Uri("http://localhost:5173")));
            Assert.That(server.HttpBaseUri, Is.EqualTo(defaultServer.HttpBaseUri));
        }

        [Test]
        public void 인자가_없으면_서버가_그대로다()
        {
            var defaultServer = new Server();
            var server = new Server();

            ArtelLaunchArguments.Parse(new string[0], null).ConfigureServer(server);

            Assert.That(server.HttpBaseUri, Is.EqualTo(defaultServer.HttpBaseUri));
            Assert.That(server.FrontendBaseUri, Is.EqualTo(defaultServer.FrontendBaseUri));
        }

        [Test]
        public void 토큰과_프로젝트를_세션에_넣는다()
        {
            ArtelLaunchArguments.Parse(new[] { "-artel-project", "42" }, "sdk-token-value")
                .InstallSession();

            Assert.That(ArtelSdkSession.TryLoadToken(out var token), Is.True);
            Assert.That(token, Is.EqualTo("sdk-token-value"));
            Assert.That(ArtelSdkSession.TryLoadProjectId(out var projectId), Is.True);
            Assert.That(projectId, Is.EqualTo("42"));

            // 만료 시각은 주는 쪽이 모른다. 비워 두면 TryLoadToken 이 만료로 치지 않는다.
            Assert.That(ArtelSdkSession.TryLoadToken(out _), Is.True);

            // 표시 이름은 이 세션이 어디서 왔는지 말한다. 오버레이가 그리는 값이다.
            Assert.That(ArtelSdkSession.DisplayName, Is.EqualTo("ARTEL_SDK_TOKEN"));
        }

        [Test]
        public void logout_이_토큰보다_먼저_처리된다()
        {
            // 지우고 나서 채우는 순서라야 한 번의 실행으로 계정을 바꿀 수 있다.
            ArtelSdkSession.SaveToken("old-token", "2999-01-01T00:00:00Z", "octocat");
            ArtelSdkSession.SaveProjectId("1");
            ArtelSdkSession.SaveInstanceId("7");

            ArtelLaunchArguments.Parse(
                    new[] { "-artel-logout", "-artel-project", "42" }, "new-token")
                .InstallSession();

            Assert.That(ArtelSdkSession.TryLoadToken(out var token), Is.True);
            Assert.That(token, Is.EqualTo("new-token"));
            Assert.That(ArtelSdkSession.TryLoadProjectId(out var projectId), Is.True);
            Assert.That(projectId, Is.EqualTo("42"));

            // Clear 가 실제로 돌았다는 증거. 앞 사용자의 등록은 새 토큰에 딸려 오지 않는다.
            Assert.That(ArtelSdkSession.TryLoadInstanceId(out _), Is.False);
        }

        [Test]
        public void logout_만_주면_세션이_비워진다()
        {
            ArtelSdkSession.SaveToken("old-token", "2999-01-01T00:00:00Z", "octocat");
            ArtelSdkSession.SaveProjectId("1");

            ArtelLaunchArguments.Parse(new[] { "-artel-logout" }, null).InstallSession();

            Assert.That(ArtelSdkSession.TryLoadToken(out _), Is.False);
            Assert.That(ArtelSdkSession.TryLoadProjectId(out _), Is.False);
        }

        [Test]
        public void 씬이_들고_온_매니저의_오버레이도_주입된_세션을_읽는다()
        {
            // BeforeSceneLoad 훅(`ArtelManager.InstallLaunchSession`)이 넣고 나면, 매니저가
            // 어디서 왔든 오버레이는 이 view model 하나로 세션을 읽는다. 훅 자체는 에디터의
            // 명령행을 읽으므로 테스트가 인자를 실을 수 없어, 같은 입구인 InstallSession 을
            // 직접 부른다. 실제 매니저를 세우는 쪽은 PlayMode 의 LaunchSessionBootstrapTests 다.
            ArtelLaunchArguments.Parse(new[] { "-artel-project", "42" }, "sdk-token-value")
                .InstallSession();

            var jsonCodec = new NewtonsoftJsonCodec();
            var viewModel = new ArtelOverlayViewModel(
                new ArtelSdkRegistrationClient(jsonCodec),
                new ArtelSdkAuthClient(jsonCodec),
                jsonCodec);

            viewModel.Initialize();

            Assert.That(viewModel.HasToken, Is.True);
            Assert.That(viewModel.SelectedProjectId, Is.EqualTo("42"));

            // 로그인도 프로젝트 선택도 물을 것이 없으므로 게이트를 띄우지 않고 바로 등록으로 간다.
            Assert.That(viewModel.HasStoredSession, Is.True);
            Assert.That(viewModel.ShowGate, Is.False);
            Assert.That(viewModel.ShowPanel, Is.False);
        }

        [Test]
        public void 인자가_없으면_저장된_세션을_건드리지_않는다()
        {
            ArtelSdkSession.SaveToken("old-token", "2999-01-01T00:00:00Z", "octocat");
            ArtelSdkSession.SaveProjectId("1");

            ArtelLaunchArguments.Parse(new string[0], null).InstallSession();

            Assert.That(ArtelSdkSession.TryLoadToken(out var token), Is.True);
            Assert.That(token, Is.EqualTo("old-token"));
            Assert.That(ArtelSdkSession.DisplayName, Is.EqualTo("octocat"));
            Assert.That(ArtelSdkSession.TryLoadProjectId(out _), Is.True);
        }
    
        [Test]
        public void logout_만_준_실행은_지우기만_하는_실행이다()
        {
            var parsed = ArtelLaunchArguments.Parse(new[] { "game.exe", "-artel-logout" }, null);

            Assert.That(parsed.ClearsSession, Is.True);
            Assert.That(parsed.ClearsSessionOnly, Is.True);
        }

        [Test]
        public void logout_에_토큰이_함께_오면_계정을_바꾸는_실행이다()
        {
            var parsed = ArtelLaunchArguments.Parse(new[] { "game.exe", "-artel-logout" }, "a-token");

            Assert.That(parsed.ClearsSession, Is.True);
            Assert.That(parsed.ClearsSessionOnly, Is.False);
        }

        [Test]
        public void logout_에_프로젝트가_함께_오면_계정을_바꾸는_실행이다()
        {
            var parsed = ArtelLaunchArguments.Parse(
                new[] { "game.exe", "-artel-logout", "-artel-project", "42" }, null);

            Assert.That(parsed.ClearsSessionOnly, Is.False);
        }

        [Test]
        public void logout_이_없으면_지우기만_하는_실행이_아니다()
        {
            var parsed = ArtelLaunchArguments.Parse(new[] { "game.exe" }, null);

            Assert.That(parsed.ClearsSessionOnly, Is.False);
        }
}
}
