using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    internal sealed class SdkRegistrationResponseDto
    {
        [JsonProperty("instanceId")]
        public string InstanceId { get; set; }

        [JsonProperty("projectId")]
        public string ProjectId { get; set; }

        [JsonProperty("instanceName")]
        public string InstanceName { get; set; }

        [JsonProperty("gameBuildId")]
        public string GameBuildId { get; set; }

        [JsonProperty("gameVersion")]
        public string GameVersion { get; set; }

        // 이전 서버는 이 필드를 주지 않는다. 없으면 false다.
        [JsonProperty("isEditor")]
        public bool IsEditor { get; set; }
    }
}
