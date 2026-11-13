using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace Artel
{
    /// <summary>
    /// <c>reset_game</c> 이 씬을 다시 여는 사이에 게임 코드의 static 을 게임을 막 켠 값으로 되돌린다.
    /// </summary>
    /// <remarks>
    /// 씬을 다시 열어도 static 은 프로세스와 함께 산다. 씬을 다시 여는 일이 없는 게임은 static 에 파괴된 오브젝트를 쥔 채로 두어도
    /// 플레이어가 그것을 만날 길이 없지만, <c>reset_game</c> 뒤의 게임은 그것을 만난다 — 예: static 이벤트 사전에 남은 리스너가
    /// 파괴된 UI 를 부르다 예외를 내고 그 뒤의 리스너를 막는다. 새로 켠 게임과 같아지려면 static 도 처음 값이어야 한다.
    ///
    /// 되돌리는 법은 도메인 재로드가 하는 일을 흉내 낸다: 필드를 기본값으로 비우고 타입 초기화(<c>.cctor</c>, 필드 초기값)를 다시
    /// 돌린 뒤, 게임의 <see cref="RuntimeInitializeOnLoadMethodAttribute"/> 메서드를 켤 때와 같은 순서로 다시 부른다. Unity 가
    /// "도메인 재로드 없이 플레이 진입" 에서 게임에 요구하는 것과 같은 약속이다.
    ///
    /// 닿지 못하는 것: 파일 · 서버에 쓴 진행도(게임마다 달라 cheat 의 몫이다), static 이 쥔 객체 안의 상태(참조만 되돌린다),
    /// 게임 코드가 아닌 어셈블리(플러그인 DLL · 패키지)의 static, 그리고 IL2CPP 빌드의 타입 초기화.
    /// </remarks>
    internal static class GameStatics
    {
        private const BindingFlags StaticFields =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly RuntimeInitializeLoadType[] BeforeScene =
        {
            RuntimeInitializeLoadType.SubsystemRegistration,
            RuntimeInitializeLoadType.AfterAssembliesLoaded,
            RuntimeInitializeLoadType.BeforeSplashScreen,
            RuntimeInitializeLoadType.BeforeSceneLoad,
        };

        private static IReadOnlyList<Type> gameTypes;

        /// <summary>되돌린 결과. 로그 한 줄과 테스트가 읽는다.</summary>
        internal readonly struct Outcome
        {
            public readonly int Types;
            public readonly int Fields;
            public readonly IReadOnlyList<string> Failed;

            public Outcome(int types, int fields, IReadOnlyList<string> failed)
            {
                Types = types;
                Fields = fields;
                Failed = failed;
            }
        }

        /// <summary>
        /// 게임 코드의 static 을 처음 값으로 되돌리고, 씬을 열기 전에 도는 초기화 메서드를 다시 부른다.
        /// </summary>
        /// <remarks>옛 씬이 다 내려간 뒤 · 새 씬을 열기 전에 부른다. 옛 씬의 OnDestroy 가 비운 static 을 읽지 않게 한다.</remarks>
        public static Outcome ResetBeforeSceneLoad()
        {
            var types = GameTypes();
            var outcome = Reset(types);
            RunInitializers(types, BeforeScene);
            return outcome;
        }

        /// <summary>시작 씬을 연 뒤 도는 초기화 메서드(<c>AfterSceneLoad</c>)를 다시 부른다.</summary>
        public static void RunAfterSceneLoad()
        {
            RunInitializers(GameTypes(), new[] { RuntimeInitializeLoadType.AfterSceneLoad });
        }

        /// <summary>
        /// 타입마다 static 필드를 기본값으로 비우고 타입 초기화를 다시 돌린다. 한 타입이 실패해도 나머지는 계속한다.
        /// </summary>
        internal static Outcome Reset(IEnumerable<Type> types)
        {
            var reset = new List<Type>();
            var failed = new List<string>();
            var fields = 0;
            foreach (var type in types)
            {
                var own = type.GetFields(StaticFields).Where(f => !f.IsLiteral).ToArray();
                if (own.Length == 0)
                {
                    continue;
                }

                object[] before;
                try
                {
                    // 읽기가 아직 돌지 않은 타입 초기화를 먼저 돌린다. 여기서 던지는 타입은 다시 돌려도 같으므로 건너뛴다.
                    before = own.Select(f => f.GetValue(null)).ToArray();
                }
                catch (Exception error)
                {
                    failed.Add(type.FullName + ": " + Innermost(error).Message);
                    continue;
                }

                try
                {
                    foreach (var field in own)
                    {
                        field.SetValue(null, field.FieldType.IsValueType ? Activator.CreateInstance(field.FieldType) : null);
                    }

                    fields += own.Length;
                    reset.Add(type);
                }
                catch (Exception error)
                {
                    // 쓸 수 없는 필드(런타임이 막은 static readonly 등)를 만났다. 반쯤 비운 타입은 되돌리지 않은 것보다 나쁘다.
                    for (var index = 0; index < own.Length; index++)
                    {
                        try
                        {
                            own[index].SetValue(null, before[index]);
                        }
                        catch (Exception)
                        {
                            // 되돌릴 수도 없는 필드는 처음부터 바뀌지 않았다.
                        }
                    }

                    failed.Add(type.FullName + ": " + Innermost(error).Message);
                }
            }

            // 비우기를 다 끝낸 뒤 초기화를 돌린다. 섞으면 앞에서 다시 채운 값을 뒤의 비우기가 지운다.
            foreach (var type in reset)
            {
                var initializer = type.TypeInitializer;
                if (initializer == null)
                {
                    continue;
                }

                try
                {
                    initializer.Invoke(null, null);
                }
                catch (Exception error)
                {
                    failed.Add(type.FullName + ": " + Innermost(error).Message);
                }
            }

            return new Outcome(reset.Count, fields, failed);
        }

        /// <summary>
        /// 게임이 켜질 때 Unity 가 부르는 <see cref="RuntimeInitializeOnLoadMethodAttribute"/> 메서드를, 주어진 시점 순서대로 부른다.
        /// </summary>
        internal static void RunInitializers(IEnumerable<Type> types, IEnumerable<RuntimeInitializeLoadType> stages)
        {
            var methods = types
                .Where(t => !t.ContainsGenericParameters)
                .SelectMany(t => t.GetMethods(StaticFields))
                .Select(m => (method: m, attribute: m.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>()))
                .Where(pair => pair.attribute != null && pair.method.GetParameters().Length == 0)
                .ToList();
            foreach (var stage in stages)
            {
                foreach (var (method, _) in methods.Where(pair => pair.attribute.loadType == stage))
                {
                    try
                    {
                        method.Invoke(null, null);
                    }
                    catch (Exception error)
                    {
                        Debug.LogWarning($"[Artel] reset_game: {method.DeclaringType?.FullName}.{method.Name} failed: {Innermost(error).Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 되돌릴 타입: 게임 어셈블리의 타입과, 게임 코드가 쓰는 게임 제네릭 타입의 닫힌 형태(<c>Bus&lt;Hit&gt;</c>).
        /// </summary>
        /// <remarks>
        /// 닫힌 제네릭은 그 형태마다 static 을 따로 갖지만 어셈블리에는 열린 정의만 있다. 그래서 게임 메서드의 IL 이 가리키는 멤버를
        /// 읽어 쓰이는 형태를 찾는다. IL 을 읽을 수 없는 빌드(IL2CPP)에서는 열린 정의만 남아 닫힌 형태를 건너뛴다.
        /// </remarks>
        internal static IReadOnlyList<Type> TypesOf(IReadOnlyCollection<Assembly> assemblies)
        {
            var plain = assemblies.SelectMany(LoadableTypes).Where(t => !t.ContainsGenericParameters && !IsArtel(t)).ToList();
            var closed = new HashSet<Type>();
            foreach (var type in assemblies.SelectMany(LoadableTypes))
            {
                const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                         BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                IEnumerable<MethodBase> bodies = type.GetMethods(all);
                bodies = bodies.Concat(type.GetConstructors(all));
                foreach (var body in bodies)
                {
                    foreach (var used in UsedTypes(body))
                    {
                        if (used.IsConstructedGenericType && !used.ContainsGenericParameters && !IsArtel(used) &&
                            assemblies.Contains(used.GetGenericTypeDefinition().Assembly))
                        {
                            closed.Add(used);
                        }
                    }
                }
            }

            return plain.Concat(closed).ToList();
        }

        /// <summary>
        /// SDK 가 게임 어셈블리에 심은 타입(<c>Artel.Generated</c>의 게임 함수 등록). 다시 돌리면 SDK 의 등록부에 같은 것이 두 번 든다.
        /// </summary>
        private static bool IsArtel(Type type)
        {
            var space = type.Namespace ?? "";
            return space == "Artel" || space.StartsWith("Artel.", StringComparison.Ordinal);
        }

        private static IReadOnlyList<Type> GameTypes()
        {
            return gameTypes ??= TypesOf(GameAssemblies());
        }

        /// <summary>
        /// 게임 코드의 어셈블리. 에디터에서는 <c>Assets/</c> 안의 소스로 만든 플레이어 어셈블리, 빌드에서는 Unity 의 기본 어셈블리다.
        /// </summary>
        private static IReadOnlyCollection<Assembly> GameAssemblies()
        {
            var names = new HashSet<string> { "Assembly-CSharp", "Assembly-CSharp-firstpass" };
#if UNITY_EDITOR
            foreach (var assembly in UnityEditor.Compilation.CompilationPipeline.GetAssemblies(
                         UnityEditor.Compilation.AssembliesType.Player))
            {
                if (assembly.sourceFiles.Any(path => path.StartsWith("Assets/", StringComparison.Ordinal)))
                {
                    names.Add(assembly.name);
                }
            }
#endif
            return AppDomain.CurrentDomain.GetAssemblies().Where(a => names.Contains(a.GetName().Name)).ToList();
        }

        private static IEnumerable<Type> LoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException partial)
            {
                return partial.Types.Where(t => t != null);
            }
        }

        private static readonly OpCode[] OneByte = new OpCode[0x100];
        private static readonly OpCode[] TwoByte = new OpCode[0x100];

        static GameStatics()
        {
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!(field.GetValue(null) is OpCode code))
                {
                    continue;
                }

                var value = (ushort)code.Value;
                if (value < 0x100)
                {
                    OneByte[value] = code;
                }
                else if ((value & 0xff00) == 0xfe00)
                {
                    TwoByte[value & 0xff] = code;
                }
            }
        }

        /// <summary>메서드 본문이 가리키는 필드 · 메서드 · 타입의 선언 타입.</summary>
        private static IEnumerable<Type> UsedTypes(MethodBase method)
        {
            byte[] il;
            try
            {
                il = method.GetMethodBody()?.GetILAsByteArray();
            }
            catch (Exception)
            {
                yield break;
            }

            if (il == null)
            {
                yield break;
            }

            var typeArguments = method.DeclaringType != null && method.DeclaringType.IsGenericType
                ? method.DeclaringType.GetGenericArguments()
                : null;
            var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
            var at = 0;
            while (at < il.Length)
            {
                OpCode code;
                if (il[at] == 0xfe && at + 1 < il.Length)
                {
                    code = TwoByte[il[at + 1]];
                    at += 2;
                }
                else
                {
                    code = OneByte[il[at]];
                    at += 1;
                }

                var size = OperandSize(code.OperandType, il, at);
                if (size < 0)
                {
                    yield break;
                }

                if (code.OperandType == OperandType.InlineField || code.OperandType == OperandType.InlineMethod ||
                    code.OperandType == OperandType.InlineTok || code.OperandType == OperandType.InlineType)
                {
                    var used = Resolve(method.Module, BitConverter.ToInt32(il, at), typeArguments, methodArguments);
                    if (used != null)
                    {
                        yield return used;
                    }
                }

                at += size;
            }
        }

        private static Type Resolve(Module module, int token, Type[] typeArguments, Type[] methodArguments)
        {
            try
            {
                var member = module.ResolveMember(token, typeArguments, methodArguments);
                return member as Type ?? member?.DeclaringType;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int OperandSize(OperandType type, byte[] il, int at)
        {
            switch (type)
            {
                case OperandType.InlineNone:
                    return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    return 1;
                case OperandType.InlineVar:
                    return 2;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    return 8;
                case OperandType.InlineSwitch:
                    return at + 4 <= il.Length ? 4 + 4 * BitConverter.ToInt32(il, at) : -1;
                default:
                    return 4;
            }
        }

        private static Exception Innermost(Exception error)
        {
            while (error.InnerException != null)
            {
                error = error.InnerException;
            }

            return error;
        }
    }
}
