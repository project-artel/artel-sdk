using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>frame 전송의 CONNECTING·LIVE·FAILED·STOPPED 상태와 transport 지원 여부를 보고한다.</summary>
    internal sealed class StreamStateMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("streamId")]
        public string StreamId { get; set; }

        [JsonProperty("state")]
        public string State { get; set; }

        [JsonProperty("transport")]
        public string Transport { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }
    }
}
