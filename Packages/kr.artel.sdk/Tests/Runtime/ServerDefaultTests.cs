using Artel.Domain;
using NUnit.Framework;

namespace Artel.Tests
{
    /// <summary>
    /// 기본값만으로 붙을 수 있는지 지킨다 (ARTEL-703).
    ///
    /// 씬에 매니저를 놓지 않은 게임은 개발 빌드가 스스로 만드는 것에 기댄다
    /// (<c>ArtelManager.SpawnInDevelopmentBuilds</c>). 그 매니저는 인스펙터를 거치지 않으므로
    /// <c>new Server()</c> 가 내는 값이 전부다. 전에는 <c>host</c> 가 비어 있어서 그 경로가
    /// 붙을 곳을 모르는 껍데기였고, 실제로 쓰려면 사람이 씬에 매니저를 놓아야 했다.
    ///
    /// 값 자체를 못 박지는 않는다. 서버가 옮겨 가면 여기가 그 사본이 될 뿐이다. 지키는 것은
    /// <b>기본값만으로 주소가 선다</b>는 것이다.
    /// </summary>
    public sealed class ServerDefaultTests
    {
        [Test]
        public void 기본값만으로_오케스트레이션_주소가_선다()
        {
            var server = new Server();

            Assert.That(server.HttpBaseUri, Is.Not.Null);
            Assert.That(server.WebSocketBaseUri, Is.Not.Null);
        }

        [Test]
        public void 기본값만으로_로그인_중계_주소가_선다()
        {
            // 이 값은 오케스트레이션 호스트에서 유도할 수 없다 — 중계 페이지는 웹 콘솔에 있고
            // 호스트도 포트도 다르다. 그래서 따로 기본값을 갖는다.
            Assert.That(new Server().FrontendBaseUri, Is.Not.Null);
        }

        [Test]
        public void 기본값이_로컬을_가리키지_않는다()
        {
            // 비어 있던 자리를 채우면서 로컬 개발값이 남아 있으면, 씬에 매니저를 놓지 않은
            // 게임이 자기 기계에 붙으려 든다. 그것은 안 붙는 것보다 진단이 어렵다.
            var server = new Server();

            Assert.That(server.HttpBaseUri.Host, Is.Not.EqualTo("localhost"));
            Assert.That(server.FrontendBaseUri.Host, Is.Not.EqualTo("localhost"));
        }

        [Test]
        public void 인스펙터로_덮어쓸_수_있다()
        {
            // 기본값을 두는 것이 곧 고정하는 것은 아니다. 자기 서버를 보는 개발자가 있고,
            // 씬에 매니저가 있으면 그쪽이 이긴다.
            var mine = new Server(false, "localhost", 8080);

            Assert.That(mine.HttpBaseUri.Host, Is.EqualTo("localhost"));
            Assert.That(mine.HttpBaseUri.Scheme, Is.EqualTo("http"));
            Assert.That(mine.WebSocketBaseUri.Scheme, Is.EqualTo("ws"));
        }
    }
}
