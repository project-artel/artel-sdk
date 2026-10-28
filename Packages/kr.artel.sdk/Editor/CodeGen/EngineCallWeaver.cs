using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.CodeGen
{
    /// <summary>
    /// 게임 어셈블리의 엔진 API 호출을 <c>Artel.ArtelEngine</c> 프록시로 바꾼다. 소리·애니메이션·스폰·씬 로드·저장이
    /// 일어났다는 사실을 <c>pulse</c> 에 싣기 위해.
    /// </summary>
    /// <remarks>
    /// <see cref="InputMethodWeaver"/> 와 같은 결정 방식이다. 호출부가 있는 어셈블리만 건드리고, 프록시를 못 찾으면 소리
    /// 내어 거절한다. 다른 점은 대상이 인스턴스 메서드라는 것 — <c>callvirt source.Play()</c> 는 <c>call ArtelEngine.Play(source)</c>
    /// 로 바뀌고 인스턴스가 첫 인자가 된다. 스택 위의 모양은 그대로라 명령 하나만 바뀐다.
    ///
    /// 프록시에 없는 overload 는 지나간다. 목록은 프록시 타입이 정하고 여기서는 이름만 안다.
    /// </remarks>
    internal sealed class EngineCallWeaver
    {
        private const string ProxyTypeName = "Artel.ArtelEngine";

        private static readonly Dictionary<string, HashSet<string>> Targets = new Dictionary<string, HashSet<string>>
        {
            ["UnityEngine.AudioSource"] = new HashSet<string> { "Play", "PlayDelayed", "PlayOneShot", "Stop", "PlayClipAtPoint" },
            ["UnityEngine.Animator"] = new HashSet<string> { "SetTrigger", "SetBool", "SetInteger", "SetFloat", "Play", "CrossFade" },
            ["UnityEngine.Object"] = new HashSet<string> { "Instantiate", "Destroy" },
            ["UnityEngine.SceneManagement.SceneManager"] = new HashSet<string> { "LoadScene", "LoadSceneAsync" },
            ["UnityEngine.PlayerPrefs"] = new HashSet<string> { "SetInt", "SetFloat", "SetString", "DeleteKey", "Save", "DeleteAll" }
        };

        private readonly ModuleDefinition module;
        private readonly Dictionary<string, MethodReference> proxyMethods;

        internal sealed class Attempt
        {
            internal static readonly Attempt NothingToDo = new Attempt(null, null);

            private Attempt(EngineCallWeaver weaver, string refusal)
            {
                Weaver = weaver;
                Refusal = refusal;
            }

            internal EngineCallWeaver Weaver { get; }

            internal string Refusal { get; }

            internal static Attempt Ready(EngineCallWeaver weaver) => new Attempt(weaver, null);

            internal static Attempt Refused(string refusal) => new Attempt(null, refusal);
        }

        public static Attempt TryCreate(ModuleDefinition module, string[] references)
        {
            if (!module.GetTypeReferences().Any(type => Targets.ContainsKey(type.FullName)))
            {
                return Attempt.NothingToDo;
            }

            var runtimePath = InputMethodWeaver.FindRuntimeAssembly(references);

            if (runtimePath == null)
            {
                return Attempt.Refused(
                    "엔진 API 호출이 있으나 Artel.Runtime 을 찾지 못해 엔진 호출 위빙을 건너뛴다. " +
                    "이 어셈블리의 소리·애니메이션·스폰·씬 로드는 PULSE.events 에 실리지 않는다.");
            }

            var proxyMethods = ReadProxyMethods(module, runtimePath);

            if (proxyMethods == null)
            {
                return Attempt.Refused("Artel.Runtime 이 " + ProxyTypeName + " 을 정의하지 않아 엔진 호출 위빙을 건너뛴다.");
            }

            return Attempt.Ready(new EngineCallWeaver(module, proxyMethods));
        }

        private EngineCallWeaver(ModuleDefinition module, Dictionary<string, MethodReference> proxyMethods)
        {
            this.module = module;
            this.proxyMethods = proxyMethods;
        }

        private static Dictionary<string, MethodReference> ReadProxyMethods(ModuleDefinition module, string runtimePath)
        {
            using (var runtime = AssemblyDefinition.ReadAssembly(
                       runtimePath,
                       new ReaderParameters(ReadingMode.Deferred) { AssemblyResolver = module.AssemblyResolver }))
            {
                var proxyType = runtime.MainModule.GetType(ProxyTypeName);

                if (proxyType == null)
                {
                    return null;
                }

                var found = new Dictionary<string, MethodReference>(StringComparer.Ordinal);

                foreach (var method in proxyType.Methods.Where(method => method.IsPublic && method.IsStatic))
                {
                    found[Signature(method.Name, method.Parameters)] = module.ImportReference(method);
                }

                return found;
            }
        }

        public bool Process()
        {
            var changed = false;

            foreach (var method in module.Types
                         .SelectMany(SelfAndNestedTypes)
                         .SelectMany(type => type.Methods)
                         .Where(method => method.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (!(instruction.Operand is MethodReference called) ||
                        !Targets.TryGetValue(called.DeclaringType.FullName, out var names) ||
                        !names.Contains(called.Name))
                    {
                        continue;
                    }

                    var replacement = ProxyFor(called);

                    if (replacement == null)
                    {
                        // 프록시가 모르는 overload. 원래 호출이 그대로 남고, 그 사건은 pulse 에 없다.
                        continue;
                    }

                    instruction.Operand = replacement;

                    // 프록시는 static 이다. 인스턴스 호출이 callvirt 였으면 call 로 — static 에 callvirt 는 틀린 IL 이다.
                    if (instruction.OpCode == OpCodes.Callvirt)
                    {
                        instruction.OpCode = OpCodes.Call;
                    }

                    changed = true;
                }
            }

            return changed;
        }

        private MethodReference ProxyFor(MethodReference called)
        {
            var element = called is GenericInstanceMethod generic ? generic.ElementMethod : called;
            var key = element.HasThis
                ? Signature(element.Name, element.DeclaringType.FullName, element.Parameters)
                : Signature(element.Name, element.Parameters);

            if (!proxyMethods.TryGetValue(key, out var proxy))
            {
                return null;
            }

            if (!(called is GenericInstanceMethod instance))
            {
                return proxy;
            }

            // `Instantiate<T>` 는 호출마다 T 가 다르다. 프록시의 generic 정의에 같은 인자를 끼운다.
            var made = new GenericInstanceMethod(proxy);

            foreach (var argument in instance.GenericArguments)
            {
                made.GenericArguments.Add(module.ImportReference(argument));
            }

            return made;
        }

        private static string Signature(string name, IEnumerable<ParameterDefinition> parameters)
        {
            return name + "(" + string.Join(",", parameters.Select(parameter => TypeName(parameter.ParameterType))) + ")";
        }

        private static string Signature(string name, string self, IEnumerable<ParameterDefinition> parameters)
        {
            var rest = string.Join(",", parameters.Select(parameter => TypeName(parameter.ParameterType)));
            return name + "(" + self + (rest.Length == 0 ? string.Empty : "," + rest) + ")";
        }

        /// <summary>
        /// generic 인자는 자리로 부른다. 컴파일된 호출부의 MemberRef 는 <c>!!0</c> 로만 남고 프록시 정의는 <c>T</c> 를 쥐고 있어,
        /// 이름으로 맞추면 <c>Instantiate&lt;T&gt;</c> 가 영영 짝을 못 찾는다.
        /// </summary>
        private static string TypeName(TypeReference type)
        {
            return type is GenericParameter parameter ? "!!" + parameter.Position : type.FullName;
        }

        private static IEnumerable<TypeDefinition> SelfAndNestedTypes(TypeDefinition type)
        {
            yield return type;

            foreach (var nested in type.NestedTypes.SelectMany(SelfAndNestedTypes))
            {
                yield return nested;
            }
        }
    }
}
