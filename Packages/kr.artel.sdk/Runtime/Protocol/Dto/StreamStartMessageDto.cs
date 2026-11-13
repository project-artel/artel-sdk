using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>서버가 정한 JPEG 전송 상한과 lease로 영상 session을 연다.</summary>
    internal sealed class StreamStartMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("streamId")]
        public string StreamId { get; set; }

        [JsonProperty("transport")]
        public string Transport { get; set; }

        [JsonProperty("iceServers")]
        public List<StreamIceServerDto> IceServers { get; set; } = new List<StreamIceServerDto>();

        [JsonProperty("video")]
        public StreamVideoConstraintsDto Video { get; set; }

        [JsonProperty("leaseSeconds")]
        public long LeaseSeconds { get; set; }
    }
}
