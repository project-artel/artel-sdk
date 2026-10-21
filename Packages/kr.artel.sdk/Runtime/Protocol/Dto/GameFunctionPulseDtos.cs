using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary><c>PULSE.functions</c>.</summary>
    internal sealed class FunctionsPulseDto
    {
        [JsonProperty("whole")]
        public bool Whole { get; set; }

        /// <summary><see cref="FunctionAvailabilityDto"/> 와 <see cref="FunctionRemovedDto"/> 가 섞인다.</summary>
        [JsonProperty("entries")]
        public List<object> Entries { get; set; } = new List<object>();
    }

    /// <summary>함수 하나가 한 대상 위에서 지금 쓸 수 있는지.</summary>
    internal sealed class FunctionAvailabilityDto
    {
        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("target", NullValueHandling = NullValueHandling.Include)]
        public string Target { get; set; }

        [JsonProperty("available")]
        public bool Available { get; set; }

        [JsonProperty("reason", NullValueHandling = NullValueHandling.Include)]
        public string Reason { get; set; }
    }

    /// <summary>
    /// 앞서 보낸 키가 사라졌다는 표시. <c>available</c> 과 <c>reason</c> 을 싣지 않는 것이 계약의 모양이다.
    /// </summary>
    internal sealed class FunctionRemovedDto
    {
        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("target", NullValueHandling = NullValueHandling.Include)]
        public string Target { get; set; }

        [JsonProperty("removed")]
        public bool Removed { get; set; } = true;
    }

    /// <summary><c>PULSE.behaviors</c> 의 항목 하나.</summary>
    internal sealed class RunningBehaviorDto
    {
        [JsonProperty("behaviorId")]
        public int BehaviorId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("target", NullValueHandling = NullValueHandling.Include)]
        public string Target { get; set; }

        [JsonProperty("seconds")]
        [JsonConverter(typeof(WholeNumberJsonConverter))]
        public double Seconds { get; set; }

        [JsonProperty("maxSeconds")]
        [JsonConverter(typeof(WholeNumberJsonConverter))]
        public double MaxSeconds { get; set; }

        [JsonProperty("report", NullValueHandling = NullValueHandling.Include)]
        public string Report { get; set; }
    }
}
