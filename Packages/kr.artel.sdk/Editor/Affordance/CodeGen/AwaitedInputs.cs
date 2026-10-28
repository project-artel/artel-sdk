using System;
using System.Collections.Generic;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// coroutine 이 마우스 handler 가 쓰는 필드를 기다리면, 그 뒤의 기록에 그 handler 를 gap 으로 붙인다(ARTEL-1227).
    /// </summary>
    /// <remarks>
    /// WordVenture 의 발동 버튼은 <c>CombineZone.CastSpell</c> 을 시작하고, 그 coroutine 은
    /// <c>while (target == null) yield return null;</c> 에서 멈춘다. <c>target</c> 은 적을 누를 때
    /// <c>SelectableObject.OnMouseDown</c> → <c>CombineZone.SetTarget</c> 이 쓴다. 공격 animation 과 주문 prefab 은
    /// 그 뒤에 오는데, 기록에는 발동 버튼 click 만 있어 TC 가 적 클릭을 빠뜨렸다(eval 의 부분 판정 13개 중 11개).
    ///
    /// 조건에 넣지 않고 gap 으로 적는다. coroutine 은 <c>yield</c> 마다 <c>MoveNext</c> 를 새로 부르므로 기다린 뒤의 코드는
    /// 기다림의 검사에 control dependent 하지 않고, 조건 트리로는 이 순서를 말할 수 없다. 그래서 같은
    /// <c>MoveNext</c> 안에서 검사 offset 보다 뒤에 있는 effect 나 호출을 가진 기록에
    /// <c>waits-for-input:&lt;type&gt;::&lt;message&gt;</c> 를 붙인다. 이 gap 은 소비자가 모르는 토큰이어도 그대로 통과한다.
    /// </remarks>
    internal static class AwaitedInputs
    {
        internal const string GapPrefix = "waits-for-input:";

        private static readonly string[] MouseMessages =
        {
            "OnMouseDown", "OnMouseUp", "OnMouseUpAsButton", "OnMouseDrag"
        };

        /// <summary>표시한 기록 수를 돌려준다.</summary>
        internal static int Mark(List<Variant> variants)
        {
            var writers = MouseWriters(variants);

            if (writers.Count == 0)
            {
                return 0;
            }

            var waits = Waits(variants, writers);
            var marked = 0;

            foreach (var variant in variants)
            {
                var before = variant.Gaps.Count;

                foreach (var wait in waits)
                {
                    if (variant.Method == wait.Method && ActsAfter(variant, wait.Offset))
                    {
                        foreach (var handler in wait.Handlers)
                        {
                            variant.AddGap(GapPrefix + handler);
                        }
                    }
                }

                if (variant.Gaps.Count > before)
                {
                    marked++;
                }
            }

            return marked;
        }

        /// <summary>마우스 메시지에서 출발한 기록이 쓰는 필드 → 그 메시지들.</summary>
        private static Dictionary<string, List<string>> MouseWriters(List<Variant> variants)
        {
            var writers = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var variant in variants)
            {
                var handlers = MouseEntries(variant);

                if (handlers.Count == 0)
                {
                    continue;
                }

                foreach (var outcome in variant.Outcomes)
                {
                    if (outcome.Kind != "write" || string.IsNullOrEmpty(outcome.Target))
                    {
                        continue;
                    }

                    if (!writers.TryGetValue(outcome.Target, out var known))
                    {
                        known = new List<string>();
                        writers[outcome.Target] = known;
                    }

                    foreach (var handler in handlers)
                    {
                        if (!known.Contains(handler))
                        {
                            known.Add(handler);
                        }
                    }
                }
            }

            return writers;
        }

        private static List<string> MouseEntries(Variant variant)
        {
            var found = new List<string>();
            AddIfMouse(variant.Entry, found);

            foreach (var arrival in variant.AlsoReachedBy)
            {
                AddIfMouse(arrival.Entry, found);
            }

            return found;
        }

        /// <summary><c>System.Void Ns.Type::OnMouseDown()</c> 이면 <c>Ns.Type::OnMouseDown</c> 을 더한다.</summary>
        internal static void AddIfMouse(string entry, List<string> into)
        {
            if (string.IsNullOrEmpty(entry))
            {
                return;
            }

            var space = entry.IndexOf(' ');
            var open = entry.IndexOf('(');
            var name = space >= 0 && open > space ? entry.Substring(space + 1, open - space - 1) : entry;
            var colon = name.LastIndexOf("::", StringComparison.Ordinal);
            var method = colon >= 0 ? name.Substring(colon + 2) : name;

            if (Array.IndexOf(MouseMessages, method) >= 0 && !into.Contains(name))
            {
                into.Add(name);
            }
        }

        private sealed class Wait
        {
            internal string Method;
            internal int Offset;
            internal List<string> Handlers;
        }

        /// <summary>coroutine 본문 안에서 마우스 handler 가 쓰는 필드를 검사하는 자리들.</summary>
        private static List<Wait> Waits(List<Variant> variants, Dictionary<string, List<string>> writers)
        {
            var waits = new List<Wait>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var variant in variants)
            {
                if (!IsCoroutineBody(variant.Method))
                {
                    continue;
                }

                foreach (var test in Tests(variant.When))
                {
                    if (test.Left == null || !writers.TryGetValue(test.Left, out var handlers))
                    {
                        continue;
                    }

                    if (seen.Add(variant.Method + "@" + test.Offset))
                    {
                        waits.Add(new Wait { Method = variant.Method, Offset = test.Offset, Handlers = handlers });
                    }
                }
            }

            return waits;
        }

        private static bool IsCoroutineBody(string method)
        {
            return method != null &&
                   method.EndsWith("::MoveNext()", StringComparison.Ordinal) &&
                   method.IndexOf('<') >= 0;
        }

        private static IEnumerable<Precondition> Tests(Condition condition)
        {
            if (condition == null)
            {
                yield break;
            }

            if (condition.Kind == ConditionKind.Test && condition.Test != null)
            {
                yield return condition.Test;
            }

            if (condition.Parts == null)
            {
                yield break;
            }

            foreach (var part in condition.Parts)
            {
                foreach (var test in Tests(part))
                {
                    yield return test;
                }
            }
        }

        private static bool ActsAfter(Variant variant, int offset)
        {
            foreach (var outcome in variant.Outcomes)
            {
                if (outcome.Offset > offset)
                {
                    return true;
                }
            }

            foreach (var call in variant.Calls)
            {
                if (call.Offset > offset)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
