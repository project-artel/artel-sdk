using Artel.Affordances.Live;
using NUnit.Framework;

namespace Artel.Tests
{
    /// <summary>
    /// 판독이 세대와 번호를 다는지(ARTEL-1299). 받는 쪽은 <c>runtime</c> 이 바뀌면 상태를 버리고, <c>stream</c> 이 바뀌거나
    /// <c>seq</c> 가 비면 whole 을 기다린다. 그러려면 둘이 문서 맨 앞에서 읽혀야 한다.
    /// </summary>
    public sealed class PulseStreamTests
    {
        [Test]
        public void 세대와_번호를_문서_맨_앞에_단다()
        {
            var stamped = Pulse.Stamp("{\"schema\":3,\"reading\":7}", "run-a", "stream-b", 4);

            Assert.That(stamped, Is.EqualTo("{\"runtime\":\"run-a\",\"stream\":\"stream-b\",\"seq\":4,\"schema\":3,\"reading\":7}"));
        }

        [Test]
        public void 빈_문서와_객체가_아닌_문서는_건드리지_않는다()
        {
            Assert.That(Pulse.Stamp("{}", "run-a", "stream-b", 1), Is.EqualTo("{\"runtime\":\"run-a\",\"stream\":\"stream-b\",\"seq\":1}"));
            Assert.That(Pulse.Stamp("[]", "run-a", "stream-b", 1), Is.EqualTo("[]"));
            Assert.That(Pulse.Stamp("", "run-a", "stream-b", 1), Is.EqualTo(""));
        }

        [Test]
        public void 프로세스_세대는_한_번_정해진다()
        {
            Assert.That(Pulse.RuntimeId, Is.Not.Empty);
            Assert.That(Pulse.RuntimeId, Is.EqualTo(Pulse.RuntimeId));
        }

        [Test]
        public void 판독이_돌지_않으면_resync_는_거절한다()
        {
            Pulse.Stop();

            Assert.That(Pulse.Resync(), Is.False);
        }
    }
}
