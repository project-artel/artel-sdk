using Artel.Protocol.Dto;

namespace Artel
{
    /// <summary><c>RUN_STATUS</c> 가 실어오는 <c>state</c> 값 (ARTEL-835).</summary>
    internal static class RunStatusState
    {
        public const string WaitingAgent = "WAITING_AGENT";
        public const string Running = "RUNNING";
        public const string Finished = "FINISHED";
    }

    /// <summary>
    /// 라벨 판 아래 상태 줄에 쓸 한 줄짜리 문구를 만든다 (ARTEL-835).
    /// </summary>
    /// <remarks>
    /// 라벨은 창이 왜 떴는지를 말하고, 이 줄은 그 창에서 지금 무엇이 도는지를 말한다 —
    /// 어느 프로젝트인지, 어느 테스트 run 인지, agent server 세션이 붙었는지, run 이
    /// 끝났는지. 넷 다 <see cref="Describe"/> 한 줄에 싣는다.
    /// </remarks>
    internal static class RunStatusLine
    {
        /// <summary>
        /// <c>RUN_STATUS</c> 가 한 번도 오지 않은 자리에 쓴다. 빈 문자열을 두면 그리다가
        /// 실패해 줄이 빈 것과 가려지지 않는다 — 그래서 뭐라도 쓴 채로 시작한다.
        /// </summary>
        public const string NoRunYet = "아직 시작된 run 이 없습니다.";

        public static string Describe(RunStatusMessageDto message)
        {
            if (message == null)
            {
                return NoRunYet;
            }

            return "project " + message.ProjectName +
                   " · test run " + message.TestRunName +
                   " · " + DescribeState(message.State, message.Outcome);
        }

        private static string DescribeState(string state, string outcome)
        {
            switch (state)
            {
                case RunStatusState.WaitingAgent:
                    return "agent session 기다리는 중";

                case RunStatusState.Running:
                    return "agent session 붙음";

                case RunStatusState.Finished:
                    return string.IsNullOrEmpty(outcome)
                        ? "run 끝남"
                        : "run 끝남 (" + outcome + ")";

                default:
                    // 이 SDK 가 아직 모르는 state 다. 지난 문구를 그대로 두거나 줄을 비우면
                    // 화면이 멈춘 것과 새 state 가 온 것을 가릴 수 없으므로, state 원문을
                    // 그대로 실어 무엇이 왔는지 화면에서 바로 읽게 한다.
                    return "알 수 없는 state: " + state;
            }
        }
    }
}
