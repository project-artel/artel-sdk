using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.CodeGen
{
    /// <summary>
    /// 검사를 통과한 함수들의 등록 코드를 IL 로 쓴다.
    /// </summary>
    /// <remarks>
    /// 함수마다 두 가지를 만든다.
    ///
    /// 1. 메서드가 사는 타입 안에 static stub <c>__artel_call_N(object target, object[] arguments)</c>. 인자를 하나씩
    ///    꺼내 unbox 하고 메서드를 부른다. stub 이 같은 타입 안에 있어야 private 메서드도 부를 수 있다. <c>when</c> 이 있으면
    ///    <c>__artel_when_N(object target)</c> 도 같은 자리에 둔다.
    /// 2. 어셈블리마다 하나인 <c>Artel.Generated.GameFunctionRegistration.Register()</c> 안에서
    ///    <c>GameFunctionTable.Begin</c> / <c>Param</c> / <c>End</c> 호출. 이 메서드는
    ///    <c>[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]</c> 라 첫 씬보다 먼저 돈다.
    ///
    /// 결과는 다음 C# 와 같다.
    /// <code>
    /// var draft = GameFunctionTable.Begin(GameFunctionKind.Cheat, "give_gold", "...", null, null,
    ///     new GameFunctionCall(DebugCheats.__artel_call_0), null, null, false);
    /// GameFunctionTable.Param(draft, "amount", CheatParamType.Int, null, true, 1, 1000000, false, null);
    /// GameFunctionTable.End(draft);
    /// </code>
    /// </remarks>
    internal sealed class GameFunctionEmitter
    {
        private const string CallStubPrefix = "__artel_call_";
        private const string ConditionStubPrefix = "__artel_when_";

        /// <summary><c>UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad</c>.</summary>
        private const int BeforeSceneLoad = 1;

        private readonly ModuleDefinition module;
        private readonly MethodReference begin;
        private readonly MethodReference param;
        private readonly MethodReference end;
        private readonly MethodReference callConstructor;
        private readonly MethodReference conditionConstructor;
        private readonly MethodReference getTypeFromHandle;
        private readonly MethodReference runtimeInitializeConstructor;
        private readonly TypeReference loadType;
        private readonly TypeReference objectArray;

        public GameFunctionEmitter(ModuleDefinition module, ModuleDefinition runtimeModule)
        {
            this.module = module;

            var table = runtimeModule.GetType("Artel.GameFunctionTable");
            begin = module.ImportReference(table.Methods.First(method => method.Name == "Begin"));
            param = module.ImportReference(table.Methods.First(method => method.Name == "Param"));
            end = module.ImportReference(table.Methods.First(method => method.Name == "End"));
            callConstructor = module.ImportReference(
                runtimeModule.GetType("Artel.GameFunctionCall").Methods.First(method => method.IsConstructor));
            conditionConstructor = module.ImportReference(
                runtimeModule.GetType("Artel.GameFunctionCondition").Methods.First(method => method.IsConstructor));

            var coreModule = module.TypeSystem.Object.Resolve().Module;
            getTypeFromHandle = module.ImportReference(
                coreModule.GetType("System.Type").Methods.First(method =>
                    method.Name == "GetTypeFromHandle" && method.Parameters.Count == 1));

            // UnityEngine.CoreModule 은 Artel.Runtime 이 참조하므로 그 참조로 찾는다. 게임 어셈블리가 static cheat 만 들고
            // 있으면 그 어셈블리 자신은 UnityEngine 을 참조하지 않을 수도 있다.
            var unityReference = runtimeModule.AssemblyReferences.First(reference => reference.Name == "UnityEngine.CoreModule");
            var unityModule = module.AssemblyResolver.Resolve(unityReference).MainModule;
            var attribute = unityModule.GetType("UnityEngine.RuntimeInitializeOnLoadMethodAttribute");
            runtimeInitializeConstructor = module.ImportReference(
                attribute.Methods.First(method => method.IsConstructor && method.Parameters.Count == 1));
            loadType = module.ImportReference(unityModule.GetType("UnityEngine.RuntimeInitializeLoadType"));
            objectArray = new ArrayType(module.TypeSystem.Object);
        }

        public void Emit(IReadOnlyList<GameFunctionCandidate> candidates)
        {
            var registration = new TypeDefinition(
                GameFunctionWeaver.RegistrationNamespace,
                GameFunctionWeaver.RegistrationTypeName,
                TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed |
                TypeAttributes.BeforeFieldInit | TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(registration);

            var register = new MethodDefinition(
                "Register",
                MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            var initialize = new CustomAttribute(runtimeInitializeConstructor);
            initialize.ConstructorArguments.Add(new CustomAttributeArgument(loadType, BeforeSceneLoad));
            register.CustomAttributes.Add(initialize);
            registration.Methods.Add(register);

            var il = register.Body.GetILProcessor();
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var call = AddCallStub(candidate, index);
                var condition = candidate.Condition == null ? null : AddConditionStub(candidate, index);
                EmitRegistration(il, candidate, call, condition);
            }

            il.Emit(OpCodes.Ret);
        }

        private MethodDefinition AddCallStub(GameFunctionCandidate candidate, int index)
        {
            var method = candidate.Method;
            var stub = new MethodDefinition(
                CallStubPrefix + index,
                MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
                module.TypeSystem.Object);
            stub.Parameters.Add(new ParameterDefinition("target", ParameterAttributes.None, module.TypeSystem.Object));
            stub.Parameters.Add(new ParameterDefinition("arguments", ParameterAttributes.None, objectArray));

            var il = stub.Body.GetILProcessor();
            if (!method.IsStatic)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, method.DeclaringType);
            }

            for (var argument = 0; argument < method.Parameters.Count; argument++)
            {
                var parameterType = method.Parameters[argument].ParameterType;
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, argument);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(parameterType.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, parameterType);
            }

            il.Emit(method.IsStatic ? OpCodes.Call : OpCodes.Callvirt, method);
            if (method.ReturnType.MetadataType == MetadataType.Void)
            {
                il.Emit(OpCodes.Ldnull);
            }

            il.Emit(OpCodes.Ret);
            method.DeclaringType.Methods.Add(stub);
            return stub;
        }

        private MethodDefinition AddConditionStub(GameFunctionCandidate candidate, int index)
        {
            var condition = candidate.Condition;
            var stub = new MethodDefinition(
                ConditionStubPrefix + index,
                MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
                module.TypeSystem.Object);
            stub.Parameters.Add(new ParameterDefinition("target", ParameterAttributes.None, module.TypeSystem.Object));

            var il = stub.Body.GetILProcessor();
            if (condition.IsStatic)
            {
                il.Emit(OpCodes.Call, condition);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, condition.DeclaringType);
                il.Emit(OpCodes.Callvirt, condition);
            }

            if (condition.ReturnType.MetadataType == MetadataType.Boolean)
            {
                il.Emit(OpCodes.Box, module.TypeSystem.Boolean);
            }

            il.Emit(OpCodes.Ret);
            candidate.Method.DeclaringType.Methods.Add(stub);
            return stub;
        }

        private void EmitRegistration(
            ILProcessor il, GameFunctionCandidate candidate, MethodDefinition call, MethodDefinition condition)
        {
            il.Emit(OpCodes.Ldc_I4, (int)candidate.Kind);
            EmitString(il, candidate.Name);
            EmitString(il, candidate.Description);
            EmitString(il, candidate.Group);

            if (candidate.Method.IsStatic)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                EmitTypeOf(il, candidate.Method.DeclaringType);
            }

            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, call);
            il.Emit(OpCodes.Newobj, callConstructor);

            if (condition == null)
            {
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldftn, condition);
                il.Emit(OpCodes.Newobj, conditionConstructor);
            }

            EmitString(il, candidate.WhenName);
            il.Emit(candidate.TakesContext ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, begin);

            foreach (var parameter in candidate.Parameters)
            {
                il.Emit(OpCodes.Dup);
                EmitString(il, parameter.Name);
                il.Emit(OpCodes.Ldc_I4, (int)parameter.Type);
                if (parameter.Enum == null)
                {
                    il.Emit(OpCodes.Ldnull);
                }
                else
                {
                    EmitTypeOf(il, module.ImportReference(parameter.Enum));
                }

                il.Emit(parameter.HasRange ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldc_R8, parameter.Min);
                il.Emit(OpCodes.Ldc_R8, parameter.Max);
                il.Emit(parameter.IsOptional ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                EmitString(il, parameter.DefaultText);
                il.Emit(OpCodes.Call, param);
            }

            il.Emit(OpCodes.Call, end);
        }

        private void EmitTypeOf(ILProcessor il, TypeReference type)
        {
            il.Emit(OpCodes.Ldtoken, type);
            il.Emit(OpCodes.Call, getTypeFromHandle);
        }

        private static void EmitString(ILProcessor il, string value)
        {
            if (value == null)
            {
                il.Emit(OpCodes.Ldnull);
                return;
            }

            il.Emit(OpCodes.Ldstr, value);
        }
    }
}
