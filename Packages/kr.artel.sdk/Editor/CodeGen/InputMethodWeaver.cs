using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace Artel.CodeGen
{
    internal sealed class InputMethodWeaver
    {
        private const string RuntimeAssemblyName = "Artel.Runtime";
        private const string ProxyTypeName = "Artel.ArtelInput";
        private const string UnityInputTypeName = "UnityEngine.Input";
        private static readonly HashSet<string> SupportedMethodNames = new HashSet<string>
        {
            "GetKeyDown",
            "GetKey",
            "GetKeyUp",
            "get_anyKey",
            "get_anyKeyDown",
            "get_mousePosition",
            "GetMouseButton",
            "GetMouseButtonDown",
            "GetMouseButtonUp",
            "GetAxis",
            "GetAxisRaw",
            "GetButton",
            "GetButtonDown",
            "GetButtonUp"
        };

        private readonly ModuleDefinition module;
        private readonly Dictionary<string, MethodReference> proxyMethods;

        /// <summary>
        /// 대상 어셈블리가 바꿀 호출을 들고 있을 때만 위버를 만든다.
        /// </summary>
        /// <remarks>
        /// 예전에는 <c>module.AssemblyReferences</c>에 <c>Artel.Runtime</c>이 있는지 물었다.
        /// <see cref="ActionMethodWeaver.TryCreate"/>에서는 그 물음이 맞다 — <c>[ArtelAction]</c>
        /// 어트리뷰트 타입 자체가 런타임 어셈블리에 있어, 하나라도 붙었다면 참조가 남는다.
        ///
        /// 여기서는 맞지 않는다. <c>Input.GetKey</c> 호출은 Artel 타입을 쓰지 않으므로 참조를
        /// 남기지 않는다. 그래서 SDK 타입을 한 번도 쓰지 않은 게임은 입력 위빙이 통째로
        /// 건너뛰어졌고, 진단조차 남지 않았다. TrashDash가 그 상태였다 — content map은 화살표
        /// 키를 정확히 읽어 냈고 <c>ACTION_RESULT</c>는 전부 성공이었는데 게임은 반응하지
        /// 않았다.
        ///
        /// 그래서 참조가 아니라 호출부를 본다. 참조는 위빙의 전제가 아니라 결과다 —
        /// <c>ImportReference</c>가 첫 교체와 함께 만들어 준다.
        /// </remarks>
        public static Attempt TryCreate(ModuleDefinition module, string[] references)
        {
            if (!module.GetTypeReferences().Any(IsUnityInput))
            {
                return Attempt.NothingToDo;
            }

            var runtimePath = FindRuntimeAssembly(references);

            if (runtimePath == null)
            {
                // 바꿀 호출이 있는데 프록시를 못 찾은 경우다. 조용히 지나가면 예전과 같은
                // 침묵이 되므로 반드시 말한다.
                return Attempt.Refused(
                    UnityInputTypeName + " 호출이 있으나 " + RuntimeAssemblyName +
                    " 을 찾지 못해 입력 위빙을 건너뛴다. " +
                    "이 어셈블리에서 Agent 의 키·축 입력이 동작하지 않는다.");
            }

            var proxyMethods = ReadProxyMethods(module, runtimePath);

            if (proxyMethods == null)
            {
                return Attempt.Refused(
                    RuntimeAssemblyName + " 이 " + ProxyTypeName +
                    " 을 정의하지 않아 입력 위빙을 건너뛴다.");
            }

            return Attempt.Ready(new InputMethodWeaver(module, proxyMethods));
        }

        private static bool IsUnityInput(TypeReference type)
        {
            return string.Equals(type.FullName, UnityInputTypeName, StringComparison.Ordinal);
        }

        /// <summary>
        /// 위버를 만들었는지, 못 만들었다면 그것이 할 말이 있는 일인지.
        /// </summary>
        /// <remarks>
        /// 진단을 여기서 만들지 않고 사유 문자열만 돌려준다. 그래야 이 파일이
        /// <c>Unity.CompilationPipeline.Common</c> 에 기대지 않고, 그 어셈블리를 참조할 수 없는
        /// 테스트에서도 같은 판단을 부를 수 있다. <c>AffordanceWriter</c> 의 <c>Refusal</c> 과
        /// 같은 방식이다.
        /// </remarks>
        internal sealed class Attempt
        {
            internal static readonly Attempt NothingToDo = new Attempt(null, null);

            private Attempt(InputMethodWeaver weaver, string refusal)
            {
                Weaver = weaver;
                Refusal = refusal;
            }

            /// <summary>만들었으면 위버, 아니면 null.</summary>
            internal InputMethodWeaver Weaver { get; }

            /// <summary>사람에게 알려야 할 사유. 알릴 것이 없으면 null.</summary>
            internal string Refusal { get; }

            internal static Attempt Ready(InputMethodWeaver weaver) => new Attempt(weaver, null);

            internal static Attempt Refused(string refusal) => new Attempt(null, refusal);
        }

        /// <summary>
        /// 프록시 메서드를 대상 모듈로 가져온다. 실패하면 null.
        /// </summary>
        /// <remarks>
        /// 런타임 어셈블리를 <c>using</c> 안에서 열고 <c>ImportReference</c>도 그 안에서 한다.
        /// 가져오기가 필요한 것을 대상 모듈로 복사하므로, 원본을 닫은 뒤에도 참조는 살아 있다.
        /// </remarks>
        private static Dictionary<string, MethodReference> ReadProxyMethods(
            ModuleDefinition module,
            string runtimePath)
        {
            using (var runtime = AssemblyDefinition.ReadAssembly(
                       runtimePath,
                       new ReaderParameters(ReadingMode.Deferred)
                       {
                           AssemblyResolver = module.AssemblyResolver
                       }))
            {
                var proxyType = runtime.MainModule.GetType(ProxyTypeName);

                if (proxyType == null)
                {
                    return null;
                }

                return proxyType.Methods
                    .Where(method => SupportedMethodNames.Contains(method.Name))
                    .ToDictionary(GetSignature, method => module.ImportReference(method));
            }
        }

        /// <summary>
        /// 런타임 어셈블리를 경로로 찾는다. 대상 모듈의 참조표는 보지 않는다.
        /// </summary>
        /// <remarks>
        /// <c>AffordanceWriter.FindRuntimeAssembly</c>와 같은 방법이다. 참조 목록에 직접 있으면
        /// 그것을 쓰고, 없으면 참조들이 놓인 폴더를 뒤진다 — 컴파일 산출물은 한자리에 모이므로,
        /// 아무것도 그것을 가리키지 않았을 때에도 이 패키지가 만드는 어셈블리는 게임이 대고
        /// 컴파일된 것들 옆에 있다.
        /// </remarks>
        private static string FindRuntimeAssembly(string[] references)
        {
            if (references == null)
            {
                return null;
            }

            var folders = new List<string>();

            foreach (var reference in references)
            {
                if (string.IsNullOrEmpty(reference))
                {
                    continue;
                }

                if (string.Equals(Path.GetFileNameWithoutExtension(reference), RuntimeAssemblyName,
                        StringComparison.Ordinal) && File.Exists(reference))
                {
                    return reference;
                }

                var folder = Path.GetDirectoryName(reference);

                if (!string.IsNullOrEmpty(folder) && !folders.Contains(folder))
                {
                    folders.Add(folder);
                }
            }

            foreach (var folder in folders)
            {
                var candidate = Path.Combine(folder, RuntimeAssemblyName + ".dll");

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private InputMethodWeaver(ModuleDefinition module, Dictionary<string, MethodReference> proxyMethods)
        {
            this.module = module;
            this.proxyMethods = proxyMethods;
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
                    if (!(instruction.Operand is MethodReference calledMethod) ||
                        calledMethod.DeclaringType.FullName != UnityInputTypeName ||
                        !proxyMethods.TryGetValue(GetSignature(calledMethod), out var proxyMethod))
                    {
                        continue;
                    }

                    instruction.Operand = proxyMethod;
                    changed = true;
                }
            }

            return changed;
        }

        private static string GetSignature(MethodReference method)
        {
            return method.Name + "(" +
                   string.Join(",", method.Parameters.Select(parameter => parameter.ParameterType.FullName)) +
                   ")";
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
