using System.Collections.Generic;
using Newtonsoft.Json;

namespace Artel.Protocol.Dto
{
    /// <summary>
    /// The `returnValue` of `list_inputs`: what this build reads from the keyboard and the Input Manager (ARTEL-1431).
    /// </summary>
    /// <remarks>
    /// 서버의 Planner 는 런 중에 매크로를 쓰면서 게임 코드를 보지 못한다. 그래서 어떤 키를 `press_key` 로 누르고 어떤 이름을
    /// `set_input_axis`·`set_input_button` 에 넘겨야 하는지를 SDK 가 직접 답한다.
    /// </remarks>
    internal sealed class InputListDto
    {
        /// <summary>게임 코드가 `Input.GetKey*` 에 리터럴로 넘기는 키의 KeyCode 이름. 위버가 빌드 때 모은 것이다.</summary>
        [JsonProperty("keys")]
        public List<string> Keys { get; set; } = new List<string>();

        /// <summary>키를 변수로 읽거나 `anyKey` 를 읽는다. 그러면 <see cref="Keys"/> 밖의 키도 게임에 닿을 수 있다.</summary>
        [JsonProperty("otherKeys")]
        public bool OtherKeys { get; set; }

        /// <summary>
        /// 게임이 `GetAxis*`·`GetButton*` 으로 읽는 Input Manager 이름. 빌드 때 모은 리터럴에, 이 런에서 실제로 읽힌 이름을
        /// 더한다. 가상 키는 이 이름들에 닿지 않는다.
        /// </summary>
        [JsonProperty("axes")]
        public List<string> Axes { get; set; } = new List<string>();

        /// <summary>지금 켜진 Input System action 과 거기 묶인 키보드 키. 씬이 바뀌면 달라진다.</summary>
        [JsonProperty("actions")]
        public List<InputActionKeysDto> Actions { get; set; } = new List<InputActionKeysDto>();
    }

    internal sealed class InputActionKeysDto
    {
        /// <summary>`<map>/<action>`. map 이 없는 action 은 action 이름만이다.</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>그 action 에 묶인 키의 KeyCode 이름. `press_key` 에 그대로 넘긴다.</summary>
        [JsonProperty("keys")]
        public List<string> Keys { get; set; } = new List<string>();
    }
}
