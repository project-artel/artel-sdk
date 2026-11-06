namespace Artel
{
    /// <summary>
    /// 도는 behavior 가 SDK 에 말하는 통로. behavior 메서드의 마지막 parameter 로 받는다.
    /// </summary>
    public sealed class ArtelBehaviorContext
    {
        /// <summary><see cref="Report"/> 가 남기는 글자 수 상한.</summary>
        public const int MaxReportLength = 200;

        internal string LatestReport { get; private set; }

        internal BehaviorEndRequest EndRequest { get; private set; }

        internal string EndReason { get; private set; }

        /// <summary>
        /// agent 가 behavior 가 도는 동안 보는 한 줄. 마지막 것만 남고 200 자에서 자른다.
        /// </summary>
        public void Report(string text)
        {
            LatestReport = text != null && text.Length > MaxReportLength
                ? text.Substring(0, MaxReportLength)
                : text;
        }

        /// <summary>behavior 를 <c>completed</c> 로 끝낸다. 보통 바로 뒤에 <c>yield break</c> 를 쓴다.</summary>
        public void Complete(string reason = null)
        {
            RequestEnd(BehaviorEndRequest.Complete, reason);
        }

        /// <summary>behavior 를 <c>failed</c> 로 끝낸다.</summary>
        public void Fail(string reason)
        {
            RequestEnd(BehaviorEndRequest.Fail, reason);
        }

        // 먼저 온 요청이 이긴다. Complete 뒤에 finally 가 Fail 을 불러도 끝난 이유가 바뀌지 않는다.
        private void RequestEnd(BehaviorEndRequest request, string reason)
        {
            if (EndRequest != BehaviorEndRequest.None)
            {
                return;
            }

            EndRequest = request;
            EndReason = reason;
        }
    }

    internal enum BehaviorEndRequest
    {
        None,
        Complete,
        Fail
    }
}
