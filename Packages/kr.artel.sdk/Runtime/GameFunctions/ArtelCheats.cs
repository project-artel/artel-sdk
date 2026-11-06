using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Artel
{
    /// <summary>
    /// 런타임에 cheat 를 더하고 뺀다. 데이터에서 만드는 cheat(아이템 표의 id 마다 하나)처럼 attribute 로 쓸 수 없는 경우를
    /// 위한 것이다.
    /// </summary>
    /// <remarks>
    /// 출시 빌드에서도 컴파일되지만 아무 일도 하지 않는다. 그래서 게임 코드는 <c>#if</c> 없이 부를 수 있다.
    ///
    /// 등록한 cheat 는 <c>[ArtelCheat]</c> 와 같은 목록에 들어가고, 바뀐 목록은 1초에 한 번까지
    /// <c>GAME_FUNCTION_CATALOG</c> 로 다시 나간다.
    /// </remarks>
    public static class ArtelCheats
    {
        /// <summary>
        /// cheat 하나를 등록한다. 같은 이름이 이미 있으면 등록하지 않고 경고를 남기며 false 를 돌려준다.
        /// </summary>
        /// <param name="handler">
        /// 검사를 마친 인자를 받는다. <c>null</c> 이나 <c>string</c>(결과 <c>message</c>), 또는 기다리는 cheat 라면
        /// <c>IEnumerator</c> 를 돌려준다.
        /// </param>
        public static bool Register(
            string name, string description, IReadOnlyList<CheatParam> parameters, Func<CheatArgs, object> handler)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            var declared = parameters ?? Array.Empty<CheatParam>();
            var duplicate = declared.GroupBy(parameter => parameter.Name).FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                throw new ArgumentException("Cheat " + name + " declares the parameter " + duplicate.Key + " twice.", nameof(parameters));
            }

            var function = new GameFunction
            {
                Kind = GameFunctionKind.Cheat,
                Name = name,
                Description = description,
                Call = (target, values) => handler(new CheatArgs(declared, values))
            };

            foreach (var parameter in declared)
            {
                function.Params.Add(new GameFunctionParam
                {
                    Name = parameter.Name,
                    Type = parameter.Type,
                    EnumValues = parameter.Values,
                    Min = parameter.Min,
                    Max = parameter.Max,
                    IsOptional = parameter.IsOptional,
                    Default = parameter.Default
                });
            }

            if (GameFunctionRegistry.Shared.TryAdd(function, out var error))
            {
                return true;
            }

            Debug.LogWarning("[Artel] " + error);
            return false;
#else
            return false;
#endif
        }

        /// <summary>이름으로 cheat 를 뺀다. 없던 이름이면 false.</summary>
        public static bool Unregister(string name)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return GameFunctionRegistry.Shared.Remove(GameFunctionKind.Cheat, name);
#else
            return false;
#endif
        }
    }
}
