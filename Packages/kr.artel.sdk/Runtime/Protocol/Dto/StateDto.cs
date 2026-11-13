using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public sealed class StateDto
    {
        /// <summary>
        /// 언제나 빈 문자열이다. 값을 채우던 <c>[ArtelState]</c> 를 ARTEL-400 이 지웠다.
        /// </summary>
        /// <remarks>
        /// 필드를 뺄 수 없어 남아 있다. orchestration 의 <c>SdkState.tag</c> 는 기본값 없는
        /// non-null 이라, 이 키가 빠지면 그쪽 파싱이 깨진다. 지우려면 두 저장소를 함께 고쳐야 한다.
        /// </remarks>
        [JsonProperty("tag")]
        public string Tag { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("value")]
        public object Value { get; set; }
    }
}
