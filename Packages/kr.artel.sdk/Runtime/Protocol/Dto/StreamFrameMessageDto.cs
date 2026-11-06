using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    internal sealed class StreamFrameMessageDto
    {
        [JsonProperty("type")] public string Type { get; set; } = "STREAM_FRAME";
        [JsonProperty("streamId")] public string StreamId { get; set; }
        [JsonProperty("frameId")] public long FrameId { get; set; }
        [JsonProperty("jpeg")] public string Jpeg { get; set; }
    }

    internal sealed class StreamFrameAckMessageDto
    {
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("streamId")] public string StreamId { get; set; }
        [JsonProperty("frameId")] public long FrameId { get; set; }
        [JsonProperty("accepted")] public bool Accepted { get; set; }
    }
}
