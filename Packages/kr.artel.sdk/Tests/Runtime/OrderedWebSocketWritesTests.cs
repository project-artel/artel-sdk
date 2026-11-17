using System.Collections.Concurrent;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Artel.Tests
{
    public sealed class OrderedWebSocketWritesTests
    {
        [Test]
        public void Pulse_포화_중에도_응답을_받고_전송_순서를_유지한다()
        {
            var entered = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var responseSent = new TaskCompletionSource<bool>();
            var frames = new ConcurrentQueue<string>();
            var writes = new OrderedWebSocketWrites(async frame =>
            {
                frames.Enqueue(frame);
                if (frame == "first chunk")
                {
                    entered.TrySetResult(true);
                    await release.Task;
                }
                if (frame == "response") responseSent.TrySetResult(true);
                return true;
            }, () => responseSent.TrySetResult(false));

            try
            {
                Assert.That(writes.TryEnqueue(new[] { "first chunk", "second chunk" }, true), Is.True);
                Assert.That(entered.Task.Wait(3000), Is.True);
                for (var index = 0; index < 16; index++)
                    Assert.That(writes.TryEnqueue(new[] { "pulse" }, true), Is.True);

                Assert.That(writes.TryEnqueue(new[] { "dropped" }, true), Is.False);
                Assert.That(writes.TryEnqueue(new[] { "response" }, false), Is.True);
                Assert.That(frames.ToArray(), Is.EqualTo(new[] { "first chunk" }));
                release.TrySetResult(true);

                Assert.That(responseSent.Task.Wait(3000), Is.True);
                Assert.That(responseSent.Task.Result, Is.True);
                CollectionAssert.AreEqual(new[] { "first chunk", "second chunk" },
                    System.Linq.Enumerable.Take(frames, 2));
                Assert.That(frames.Count, Is.EqualTo(19));
            }
            finally
            {
                writes.Close();
                release.TrySetResult(true);
            }
        }

        [Test]
        public void 송신_실패_후_대기_프레임을_버린다()
        {
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failed = new TaskCompletionSource<bool>();
            var frames = new ConcurrentQueue<string>();
            var writes = new OrderedWebSocketWrites(frame =>
            {
                frames.Enqueue(frame);
                return release.Task;
            }, () => failed.TrySetResult(true));

            Assert.That(writes.TryEnqueue(new[] { "first", "second" }, true), Is.True);
            Assert.That(writes.TryEnqueue(new[] { "response" }, false), Is.True);
            release.TrySetResult(false);

            Assert.That(failed.Task.Wait(3000), Is.True);
            Assert.That(frames.ToArray(), Is.EqualTo(new[] { "first" }));
            Assert.That(writes.TryEnqueue(new[] { "stale" }, false), Is.False);
        }
    }
}
