using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// 근거 스캔 도중의 진행(<c>EVIDENCE_SCAN_PROGRESS</c>, ARTEL-1177). 서버는 이것을 화면의 도넛으로 흘린다.
    /// </summary>
    /// <remarks>
    /// <c>stage</c> 는 <c>SCANNING</c>(씬을 하나씩 연다, 수는 씬) 과 <c>UPLOADING</c>(문서를 올린다, 수는 걸음) 둘뿐이다.
    /// 그 뒤의 적재 · TC 생성은 서버가 스스로 단계를 옮긴다.
    /// </remarks>
    internal sealed class EvidenceScanProgressMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("stage")]
        public string Stage { get; set; }

        [JsonProperty("done")]
        public int Done { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }

        /// <summary>지금 여는 씬. 모르거나 업로드 단계면 null.</summary>
        [JsonProperty("scene", NullValueHandling = NullValueHandling.Ignore)]
        public string Scene { get; set; }
    }
}
