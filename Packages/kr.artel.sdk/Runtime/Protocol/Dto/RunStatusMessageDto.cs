using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// orchestration 이 보내는 <c>RUN_STATUS</c>. 창이 왜 떴는지는 <see cref="ArtelWindowLabel"/> 이
    /// 말하고, 이것은 그 창에서 지금 무엇이 도는지를 말한다 (ARTEL-835).
    /// </summary>
    internal sealed class RunStatusMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary><see cref="RunStatusState"/> 중 하나. 서버가 아직 보내지 않은 값이 올 수도 있다.</summary>
        [JsonProperty("state")]
        public string State { get; set; }

        [JsonProperty("projectName")]
        public string ProjectName { get; set; }

        [JsonProperty("testRunName")]
        public string TestRunName { get; set; }

        [JsonProperty("qaRunId")]
        public long QaRunId { get; set; }

        [JsonProperty("qaTryId")]
        public long QaTryId { get; set; }

        /// <summary>null 일 수 있다.</summary>
        [JsonProperty("label")]
        public string Label { get; set; }

        /// <summary><c>FINISHED</c> 에서만 실려 오고, 그 밖에는 null 이다.</summary>
        [JsonProperty("outcome")]
        public string Outcome { get; set; }

        [JsonProperty("at")]
        public string At { get; set; }
    }
}
