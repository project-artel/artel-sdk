using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// 등록 시 서버에 보고하는 씬 목록. Build Settings 에 담긴 순서 그대로이고, 0번이 게임을
    /// 켜면 열리는 씬이다 — 서버는 그 하나를 지도의 입구(`scene.is_entry`)로 적는다.
    /// </summary>
    internal sealed class SceneScanReportDto
    {
        [JsonProperty("scenesInBuild")]
        public List<string> ScenesInBuild { get; set; } = new List<string>();
    }
}
