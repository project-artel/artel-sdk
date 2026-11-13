using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    internal sealed class SdkRegistrationRequestDto
    {
        [JsonProperty("projectId")]
        public string ProjectId { get; set; }

        // 인스턴스를 가르는 값. 서버는 (projectId, sdkUuid)로 인스턴스를 찾거나 만든다.
        [JsonProperty("sdkUuid")]
        public string SdkUuid { get; set; }

        // 대시보드에서 인스턴스를 알아볼 첫 이름. 서버가 선택 필드로 받으므로 비면 보내지 않는다.
        [JsonProperty("instanceName", NullValueHandling = NullValueHandling.Ignore)]
        public string InstanceName { get; set; }

        [JsonProperty("gameVersion")]
        public string GameVersion { get; set; }

        // 에디터 실행인지 development build인지를 서버가 가르는 값. 에디터 실행은 별도 game build 행에 기록된다.
        [JsonProperty("isEditor")]
        public bool IsEditor { get; set; }

        // 스캔이 실패한 등록도 유효해야 하므로 null이면 필드 자체를 보내지 않는다.
        [JsonProperty("sceneScan", NullValueHandling = NullValueHandling.Ignore)]
        public SceneScanReportDto SceneScan { get; set; }
    }
}
