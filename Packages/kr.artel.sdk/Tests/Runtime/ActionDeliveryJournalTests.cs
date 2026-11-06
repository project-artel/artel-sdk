using System;
using Artel.Protocol.Dto;
using NUnit.Framework;

namespace Artel.Tests
{
    public sealed class ActionDeliveryJournalTests
    {
        private static ArtelRequestDto Request(ActionDeliveryJournal journal, long id = 41, long expiry = 100)
        {
            return new ArtelRequestDto
            {
                Id = id, Type = "ACTION",
                Delivery = new ActionDeliveryDto { ProcessId = journal.ProcessId, ExpiresAtUnixMs = expiry }
            };
        }

        [Test]
        public void 같은_요청은_실행_등록을_한_번만_한다()
        {
            var journal = new ActionDeliveryJournal();
            var request = Request(journal);
            var first = journal.Register(request, "wire", 1, out var added, out var error);
            Assert.That(added, Is.True);
            Assert.That(error, Is.Null);
            var duplicate = journal.Register(request, "wire", 2, out added, out error);
            Assert.That(added, Is.False);
            Assert.That(duplicate, Is.SameAs(first));
            Assert.That(duplicate.Result, Is.Null);
        }

        [Test]
        public void 완료한_결과는_전송_실패와_취소에도_덮어쓰지_않는다()
        {
            var journal = new ActionDeliveryJournal();
            var entry = journal.Register(Request(journal), "wire", 1, out _, out _);
            entry.Complete("{\"frame\":19,\"success\":true}");
            entry.Complete("unknown");
            Assert.That(entry.Result, Is.EqualTo("{\"frame\":19,\"success\":true}"));
        }

        [Test]
        public void 같은_ID의_다른_본문은_원래_실행과_결과를_바꾸지_않는다()
        {
            var journal = new ActionDeliveryJournal();
            var request = Request(journal);
            var entry = journal.Register(request, "first", 1, out _, out _);
            Assert.That(journal.Register(request, "second", 2, out var added, out var error), Is.Null);
            Assert.That(added, Is.False);
            Assert.That(error, Does.Contain("different request body"));
            Assert.That(entry.Text, Is.EqualTo("first"));
        }

        [Test]
        public void 만료한_미등록_요청은_실행하지_않는다()
        {
            var journal = new ActionDeliveryJournal();
            Assert.That(journal.Register(Request(journal), "wire", 100, out var added, out var error), Is.Null);
            Assert.That(added, Is.False);
            Assert.That(error, Does.Contain("expired"));
        }

        [Test]
        public void 만료한_완료_중복도_기존_결과를_읽을_수_있다()
        {
            var journal = new ActionDeliveryJournal();
            var request = Request(journal);
            var entry = journal.Register(request, "wire", 1, out _, out _);
            entry.Complete("cached");
            var duplicate = journal.Register(request, "wire", 101, out var added, out var error);
            Assert.That(added, Is.False);
            Assert.That(error, Is.Null);
            Assert.That(duplicate.Result, Is.EqualTo("cached"));
        }

        [Test]
        public void 다른_process의_요청은_기존_ID와_같아도_거절한다()
        {
            var journal = new ActionDeliveryJournal();
            var request = Request(journal);
            journal.Register(request, "wire", 1, out _, out _);
            request.Delivery.ProcessId = Guid.NewGuid().ToString();
            Assert.That(journal.Register(request, "wire", 2, out _, out var error), Is.Null);
            Assert.That(error, Does.Contain("different SDK process"));
        }

        [Test]
        public void 용량이_가득_차도_실행_중인_요청은_제거하지_않는다()
        {
            var journal = new ActionDeliveryJournal(1);
            var request = Request(journal);
            var entry = journal.Register(request, "wire", 1, out _, out _);
            Assert.That(journal.Register(Request(journal, 42, 300), "other", 101, out _, out var error), Is.Null);
            Assert.That(error, Does.Contain("full"));
            Assert.That(journal.Register(request, "wire", 101, out _, out _), Is.SameAs(entry));
        }

        [Test]
        public void 만료한_완료_항목만_제거해_새_요청의_공간을_만든다()
        {
            var journal = new ActionDeliveryJournal(1);
            var request = Request(journal);
            journal.Register(request, "wire", 1, out _, out _).Complete("done");
            Assert.That(journal.Register(Request(journal, 42, 300), "other", 101, out var added, out _), Is.Not.Null);
            Assert.That(added, Is.True);
            Assert.That(journal.Register(request, "wire", 102, out _, out var error), Is.Null);
            Assert.That(error, Does.Contain("expired"));
        }

        [Test]
        public void 같은_서버와_instance의_재접속은_process와_journal을_유지한다()
        {
            var server = new Uri("ws://delivery-test.example:8090/");
            var first = ActionDeliveryJournal.ForTarget(server, "stable");
            Assert.That(ActionDeliveryJournal.ForTarget(server, "stable"), Is.SameAs(first));
            var differentInstance = ActionDeliveryJournal.ForTarget(server, "different");
            Assert.That(differentInstance.ProcessId, Is.Not.EqualTo(first.ProcessId));
            var differentServer = ActionDeliveryJournal.ForTarget(new Uri("ws://other.example:8090/"), "different");
            Assert.That(differentServer.ProcessId, Is.Not.EqualTo(differentInstance.ProcessId));
        }
    }
}
