using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.CodeGen
{
    /// <summary>
    /// getter 하나가 부작용 없이 값을 계산만 하는지 IL 로 판정한다(ARTEL-1294).
    /// </summary>
    /// <remarks>
    /// 허용: 필드·static 읽기, 지역 변수 읽기·쓰기(반환하면 사라진다), 상수, 산술·비교·분기, 반환, 예외 던지기(런타임이 잡아
    /// <c>unread</c> 로 보낸다). 금지: 필드·static·배열·포인터에 쓰기, 참조 타입 생성. 호출은 같은 어셈블리의 메서드면 같은 규칙으로
    /// 재귀 검사하고, 다른 어셈블리면 <see cref="Allowed"/> 목록만 받는다. 모르는 것은 전부 순수하지 않다고 본다 — 틀렸을 때 잃는
    /// 것이 "못 읽는다" 이고 "게임 상태를 바꿨다" 가 아니게 하려는 것이다.
    ///
    /// override 될 수 있는 virtual 호출은 실제로 어느 몸체가 돌지 빌드 때 알 수 없어 거절한다. 같은 어셈블리의 sealed 타입이나
    /// final 메서드는 검사한다.
    /// </remarks>
    internal sealed class GetterPurity
    {
        private const int MaxDepth = 8;

        // 다른 어셈블리에서 부르면 순수하다고 보는 것. 타입 이름(제네릭 인자는 뗀다) → 멤버 이름. `*` 는 그 타입의 static 메서드 전부.
        private static readonly Dictionary<string, HashSet<string>> Allowed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["UnityEngine.Time"] = Names("get_frameCount", "get_time", "get_unscaledTime", "get_deltaTime", "get_unscaledDeltaTime",
                "get_timeScale", "get_realtimeSinceStartup", "get_fixedTime", "get_timeSinceLevelLoad", "get_renderedFrameCount"),
            ["UnityEngine.Mathf"] = Names("*"),
            ["System.Math"] = Names("*"),
            ["UnityEngine.Object"] = Names("op_Equality", "op_Inequality", "op_Implicit", "get_name"),
            ["UnityEngine.Component"] = Names("get_gameObject", "get_transform", "get_tag", "CompareTag"),
            ["UnityEngine.GameObject"] = Names("get_activeSelf", "get_activeInHierarchy", "get_transform", "get_tag", "get_layer", "CompareTag"),
            ["UnityEngine.Behaviour"] = Names("get_enabled", "get_isActiveAndEnabled"),
            ["UnityEngine.Transform"] = Names("get_position", "get_localPosition", "get_rotation", "get_localRotation", "get_localScale",
                "get_childCount", "get_parent"),
            ["System.String"] = Names("get_Length", "get_Chars", "op_Equality", "op_Inequality", "IsNullOrEmpty", "IsNullOrWhiteSpace",
                "Equals", "Contains", "StartsWith", "EndsWith"),
            ["System.Nullable`1"] = Names("get_HasValue", "get_Value", "GetValueOrDefault"),
            ["System.DateTime"] = Names("get_UtcNow", "get_Now", "op_Subtraction", "op_LessThan", "op_GreaterThan", "op_LessThanOrEqual",
                "op_GreaterThanOrEqual", "op_Equality", "op_Inequality"),
            ["System.TimeSpan"] = Names("get_TotalSeconds", "get_TotalMilliseconds", "op_LessThan", "op_GreaterThan"),
            ["System.Collections.Generic.List`1"] = Names("get_Count", "get_Item", "Contains"),
            ["System.Collections.Generic.Dictionary`2"] = Names("get_Count", "get_Item", "ContainsKey", "ContainsValue"),
            ["System.Collections.Generic.HashSet`1"] = Names("get_Count", "Contains"),
            ["System.Collections.Generic.Queue`1"] = Names("get_Count", "Contains"),
            ["System.Collections.Generic.Stack`1"] = Names("get_Count", "Contains"),
        };

        // 값 타입 수학. getter·연산자·생성자·static 메서드는 받는다. 인스턴스 메서드는 받지 않는다 — `Normalize()` 처럼 자기
        // 자신을 바꾸는 것이 있고, 받는 쪽 주소가 필드(`ldflda`)면 그것이 필드 쓰기다.
        private static readonly HashSet<string> ValueMath = new HashSet<string>(StringComparer.Ordinal)
        {
            "UnityEngine.Vector2", "UnityEngine.Vector3", "UnityEngine.Vector4", "UnityEngine.Vector2Int", "UnityEngine.Vector3Int",
            "UnityEngine.Quaternion", "UnityEngine.Color", "UnityEngine.Color32", "UnityEngine.Rect"
        };

        private readonly ModuleDefinition module;
        private readonly Dictionary<MethodDefinition, string> verdicts = new Dictionary<MethodDefinition, string>();
        private readonly HashSet<MethodDefinition> visiting = new HashSet<MethodDefinition>();

        internal GetterPurity(ModuleDefinition module)
        {
            this.module = module;
        }

        /// <summary>순수하면 null, 아니면 첫 이유(<c>"writes Game.Lock::cache"</c>).</summary>
        internal string Refusal(MethodDefinition method)
        {
            return Check(method, 0);
        }

        private string Check(MethodDefinition method, int depth)
        {
            if (verdicts.TryGetValue(method, out var known))
            {
                return known;
            }

            if (depth > MaxDepth)
            {
                return "calls nest deeper than " + MaxDepth;
            }

            // 재귀 호출은 지금 검사 중인 몸체로 돌아올 뿐이라 새 부작용을 만들지 않는다.
            if (!visiting.Add(method))
            {
                return null;
            }

            var refusal = Body(method, depth);
            visiting.Remove(method);
            verdicts[method] = refusal;
            return refusal;
        }

        private string Body(MethodDefinition method, int depth)
        {
            if (!method.HasBody)
            {
                return "has no IL body: " + Name(method);
            }

            Instruction previous = null;

            foreach (var instruction in method.Body.Instructions)
            {
                var refusal = Judge(instruction, previous, depth);

                if (refusal != null)
                {
                    return refusal;
                }

                previous = instruction;
            }

            return null;
        }

        private string Judge(Instruction instruction, Instruction previous, int depth)
        {
            switch (instruction.OpCode.Code)
            {
                case Code.Stfld:
                case Code.Stsfld:
                    return "writes " + Name((FieldReference)instruction.Operand);
                case Code.Stelem_Any: case Code.Stelem_I: case Code.Stelem_I1: case Code.Stelem_I2: case Code.Stelem_I4:
                case Code.Stelem_I8: case Code.Stelem_R4: case Code.Stelem_R8: case Code.Stelem_Ref:
                    return "writes an array element";
                case Code.Stind_I: case Code.Stind_I1: case Code.Stind_I2: case Code.Stind_I4: case Code.Stind_I8:
                case Code.Stind_R4: case Code.Stind_R8: case Code.Stind_Ref: case Code.Stobj: case Code.Cpobj:
                case Code.Cpblk: case Code.Initblk: case Code.Localloc:
                    return "writes through an address";
                case Code.Initobj:
                    // 지역 struct 를 비우는 것만 받는다. `ldflda` 뒤의 initobj 는 필드 쓰기다.
                    return previous != null && (previous.OpCode.Code == Code.Ldloca || previous.OpCode.Code == Code.Ldloca_S)
                        ? null
                        : "writes through an address";
                case Code.Newarr:
                    return "creates an array";
                case Code.Newobj:
                    return Creates((MethodReference)instruction.Operand);
                case Code.Call:
                case Code.Callvirt:
                    return Calls((MethodReference)instruction.Operand, instruction.OpCode.Code == Code.Callvirt, depth);
                case Code.Calli:
                case Code.Jmp:
                case Code.Ldftn:
                case Code.Ldvirtftn:
                    return "calls through a pointer";
                default:
                    return null;
            }
        }

        private static string Creates(MethodReference constructor)
        {
            var type = constructor.DeclaringType.GetElementType().FullName;
            return ValueMath.Contains(type) || type == "System.Nullable`1" ? null : "creates " + type;
        }

        private string Calls(MethodReference called, bool virtualCall, int depth)
        {
            MethodDefinition resolved = null;

            try
            {
                resolved = called.Resolve();
            }
            catch (AssemblyResolutionException)
            {
            }

            if (resolved != null && resolved.Module == module)
            {
                if (virtualCall && resolved.IsVirtual && !resolved.IsFinal && !resolved.DeclaringType.IsSealed)
                {
                    return "calls " + Name(resolved) + ", which a subclass may override";
                }

                var inner = Check(resolved, depth + 1);
                return inner == null ? null : inner + " (via " + Name(resolved) + ")";
            }

            var type = called.DeclaringType.GetElementType().FullName;

            if (ValueMath.Contains(type))
            {
                return !called.HasThis || called.Name.StartsWith("get_", StringComparison.Ordinal) ||
                       called.Name.StartsWith("op_", StringComparison.Ordinal) || called.Name == ".ctor"
                    ? null
                    : "calls " + type + "::" + called.Name + ", which may change its receiver";
            }

            if (Allowed.TryGetValue(type, out var names) &&
                (names.Contains(called.Name) || (names.Contains("*") && !called.HasThis)))
            {
                return null;
            }

            return "calls " + type + "::" + called.Name;
        }

        private static HashSet<string> Names(params string[] names)
        {
            return new HashSet<string>(names, StringComparer.Ordinal);
        }

        private static string Name(MethodReference method)
        {
            return method.DeclaringType.Name + "::" + method.Name;
        }

        private static string Name(FieldReference field)
        {
            return field.DeclaringType.FullName + "::" + field.Name;
        }
    }
}
