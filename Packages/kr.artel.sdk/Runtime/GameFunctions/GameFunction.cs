using System;
using System.Collections.Generic;

namespace Artel
{
    /// <summary>
    /// 등록된 cheat 나 behavior 하나. weaver 가 만든 등록과 <see cref="ArtelCheats.Register"/> 가 같은 이 모양으로 끝난다.
    /// </summary>
    /// <remarks>
    /// 그래서 인자 검사, catalog, <c>PULSE.functions</c> 가 입력 모양을 하나만 안다.
    /// </remarks>
    internal sealed class GameFunction
    {
        public GameFunctionKind Kind;
        public string Name;
        public string Description;

        /// <summary>behavior 만 쓴다. cheat 와 group 없는 behavior 는 null.</summary>
        public string Group;

        /// <summary>instance 메서드가 사는 <c>MonoBehaviour</c> 타입. static 이면 null.</summary>
        public Type Component;

        public GameFunctionCall Call;

        /// <summary><c>when</c> 이 없으면 null.</summary>
        public GameFunctionCondition Condition;

        /// <summary><c>when</c> 메서드 이름. 그것이 false 를 돌려줄 때의 이유 문장에 쓴다.</summary>
        public string ConditionName;

        /// <summary>마지막 인자로 <see cref="ArtelBehaviorContext"/> 를 받는지.</summary>
        public bool TakesContext;

        public readonly List<GameFunctionParam> Params = new List<GameFunctionParam>();

        public bool IsStatic => Component == null;
    }

    internal sealed class GameFunctionParam
    {
        public string Name;
        public CheatParamType Type;

        /// <summary>weaver 가 등록한 enum parameter 의 C# 타입. 런타임 등록은 이름만 있으므로 null.</summary>
        public Type EnumType;

        public IReadOnlyList<string> EnumValues;
        public double? Min;
        public double? Max;
        public bool IsOptional;

        /// <summary>생략했을 때 넘길 값. 메서드가 받는 그 타입으로 들고 있다.</summary>
        public object Default;
    }
}
