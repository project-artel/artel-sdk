using System;
using Artel.Auth;
using Artel.Domain;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests
{
    public sealed class ServerConnectionTests
    {
        private readonly Server first = new Server("https://first.example:8443");
        private readonly Server second = new Server("http://second.example:8080");

        [SetUp]
        public void SetUp()
        {
            ArtelSecretStore.Current = new PlayerPrefsSecretStore();
            ArtelSdkSession.UseServer(first);
            ArtelSdkSession.Clear();
            ArtelSdkSession.UseServer(second);
            ArtelSdkSession.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ArtelSdkSession.UseServer(first);
            ArtelSdkSession.Clear();
            ArtelSdkSession.UseServer(second);
            ArtelSdkSession.Clear();
            ArtelSdkSession.UseServer(new Server());
            ArtelSecretStore.Current = null;
        }

        [Test]
        public void 서버를_바꾸면_인증과_프로젝트를_분리하고_돌아오면_복원한다()
        {
            ArtelSdkSession.UseServer(first);
            ArtelSdkSession.SaveToken("first-token", "2999-01-01T00:00:00Z", "first-user");
            ArtelSdkSession.SaveRefreshToken("first-refresh", "2999-01-01T00:00:00Z");
            ArtelSdkSession.SaveProjectId("first-project");
            ArtelSdkSession.SaveInstanceId("first-instance");
            ArtelSdkSession.SaveGameBuildId("first-build");

            ArtelSdkSession.UseServer(second);
            Assert.That(ArtelSdkSession.LoadToken(), Is.Empty);
            Assert.That(ArtelSdkSession.TryLoadRefreshToken(out _), Is.False);
            Assert.That(ArtelSdkSession.TryLoadProjectId(out _), Is.False);
            Assert.That(ArtelSdkSession.LoadInstanceId(), Is.Empty);
            Assert.That(ArtelSdkSession.LoadGameBuildId(), Is.Empty);
            Assert.That(ArtelSdkSession.DisplayName, Is.Empty);
            ArtelSdkSession.SaveToken("second-token", "", "second-user");
            ArtelSdkSession.Clear();

            ArtelSdkSession.UseServer(first);
            Assert.That(ArtelSdkSession.LoadToken(), Is.EqualTo("first-token"));
            Assert.That(ArtelSdkSession.TryLoadRefreshToken(out var refresh), Is.True);
            Assert.That(refresh, Is.EqualTo("first-refresh"));
            Assert.That(ArtelSdkSession.TryLoadProjectId(out var project), Is.True);
            Assert.That(project, Is.EqualTo("first-project"));
            Assert.That(ArtelSdkSession.LoadInstanceId(), Is.EqualTo("first-instance"));
            Assert.That(ArtelSdkSession.LoadGameBuildId(), Is.EqualTo("first-build"));
            Assert.That(ArtelSdkSession.DisplayName, Is.EqualTo("first-user"));
        }

        [Test]
        public void 게임_설정을_지워도_두_서버의_로그인은_남는다()
        {
            ArtelSdkSession.UseServer(first);
            ArtelSdkSession.SaveToken("first-token", "", "first");
            ArtelSdkSession.UseServer(second);
            ArtelSdkSession.SaveToken("second-token", "", "second");
            ArtelOwnedPlayerPrefs.DeleteAllExceptOwn();
            Assert.That(ArtelSdkSession.LoadToken(), Is.EqualTo("second-token"));
            ArtelSdkSession.UseServer(first);
            Assert.That(ArtelSdkSession.LoadToken(), Is.EqualTo("first-token"));
        }

        [Test]
        public void 서버가_없는_이전_토큰을_재사용하지_않는다()
        {
            PlayerPrefs.SetString(ArtelOwnedPlayerPrefs.SdkTokenSecret, "legacy-token");
            try
            {
                ArtelSdkSession.UseServer(first);
                Assert.That(ArtelSdkSession.LoadToken(), Is.Empty);
            }
            finally { PlayerPrefs.DeleteKey(ArtelOwnedPlayerPrefs.SdkTokenSecret); }
        }

        [Test]
        public void URL_대소문자와_기본_port는_같은_서버다()
        {
            ArtelSdkSession.UseServer(new Server("https://FIRST.example:8443/"));
            ArtelSdkSession.SaveToken("token", "", "user");
            ArtelSdkSession.UseServer(first);
            Assert.That(ArtelSdkSession.LoadToken(), Is.EqualTo("token"));
            Assert.That(new Server("https://example.com:443/").HttpBaseUri,
                Is.EqualTo(new Server("https://example.com").HttpBaseUri));
        }

        [TestCase("ftp://example.com")]
        [TestCase("https://user:password@example.com")]
        [TestCase("https://example.com/path")]
        [TestCase("https://example.com?query=1")]
        [TestCase("https://example.com#fragment")]
        [TestCase("example.com")]
        public void 잘못된_서버_URL을_거절한다(string url)
        {
            Assert.Throws<ArgumentException>(() => new Server(url));
        }

        [Test]
        public void 실행_인자_URL로_HTTP와_WebSocket_주소를_정한다()
        {
            var server = new Server();
            var arguments = ArtelLaunchArguments.Parse(new[] { "-artel-server", "http://localhost:8080" }, null);
            arguments.ConfigureServer(server);
            Assert.That(arguments.Errors, Is.Empty);
            Assert.That(server.HttpBaseUri.AbsoluteUri, Is.EqualTo("http://localhost:8080/"));
            Assert.That(server.WebSocketBaseUri.AbsoluteUri, Is.EqualTo("ws://localhost:8080/"));
        }
    }
}
