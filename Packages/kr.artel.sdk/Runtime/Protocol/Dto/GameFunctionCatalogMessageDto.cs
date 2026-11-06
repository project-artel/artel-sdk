using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// <c>GAME_FUNCTION_CATALOG</c>. 연결마다 <c>DEVICE_CONTEXT</c> 바로 뒤에 한 번, 그리고 목록이 바뀌면 1초에 한 번까지 다시
    /// 보낸다. 모양은 <c>.plan/general/2026-10-14-game-functions-wire-contract.md</c> 1·2 절이 정한다.
    /// </summary>
    internal sealed class GameFunctionCatalogMessageDto
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "GAME_FUNCTION_CATALOG";

        [JsonProperty("revision")]
        public long Revision { get; set; }

        [JsonProperty("cheats")]
        public List<GameFunctionEntryDto> Cheats { get; set; } = new List<GameFunctionEntryDto>();

        [JsonProperty("behaviors")]
        public List<GameFunctionEntryDto> Behaviors { get; set; } = new List<GameFunctionEntryDto>();
    }

    /// <summary>catalog 의 함수 하나. 모든 키가 한 단어라 orchestration 과 agent 까지 그대로 간다.</summary>
    internal sealed class GameFunctionEntryDto
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("params")]
        public List<GameFunctionParamDto> Params { get; set; } = new List<GameFunctionParamDto>();

        /// <summary>static 이면 null.</summary>
        [JsonProperty("target", NullValueHandling = NullValueHandling.Include)]
        public GameFunctionTargetDto Target { get; set; }

        /// <summary>behavior 만 쓴다. 그 밖에는 null.</summary>
        [JsonProperty("group", NullValueHandling = NullValueHandling.Include)]
        public string Group { get; set; }
    }

    internal sealed class GameFunctionTargetDto
    {
        [JsonProperty("component")]
        public string Component { get; set; }
    }

    internal sealed class GameFunctionParamDto
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary><c>int</c>, <c>float</c>, <c>bool</c>, <c>string</c>, <c>enum</c>.</summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("min", NullValueHandling = NullValueHandling.Include)]
        [JsonConverter(typeof(WholeNumberJsonConverter))]
        public double? Min { get; set; }

        [JsonProperty("max", NullValueHandling = NullValueHandling.Include)]
        [JsonConverter(typeof(WholeNumberJsonConverter))]
        public double? Max { get; set; }

        /// <summary>C# 기본값. enum 은 멤버 이름. 기본값이 없으면 null.</summary>
        [JsonProperty("default", NullValueHandling = NullValueHandling.Include)]
        public object Default { get; set; }

        /// <summary>enum 이 받는 이름들. 그 밖에는 null.</summary>
        [JsonProperty("values", NullValueHandling = NullValueHandling.Include)]
        public List<string> Values { get; set; }
    }
}
