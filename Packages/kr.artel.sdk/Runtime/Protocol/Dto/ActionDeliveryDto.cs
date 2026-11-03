using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    internal sealed class ActionDeliveryDto
    {
        [JsonProperty("processId")]
        public string ProcessId { get; set; }

        [JsonProperty("expiresAtUnixMs")]
        public long ExpiresAtUnixMs { get; set; }
    }

    internal sealed class ActionAcceptedMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "ACTION_ACCEPTED";

        [JsonProperty("requestId")]
        public long RequestId { get; set; }

        [JsonProperty("processId")]
        public string ProcessId { get; set; }
    }
}
