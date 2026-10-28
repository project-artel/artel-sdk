using System;
using System.Collections.Generic;
using System.Globalization;
using Artel.Affordances.Live;
using Artel.Affordances.Scan;
using Newtonsoft.Json;
using UnityEngine;

namespace Artel
{
    /// <summary>
    /// 엔진 API 호출 하나. <see cref="ArtelEngine"/> 프록시가 적고 <see cref="EngineEventPulse"/> 가 다음 <c>pulse</c> 에 싣는다.
    /// </summary>
    public sealed class EngineEventDto
    {
        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("target")]
        public string Target { get; set; }

        [JsonProperty("detail")]
        public string Detail { get; set; }

        [JsonProperty("value")]
        public object Value { get; set; }

        [JsonProperty("frame")]
        public int Frame { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }
    }

    /// <summary>
    /// 게임이 엔진에 건 호출들. 상태가 아니라 사건이라 <c>pulse</c> 가 보지 못하던 것.
    /// </summary>
    /// <remarks>
    /// 소리 재생, 애니메이션 트리거, 스폰과 파괴, 씬 로드, 저장은 한 tick 안에 일어나고 끝난다. 그 뒤에 남는 상태 — 재생
    /// 중인 소스, 바뀐 state — 는 adapter 가 읽지만, 일어났다는 사실 자체는 호출부에서만 잡힌다. 그래서 빌드 때 호출부를
    /// 프록시로 바꾸고(<c>EngineCallWeaver</c>), 프록시가 원래 호출을 그대로 한 뒤 여기에 적는다.
    ///
    /// 같은 사건이 한 판독 안에 되풀이되면 <c>count</c> 로 접는다. <c>Update</c> 에서 매 프레임 <c>SetFloat("Speed", v)</c>
    /// 를 거는 게임이 흔하고, 그것을 전부 실으면 사건 목록이 곧 프레임 목록이다. 상한을 넘긴 나머지는 버리고 그 수를
    /// 말한다.
    /// </remarks>
    public static class EngineEvents
    {
        internal const int MaxPerReading = 50;

        private static readonly object Gate = new object();
        private static readonly List<EngineEventDto> Pending = new List<EngineEventDto>();
        private static readonly Dictionary<string, EngineEventDto> Folded =
            new Dictionary<string, EngineEventDto>(StringComparer.Ordinal);
        private static int _dropped;

        /// <summary>프록시가 부른다. <paramref name="on"/> 은 호출을 받은 컴포넌트, static 호출이면 null.</summary>
        public static void Record(string kind, Component on, string detail, object value)
        {
            string target = null;

            try
            {
                if (on != null && on.transform != null)
                {
                    target = ScenePath.Of(on.transform);
                }
            }
            catch (Exception)
            {
                // 파괴되는 중인 객체. 경로 없이 적는다.
            }

            var lowered = Lower(value);
            var key = kind + "\u0001" + target + "\u0001" + detail + "\u0001" +
                      Convert.ToString(lowered, CultureInfo.InvariantCulture);

            lock (Gate)
            {
                if (Folded.TryGetValue(key, out var same))
                {
                    same.Count++;
                    return;
                }

                if (Pending.Count >= MaxPerReading)
                {
                    _dropped++;
                    return;
                }

                var made = new EngineEventDto
                {
                    Kind = kind, Target = target, Detail = detail, Value = lowered, Frame = Time.frameCount, Count = 1
                };

                Pending.Add(made);
                Folded[key] = made;
            }
        }

        /// <summary>지난 판독 이후 쌓인 것을 비우며 돌려준다. <paramref name="dropped"/> 는 상한에 걸려 버린 수.</summary>
        public static List<EngineEventDto> Drain(out int dropped)
        {
            lock (Gate)
            {
                var taken = new List<EngineEventDto>(Pending);
                dropped = _dropped;
                Pending.Clear();
                Folded.Clear();
                _dropped = 0;
                return taken;
            }
        }

        internal static void Forget()
        {
            Drain(out _);
        }

        /// <summary>JSON 으로 쓸 수 있고 사람이 읽을 수 있는 모양으로. Unity 객체는 이름, 그 밖의 객체는 타입 이름.</summary>
        internal static object Lower(object value)
        {
            switch (value)
            {
                case null:
                    return null;
                case string _:
                case bool _:
                case int _:
                case long _:
                case float _:
                case double _:
                    return value;
                case Enum member:
                    return member.ToString();
                case UnityEngine.Object reference:
                    return reference == null ? null : reference.name;
                case Vector2 _:
                case Vector3 _:
                case Quaternion _:
                case Color _:
                    return value.ToString();
            }

            return value.GetType().FullName;
        }
    }

    /// <summary><c>PULSE.events</c>. 직전 판독 이후 게임이 엔진에 건 호출들.</summary>
    internal sealed class EngineEventPulse : IPulseAttachment
    {
        public string Compose(bool whole, out bool news)
        {
            var events = EngineEvents.Drain(out var dropped);
            news = events.Count > 0;

            if (!news)
            {
                return null;
            }

            var fragment = ",\"events\":" + JsonConvert.SerializeObject(events);

            return dropped > 0 ? fragment + ",\"eventsDropped\":" + dropped : fragment;
        }
    }
}
