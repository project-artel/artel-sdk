using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary><c>BEHAVIOR_ENDED</c>. behavior 하나가 어떤 이유로든 끝날 때 한 번 보낸다.</summary>
    internal sealed class BehaviorEndedMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "BEHAVIOR_ENDED";

        [JsonProperty("behaviorId")]
        public int BehaviorId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>static behavior 면 null.</summary>
        [JsonProperty("target", NullValueHandling = NullValueHandling.Include)]
        public string Target { get; set; }

        /// <summary><see cref="BehaviorEndReason"/> 중 하나.</summary>
        [JsonProperty("reason")]
        public string Reason { get; set; }

        [JsonProperty("detail", NullValueHandling = NullValueHandling.Include)]
        public string Detail { get; set; }

        /// <summary>게임 시간으로 돈 초. 소수 한 자리.</summary>
        [JsonProperty("seconds")]
        [JsonConverter(typeof(WholeNumberJsonConverter))]
        public double Seconds { get; set; }

        [JsonProperty("frame")]
        public int Frame { get; set; }
    }

    /// <summary><c>BEHAVIOR_ENDED.reason</c> 의 값. 계약에 적힌 그대로다.</summary>
    internal static class BehaviorEndReason
    {
        public const string Stopped = "stopped";
        public const string NotAllowed = "not_allowed";
        public const string Completed = "completed";
        public const string Failed = "failed";
        public const string Timeout = "timeout";
        public const string Replaced = "replaced";
        public const string TargetGone = "target_gone";
        public const string Unavailable = "unavailable";
        public const string RunEnded = "run_ended";
    }
}
