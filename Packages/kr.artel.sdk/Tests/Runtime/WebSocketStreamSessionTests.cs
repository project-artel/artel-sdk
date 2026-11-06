using System;
using System.Collections.Generic;
using Artel.Protocol.Dto;
using Artel.Serialization;
using Artel.Streaming;
using NUnit.Framework;

namespace Artel.Tests.Streaming
{
    public sealed class WebSocketStreamSessionTests
    {
        private sealed class Source : IJpegFrameSource
        {
            public Func<bool> CanCapture;
            public Action<byte[]> Captured;
            public bool Stopped;
            public void Start(Func<bool> canCapture, Action<byte[]> captured, Action<string> failed) { CanCapture = canCapture; Captured = captured; }
            public void Stop() { Stopped = true; }
            public void Frame(int length = 32) { if (CanCapture()) Captured(new byte[length]); }
        }

        private sealed class Sender : IStreamFrameSender
        {
            public readonly List<StreamFrameMessageDto> Frames = new List<StreamFrameMessageDto>();
            public Action<bool> Completed;
            public bool Queue = true;
            public bool TrySendFrame(StreamFrameMessageDto frame, Action<bool> completed)
            {
                if (!Queue) return false;
                Frames.Add(frame); Completed = completed; return true;
            }
        }

        private static ArtelStreamSession Session(Sender sender, Source source)
        {
            var session = new ArtelStreamSession(sender, source, new StreamStartMessageDto
            {
                StreamId = "one", Transport = "JPEG_WS", LeaseSeconds = 90,
                Video = new StreamVideoConstraintsDto { MaxFrameBytes = 131072 }
            });
            session.Start(0); return session;
        }

        private static StreamFrameAckMessageDto Ack(long id, bool accepted = true, string streamId = "one")
        {
            return new StreamFrameAckMessageDto { StreamId = streamId, FrameId = id, Accepted = accepted };
        }

        [Test]
        public void 송신과_ACK가_완료되기_전에는_다음_capture를_시작하지_않는다()
        {
            var source = new Source(); var sender = new Sender(); var session = Session(sender, source);
            source.Frame(); source.Frame();
            Assert.That(sender.Frames.Count, Is.EqualTo(1));
            session.AcceptFrameAck(Ack(1)); source.Frame();
            Assert.That(sender.Frames.Count, Is.EqualTo(1));
            sender.Completed(true); source.Frame();
            Assert.That(sender.Frames.Count, Is.EqualTo(2));
            Assert.That(sender.Frames[1].FrameId, Is.EqualTo(2));
            session.Stop();
        }

        [Test]
        public void 다른_stream과_이전_frame의_ACK는_현재_대기를_풀지_않는다()
        {
            var source = new Source(); var sender = new Sender(); var session = Session(sender, source);
            source.Frame(); sender.Completed(true);
            session.AcceptFrameAck(Ack(1, streamId: "old"));
            session.AcceptFrameAck(Ack(0)); source.Frame();
            Assert.That(sender.Frames.Count, Is.EqualTo(1));
            session.AcceptFrameAck(Ack(1, false)); source.Frame();
            Assert.That(sender.Frames.Count, Is.EqualTo(2));
            session.Stop();
        }

        [Test]
        public void ACK_시간초과는_capture를_종료하고_실패를_보고한다()
        {
            var source = new Source(); var sender = new Sender(); var session = Session(sender, source);
            string state = null; session.StateChanged += (value, error) => state = value;
            source.Frame(); sender.Completed(true); session.Tick(10);
            Assert.That(state, Is.EqualTo(StreamState.Failed));
            Assert.That(source.Stopped, Is.True);
            Assert.That(source.CanCapture(), Is.False);
        }

        [Test]
        public void 종료_후_늦은_callback과_ACK는_새_frame을_만들지_않는다()
        {
            var source = new Source(); var sender = new Sender(); var session = Session(sender, source);
            source.Frame(); var callback = sender.Completed; session.Stop();
            callback(false); session.AcceptFrameAck(Ack(1)); session.Tick(20); source.Frame();
            Assert.That(sender.Frames.Count, Is.EqualTo(1));
            Assert.That(source.Stopped, Is.True);
        }

        [Test]
        public void 최대_JPEG를_넘는_frame은_전송하지_않고_ACK도_기다리지_않는다()
        {
            var source = new Source(); var sender = new Sender(); var session = Session(sender, source);
            source.Frame(131073); Assert.That(sender.Frames, Is.Empty);
            source.Frame(131072); Assert.That(sender.Frames.Count, Is.EqualTo(1));
            Assert.That(Convert.FromBase64String(sender.Frames[0].Jpeg).Length, Is.EqualTo(131072));
            session.Stop();
        }

        [Test]
        public void 송신_실패는_main_thread의_다음_tick에서_보고한다()
        {
            var source = new Source(); var sender = new Sender { Queue = false }; var session = Session(sender, source);
            string state = null; session.StateChanged += (value, error) => state = value;
            source.Frame(); Assert.That(state, Is.Null);
            session.Tick(1); Assert.That(state, Is.EqualTo(StreamState.Failed));
            Assert.That(source.Stopped, Is.True);
        }

        [Test]
        public void ACK_후에도_송신_callback이_끝나지_않으면_시간초과로_종료한다()
        {
            var source = new Source(); var sender = new Sender(); var session = Session(sender, source);
            string state = null; session.StateChanged += (value, error) => state = value;
            source.Frame(); session.AcceptFrameAck(Ack(1)); session.Tick(10);
            Assert.That(state, Is.EqualTo(StreamState.Failed));
            Assert.That(source.Stopped, Is.True);
        }
    }
}
