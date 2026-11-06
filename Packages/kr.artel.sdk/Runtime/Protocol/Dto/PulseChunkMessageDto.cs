using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    internal sealed class PulseChunkMessageDto
    {
        [JsonProperty("type")] public string Type { get; } = "PULSE_CHUNK";
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("index")] public int Index { get; set; }
        [JsonProperty("count")] public int Count { get; set; }
        [JsonProperty("data")] public string Data { get; set; }
    }
}
