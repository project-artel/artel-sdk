using Artel.Protocol.Dto;
using Artel.Serialization;
using NUnit.Framework;

namespace Artel.Tests
{
    /// <summary>
    /// <c>RUN_STATUS</c> 파싱과 상태 줄 문구를 검증한다 (ARTEL-835).
    /// </summary>
    public sealed class RunStatusLineTests
    {
        private static readonly IJsonCodec Codec = new NewtonsoftJsonCodec();

        [Test]
        public void Describe_BeforeAnyMessage_SaysThereIsNoRunYet()
        {
            Assert.That(RunStatusLine.Describe(null), Is.EqualTo(RunStatusLine.NoRunYet));
        }

        [Test]
        public void Describe_WaitingAgent_SaysTheSessionHasNotAttached()
        {
            var message = Parse(RunStatusJson("WAITING_AGENT", outcome: null));

            Assert.That(
                RunStatusLine.Describe(message),
                Is.EqualTo("project WordVenture · test run 타이틀에서 전투까지 · agent session 기다리는 중"));
        }

        [Test]
        public void Describe_Running_SaysTheSessionHasAttached()
        {
            var message = Parse(RunStatusJson("RUNNING", outcome: null));

            Assert.That(
                RunStatusLine.Describe(message),
                Is.EqualTo("project WordVenture · test run 타이틀에서 전투까지 · agent session 붙음"));
        }

        [Test]
        public void Describe_Finished_CarriesTheOutcome()
        {
            var message = Parse(RunStatusJson("FINISHED", outcome: "PASSED"));

            Assert.That(
                RunStatusLine.Describe(message),
                Is.EqualTo("project WordVenture · test run 타이틀에서 전투까지 · run 끝남 (PASSED)"));
        }

        /// <summary>outcome 은 null 일 수 있다 — FINISHED 라도 예외는 아니다.</summary>
        [Test]
        public void Describe_FinishedWithoutOutcome_OmitsTheParentheses()
        {
            var message = Parse(RunStatusJson("FINISHED", outcome: null));

            Assert.That(
                RunStatusLine.Describe(message),
                Is.EqualTo("project WordVenture · test run 타이틀에서 전투까지 · run 끝남"));
        }

        /// <summary>
        /// 이 SDK 가 모르는 state 는 던지지 않는다. state 원문을 그대로 실어, 화면에서
        /// 새 state 가 왔다는 것을 바로 읽을 수 있게 한다.
        /// </summary>
        [Test]
        public void Describe_UnknownState_ShowsTheRawValueInsteadOfThrowing()
        {
            var message = Parse(RunStatusJson("SOMETHING_NEW", outcome: null));

            Assert.That(() => RunStatusLine.Describe(message), Throws.Nothing);
            Assert.That(
                RunStatusLine.Describe(message),
                Is.EqualTo("project WordVenture · test run 타이틀에서 전투까지 · 알 수 없는 state: SOMETHING_NEW"));
        }

        [Test]
        public void RunStatusMessageDto_ReadsLabelAndOutcomeAsNull_WhenTheWireSendsNull()
        {
            var message = Parse(RunStatusJson("WAITING_AGENT", outcome: null));

            Assert.That(message.Label, Is.Null);
            Assert.That(message.Outcome, Is.Null);
        }

        [Test]
        public void RunStatusMessageDto_ReadsTheIdentifyingFieldsFromTheWire()
        {
            var message = Parse(RunStatusJson("RUNNING", outcome: null));

            Assert.That(message.Type, Is.EqualTo("RUN_STATUS"));
            Assert.That(message.ProjectName, Is.EqualTo("WordVenture"));
            Assert.That(message.TestRunName, Is.EqualTo("타이틀에서 전투까지"));
            Assert.That(message.QaRunId, Is.EqualTo(41));
            Assert.That(message.QaTryId, Is.EqualTo(77));
        }

        private static RunStatusMessageDto Parse(string json)
        {
            return Codec.Deserialize<RunStatusMessageDto>(json);
        }

        private static string RunStatusJson(string state, string outcome)
        {
            var outcomeLiteral = outcome == null ? "null" : "\"" + outcome + "\"";
            return "{\"type\":\"RUN_STATUS\",\"state\":\"" + state + "\"," +
                   "\"projectName\":\"WordVenture\",\"testRunName\":\"타이틀에서 전투까지\"," +
                   "\"qaRunId\":41,\"qaTryId\":77,\"label\":null,\"outcome\":" + outcomeLiteral + "," +
                   "\"at\":\"2026-09-04T16:30:00Z\"}";
        }
    }
}
