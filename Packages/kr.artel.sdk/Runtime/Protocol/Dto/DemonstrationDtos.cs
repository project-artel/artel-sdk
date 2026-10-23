using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    internal sealed class DemonstrationReferenceDto
    {
        [JsonProperty("sessionId")] public string SessionId { get; set; }
        [JsonProperty("generation")] public long Generation { get; set; }
    }

    internal sealed class DemonstrationControlDto
    {
        [JsonProperty("sessionId")] public string SessionId { get; set; }
        [JsonProperty("generation")] public long Generation { get; set; }
        [JsonProperty("operation")] public string Operation { get; set; }
        [JsonProperty("leaseMilliseconds")] public int LeaseMilliseconds { get; set; }
    }

    internal sealed class DemonstrationControlResultDto
    {
        [JsonProperty("type")] public string Type => "DEMONSTRATION_CONTROL_RESULT";
        [JsonProperty("sessionId")] public string SessionId { get; set; }
        [JsonProperty("generation")] public long Generation { get; set; }
        [JsonProperty("operation")] public string Operation { get; set; }
        [JsonProperty("accepted")] public bool Accepted { get; set; }
    }

    internal sealed class DemonstrationCapabilitiesDto
    {
        [JsonProperty("type")] public string Type => "DEMONSTRATION_CAPABILITIES";
        [JsonProperty("protocolVersion")] public int ProtocolVersion => 1;
        [JsonProperty("methods")] public string[] Methods => new[]
        {
            "move_mouse", "mouse_down", "mouse_up", "key_down", "key_up", "enter_text"
        };
    }
}
