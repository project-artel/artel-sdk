using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace Artel.CodeGen
{
    /// <summary>
    /// <c>[ArtelCheat]</c> 와 <c>[ArtelBehavior]</c> 메서드를 찾아 검사하고, 그것들을 직접 부르는 등록 코드를 만든다.
    /// </summary>
    /// <remarks>
    /// 런타임에 reflection 으로 어셈블리를 훑지 않으려는 것이 이 pass 의 이유다. 만들어진 코드가 메서드를 직접 참조하므로
    /// managed stripping 이 그 메서드를 지우지 않고, 시작할 때 어셈블리를 훑는 값도 없다.
    ///
    /// 잘못 쓴 함수는 컴파일 오류다(<see cref="Errors"/>). 지원하지 않는 parameter 타입이나 없는 <c>when</c> 을 run 중에
    /// 처음 알게 되면, 그것을 고칠 개발자는 그 자리에 없다.
    ///
    /// 진단을 <c>DiagnosticMessage</c> 가 아니라 문자열로 모으는 것은 <see cref="InputMethodWeaver"/> 와 같은 이유다. 그
    /// 타입이 사는 어셈블리는 테스트가 참조할 수 없다.
    ///
    /// 출시 빌드에서는 부르지 않는다. 그 판단은 <see cref="ArtelILPostProcessor"/> 가 define 을 보고 한다.
    /// </remarks>
    internal sealed class GameFunctionWeaver
    {
        internal const string RuntimeAssemblyName = "Artel.Runtime";
        internal const string CheatAttributeName = "Artel.ArtelCheatAttribute";
        internal const string BehaviorAttributeName = "Artel.ArtelBehaviorAttribute";
        internal const string RangeAttributeName = "Artel.CheatRangeAttribute";
        internal const string ContextTypeName = "Artel.ArtelBehaviorContext";
        internal const string RegistrationNamespace = "Artel.Generated";
        internal const string RegistrationTypeName = "GameFunctionRegistration";

        private const string EnumeratorTypeName = "System.Collections.IEnumerator";
        private static readonly Regex NamePattern = new Regex("^[a-z][a-z0-9_]*$");

        private readonly ModuleDefinition module;
        private readonly AssemblyNameReference runtimeReference;

        public List<string> Errors { get; } = new List<string>();

        private GameFunctionWeaver(ModuleDefinition module, AssemblyNameReference runtimeReference)
        {
            this.module = module;
            this.runtimeReference = runtimeReference;
        }

        /// <summary>
        /// 어셈블리가 <c>Artel.Runtime</c> 을 참조할 때만 만든다. attribute 타입이 그 어셈블리에 있으므로, 하나라도 붙었다면
        /// 참조가 남는다.
        /// </summary>
        public static GameFunctionWeaver TryCreate(ModuleDefinition module)
        {
            var reference = module.AssemblyReferences.FirstOrDefault(item => item.Name == RuntimeAssemblyName);
            return reference == null ? null : new GameFunctionWeaver(module, reference);
        }

        /// <summary>
        /// 찾고, 검사하고, 오류가 없으면 만든다. 어셈블리를 바꿨으면 true. 오류가 하나라도 있으면 아무것도 바꾸지 않는다.
        /// </summary>
        public bool Process()
        {
            if (module.GetType(RegistrationNamespace + "." + RegistrationTypeName) != null)
            {
                return false;
            }

            var candidates = Discover();
            if (candidates.Count == 0 || Errors.Count > 0)
            {
                return false;
            }

            var runtimeModule = module.AssemblyResolver.Resolve(runtimeReference).MainModule;
            new GameFunctionEmitter(module, runtimeModule).Emit(candidates);
            return true;
        }

        /// <summary><c>GiveGold</c> → <c>give_gold</c>, <c>GiveXPNow</c> → <c>give_xp_now</c>.</summary>
        internal static string SnakeCase(string name)
        {
            var text = new StringBuilder(name.Length + 4);
            for (var index = 0; index < name.Length; index++)
            {
                var character = name[index];
                if (!char.IsUpper(character))
                {
                    text.Append(character);
                    continue;
                }

                if (index > 0)
                {
                    var previous = name[index - 1];
                    var nextIsLower = index + 1 < name.Length && char.IsLower(name[index + 1]);
                    if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower))
                    {
                        text.Append('_');
                    }
                }

                text.Append(char.ToLowerInvariant(character));
            }

            return text.ToString();
        }

        private List<GameFunctionCandidate> Discover()
        {
            var candidates = new List<GameFunctionCandidate>();
            var namesByKind = new Dictionary<string, MethodDefinition>(StringComparer.Ordinal);

            foreach (var type in module.Types.SelectMany(SelfAndNestedTypes))
            {
                foreach (var method in type.Methods)
                {
                    var attribute = method.CustomAttributes.FirstOrDefault(item =>
                        item.AttributeType.FullName == CheatAttributeName ||
                        item.AttributeType.FullName == BehaviorAttributeName);
                    if (attribute == null)
                    {
                        continue;
                    }

                    var candidate = Read(method, attribute);
                    if (candidate == null)
                    {
                        continue;
                    }

                    var key = candidate.Kind + "/" + candidate.Name;
                    if (namesByKind.TryGetValue(key, out var first))
                    {
                        AddError(method, "uses the name " + candidate.Name + ", which " + first.FullName + " already has");
                        continue;
                    }

                    namesByKind[key] = method;
                    candidates.Add(candidate);
                }
            }

            return candidates;
        }

        private GameFunctionCandidate Read(MethodDefinition method, CustomAttribute attribute)
        {
            var isBehavior = attribute.AttributeType.FullName == BehaviorAttributeName;
            var arguments = attribute.ConstructorArguments;
            var candidate = new GameFunctionCandidate
            {
                Method = method,
                Kind = isBehavior ? GameFunctionKind.Behavior : GameFunctionKind.Cheat,
                Description = ArgumentAt(arguments, 0),
                Group = isBehavior ? ArgumentAt(arguments, 1) : null,
                Name = ArgumentAt(arguments, isBehavior ? 2 : 1),
                WhenName = ArgumentAt(arguments, isBehavior ? 3 : 2)
            };

            var errorsBefore = Errors.Count;
            if (candidate.Name == null)
            {
                candidate.Name = SnakeCase(method.Name);
            }

            if (!NamePattern.IsMatch(candidate.Name))
            {
                AddError(method, "has the name " + candidate.Name + ", which must match ^[a-z][a-z0-9_]*$");
            }

            CheckShape(method, isBehavior);
            ReadParameters(candidate, isBehavior);
            ReadCondition(candidate);
            return Errors.Count == errorsBefore ? candidate : null;
        }

        private void CheckShape(MethodDefinition method, bool isBehavior)
        {
            if (method.HasGenericParameters || IsGenericAnywhere(method.DeclaringType))
            {
                AddError(method, "cannot be generic or live on a generic type");
            }

            if (!IsReachable(method.DeclaringType))
            {
                AddError(method, "lives on a type that is private or protected inside another type; make the type internal or public");
            }

            if (method.IsConstructor || method.IsGetter || method.IsSetter)
            {
                AddError(method, "must be an ordinary method");
            }

            if (!method.IsStatic && !IsMonoBehaviour(method.DeclaringType))
            {
                AddError(method, "is an instance method, so its type must derive from UnityEngine.MonoBehaviour");
            }

            var returns = method.ReturnType.FullName;
            if (isBehavior && returns != EnumeratorTypeName)
            {
                AddError(method, "is a behavior, so it must return System.Collections.IEnumerator");
            }

            if (!isBehavior && returns != "System.Void" && returns != "System.String" && returns != EnumeratorTypeName)
            {
                AddError(method, "is a cheat, so it must return void, string or System.Collections.IEnumerator");
            }
        }

        private void ReadParameters(GameFunctionCandidate candidate, bool isBehavior)
        {
            var method = candidate.Method;
            for (var index = 0; index < method.Parameters.Count; index++)
            {
                var parameter = method.Parameters[index];
                var type = parameter.ParameterType;

                if (type.FullName == ContextTypeName)
                {
                    if (!isBehavior || index != method.Parameters.Count - 1)
                    {
                        AddError(method, "may take ArtelBehaviorContext only as the last parameter of a behavior");
                    }

                    candidate.TakesContext = true;
                    continue;
                }

                if (type.IsByReference || parameter.IsOut)
                {
                    AddError(method, "has the ref or out parameter " + parameter.Name + ", which is not supported");
                    continue;
                }

                var read = ReadParameter(method, parameter);
                if (read != null)
                {
                    candidate.Parameters.Add(read);
                }
            }
        }

        private GameFunctionCandidate.Parameter ReadParameter(MethodDefinition method, ParameterDefinition parameter)
        {
            var type = parameter.ParameterType;
            var result = new GameFunctionCandidate.Parameter { Definition = parameter, Name = parameter.Name };

            switch (type.FullName)
            {
                case "System.Int32":
                    result.Type = CheatParamType.Int;
                    break;
                case "System.Single":
                    result.Type = CheatParamType.Float;
                    break;
                case "System.Boolean":
                    result.Type = CheatParamType.Bool;
                    break;
                case "System.String":
                    result.Type = CheatParamType.String;
                    break;
                default:
                    var resolved = TryResolve(type);
                    if (resolved == null || !resolved.IsEnum)
                    {
                        AddError(method, "has the parameter " + parameter.Name + " of type " + type.FullName +
                                         "; supported types are int, float, bool, string and enums");
                        return null;
                    }

                    if (!IsReachable(resolved))
                    {
                        AddError(method, "has the parameter " + parameter.Name + " whose enum " + type.FullName +
                                         " is private or protected inside another type");
                        return null;
                    }

                    result.Type = CheatParamType.Enum;
                    result.Enum = resolved;
                    break;
            }

            ReadRange(method, parameter, result);
            ReadDefault(method, parameter, result);
            return result;
        }

        private void ReadRange(MethodDefinition method, ParameterDefinition parameter, GameFunctionCandidate.Parameter result)
        {
            var range = parameter.CustomAttributes.FirstOrDefault(item => item.AttributeType.FullName == RangeAttributeName);
            if (range == null)
            {
                return;
            }

            if (result.Type != CheatParamType.Int && result.Type != CheatParamType.Float)
            {
                AddError(method, "puts [CheatRange] on " + parameter.Name + ", which is not an int or a float");
                return;
            }

            result.HasRange = true;
            result.Min = Convert.ToDouble(range.ConstructorArguments[0].Value, CultureInfo.InvariantCulture);
            result.Max = Convert.ToDouble(range.ConstructorArguments[1].Value, CultureInfo.InvariantCulture);
            if (result.Min > result.Max)
            {
                AddError(method, "has [CheatRange] on " + parameter.Name + " whose min is above its max");
            }
        }

        /// <summary>
        /// C# 기본값이 있는 parameter 만 생략할 수 있다. 기본값은 invariant culture 문자열로 넘기고, enum 은 멤버 이름으로 넘긴다.
        /// </summary>
        private void ReadDefault(MethodDefinition method, ParameterDefinition parameter, GameFunctionCandidate.Parameter result)
        {
            if (!parameter.HasDefault || !parameter.HasConstant)
            {
                return;
            }

            result.IsOptional = true;
            var constant = parameter.Constant;
            switch (result.Type)
            {
                case CheatParamType.Int:
                    result.DefaultText = Convert.ToInt32(constant, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    break;
                case CheatParamType.Float:
                    result.DefaultText = Convert.ToSingle(constant, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);
                    break;
                case CheatParamType.Bool:
                    result.DefaultText = (bool)constant ? "true" : "false";
                    break;
                case CheatParamType.String:
                    result.DefaultText = constant as string;
                    break;
                default:
                    result.DefaultText = EnumMemberName(result.Enum, constant);
                    if (result.DefaultText == null)
                    {
                        AddError(method, "has a default for " + parameter.Name + " that is not a named member of " +
                                         result.Enum.FullName);
                    }

                    break;
            }
        }

        private void ReadCondition(GameFunctionCandidate candidate)
        {
            if (candidate.WhenName == null)
            {
                return;
            }

            var method = candidate.Method;
            var condition = method.DeclaringType.Methods.FirstOrDefault(item =>
                item.Name == candidate.WhenName && !item.HasParameters && !item.HasGenericParameters);
            if (condition == null)
            {
                AddError(method, "names when: " + candidate.WhenName + ", but " + method.DeclaringType.FullName +
                                 " has no method of that name without parameters");
                return;
            }

            var returns = condition.ReturnType.FullName;
            if (returns != "System.Boolean" && returns != "System.String")
            {
                AddError(method, "names when: " + candidate.WhenName + ", which must return bool or string");
                return;
            }

            if (method.IsStatic && !condition.IsStatic)
            {
                AddError(method, "is static, so its when: " + candidate.WhenName + " must be static too");
                return;
            }

            candidate.Condition = condition;
        }

        private static string EnumMemberName(TypeDefinition enumType, object constant)
        {
            if (constant == null)
            {
                return null;
            }

            var value = Convert.ToInt64(constant, CultureInfo.InvariantCulture);
            var member = enumType.Fields.FirstOrDefault(field =>
                field.IsStatic && field.HasConstant &&
                Convert.ToInt64(field.Constant, CultureInfo.InvariantCulture) == value);
            return member?.Name;
        }

        private static string ArgumentAt(IList<CustomAttributeArgument> arguments, int index)
        {
            return index < arguments.Count ? arguments[index].Value as string : null;
        }

        private static TypeDefinition TryResolve(TypeReference type)
        {
            try
            {
                return type.Resolve();
            }
            catch (AssemblyResolutionException)
            {
                return null;
            }
        }

        /// <summary>
        /// 만들어진 등록 코드가 이 타입을 <c>ldtoken</c> 하고 그 안의 stub 을 가리킬 수 있는지. 다른 타입 안의 private 이나
        /// protected 타입은 같은 어셈블리의 다른 클래스에서 닿지 않는다.
        /// </summary>
        private static bool IsReachable(TypeDefinition type)
        {
            for (var current = type; current != null; current = current.DeclaringType)
            {
                if (!current.IsNested)
                {
                    return current.Module == type.Module || current.IsPublic;
                }

                if (!current.IsNestedPublic && !current.IsNestedAssembly && !current.IsNestedFamilyOrAssembly)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsGenericAnywhere(TypeDefinition type)
        {
            for (var current = type; current != null; current = current.DeclaringType)
            {
                if (current.HasGenericParameters)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsMonoBehaviour(TypeDefinition type)
        {
            try
            {
                for (var current = type; current != null; current = current.BaseType?.Resolve())
                {
                    if (current.FullName == "UnityEngine.MonoBehaviour")
                    {
                        return true;
                    }
                }
            }
            catch (AssemblyResolutionException)
            {
            }

            return false;
        }

        private static IEnumerable<TypeDefinition> SelfAndNestedTypes(TypeDefinition type)
        {
            yield return type;
            foreach (var nested in type.NestedTypes.SelectMany(SelfAndNestedTypes))
            {
                yield return nested;
            }
        }

        private void AddError(MethodDefinition method, string message)
        {
            Errors.Add(method.DeclaringType.FullName + "." + method.Name + " " + message + ".");
        }
    }

    /// <summary>검사를 통과한 함수 하나. <see cref="GameFunctionEmitter"/> 가 이것만 보고 IL 을 쓴다.</summary>
    internal sealed class GameFunctionCandidate
    {
        public MethodDefinition Method;
        public GameFunctionKind Kind;
        public string Name;
        public string Description;
        public string Group;
        public string WhenName;
        public MethodDefinition Condition;
        public bool TakesContext;
        public readonly List<Parameter> Parameters = new List<Parameter>();

        internal sealed class Parameter
        {
            public ParameterDefinition Definition;
            public string Name;
            public CheatParamType Type;
            public TypeDefinition Enum;
            public bool HasRange;
            public double Min;
            public double Max;
            public bool IsOptional;
            public string DefaultText;
        }
    }
}
