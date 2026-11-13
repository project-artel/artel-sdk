using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.CodeGen
{
    internal sealed class InputMethodWeaver
    {
        private const string RuntimeAssemblyName = "Artel.Runtime";
        private const string ProxyTypeName = "Artel.ArtelInput";
        private const string UnityInputTypeName = "UnityEngine.Input";
        private const string UnityGuiTypeName = "UnityEngine.GUI";
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
            "GetButtonUp",
            "Button"
        };

        // 인스턴스 메서드도 바꾼다. 게임은 UI 위를 눌렀는지를 입력 모듈의 포인터로 묻는데, 그 포인터는 실제 마우스로
        // 만든 것이다 — 에이전트가 UI 를 눌러도 "UI 밖" 으로 답해 차트를 닫는 게임이 있었다. `callvirt es.M(a)` 는
        // `call ArtelInput.M(es, a)` 가 된다. 스택 위의 모양은 그대로다(EngineCallWeaver 와 같은 방식).
        private const string EventSystemTypeName = "UnityEngine.EventSystems.EventSystem";
        private static readonly HashSet<string> EventSystemMethodNames = new HashSet<string>
        {
            "IsPointerOverGameObject"
        };

        private const string ManifestTypeName = "Artel.InputReadManifest";
        internal const string RegistrationNamespace = "Artel.Generated";
        internal const string RegistrationTypeName = "InputReadRegistration";

        private readonly ModuleDefinition module;
        private readonly Dictionary<string, MethodReference> proxyMethods;
        private readonly RegistrationReferences registrationReferences;
        private readonly InputReads reads = new InputReads();

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
            if (!module.GetTypeReferences().Any(type =>
                    IsStaticInputType(type) || IsEventSystem(type)))
            {
                return Attempt.NothingToDo;
            }

            var runtimePath = FindRuntimeAssembly(references);

            if (runtimePath == null)
            {
                // 바꿀 호출이 있는데 프록시를 못 찾은 경우다. 조용히 지나가면 예전과 같은
                // 침묵이 되므로 반드시 말한다.
                return Attempt.Refused(
                    "Unity 입력 호출이 있으나 " + RuntimeAssemblyName +
                    " 을 찾지 못해 입력 위빙을 건너뛴다. " +
                    "이 어셈블리에서 Agent의 가상 입력이 동작하지 않는다.");
            }

            var proxyMethods = ReadProxyMethods(module, runtimePath, out var registrationReferences);

            if (proxyMethods == null)
            {
                return Attempt.Refused(
                    RuntimeAssemblyName + " 이 " + ProxyTypeName +
                    " 을 정의하지 않아 입력 위빙을 건너뛴다.");
            }

            var weaver = new InputMethodWeaver(module, proxyMethods, registrationReferences);
            return registrationReferences == null
                ? Attempt.Ready(weaver, RuntimeAssemblyName + " 의 InputReadManifest 나 UnityEngine.CoreModule 을 찾지 못해 " +
                                        "이 어셈블리가 읽는 키를 기록하지 않는다. 키 명령의 축 안내 판정이 이 어셈블리를 모른다.")
                : Attempt.Ready(weaver);
        }

        private static bool IsStaticInputType(TypeReference type)
        {
            return type.FullName == UnityInputTypeName || type.FullName == UnityGuiTypeName;
        }

        private static bool IsEventSystem(TypeReference type)
        {
            return string.Equals(type.FullName, EventSystemTypeName, StringComparison.Ordinal);
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
            internal static readonly Attempt NothingToDo = new Attempt(null, null, null);

            private Attempt(InputMethodWeaver weaver, string refusal, string warning)
            {
                Weaver = weaver;
                Refusal = refusal;
                Warning = warning;
            }

            /// <summary>만들었으면 위버, 아니면 null.</summary>
            internal InputMethodWeaver Weaver { get; }

            /// <summary>사람에게 알려야 할 사유. 알릴 것이 없으면 null.</summary>
            internal string Refusal { get; }

            /// <summary>위빙은 하지만 사람에게 알릴 것이 있다. 없으면 null.</summary>
            internal string Warning { get; }

            internal static Attempt Ready(InputMethodWeaver weaver, string warning = null) => new Attempt(weaver, null, warning);

            internal static Attempt Refused(string refusal) => new Attempt(null, refusal, null);
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
            string runtimePath,
            out RegistrationReferences registrationReferences)
        {
            registrationReferences = null;
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

                registrationReferences = RegistrationReferences.Read(module, runtime.MainModule);
                return proxyType.Methods
                    .Where(method => SupportedMethodNames.Contains(method.Name) ||
                                     EventSystemMethodNames.Contains(method.Name))
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
        internal static string FindRuntimeAssembly(string[] references)
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

        private InputMethodWeaver(
            ModuleDefinition module,
            Dictionary<string, MethodReference> proxyMethods,
            RegistrationReferences registrationReferences)
        {
            this.module = module;
            this.proxyMethods = proxyMethods;
            this.registrationReferences = registrationReferences;
        }

        /// <summary>이 assembly 가 읽는 키와 축. <see cref="Process"/> 뒤에 채워진다.</summary>
        internal InputReads Reads => reads;

        public bool Process()
        {
            var changed = false;
            foreach (var method in module.Types
                         .SelectMany(SelfAndNestedTypes)
                         .SelectMany(type => type.Methods)
                         .Where(method => method.HasBody))
            {
                HashSet<Instruction> jumpTargets = null;
                foreach (var instruction in method.Body.Instructions)
                {
                    if (!(instruction.Operand is MethodReference calledMethod))
                    {
                        continue;
                    }

                    if (IsStaticInputType(calledMethod.DeclaringType) &&
                        proxyMethods.TryGetValue(GetSignature(calledMethod), out var proxyMethod))
                    {
                        jumpTargets = jumpTargets ?? JumpTargets(method.Body);
                        reads.Record(calledMethod, instruction, jumpTargets);
                        instruction.Operand = proxyMethod;
                        changed = true;
                        continue;
                    }

                    if (calledMethod.HasThis && IsEventSystem(calledMethod.DeclaringType) &&
                        EventSystemMethodNames.Contains(calledMethod.Name) &&
                        proxyMethods.TryGetValue(GetInstanceSignature(calledMethod), out var instanceProxy))
                    {
                        instruction.OpCode = OpCodes.Call;
                        instruction.Operand = instanceProxy;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                EmitRegistration();
            }

            return changed;
        }

        /// <summary>
        /// 이 assembly 가 읽는 키와 축을 <c>InputReadManifest.Declare</c> 로 알리는 등록 메서드를 만든다(ARTEL-1412).
        /// </summary>
        /// <remarks>
        /// <c>GameFunctionEmitter</c> 처럼 <c>[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]</c> static 메서드다. 인자는
        /// 문자열과 bool 만 넘긴다. 배열을 IL 로 만드는 것보다 짧고, 런타임이 한 번만 풀면 된다.
        ///
        /// 테스트 assembly(NUnit 참조)에는 만들지 않는다. 위빙이 실제로 일어났는지 보는 테스트가 <c>Input</c> 을 부르는데, 그
        /// 기록이 실행 중 판정에 섞이면 키 명령 테스트가 테스트 코드가 읽는 키에 따라 달라진다. 출시 빌드에는 테스트 assembly 가
        /// 들어가지 않는다.
        /// </remarks>
        private void EmitRegistration()
        {
            if (registrationReferences == null ||
                module.AssemblyReferences.Any(reference =>
                    string.Equals(reference.Name, "nunit.framework", StringComparison.OrdinalIgnoreCase)) ||
                module.GetType(RegistrationNamespace + "." + RegistrationTypeName) != null)
            {
                return;
            }

            var registration = new TypeDefinition(
                RegistrationNamespace,
                RegistrationTypeName,
                TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed |
                TypeAttributes.BeforeFieldInit | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(registration);

            var register = new MethodDefinition(
                "Register",
                MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            var initialize = new CustomAttribute(registrationReferences.RuntimeInitializeConstructor);
            initialize.ConstructorArguments.Add(
                new CustomAttributeArgument(registrationReferences.LoadType, RuntimeInitializeReferences.BeforeSceneLoad));
            register.CustomAttributes.Add(initialize);
            registration.Methods.Add(register);

            var il = register.Body.GetILProcessor();
            il.Emit(OpCodes.Ldstr, module.Assembly?.Name?.Name ?? module.Name);
            il.Emit(OpCodes.Ldstr, string.Join(",", reads.KeyCodes));
            il.Emit(OpCodes.Ldstr, string.Join("\n", reads.KeyNames));
            il.Emit(reads.ReadsUnknownKeys ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldstr, string.Join("\n", reads.AxisNames));
            il.Emit(reads.ReadsUnknownAxes ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, registrationReferences.Declare);
            il.Emit(OpCodes.Ret);
        }

        /// <summary>분기가 닿는 instruction. 인자 바로 앞이 여기면 다른 길로 온 값이 있을 수 있다.</summary>
        private static HashSet<Instruction> JumpTargets(MethodBody body)
        {
            var targets = new HashSet<Instruction>();
            foreach (var instruction in body.Instructions)
            {
                if (instruction.Operand is Instruction target)
                {
                    targets.Add(target);
                }
                else if (instruction.Operand is Instruction[] cases)
                {
                    targets.UnionWith(cases);
                }
            }

            foreach (var handler in body.ExceptionHandlers)
            {
                if (handler.HandlerStart != null) targets.Add(handler.HandlerStart);
                if (handler.FilterStart != null) targets.Add(handler.FilterStart);
            }

            return targets;
        }

        /// <summary>
        /// 호출 지점에서 모은 키와 축. 인자가 리터럴이 아니면 "모른다"로 남긴다. 모르는 것은 실행 중 판정을 거두게 한다.
        /// </summary>
        internal sealed class InputReads
        {
            internal readonly SortedSet<int> KeyCodes = new SortedSet<int>();
            internal readonly SortedSet<string> KeyNames = new SortedSet<string>(StringComparer.Ordinal);
            internal readonly SortedSet<string> AxisNames = new SortedSet<string>(StringComparer.Ordinal);
            internal bool ReadsUnknownKeys;
            internal bool ReadsUnknownAxes;

            internal void Record(MethodReference called, Instruction call, HashSet<Instruction> jumpTargets)
            {
                switch (called.Name)
                {
                    case "get_anyKey":
                    case "get_anyKeyDown":
                        ReadsUnknownKeys = true;
                        return;
                    case "GetKey":
                    case "GetKeyDown":
                    case "GetKeyUp":
                        RecordKey(called, call, jumpTargets);
                        return;
                    case "GetAxis":
                    case "GetAxisRaw":
                    case "GetButton":
                    case "GetButtonDown":
                    case "GetButtonUp":
                        if (TryReadString(call, jumpTargets, out var axis)) AxisNames.Add(axis);
                        else ReadsUnknownAxes = true;
                        return;
                }
            }

            private void RecordKey(MethodReference called, Instruction call, HashSet<Instruction> jumpTargets)
            {
                var isString = called.Parameters.Count == 1 &&
                               called.Parameters[0].ParameterType.FullName == "System.String";
                if (isString)
                {
                    if (TryReadString(call, jumpTargets, out var name)) KeyNames.Add(name);
                    else ReadsUnknownKeys = true;
                    return;
                }

                if (TryReadInt(call, jumpTargets, out var value)) KeyCodes.Add(value);
                else ReadsUnknownKeys = true;
            }

            private static bool TryReadString(Instruction call, HashSet<Instruction> jumpTargets, out string value)
            {
                value = null;
                var argument = call.Previous;
                if (argument == null || jumpTargets.Contains(call) || jumpTargets.Contains(argument) ||
                    argument.OpCode.Code != Code.Ldstr)
                {
                    return false;
                }

                value = argument.Operand as string;
                return !string.IsNullOrEmpty(value);
            }

            private static bool TryReadInt(Instruction call, HashSet<Instruction> jumpTargets, out int value)
            {
                value = 0;
                var argument = call.Previous;
                if (argument == null || jumpTargets.Contains(call) || jumpTargets.Contains(argument))
                {
                    return false;
                }

                switch (argument.OpCode.Code)
                {
                    case Code.Ldc_I4_M1: value = -1; return true;
                    case Code.Ldc_I4_0: value = 0; return true;
                    case Code.Ldc_I4_1: value = 1; return true;
                    case Code.Ldc_I4_2: value = 2; return true;
                    case Code.Ldc_I4_3: value = 3; return true;
                    case Code.Ldc_I4_4: value = 4; return true;
                    case Code.Ldc_I4_5: value = 5; return true;
                    case Code.Ldc_I4_6: value = 6; return true;
                    case Code.Ldc_I4_7: value = 7; return true;
                    case Code.Ldc_I4_8: value = 8; return true;
                    case Code.Ldc_I4_S: value = (sbyte)argument.Operand; return true;
                    case Code.Ldc_I4: value = (int)argument.Operand; return true;
                    default: return false;
                }
            }
        }

        /// <summary>등록 메서드를 만드는 데 필요한 참조. 런타임 어셈블리가 열려 있는 동안 가져온다.</summary>
        private sealed class RegistrationReferences
        {
            internal MethodReference Declare;
            internal MethodReference RuntimeInitializeConstructor;
            internal TypeReference LoadType;

            internal static RegistrationReferences Read(ModuleDefinition module, ModuleDefinition runtimeModule)
            {
                var declare = runtimeModule.GetType(ManifestTypeName)?.Methods.FirstOrDefault(method => method.Name == "Declare");
                if (declare == null ||
                    !RuntimeInitializeReferences.TryImport(module, runtimeModule, out var constructor, out var loadType))
                {
                    return null;
                }

                return new RegistrationReferences
                {
                    Declare = module.ImportReference(declare),
                    RuntimeInitializeConstructor = constructor,
                    LoadType = loadType
                };
            }
        }

        private static string GetSignature(MethodReference method)
        {
            return method.Name + "(" +
                   string.Join(",", method.Parameters.Select(parameter => parameter.ParameterType.FullName)) +
                   ")";
        }

        /// <summary>인스턴스 호출을 프록시의 모양으로 읽는다. 인스턴스가 첫 인자다.</summary>
        private static string GetInstanceSignature(MethodReference method)
        {
            return method.Name + "(" +
                   string.Join(",", new[] { method.DeclaringType.FullName }
                       .Concat(method.Parameters.Select(parameter => parameter.ParameterType.FullName))) +
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
