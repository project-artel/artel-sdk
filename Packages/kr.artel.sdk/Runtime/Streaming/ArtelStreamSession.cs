using System;
using Artel.Protocol.Dto;
using UnityEngine;

namespace Artel.Streaming
{
    internal interface IArtelStreamSession
    {
        string StreamId { get; }
        event Action<string, string> StateChanged;
        void Start(float currentTime);
        void Renew(float currentTime);
        bool HasExpired(float currentTime);
        void Tick(float currentTime);
        void AcceptFrameAck(StreamFrameAckMessageDto acknowledgement);
        void Stop();
    }

    internal interface IArtelStreamSessionFactory
    {
        IArtelStreamSession Create(StreamStartMessageDto start);
    }

    internal interface IJpegFrameSource
    {
        void Start(Func<bool> canCapture, Action<byte[]> captured, Action<string> failed);
        void Stop();
    }

    /// <summary>소켓 송신과 ACK를 각각 하나로 제한해 느린 네트워크에 화면이 쌓이지 않게 한다.</summary>
    internal sealed class ArtelStreamSession : IArtelStreamSession
    {
        private const float AcknowledgementTimeoutSeconds = 10f;
        private readonly IStreamFrameSender frames;
        private readonly IJpegFrameSource source;
        private readonly StreamLease lease;
        private readonly int maxBytes;
        private readonly object gate = new object();
        private bool stopped;
        private bool waitingForAck;
        private bool sending;
        private bool sendFailed;
        private bool live;
        private long frameId;
        private long sendVersion;
        private float currentTime;
        private float sentAt;

        public ArtelStreamSession(IStreamFrameSender frames, IJpegFrameSource source, StreamStartMessageDto start)
        {
            this.frames = frames ?? throw new ArgumentNullException(nameof(frames));
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            if (start == null) throw new ArgumentNullException(nameof(start));
            StreamId = start.StreamId;
            lease = new StreamLease(start.LeaseSeconds);
            maxBytes = Mathf.Clamp(start.Video != null ? start.Video.MaxFrameBytes : 131072, 1024, 131072);
        }

        public string StreamId { get; private set; }
        public event Action<string, string> StateChanged;

        public void Start(float time)
        {
            currentTime = time;
            lease.Renew(time);
            source.Start(CanCapture, Captured, error => { Stop(); Report(StreamState.Failed, error); });
            Report(StreamState.Connecting, null);
        }

        public void Renew(float time) { lease.Renew(time); }
        public bool HasExpired(float time) { return lease.HasExpired(time); }

        public void Tick(float time)
        {
            currentTime = time;
            string error = null;
            lock (gate)
            {
                if (stopped) return;
                if (sendFailed) error = "영상 frame 전송에 실패했습니다.";
                else if ((waitingForAck || sending) && time - sentAt >= AcknowledgementTimeoutSeconds)
                    error = "영상 frame ACK를 10초 동안 받지 못했습니다.";
            }
            if (error != null)
            {
                Stop();
                Report(StreamState.Failed, error);
            }
        }

        private bool CanCapture()
        {
            lock (gate) return !stopped && !sending && !waitingForAck;
        }

        private void Captured(byte[] jpeg)
        {
            if (jpeg == null || jpeg.Length == 0 || jpeg.Length > maxBytes) return;
            long id;
            long version;
            lock (gate)
            {
                if (stopped || sending || waitingForAck) return;
                id = ++frameId;
                version = ++sendVersion;
                waitingForAck = true;
                sending = true;
                sentAt = currentTime;
            }
            var message = new StreamFrameMessageDto
            {
                StreamId = StreamId,
                FrameId = id,
                Jpeg = Convert.ToBase64String(jpeg)
            };
            var queued = frames.TrySendFrame(message, success =>
            {
                lock (gate)
                {
                    if (stopped || version != sendVersion) return;
                    sending = false;
                    if (!success) sendFailed = true;
                }
            });
            if (!queued)
            {
                lock (gate)
                {
                    if (stopped || version != sendVersion) return;
                    sending = false;
                    waitingForAck = false;
                    sendFailed = true;
                }
            }
        }

        public void AcceptFrameAck(StreamFrameAckMessageDto acknowledgement)
        {
            bool becameLive;
            lock (gate)
            {
                if (stopped || acknowledgement == null || acknowledgement.StreamId != StreamId ||
                    !waitingForAck || acknowledgement.FrameId != frameId) return;
                waitingForAck = false;
                becameLive = acknowledgement.Accepted && !live;
                if (becameLive) live = true;
            }
            if (becameLive) Report(StreamState.Live, null);
        }

        public void Stop()
        {
            lock (gate)
            {
                if (stopped) return;
                stopped = true;
                sendVersion++;
                waitingForAck = false;
            }
            // 네트워크 callback을 기다리지 않고 Unity texture와 capture loop부터 해제한다.
            source.Stop();
        }

        private void Report(string state, string error)
        {
            var handler = StateChanged;
            if (handler != null) handler(state, error);
        }
    }

    internal sealed class WebSocketStreamSessionFactory : IArtelStreamSessionFactory
    {
        private readonly MonoBehaviour host;
        private readonly IStreamFrameSender frames;
        public WebSocketStreamSessionFactory(MonoBehaviour host, IStreamFrameSender frames)
        {
            this.host = host != null ? host : throw new ArgumentNullException(nameof(host));
            this.frames = frames ?? throw new ArgumentNullException(nameof(frames));
        }
        public IArtelStreamSession Create(StreamStartMessageDto start)
        {
            return new ArtelStreamSession(frames, new ScreenJpegFrameSource(host, start.Video), start);
        }
    }

    internal sealed class ScreenJpegFrameSource : IJpegFrameSource
    {
        private readonly ScreenVideoSource source;
        private readonly int quality;
        private readonly int maxBytes;
        private readonly int maxHeight;
        public ScreenJpegFrameSource(MonoBehaviour host, StreamVideoConstraintsDto video)
        {
            var width = Mathf.Clamp(video != null ? video.MaxWidth : 1280, 2, 1280);
            var rate = Mathf.Clamp(video != null ? video.MaxFramerate : 10, 1, 10);
            quality = Mathf.Clamp(video != null ? video.JpegQuality : 65, 20, 80);
            maxBytes = Mathf.Clamp(video != null ? video.MaxFrameBytes : 131072, 1024, 131072);
            maxHeight = Mathf.Clamp(video != null ? video.MaxHeight : 720, 2, 720);
            source = new ScreenVideoSource(new BehaviourCaptureLoopRunner(host), width, rate);
        }
        public void Start(Func<bool> canCapture, Action<byte[]> captured, Action<string> failed)
        {
            source.ConfigureJpeg(canCapture, captured, failed, quality, maxBytes, maxHeight);
            source.Start();
        }
        public void Stop() { source.Stop(); }
    }
}
