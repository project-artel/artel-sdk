using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Artel.Affordances.Live
{
    /// <summary>
    /// 이름으로 청한 멤버를 찾는다. 분석이 구운 목록 밖의 것을, 게임이 도는 동안.
    /// </summary>
    /// <remarks>
    /// watch list 는 조건과 효과가 이름 댄 멤버만 쥔다. 사람이 쓴 테스트 케이스는 그 밖의 값을 묻는다 — 코드의 어느 줄도
    /// 조건으로 읽지 않는 점수, DI 컨테이너 안의 상태, ScriptableObject 의 플래그. 이것은 그 이름을 받아 같은 모양의
    /// <see cref="Watched"/> 로 풀고, 그것을 쥔 인스턴스가 씬에 없으면 참조를 따라가 찾는다.
    ///
    /// 걷기는 유한하다. 뿌리는 게임 어셈블리의 static 필드와 씬 위 컴포넌트의 필드, 깊이는 넷, 컬렉션은 앞 64개, 한 번 본
    /// 객체는 다시 보지 않고, 청한 타입을 전부 하나씩 찾으면 멈춘다. 그래도 큰 게임에서는 밀리초 수십이 들 수 있어 예산을 넘기면
    /// 멈추고 그렇다고 말한다 — 못 찾은 것과 못 다 본 것은 다른 답이다.
    /// </remarks>
    internal static class Lookup
    {
        internal const int MaxDepth = 4;
        internal const int MaxElements = 64;
        internal const int DefaultBudgetMilliseconds = 50;

        private const string Static = "static";
        private const string Component_ = "component";
        private const string Object_ = "object";

        /// <summary>이름 하나에 대한 답. 풀렸으면 <see cref="Member"/>, 아니면 <see cref="Reason"/>.</summary>
        internal sealed class Answer
        {
            internal string Name;
            internal Watched Member;

            /// <summary><c>static</c> · <c>component</c> · <c>object</c>. 값이 어디서 읽히는지.</summary>
            internal string Kind;

            /// <summary>지금 그 멤버를 쥔 인스턴스의 수. static 은 0.</summary>
            internal int Holders;

            internal string Reason;

            internal bool Resolved => Reason == null;
        }

        /// <summary>한 번의 걷기가 얼마나 들었는지. 응답에 실어 실측의 근거로 삼는다.</summary>
        internal sealed class Walk
        {
            internal int Visited;
            internal double Milliseconds;

            /// <summary>예산이 먼저 떨어져 뿌리를 다 보지 못했는지.</summary>
            internal bool Exhausted;
        }

        private static readonly Dictionary<string, Type> Types = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, FieldInfo[]> Fields = new Dictionary<Type, FieldInfo[]>();
        private static List<Type> _gameTypes;
        private static List<FieldInfo> _roots;
        private static bool _listening;
        private static bool _stale;

        /// <summary>이름들을 풀고, 풀린 것을 watch list 에 더한다.</summary>
        internal static List<Answer> Watch(
            IReadOnlyList<string> names, Walk walk, int budgetMilliseconds = DefaultBudgetMilliseconds)
        {
            var answers = Resolve(names, walk, budgetMilliseconds);

            foreach (var answer in answers)
            {
                if (answer.Resolved)
                {
                    WatchList.Request(answer.Member);
                }
            }

            Listen();
            return answers;
        }

        /// <summary>이름들을 푼다. 목록에는 더하지 않는다 — <c>read_member</c> 가 한 번 읽을 때.</summary>
        internal static List<Answer> Resolve(
            IReadOnlyList<string> names, Walk walk, int budgetMilliseconds = DefaultBudgetMilliseconds)
        {
            var answers = new List<Answer>(names.Count);
            var plain = new List<Answer>();
            var wanted = new List<Type>();

            foreach (var name in names)
            {
                var answer = Describe(name);
                answers.Add(answer);

                if (!answer.Resolved)
                {
                    continue;
                }

                var member = answer.Member;

                if (member.Static)
                {
                    answer.Kind = Static;
                    continue;
                }

                if (typeof(Component).IsAssignableFrom(member.Owner))
                {
                    // 씬 걷기가 매 판독 만난다. 여기서는 몇이나 있는지만 센다.
                    answer.Kind = Component_;
                    answer.Holders = InScene(member.Owner).Count;
                    continue;
                }

                answer.Kind = Object_;

                if (typeof(Object).IsAssignableFrom(member.Owner))
                {
                    // ScriptableObject 와 그 밖의 애셋. Unity 가 전부 쥐고 있으므로 걷지 않고 묻는다.
                    member.Holders = Loaded(member.Owner);
                    answer.Holders = member.Holders.Count;
                    continue;
                }

                member.Holders = new List<object>();
                plain.Add(answer);

                if (!wanted.Contains(member.Owner))
                {
                    wanted.Add(member.Owner);
                }
            }

            if (wanted.Count == 0)
            {
                return answers;
            }

            var found = Find(wanted, walk, budgetMilliseconds);

            foreach (var answer in plain)
            {
                var holders = found[answer.Member.Owner];

                if (holders.Count == 0)
                {
                    answer.Reason = walk.Exhausted ? "not reachable within budget" : "not reachable";
                    continue;
                }

                answer.Member.Holders = holders;
                answer.Holders = holders.Count;
            }

            return answers;
        }

        /// <summary>
        /// 찾아 둔 인스턴스가 쥔 멤버의, 지금의 인스턴스들. 씬이 바뀌었으면 다시 찾는다.
        /// </summary>
        /// <remarks>
        /// 판독마다 걷지 않는다. 씬이 로드되는 순간이 게임이 객체를 갈아 치우는 순간이고, 그때 한 번 다시 찾는 값은 그 로드
        /// 자체가 드는 값에 묻힌다. 파괴된 Unity 객체는 그 자리에서 떨어뜨리고 다음 판독이 다시 찾게 둔다.
        /// </remarks>
        internal static List<object> HoldersOf(Watched member)
        {
            if (_stale)
            {
                _stale = false;
                Refind();
            }

            var holders = member.Holders;

            for (var at = holders.Count - 1; at >= 0; at--)
            {
                if (holders[at] is Object reference && reference == null)
                {
                    holders.RemoveAt(at);
                    _stale = true;
                }
            }

            return holders;
        }

        /// <summary>찾아 둔 인스턴스를 다음 판독에 다시 찾게 한다. 선언된 멤버가 목록에 들어올 때.</summary>
        internal static void MarkStale()
        {
            _stale = true;
            Listen();
        }

        private static void Listen()
        {
            if (_listening)
            {
                return;
            }

            _listening = true;
            SceneManager.sceneLoaded += (scene, mode) => _stale = true;
        }

        private static void Refind()
        {
            var wanted = new List<Type>();
            var plain = new List<Watched>();

            // 청한 것과 선언된 것 둘 다. 둘 다 씬 걷기가 만나지 못하는 owner 를 가질 수 있다.
            foreach (var member in WatchList.All())
            {
                if (member.Holders == null)
                {
                    continue;
                }

                if (typeof(Object).IsAssignableFrom(member.Owner))
                {
                    member.Holders = Loaded(member.Owner);
                    continue;
                }

                plain.Add(member);

                if (!wanted.Contains(member.Owner))
                {
                    wanted.Add(member.Owner);
                }
            }

            if (wanted.Count == 0)
            {
                return;
            }

            var found = Find(wanted, new Walk(), DefaultBudgetMilliseconds);

            foreach (var member in plain)
            {
                member.Holders = found[member.Owner];
            }
        }

        /// <summary><c>Type::member</c> 또는 <c>Type::member.path.to.value</c> 를 <see cref="Watched"/> 로.</summary>
        private static Answer Describe(string name)
        {
            var answer = new Answer { Name = name };

            var split = name == null ? -1 : name.IndexOf("::", StringComparison.Ordinal);

            if (split <= 0 || split + 2 >= name.Length)
            {
                answer.Reason = "bad name: use Type::member";
                return answer;
            }

            var declaring = name.Substring(0, split);
            var rest = name.Substring(split + 2);
            var dot = rest.IndexOf('.');
            var spoken = dot < 0 ? rest : rest.Substring(0, dot);
            var via = dot < 0 ? null : rest.Substring(dot + 1);

            if (spoken.Length == 0 || (via != null && via.Length == 0))
            {
                answer.Reason = "bad name: use Type::member";
                return answer;
            }

            var owner = FindType(declaring, out var ambiguous);

            if (ambiguous != null)
            {
                answer.Reason = "ambiguous type: " + ambiguous;
                return answer;
            }

            if (owner == null)
            {
                answer.Reason = "no such type";
                return answer;
            }

            const BindingFlags Flags =
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

            FieldInfo field = null;
            PropertyInfo getter = null;

            // 기반 클래스의 private 필드는 파생 타입에 물어서는 나오지 않는다. 게임의 상태는 대개 private 이고 기반 클래스에
            // 두는 일이 흔하므로 위로 올라가며 묻는다.
            for (var type = owner; type != null && field == null; type = type.BaseType)
            {
                field = type.GetField(spoken, Flags) ??
                        type.GetField("<" + spoken + ">k__BackingField", Flags);
            }

            Type held;

            if (field != null)
            {
                held = field.FieldType;
            }
            else
            {
                for (var type = owner; type != null && getter == null; type = type.BaseType)
                {
                    getter = type.GetProperty(spoken, Flags);
                }

                if (getter == null || !getter.CanRead || getter.GetIndexParameters().Length != 0)
                {
                    answer.Reason = "no such member";
                    return answer;
                }

                held = getter.PropertyType;
            }

            if (via != null && WatchList.Walkable(held, via) == null)
            {
                answer.Reason = "no such path: " + via;
                return answer;
            }

            answer.Member = new Watched
            {
                Declaring = owner.FullName,
                Member = field != null ? field.Name : getter.Name,
                Property = field != null ? (field.Name == spoken ? null : spoken) : getter.Name,
                Via = via,
                Type = held.FullName,
                Static = field != null ? field.IsStatic : getter.GetGetMethod(true).IsStatic,
                Field = field,
                Getter = getter,
                Owner = owner
            };

            return answer;
        }

        private static Type FindType(string declaring, out string ambiguous)
        {
            ambiguous = null;

            if (Types.TryGetValue(declaring, out var known))
            {
                return known;
            }

            Type exact = null;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    exact = assembly.GetType(declaring, false);
                }
                catch (Exception)
                {
                    continue;
                }

                if (exact != null)
                {
                    break;
                }
            }

            if (exact == null)
            {
                // 네임스페이스 없이 댄 이름. 게임 타입 가운데서만 찾는다 — 엔진의 `Object` 를 뜻한 사람은 없다.
                var matches = new List<Type>();

                foreach (var type in GameTypes())
                {
                    if (type.Name == declaring ||
                        (type.FullName != null && type.FullName.EndsWith("." + declaring, StringComparison.Ordinal)))
                    {
                        matches.Add(type);
                    }
                }

                if (matches.Count == 1)
                {
                    exact = matches[0];
                }
                else if (matches.Count > 1)
                {
                    var names = new List<string>();

                    foreach (var match in matches)
                    {
                        names.Add(match.FullName);
                    }

                    ambiguous = string.Join(", ", names);
                    return null;
                }
            }

            if (exact != null)
            {
                Types[declaring] = exact;
            }

            return exact;
        }

        /// <summary>지금 씬 안에 있는 그 타입의 컴포넌트들. 애셋 안의 것은 뺀다.</summary>
        private static List<object> InScene(Type owner)
        {
            var found = new List<object>();

            foreach (var one in Resources.FindObjectsOfTypeAll(owner))
            {
                if (one is Component component && component != null && component.gameObject.scene.IsValid())
                {
                    found.Add(component);
                }
            }

            return found;
        }

        private static List<object> Loaded(Type owner)
        {
            var found = new List<object>();

            foreach (var one in Resources.FindObjectsOfTypeAll(owner))
            {
                if (one != null)
                {
                    found.Add(one);
                }
            }

            return found;
        }

        /// <summary>
        /// 참조를 따라가 청한 타입의 인스턴스를 찾는다. 결과는 타입마다 찾은 것들, 비어 있을 수 있다.
        /// </summary>
        private static Dictionary<Type, List<object>> Find(List<Type> wanted, Walk walk, int budgetMilliseconds)
        {
            var found = new Dictionary<Type, List<object>>();

            foreach (var type in wanted)
            {
                found[type] = new List<object>();
            }

            var clock = Stopwatch.StartNew();
            var visited = new HashSet<object>(Identity.Instance);
            var pending = new Stack<KeyValuePair<object, int>>();

            foreach (var root in Roots())
            {
                object held;

                try
                {
                    held = root.GetValue(null);
                }
                catch (Exception)
                {
                    // static 생성자가 던지는 타입. 그 뿌리 하나를 잃을 뿐이다.
                    continue;
                }

                if (held != null)
                {
                    pending.Push(new KeyValuePair<object, int>(held, 0));
                }
            }

            foreach (var behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (behaviour != null && behaviour.gameObject.scene.IsValid() && Game(behaviour.GetType().Assembly))
                {
                    pending.Push(new KeyValuePair<object, int>(behaviour, 0));
                }
            }

            while (pending.Count > 0)
            {
                if (clock.ElapsedMilliseconds >= budgetMilliseconds)
                {
                    walk.Exhausted = true;
                    break;
                }

                var next = pending.Pop();
                var node = next.Key;
                var depth = next.Value;

                if (node == null || !visited.Add(node))
                {
                    continue;
                }

                if (node is Object reference && reference == null)
                {
                    continue;
                }

                walk.Visited++;

                var type = node.GetType();
                var done = true;

                foreach (var want in wanted)
                {
                    if (want.IsInstanceOfType(node))
                    {
                        found[want].Add(node);
                    }

                    done &= found[want].Count > 0;
                }

                if (done)
                {
                    // 청한 타입을 전부 하나씩은 찾았다. 게임의 상태는 대개 하나씩이고, 둘 이상 쥔 타입은 다음 씬 로드의
                    // 다시 찾기가 나머지를 데려온다. 더 걷는 값은 매 `watch` 마다 든다.
                    break;
                }

                if (depth >= MaxDepth || Leaf(type))
                {
                    continue;
                }

                if (node is IDictionary dictionary)
                {
                    Push(pending, dictionary.Values, depth + 1);
                    continue;
                }

                if (node is IEnumerable sequence && !(node is Object))
                {
                    Push(pending, sequence, depth + 1);
                    continue;
                }

                if (!Game(type.Assembly))
                {
                    // 엔진 객체의 안쪽은 게임의 상태가 아니다. 그 참조가 가리키는 것만 보고, 그 안은 걷지 않는다.
                    continue;
                }

                foreach (var field in FieldsOf(type))
                {
                    object held;

                    try
                    {
                        held = field.GetValue(node);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (held != null && !Leaf(held.GetType()))
                    {
                        pending.Push(new KeyValuePair<object, int>(held, depth + 1));
                    }
                }
            }

            walk.Milliseconds = clock.Elapsed.TotalMilliseconds;
            return found;
        }

        private static void Push(Stack<KeyValuePair<object, int>> pending, IEnumerable sequence, int depth)
        {
            var taken = 0;

            try
            {
                foreach (var element in sequence)
                {
                    if (taken++ >= MaxElements)
                    {
                        break;
                    }

                    if (element != null && !Leaf(element.GetType()))
                    {
                        pending.Push(new KeyValuePair<object, int>(element, depth));
                    }
                }
            }
            catch (Exception)
            {
                // 열거 중 바뀌는 컬렉션. 앞까지 본 것으로 족하다.
            }
        }

        private static bool Leaf(Type type)
        {
            return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
                   typeof(Delegate).IsAssignableFrom(type) || typeof(Type).IsAssignableFrom(type) ||
                   type.IsPointer;
        }

        private static FieldInfo[] FieldsOf(Type type)
        {
            if (Fields.TryGetValue(type, out var known))
            {
                return known;
            }

            var all = new List<FieldInfo>();

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            for (var at = type; at != null && Game(at.Assembly); at = at.BaseType)
            {
                try
                {
                    foreach (var field in at.GetFields(Flags | BindingFlags.DeclaredOnly))
                    {
                        if (!Leaf(field.FieldType))
                        {
                            all.Add(field);
                        }
                    }
                }
                catch (Exception)
                {
                    break;
                }
            }

            var fields = all.ToArray();
            Fields[type] = fields;
            return fields;
        }

        /// <summary>게임 어셈블리의 static 필드 전부. 걷기의 뿌리. 한 번 모은다 — 타입은 게임이 도는 동안 바뀌지 않는다.</summary>
        private static List<FieldInfo> Roots()
        {
            if (_roots != null)
            {
                return _roots;
            }

            _roots = new List<FieldInfo>();

            const BindingFlags Flags =
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            foreach (var type in GameTypes())
            {
                if (type.IsGenericTypeDefinition)
                {
                    continue;
                }

                FieldInfo[] fields;

                try
                {
                    fields = type.GetFields(Flags);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var field in fields)
                {
                    if (!field.IsLiteral && !Leaf(field.FieldType))
                    {
                        _roots.Add(field);
                    }
                }
            }

            return _roots;
        }

        internal static List<Type> GameTypes()
        {
            if (_gameTypes != null)
            {
                return _gameTypes;
            }

            _gameTypes = new List<Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!Game(assembly))
                {
                    continue;
                }

                Type[] types;

                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException partial)
                {
                    types = partial.Types;
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var type in types)
                {
                    if (type != null)
                    {
                        _gameTypes.Add(type);
                    }
                }
            }

            return _gameTypes;
        }

        private static readonly Assembly Mine = typeof(Lookup).Assembly;
        private static readonly Assembly Attributes = typeof(AffordanceAttribute).Assembly;

        private static readonly string[] NotTheGames =
        {
            "UnityEngine", "UnityEditor", "Unity", "System", "mscorlib", "netstandard", "nunit", "Newtonsoft",
            "Mono", "TMPro"
        };

        /// <summary>게임의 코드인가. 엔진·런타임·이 패키지 자신이 아닌 것.</summary>
        /// <remarks>
        /// <see cref="Readable"/> 의 규칙과 다른 점 하나: 이 패키지는 이름이 아니라 어셈블리 그 자체로 뺀다. 이름 앞머리
        /// <c>Artel</c> 로 빼면 게임이 그 이름을 쓸 수 없고, 이 패키지의 테스트도 게임이 아니게 된다.
        /// </remarks>
        private static bool Game(Assembly assembly)
        {
            if (assembly == null || assembly == Mine || assembly == Attributes || assembly.IsDynamic)
            {
                return false;
            }

            string name;

            try
            {
                name = assembly.GetName().Name;
            }
            catch (Exception)
            {
                return false;
            }

            if (string.IsNullOrEmpty(name) || name == "Artel.Runtime")
            {
                return false;
            }

            foreach (var prefix in NotTheGames)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal) &&
                    (name.Length == prefix.Length || name[prefix.Length] == '.'))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>참조로만 같음을 본다. 게임 타입이 <c>Equals</c> 를 오버라이드해도 방문 집합이 틀리지 않도록.</summary>
        private sealed class Identity : IEqualityComparer<object>
        {
            internal static readonly Identity Instance = new Identity();

            public new bool Equals(object left, object right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(object subject)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(subject);
            }
        }

        /// <summary>테스트가 캐시를 비울 때.</summary>
        internal static void Forget()
        {
            Types.Clear();
            Fields.Clear();
            _gameTypes = null;
            _roots = null;
            _stale = false;
        }
    }
}
