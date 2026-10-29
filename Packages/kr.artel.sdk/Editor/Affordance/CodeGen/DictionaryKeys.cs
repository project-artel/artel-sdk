using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// <c>Input.GetKey(keyMappings[action])</c> 처럼 dictionary 에서 꺼낸 key 를 상수로 읽는다(ARTEL-1230).
    /// </summary>
    /// <remarks>
    /// gyemong 은 이동과 상호작용 key 를 모두 <c>Dictionary&lt;ActionCode, KeyCode&gt;</c> 에서 읽고, evidence 에는
    /// <c>(not a literal)</c> 로 남았다. agent 는 key 를 몰라 A, LeftArrow, E, F, KeypadEnter 를 차례로 눌러 봤다.
    ///
    /// 그 dictionary 필드를 채우는 상수를 그 필드가 선언된 class 의 모든 메서드에서 모은다. collection initializer
    /// (<c>new Dictionary { { k, v } }</c> 를 필드에 대입)와 필드에 대고 부른 <c>Add</c>, indexer 대입을 읽는다. 이름이
    /// <c>.cctor</c>, 생성자, <c>Awake</c> 인 메서드만 보지 않는 이유는 gyemong 이 <c>Start</c> 가 부르는
    /// <c>SetDefaultKey</c> 에서 채우기 때문이다.
    ///
    /// 꺼낼 때 쓴 key 가 상수이면 그 값 하나를, 아니면 표의 값 전체를 후보로 돌려준다. 후보는 그중 하나가 눌린다는 뜻이고,
    /// 조건에는 그 key 들의 <c>either</c> 로 들어간다.
    /// </remarks>
    internal static class DictionaryKeys
    {
        private const string KeyCodeType = "UnityEngine.KeyCode";

        /// <summary>뒤로 몇 명령어까지 dictionary 를 올린 자리를 찾는지.</summary>
        private const int MaxLookBack = 8;

        private static readonly Dictionary<FieldDefinition, Table> Known = new Dictionary<FieldDefinition, Table>();

        internal sealed class Table
        {
            internal readonly Dictionary<string, string> ByKey = new Dictionary<string, string>(StringComparer.Ordinal);
            internal readonly List<string> Values = new List<string>();

            internal void Add(string key, string value)
            {
                if (key != null)
                {
                    ByKey[key] = value;
                }

                if (!Values.Contains(value))
                {
                    Values.Add(value);
                }
            }
        }

        /// <summary>assembly 하나를 다 읽으면 비운다. 표의 key 가 그 module 의 FieldDefinition 이다.</summary>
        internal static void Forget()
        {
            Known.Clear();
        }

        /// <summary>
        /// <paramref name="argument"/> 가 <c>dict[x]</c> 로 꺼낸 KeyCode 이면 참.
        /// </summary>
        /// <param name="argument">key 를 스택에 올린 명령어. 보통 <c>Dictionary.get_Item</c> 호출이다.</param>
        /// <param name="name">x 가 상수라 하나로 정해지면 그 KeyCode 이름, 아니면 null.</param>
        /// <param name="candidates">x 가 상수가 아니면 표의 값 전체, 아니면 null.</param>
        internal static bool TryRead(Instruction argument, out string name, out List<string> candidates)
        {
            name = null;
            candidates = null;

            if (!IsKeyCodeLookup(argument, "get_Item"))
            {
                return false;
            }

            var keyArgument = argument.Previous;
            var field = DictionaryBefore(keyArgument);
            var table = field == null ? null : TableOf(field);

            if (table == null || table.Values.Count == 0)
            {
                return false;
            }

            var key = Constant(keyArgument);

            if (key != null && table.ByKey.TryGetValue(key, out var value))
            {
                name = value;
                return true;
            }

            candidates = new List<string>(table.Values);
            return true;
        }

        /// <summary>호출이 <c>Dictionary&lt;?, KeyCode&gt;</c> 의 <paramref name="method"/> 인가.</summary>
        private static bool IsKeyCodeLookup(Instruction instruction, string method)
        {
            if (instruction == null || instruction.OpCode.FlowControl != FlowControl.Call ||
                !(instruction.Operand is MethodReference called) || called.Name != method)
            {
                return false;
            }

            return called.DeclaringType is GenericInstanceType generic &&
                   generic.ElementType.Name.IndexOf("Dictionary`2", StringComparison.Ordinal) >= 0 &&
                   generic.GenericArguments.Count == 2 &&
                   generic.GenericArguments[1].FullName == KeyCodeType;
        }

        private static bool IsKeyCodeDictionary(TypeReference type)
        {
            return type is GenericInstanceType generic &&
                   generic.ElementType.Name.IndexOf("Dictionary`2", StringComparison.Ordinal) >= 0 &&
                   generic.GenericArguments.Count == 2 &&
                   generic.GenericArguments[1].FullName == KeyCodeType;
        }

        /// <summary>key 를 올리기 전에 dictionary 를 올린 <c>ldfld</c>/<c>ldsfld</c> 의 필드.</summary>
        private static FieldDefinition DictionaryBefore(Instruction keyArgument)
        {
            var at = keyArgument?.Previous;

            for (var step = 0; step < MaxLookBack && at != null; step++, at = at.Previous)
            {
                if ((at.OpCode.Code == Code.Ldfld || at.OpCode.Code == Code.Ldsfld) &&
                    at.Operand is FieldReference field && IsKeyCodeDictionary(field.FieldType))
                {
                    return Resolve(field);
                }
            }

            return null;
        }

        private static FieldDefinition Resolve(FieldReference field)
        {
            try
            {
                return field.Resolve();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>상수 key 를 문자열로. enum 과 int 는 숫자, 문자열은 따옴표로 감싸 서로 섞이지 않게 한다.</summary>
        private static string Constant(Instruction instruction)
        {
            if (instruction == null)
            {
                return null;
            }

            if (instruction.OpCode.Code == Code.Ldstr)
            {
                return "\"" + instruction.Operand + "\"";
            }

            return IlReading.TryConstant(instruction, out var value) ? value.ToString() : null;
        }

        internal static Table TableOf(FieldDefinition field)
        {
            if (Known.TryGetValue(field, out var known))
            {
                return known;
            }

            var table = new Table();
            Known[field] = table;

            foreach (var method in field.DeclaringType.Methods)
            {
                if (!method.HasBody)
                {
                    continue;
                }

                foreach (var instruction in method.Body.Instructions)
                {
                    if (!IsKeyCodeLookup(instruction, "Add") && !IsKeyCodeLookup(instruction, "set_Item"))
                    {
                        continue;
                    }

                    var valueArgument = instruction.Previous;
                    var keyArgument = valueArgument?.Previous;

                    if (!IlReading.TryConstant(valueArgument, out var value) ||
                        TargetOf(keyArgument?.Previous, instruction) != field)
                    {
                        continue;
                    }

                    var keyCode = ((GenericInstanceType)((MethodReference)instruction.Operand).DeclaringType)
                        .GenericArguments[1];
                    table.Add(Constant(keyArgument), IlReading.EnumName(keyCode, value));
                }
            }

            return table;
        }

        /// <summary>
        /// <c>Add</c> 가 어느 필드의 dictionary 에 대고 불렸나.
        /// </summary>
        /// <remarks>
        /// 필드를 올려 부르면 <paramref name="receiver"/> 가 그 <c>ldfld</c> 다. collection initializer 는 새 dictionary 를
        /// <c>dup</c> 해 가며 <c>Add</c> 를 부르고 끝에서 필드에 저장하므로, 그때는 앞으로 가며 첫 <c>stfld</c> 를 찾는다.
        /// </remarks>
        private static FieldDefinition TargetOf(Instruction receiver, Instruction call)
        {
            if (receiver == null)
            {
                return null;
            }

            if ((receiver.OpCode.Code == Code.Ldfld || receiver.OpCode.Code == Code.Ldsfld) &&
                receiver.Operand is FieldReference loaded)
            {
                return Resolve(loaded);
            }

            if (receiver.OpCode.Code != Code.Dup)
            {
                return null;
            }

            for (var at = call.Next; at != null; at = at.Next)
            {
                if (at.OpCode.Code == Code.Newobj || at.OpCode.FlowControl == FlowControl.Return ||
                    at.OpCode.FlowControl == FlowControl.Branch || at.OpCode.FlowControl == FlowControl.Cond_Branch)
                {
                    return null;
                }

                if ((at.OpCode.Code == Code.Stfld || at.OpCode.Code == Code.Stsfld) &&
                    at.Operand is FieldReference stored && IsKeyCodeDictionary(stored.FieldType))
                {
                    return Resolve(stored);
                }
            }

            return null;
        }
    }
}
