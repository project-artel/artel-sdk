using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// evidence 가 있는 컴포넌트가 가리키는 <c>ScriptableObject</c> 의 직렬화된 값을 읽는다(ARTEL-1226).
    /// </summary>
    /// <remarks>
    /// scan 은 asset 의 참조 필드만 따라가고 값은 읽지 않았다. WordVenture 의 속성 상성표
    /// (<c>Magic Affinity Table.asset</c>), 웨이브 구성(<c>*BattleWaveData.asset</c>), 적 HP
    /// (<c>EnemyDataContainer.asset</c>, 보스 HP 80)는 코드가 아니라 이 값에 있고, 정답 rule 20 개 중 14 개가 빠졌다.
    ///
    /// 문서의 최상위 <c>assets</c> 에 asset 마다 하나씩 싣는다. key 는 <c>refs[].id</c> 와 같은 값이라, 소비자는 컴포넌트의
    /// 참조에서 그 값으로 간다. 값은 숫자, bool, 문자열, enum 이름과 그것들의 배열이고, <c>[Serializable]</c> class 와
    /// struct 는 두 단계까지 객체로 들어간다. 다른 asset 을 가리키는 필드는 <c>{"ref": 이름, "id": id}</c> 로 적고, 그것이
    /// 게임의 <c>ScriptableObject</c> 이면 그 값도 읽는다.
    ///
    /// 상한이 둘이다. asset 하나에 값 200 개, 문서 전체에 2MB. 넘으면 <c>truncated</c> 를 참으로 두거나 asset 을 건너뛰고,
    /// 건너뛴 수를 gap 에 적는다. 게임 assembly 의 type 만 읽는다 — TextMeshPro 의 font asset 처럼 엔진이나 패키지의
    /// asset 은 게임 규칙이 아니다.
    /// </remarks>
    internal static class AssetValues
    {
        internal const int MaxValuesPerAsset = 200;
        internal const int MaxDocumentCharacters = 2 * 1024 * 1024;
        internal const int MaxNesting = 2;

        private static readonly SortedDictionary<string, string> Written =
            new SortedDictionary<string, string>(StringComparer.Ordinal);

        private static readonly HashSet<int> Seen = new HashSet<int>();
        private static int _characters;
        private static int _skipped;

        /// <summary>읽은 asset 수.</summary>
        internal static int Count => Written.Count;

        /// <summary>전체 상한 때문에 건너뛴 asset 수.</summary>
        internal static int Skipped => _skipped;

        /// <summary>
        /// 게임 assembly 의 type 인지 묻는다. 기본은 분석기가 근거를 구운 assembly 인지다. 테스트가 바꾼다.
        /// </summary>
        internal static Func<Type, bool> IsGameType = type => AffordanceCatalog.Carries(type.Assembly);

        internal static void Forget()
        {
            Written.Clear();
            Seen.Clear();
            _characters = 0;
            _skipped = 0;
        }

        /// <summary><paramref name="held"/> 가 게임의 <c>ScriptableObject</c> 이면 그 값을 읽어 둔다.</summary>
        internal static void Read(UnityEngine.Object held)
        {
            var queue = new Queue<ScriptableObject>();
            Enqueue(held as ScriptableObject, queue);

            while (queue.Count > 0)
            {
                Write(queue.Dequeue(), queue);
            }
        }

        private static void Enqueue(ScriptableObject asset, Queue<ScriptableObject> queue)
        {
            if (asset == null || !IsGameType(asset.GetType()) || !Seen.Add(ObjectIds.Of(asset)))
            {
                return;
            }

            queue.Enqueue(asset);
        }

        private static void Write(ScriptableObject asset, Queue<ScriptableObject> queue)
        {
            if (_characters >= MaxDocumentCharacters)
            {
                _skipped++;
                return;
            }

            var walk = new Walk { Queue = queue };
            var values = new StringBuilder(256);
            walk.Object(values, asset, 0);

            var text = new StringBuilder(values.Length + 128);
            text.Append('{');
            Json.Property(text, "name", asset.name);
            text.Append(',');
            Json.Property(text, "type", asset.GetType().FullName);
            text.Append(",\"values\":").Append(values).Append(',');
            Json.Property(text, "truncated", walk.Truncated);
            text.Append('}');

            if (_characters + text.Length > MaxDocumentCharacters)
            {
                _skipped++;
                return;
            }

            _characters += text.Length;
            Written[ObjectIds.Of(asset).ToString(CultureInfo.InvariantCulture)] = text.ToString();
        }

        /// <summary>문서의 <c>"assets":{...}</c> 를 쓴다.</summary>
        internal static void Compose(StringBuilder text)
        {
            text.Append("\"assets\":{");
            var first = true;

            foreach (var pair in Written)
            {
                if (!first)
                {
                    text.Append(',');
                }

                Json.String(text, pair.Key);
                text.Append(':').Append(pair.Value);
                first = false;
            }

            text.Append('}');
        }

        /// <summary>asset 하나를 걷는 동안의 상태. 값 수를 세고, 다음에 읽을 asset 을 모은다.</summary>
        private sealed class Walk
        {
            internal Queue<ScriptableObject> Queue;
            internal int Values;
            internal bool Truncated;

            internal void Object(StringBuilder text, object subject, int nesting)
            {
                text.Append('{');
                var first = true;

                foreach (var field in SerializedReferences.FieldsOf(subject.GetType()))
                {
                    if (Values >= MaxValuesPerAsset)
                    {
                        Truncated = true;
                        break;
                    }

                    object value;

                    try
                    {
                        value = field.GetValue(subject);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    var written = new StringBuilder();

                    if (!Value(written, value, field.FieldType, nesting))
                    {
                        continue;
                    }

                    if (!first)
                    {
                        text.Append(',');
                    }

                    Json.String(text, field.Name);
                    text.Append(':').Append(written);
                    first = false;
                }

                text.Append('}');
            }

            /// <summary>값 하나를 쓰고, 쓸 수 있는 종류였는지 말한다.</summary>
            private bool Value(StringBuilder text, object value, Type declared, int nesting)
            {
                if (value == null)
                {
                    if (typeof(UnityEngine.Object).IsAssignableFrom(declared) || declared == typeof(string))
                    {
                        text.Append("null");
                        return true;
                    }

                    return false;
                }

                if (Leaf(text, value))
                {
                    Values++;
                    return true;
                }

                if (value is UnityEngine.Object unity)
                {
                    // Unity 의 빈 슬롯은 null 과 같다고 비교되지만 is 검사는 통과한다.
                    if (unity == null)
                    {
                        text.Append("null");
                        return true;
                    }

                    text.Append('{');
                    Json.Property(text, "ref", unity.name);
                    text.Append(',');
                    Json.Property(text, "id", ObjectIds.Of(unity));
                    text.Append('}');
                    Values++;
                    Enqueue(unity as ScriptableObject, Queue);
                    return true;
                }

                if (value is IList list)
                {
                    text.Append('[');

                    for (var index = 0; index < list.Count; index++)
                    {
                        if (Values >= MaxValuesPerAsset)
                        {
                            Truncated = true;
                            break;
                        }

                        if (index > 0)
                        {
                            text.Append(',');
                        }

                        var element = list[index];

                        if (!Value(text, element, element?.GetType() ?? typeof(object), nesting))
                        {
                            text.Append("null");
                        }
                    }

                    text.Append(']');
                    return true;
                }

                if (!IsSerializableObject(value.GetType()))
                {
                    return false;
                }

                if (nesting >= MaxNesting)
                {
                    Truncated = true;
                    return false;
                }

                Object(text, value, nesting + 1);
                return true;
            }

            private static bool Leaf(StringBuilder text, object value)
            {
                switch (value)
                {
                    case string word:
                        Json.String(text, word);
                        return true;
                    case bool flag:
                        text.Append(flag ? "true" : "false");
                        return true;
                    case Enum named:
                        Json.String(text, named.ToString());
                        return true;
                    case float real:
                        text.Append(float.IsNaN(real) || float.IsInfinity(real)
                            ? Number(real)
                            : real.ToString("R", CultureInfo.InvariantCulture));
                        return true;
                    case double precise:
                        text.Append(Number(precise));
                        return true;
                    case int _:
                    case long _:
                    case short _:
                    case byte _:
                    case uint _:
                    case ulong _:
                    case ushort _:
                    case sbyte _:
                        text.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                        return true;
                    case Vector2 _:
                    case Vector3 _:
                    case Vector2Int _:
                    case Vector3Int _:
                    case Color _:
                        Json.String(text, Convert.ToString(value, CultureInfo.InvariantCulture));
                        return true;
                    default:
                        return false;
                }
            }

            /// <summary>JSON 이 받는 숫자. NaN 과 무한대는 숫자로 쓸 수 없어 문자열로 둔다.</summary>
            private static string Number(double value)
            {
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    return "\"" + value.ToString(CultureInfo.InvariantCulture) + "\"";
                }

                return value.ToString("R", CultureInfo.InvariantCulture);
            }

            private static bool IsSerializableObject(Type type)
            {
                if (type.IsPrimitive || type.IsEnum || typeof(UnityEngine.Object).IsAssignableFrom(type))
                {
                    return false;
                }

                var space = type.Namespace;

                if (space != null && (space == "UnityEngine" || space.StartsWith("UnityEngine.", StringComparison.Ordinal) ||
                                      space == "System" || space.StartsWith("System.", StringComparison.Ordinal)))
                {
                    return false;
                }

                return type.IsDefined(typeof(SerializableAttribute), false);
            }
        }
    }
}
