using System;
using System.Collections.Generic;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// 버튼 click 에 잘못 묶이는 기록을 굽기 전에 걸러낸다(ARTEL-1229).
    /// </summary>
    /// <remarks>
    /// WordVenture 의 발동 버튼에 묶인 step capability 오류 11 개 중 7 개가 이 버튼이었다. 원인은 셋이다.
    ///
    /// 1. 다른 진입점이 <c>onClick.AddListener(OnButtonClick)</c> 로 handler 를 넘긴 자리에서 분석이 그 delegate 를 따라가,
    ///    handler 본문을 그 진입점의 기록으로 한 번 더 적었다. handler 가 public 이면 그 자신이 진입점이라 같은 본문이 이미
    ///    제 진입점으로 적혀 있고, 넘긴 쪽의 기록은 카드 drop 이나 <c>Start</c> 의 조건을 버튼 click 에 붙인다.
    ///    <see cref="DropHandedRoots"/> 가 이것을 지운다.
    /// 2. 내용이 같은 기록을 접을 때 버튼 handler 에서 출발한 기록이 다른 진입점의 기록 밑 <c>alsoReachedBy</c> 로 들어갔다.
    ///    소비자는 그 갈래로도 버튼을 잇는데, 남은 기록의 <c>callPath</c> 와 진입점은 다른 진입점의 것이다.
    ///    <c>DuplicateVariants</c> 가 inspector 에서 부를 수 있는 진입점의 기록을 진입점까지 같을 때만 접는다.
    /// 3. 값 type 의 생성자(<c>Prs..ctor</c>)가 자기 필드에 쓰는 것만 하는 기록이 effect 를 가진 capability 로 남았다.
    ///    플레이어가 볼 수 있는 객체가 아니다. <see cref="DropValueConstructors"/> 가 지운다.
    /// </remarks>
    internal static class ButtonJoin
    {
        private const string UnityEventPrefix = "UnityEngine.Events.UnityEvent";

        /// <summary>
        /// UnityEvent listener 로 넘겨진 메서드가 그 자체로 진입점이면, 넘긴 쪽에서 따라간 기록을 지운다. 지운 수를 돌려준다.
        /// </summary>
        internal static int DropHandedRoots(List<Variant> variants, ICollection<string> roots)
        {
            return variants.RemoveAll(variant => IsHandedRoot(variant, roots));
        }

        internal static bool IsHandedRoot(Variant variant, ICollection<string> roots)
        {
            if (variant.HandedIn < 0 || variant.HandedTo == null ||
                !variant.HandedTo.StartsWith(UnityEventPrefix, StringComparison.Ordinal) ||
                !variant.HandedTo.EndsWith("::AddListener", StringComparison.Ordinal))
            {
                return false;
            }

            var handed = variant.HandedIn + 1;
            return handed < variant.CallPath.Count && roots.Contains(variant.CallPath[handed]);
        }

        /// <summary>
        /// behaviour 가 아닌 type 의 생성자가 자기 필드에 쓰기만 하는 기록을 지운다. 지운 수를 돌려준다.
        /// </summary>
        internal static int DropValueConstructors(List<Variant> variants, Func<string, bool> isBehaviour)
        {
            return variants.RemoveAll(variant => IsValueConstructor(variant, isBehaviour));
        }

        internal static bool IsValueConstructor(Variant variant, Func<string, bool> isBehaviour)
        {
            var method = variant.Method;

            if (method == null || method.IndexOf("::.ctor(", StringComparison.Ordinal) < 0 ||
                variant.Calls.Count > 0 || variant.Handles.Count > 0)
            {
                return false;
            }

            var space = method.IndexOf(' ');
            var colon = method.IndexOf("::", StringComparison.Ordinal);
            var type = space >= 0 && colon > space ? method.Substring(space + 1, colon - space - 1) : null;

            if (type == null || isBehaviour(type))
            {
                return false;
            }

            foreach (var outcome in variant.Outcomes)
            {
                if (outcome.Kind != "write")
                {
                    return false;
                }
            }

            return true;
        }
    }
}
