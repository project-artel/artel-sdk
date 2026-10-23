using System;
using Artel.Protocol.Dto;

namespace Artel
{
    internal sealed class DemonstrationLease
    {
        private readonly Func<double> clock;
        private long lastGeneration;
        private string sessionId;
        private double expiresAt;

        internal DemonstrationLease(Func<double> clock) { this.clock = clock; }
        internal bool Active => sessionId != null && clock() < expiresAt;
        internal bool HasLease => sessionId != null;

        internal bool Apply(DemonstrationControlDto control)
        {
            if (control == null || string.IsNullOrWhiteSpace(control.SessionId) || control.Generation <= 0)
                return false;
            if (control.Operation == "RELEASE")
            {
                if (!Matches(control.SessionId, control.Generation)) return false;
                Invalidate();
                return true;
            }
            if (control.LeaseMilliseconds <= 0 || control.LeaseMilliseconds > 30000) return false;
            if (control.Operation == "ACQUIRE")
            {
                if (control.Generation <= lastGeneration) return false;
                lastGeneration = control.Generation;
                sessionId = control.SessionId;
            }
            else if (control.Operation != "RENEW" || !Matches(control.SessionId, control.Generation))
                return false;
            expiresAt = clock() + control.LeaseMilliseconds / 1000d;
            return true;
        }

        internal bool Allows(DemonstrationReferenceDto reference)
        {
            return reference == null ? !Active : Matches(reference.SessionId, reference.Generation);
        }

        internal bool Expire()
        {
            if (sessionId == null || Active) return false;
            Invalidate();
            return true;
        }

        internal void Invalidate()
        {
            // generation은 연결이 바뀌어도 보존해 지연된 ACQUIRE를 거절한다.
            sessionId = null;
            expiresAt = 0;
        }

        private bool Matches(string candidateSession, long generation)
        {
            return Active && sessionId == candidateSession && lastGeneration == generation;
        }
    }
}
