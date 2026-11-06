using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary><c>invoke_cheat</c> 의 <c>returnValue</c>.</summary>
    internal sealed class CheatInvokedDto
    {
        /// <summary>cheat 가 돌려준 문자열. 없으면 null 이고, 키는 언제나 있다.</summary>
        [JsonProperty("message", NullValueHandling = NullValueHandling.Include)]
        public string Message { get; set; }
    }

    /// <summary><c>start_behavior</c> 의 <c>returnValue</c>.</summary>
    internal sealed class BehaviorStartedDto
    {
        [JsonProperty("behaviorId")]
        public int BehaviorId { get; set; }

        /// <summary>첫 <c>MoveNext()</c> 뒤의 report. 없으면 null.</summary>
        [JsonProperty("report", NullValueHandling = NullValueHandling.Include)]
        public string Report { get; set; }
    }

    /// <summary><c>stop_behavior</c> 의 <c>returnValue</c>.</summary>
    internal sealed class BehaviorsStoppedDto
    {
        [JsonProperty("stopped")]
        public List<int> Stopped { get; set; } = new List<int>();
    }

    /// <summary><c>extend_behavior</c> 의 <c>returnValue</c>.</summary>
    internal sealed class BehaviorExtendedDto
    {
        [JsonProperty("maxSeconds")]
        [JsonConverter(typeof(WholeNumberJsonConverter))]
        public double MaxSeconds { get; set; }
    }
}
