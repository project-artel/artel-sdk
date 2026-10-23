using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// 같은 예외나 Error 로그를 하나로 묶은 항목. <c>PULSE.errors</c> 와 <c>ACTION_RESULT.errors</c> 가 같은 모양을 쓴다.
    /// </summary>
    public sealed class LoggedErrorDto
    {
        /// <summary><c>Exception</c> · <c>Error</c> · <c>Assert</c> 중 하나. <c>LogType</c> 의 이름 그대로다.</summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>로그 문장. 200자를 넘으면 잘리고 <see cref="Truncated"/> 가 true 다.</summary>
        [JsonProperty("message")]
        public string Message { get; set; }

        /// <summary>stack trace 의 첫 줄. stack trace 가 비어 있으면 null.</summary>
        [JsonProperty("stackHead")]
        public string StackHead { get; set; }

        /// <summary>이 묶음이 처음 찍힌 <c>Time.frameCount</c>.</summary>
        [JsonProperty("firstFrame")]
        public int FirstFrame { get; set; }

        /// <summary>이 묶음이 마지막으로 찍힌 <c>Time.frameCount</c>.</summary>
        [JsonProperty("lastFrame")]
        public int LastFrame { get; set; }

        /// <summary>프로세스가 시작한 뒤 이 묶음이 찍힌 누적 횟수.</summary>
        [JsonProperty("count")]
        public int Count { get; set; }

        /// <summary><see cref="Message"/> 가 잘렸을 때만 실린다.</summary>
        [JsonProperty("truncated", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Truncated { get; set; }
    }
}
