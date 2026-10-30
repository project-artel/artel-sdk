using System;
using Artel.Protocol.Dto;
using Artel.Serialization;
using UnityEngine;

namespace Artel.Streaming
{
    /// <summary>
    /// The `type` discriminators from the streaming protocol, which the orchestration server owns:
    /// `artel-orchestration-server/docs/streaming-protocol.md`. Named here so a change to the
    /// contract is a compile-time search rather than a hunt through string literals.
    /// </summary>
    internal static class StreamMessageType
    {
        public const string StreamStart = "STREAM_START";
        public const string StreamRenew = "STREAM_RENEW";
        public const string StreamStop = "STREAM_STOP";
        public const string StreamState = "STREAM_STATE";
        public const string StreamFrameAck = "STREAM_FRAME_ACK";
        public const string Transport = "JPEG_WS";
    }

    internal static class StreamState
    {
        public const string Connecting = "CONNECTING";
        public const string Live = "LIVE";
        public const string Failed = "FAILED";
        public const string Stopped = "STOPPED";
    }

    /// <summary>
    /// Holds at most one <see cref="IArtelStreamSession"/> and is the only place that emits
    /// STREAM_STATE.
    /// </summary>
    internal sealed class ArtelStreamHost
    {
        private readonly IJsonCodec jsonCodec;
        private readonly IStreamSignalSender signals;
        private readonly IArtelStreamSessionFactory sessionFactory;
        private IArtelStreamSession session;

        public ArtelStreamHost(
            IJsonCodec jsonCodec,
            IStreamSignalSender signals,
            IArtelStreamSessionFactory sessionFactory)
        {
            this.jsonCodec = jsonCodec ?? throw new ArgumentNullException(nameof(jsonCodec));
            this.signals = signals ?? throw new ArgumentNullException(nameof(signals));
            this.sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        }

        public bool HasLiveSession { get { return session != null; } }

        /// <summary>
        /// Returns false for anything that is not streaming signalling, so the caller's existing
        /// message routing is left to handle it.
        /// </summary>
        public bool TryHandleMessage(string type, string json, float currentTime)
        {
            switch (type)
            {
                case StreamMessageType.StreamStart:
                    StartSession(jsonCodec.Deserialize<StreamStartMessageDto>(json), currentTime);
                    return true;

                case StreamMessageType.StreamRenew:
                    RenewSession(jsonCodec.Deserialize<StreamRenewMessageDto>(json), currentTime);
                    return true;

                case StreamMessageType.StreamStop:
                    StopSession(jsonCodec.Deserialize<StreamStopMessageDto>(json));
                    return true;

                case StreamMessageType.StreamFrameAck:
                    var acknowledgement = jsonCodec.Deserialize<StreamFrameAckMessageDto>(json);
                    if (acknowledgement != null && BelongsToCurrentSession(acknowledgement.StreamId))
                        session.AcceptFrameAck(acknowledgement);
                    return true;

                default:
                    return false;
            }
        }

        public void Tick(float currentTime)
        {
            session?.Tick(currentTime);
            if (session == null || !session.HasExpired(currentTime))
            {
                return;
            }

            // Lease expiry is the ordinary end of a stream, not a fault: the cases it exists for —
            // a closed lid, a killed browser, a dead server — never send STREAM_STOP.
            Debug.Log("[Artel] 영상 lease가 만료되어 전송을 종료합니다.");
            EndSession(StreamState.Stopped, null);
        }

        public void Stop()
        {
            EndSession(StreamState.Stopped, null);
        }

        // 이전 session의 종료 통지를 새 연결에 보내지 않고 리소스만 정리한다.
        public void Abandon()
        {
            DisposeSession();
        }

        private void StartSession(StreamStartMessageDto start, float currentTime)
        {
            if (start == null || string.IsNullOrEmpty(start.StreamId))
            {
                return;
            }

            if (start.LeaseSeconds <= 0)
            {
                // A non-positive lease would expire on the first tick, which looks like a stream
                // that connects and immediately dies rather than like a malformed message.
                Debug.LogWarning("[Artel] STREAM_START carried no usable lease, ignoring it.");
                return;
            }

            // STREAM_START arriving while a session is live replaces it. The server sends no
            // preceding STREAM_STOP, so replacement must never be an assumption about two
            // messages arriving in order.
            EndSession(StreamState.Stopped, null);

            if (start.Transport != StreamMessageType.Transport)
            {
                ReportState(start.StreamId, StreamState.Failed, "서버의 JPEG_WS 영상 전송 지원이 필요합니다.");
                return;
            }

            session = sessionFactory.Create(start);
            session.StateChanged += OnSessionStateChanged;
            try { session.Start(currentTime); }
            catch (Exception error)
            {
                try { ReportState(start.StreamId, StreamState.Failed, "영상 capture 시작 실패: " + error.Message); }
                finally { DisposeSession(); }
            }
        }

        private void RenewSession(StreamRenewMessageDto renew, float currentTime)
        {
            if (renew == null || !BelongsToCurrentSession(renew.StreamId))
            {
                return;
            }

            session.Renew(currentTime);
        }

        private void StopSession(StreamStopMessageDto stop)
        {
            if (stop == null || !BelongsToCurrentSession(stop.StreamId))
            {
                return;
            }

            EndSession(StreamState.Stopped, null);
        }

        /// <summary>
        /// 이전 session의 늦은 ACK나 STOP이 새 session을 바꾸지 않도록 streamId를 확인한다.
        /// </summary>
        private bool BelongsToCurrentSession(string streamId)
        {
            return session != null && session.StreamId == streamId;
        }

        private void OnSessionStateChanged(string state, string error)
        {
            if (session == null)
            {
                return;
            }

            if (state != StreamState.Failed)
            {
                ReportState(session.StreamId, state, error);
                return;
            }

            // 실패한 session이 texture와 capture loop를 계속 보유하지 않게 한다.
            try { ReportState(session.StreamId, state, error); }
            finally { DisposeSession(); }
        }

        private void EndSession(string state, string error)
        {
            if (session == null)
            {
                return;
            }

            try
            {
                ReportState(session.StreamId, state, error);
            }
            finally
            {
                DisposeSession();
            }
        }

        private void DisposeSession()
        {
            if (session == null)
            {
                return;
            }

            var endingSession = session;
            session = null;
            endingSession.StateChanged -= OnSessionStateChanged;
            endingSession.Stop();
        }

        private void ReportState(string streamId, string state, string error)
        {
            signals.Send(new StreamStateMessageDto
            {
                Type = StreamMessageType.StreamState,
                StreamId = streamId,
                State = state,
                Transport = StreamMessageType.Transport,
                Error = error
            });
        }
    }
}
