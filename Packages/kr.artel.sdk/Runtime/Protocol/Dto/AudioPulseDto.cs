using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary><c>PULSE.audio</c>. 지금 재생 중인 <c>AudioSource</c> 수와 직전 <c>pulse</c> 이후 재생을 시작한 것.</summary>
    public sealed class AudioPulseDto
    {
        /// <summary>이 <c>pulse</c> 를 쓴 순간 <c>isPlaying</c> 이 true 인 <c>AudioSource</c> 수.</summary>
        [JsonProperty("playing")]
        public int Playing { get; set; }

        /// <summary>직전 <c>pulse</c> 이후 재생을 시작한 <c>AudioSource</c>. 20 개까지.</summary>
        [JsonProperty("started")]
        public List<AudioStartDto> Started { get; set; } = new List<AudioStartDto>();

        /// <summary><see cref="Started"/> 가 상한에서 잘렸을 때만 실린다.</summary>
        [JsonProperty("truncated", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Truncated { get; set; }
    }

    public sealed class AudioStartDto
    {
        /// <summary><c>AudioSource.clip</c> 의 이름. clip 이 없으면 null.</summary>
        [JsonProperty("clip")]
        public string Clip { get; set; }

        /// <summary>시작을 알아챈 <c>Time.frameCount</c>. 실제로 시작한 frame 보다 최대 한 <c>pulse</c> 간격만큼 늦다.</summary>
        [JsonProperty("frame")]
        public int Frame { get; set; }
    }
}
