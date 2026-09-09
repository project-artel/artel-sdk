using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel.Pointer
{
    /// <summary>
    /// <c>Artel.Affordances.Scan.ScenePath.SelectorOf</c> 가 쓴 문자열을 거꾸로 읽어, 액티브 씬에서 그
    /// <see cref="Transform"/> 을 되찾는다.
    /// </summary>
    /// <remarks>
    /// <c>SelectorOf</c> 는 <c>name[siblingIndex]</c> 를 계층을 따라 <c>/</c> 로 이은 것을 쓴다. 다만 루트
    /// 한 단계만은 다르다 — <c>GetSiblingIndex()</c> 가 루트 오브젝트에서는 몇 번째 루트든 0 을 답하기 때문에,
    /// 그 값 대신 씬을 순회한 자리(<c>scene.GetRootGameObjects()</c> 의 인덱스)를 쓴다. 그래서 여기서도 첫
    /// 세그먼트만 <c>GetRootGameObjects()[index]</c> 로 찾고, 나머지는 <c>parent.GetChild(index)</c> 로
    /// 찾는다.
    /// <para>
    /// <c>SelectorOf</c> 가 계층 깊이 한계(64)를 넘겨 <c>.../</c> 로 자른 selector 는 루트로 거슬러 올라갈
    /// 방법이 없어 애초에 풀 수 없다 — 앞머리만 보고 거절한다.
    /// </para>
    /// <para>
    /// 이름에 <c>/</c> 가 들어 있으면 이 리졸버는 세그먼트 경계를 되짚을 방법이 없어 왕복시키지 못한다.
    /// <c>SelectorOf</c> 는 이름을 이스케이프하지 않고 그대로 이어 쓰기 때문이다. 이름에 <c>[</c> 나 <c>]</c>
    /// 가 있는 경우는, 각 세그먼트의 마지막 <c>[...]</c> 를 인덱스로 읽어 지원한다 — <c>SelectorOf</c> 가 매
    /// 세그먼트 끝에 <c>[index]</c> 를 붙이는 방식과 맞물린다.
    /// </para>
    /// </remarks>
    internal static class ScenePathSelectorResolver
    {
        private const string TruncatedPrefix = ".../";

        internal static bool TryResolve(string selector, out Transform transform, out string error)
        {
            transform = null;

            if (string.IsNullOrEmpty(selector))
            {
                error = "Empty selector.";
                return false;
            }

            if (selector.StartsWith(TruncatedPrefix, StringComparison.Ordinal))
            {
                error = "Selector was truncated by the scan and cannot be resolved: " + selector;
                return false;
            }

            var segments = selector.Split('/');

            if (!TryParseSegment(segments[0], out var rootName, out var rootIndex))
            {
                error = "Nothing at selector: " + selector;
                return false;
            }

            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            if (rootIndex < 0 || rootIndex >= roots.Length || roots[rootIndex].name != rootName)
            {
                error = "Nothing at selector: " + selector;
                return false;
            }

            var current = roots[rootIndex].transform;
            for (var i = 1; i < segments.Length; i++)
            {
                if (!TryParseSegment(segments[i], out var name, out var siblingIndex))
                {
                    error = "Nothing at selector: " + selector;
                    return false;
                }

                if (siblingIndex < 0 || siblingIndex >= current.childCount)
                {
                    error = "Nothing at selector: " + selector;
                    return false;
                }

                var child = current.GetChild(siblingIndex);

                // 스캔과 액션 사이에 계층이 바뀌었을 수 있다. 자리만 맞고 이름이 다르면 엉뚱한 것을 겨눈
                // 것이므로, 조용히 넘어가지 않고 여기서 거절한다.
                if (child.name != name)
                {
                    error = "Nothing at selector: " + selector;
                    return false;
                }

                current = child;
            }

            transform = current;
            error = null;
            return true;
        }

        /// <summary>
        /// 한 세그먼트 <c>name[index]</c> 를 이름과 인덱스로 가른다. 이름 자체가 <c>[</c> 나 <c>]</c> 를
        /// 담을 수 있으므로, 인덱스는 세그먼트의 마지막 <c>[...]</c> 에서만 읽는다.
        /// </summary>
        private static bool TryParseSegment(string segment, out string name, out int index)
        {
            name = null;
            index = -1;

            if (string.IsNullOrEmpty(segment))
            {
                return false;
            }

            var closeBracket = segment.Length - 1;
            if (segment[closeBracket] != ']')
            {
                return false;
            }

            var openBracket = -1;
            for (var i = closeBracket - 1; i >= 0; i--)
            {
                if (segment[i] == '[')
                {
                    openBracket = i;
                    break;
                }
            }

            if (openBracket < 0)
            {
                return false;
            }

            var indexText = segment.Substring(openBracket + 1, closeBracket - openBracket - 1);
            if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            {
                return false;
            }

            name = segment.Substring(0, openBracket);
            return true;
        }
    }
}
