using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    public abstract class SceneComponentDto
    {
        [JsonProperty("type")]
        public abstract string Type { get; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        /// <summary>
        /// 이 컴포넌트에서 읽은 값들. <c>scan_all_scenes ["full"]</c> 만 채운다.
        /// </summary>
        /// <remarks>
        /// 없으면 키 자체를 싣지 않는다. 한때 <c>[ArtelState]</c> 가 붙은 멤버를 매 스캔마다 여기에
        /// 실었고, 그 attribute 는 ARTEL-400 이 지웠다 — 상태는 pulse 채널이 말한다. 빈 목록을 싣는
        /// 대신 키를 빼는 것은 <see cref="ButtonComponentDto.OnClick"/> 과 같은 규칙이다.
        /// </remarks>
        [JsonProperty("states", NullValueHandling = NullValueHandling.Ignore)]
        public List<StateDto> States { get; set; }

        [JsonProperty("actions")]
        public List<ActionInvocationDto> Actions { get; set; } = new List<ActionInvocationDto>();
    }
}
