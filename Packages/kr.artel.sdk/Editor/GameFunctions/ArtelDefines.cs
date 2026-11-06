using System.Collections.Generic;
using System.Linq;

namespace Artel.GameFunctions.Editor
{
    /// <summary>
    /// 이 SDK 버전이 켜는 scripting define 목록과, 프로젝트의 define 목록을 그 목록에 맞추는 규칙.
    /// </summary>
    /// <remarks>
    /// <c>ARTEL_</c> 로 시작하는 define 은 SDK 의 것이다. 게임은 그 define 으로 코드를 감싸기만 하고 직접 정의하지 않는다.
    /// SDK 는 지원하는 define 을 켜고, 목록에 없는 <c>ARTEL_</c> define 은 끈다. 목록에 없는 define 은 더 새로운 SDK 가
    /// 켜 두고 간 것이라서, 그 define 으로 감싼 코드는 이 SDK 에 없는 타입을 쓴다. <c>ARTEL_</c> 로 시작하지 않는 define 은
    /// 게임의 것이므로 건드리지 않는다.
    /// </remarks>
    internal static class ArtelDefines
    {
        internal const string Prefix = "ARTEL_";

        /// <summary><c>[ArtelCheat]</c>, <c>[ArtelBehavior]</c>, <c>[CheatRange]</c>, <c>ArtelBehaviorContext</c> 가 있다.</summary>
        internal const string GameFunctions = "ARTEL_GAME_FUNCTIONS";

        internal static readonly IReadOnlyList<string> Supported = new[] { GameFunctions };

        /// <summary>
        /// <paramref name="current"/> 에서 <paramref name="supported"/> 에 없는 <c>ARTEL_</c> define 을 빼고, 빠진
        /// <paramref name="supported"/> define 을 뒤에 붙인다. 나머지 define 의 순서는 그대로 둔다.
        /// </summary>
        internal static string[] Reconcile(IEnumerable<string> current, IReadOnlyCollection<string> supported)
        {
            var kept = current
                .Where(symbol => !symbol.StartsWith(Prefix) || supported.Contains(symbol))
                .ToList();
            var missing = supported.Where(symbol => !kept.Contains(symbol));
            return kept.Concat(missing).ToArray();
        }
    }
}
