using System;
using System.Collections.Generic;
using System.Globalization;
using Artel.Affordances.Scan;
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

        /// <summary>
        /// selector 를 지금 로드된 씬에서 객체로. 활성 씬 → <c>DontDestroyOnLoad</c> → 나머지 로드된 씬 순.
        /// </summary>
        /// <remarks>
        /// selector 의 root 번호는 그 객체가 사는 씬의 root 순서다. 활성 씬만 보면 <c>DontDestroyOnLoad</c> 에 사는 객체 —
        /// WordVenture 의 튜토리얼 오버레이가 그렇다 — 는 pulse 가 보고하는데 액션은 "없다" 고 답했다(ARTEL-1287).
        /// 같은 selector 가 두 씬에 있으면 활성 씬이 이긴다. 전과 같은 답이다.
        /// </remarks>
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

            foreach (var scene in Candidates())
            {
                if (TryResolveIn(scene, segments, out transform))
                {
                    error = null;
                    return true;
                }
            }

            error = "Nothing at selector: " + selector;
            return false;
        }

        private static IEnumerable<Scene> Candidates()
        {
            var active = SceneManager.GetActiveScene();
            yield return active;

            var persistent = PersistentScene();

            if (persistent.IsValid() && persistent != active)
            {
                yield return persistent;
            }

            for (var at = 0; at < SceneManager.sceneCount; at++)
            {
                var scene = SceneManager.GetSceneAt(at);

                if (scene.isLoaded && scene != active && scene != persistent)
                {
                    yield return scene;
                }
            }
        }

        private static Scene _persistent;

        /// <summary>한 번 받아 둔다. 씬 핸들은 프로세스가 사는 동안 그대로이고, 받는 길이 객체 하나를 만들어 옮기는 것이다.</summary>
        private static Scene PersistentScene()
        {
            if (!_persistent.IsValid())
            {
                _persistent = StraySpawnTracker.DontDestroyOnLoadScene();
            }

            return _persistent;
        }

        private static bool TryResolveIn(Scene scene, string[] segments, out Transform transform)
        {
            transform = null;

            if (!scene.IsValid() || !scene.isLoaded)
            {
                return false;
            }

            if (!TryParseSegment(segments[0], out var rootName, out var rootIndex))
            {
                return false;
            }

            var roots = scene.GetRootGameObjects();
            if (rootIndex < 0 || rootIndex >= roots.Length || roots[rootIndex].name != rootName)
            {
                return false;
            }

            var current = roots[rootIndex].transform;
            for (var i = 1; i < segments.Length; i++)
            {
                if (!TryParseSegment(segments[i], out var name, out var siblingIndex))
                {
                    return false;
                }

                if (siblingIndex < 0 || siblingIndex >= current.childCount)
                {
                    return false;
                }

                var child = current.GetChild(siblingIndex);

                // 스캔과 액션 사이에 계층이 바뀌었을 수 있다. 자리만 맞고 이름이 다르면 엉뚱한 것을 겨눈
                // 것이므로, 조용히 넘어가지 않고 여기서 거절한다.
                if (child.name != name)
                {
                    return false;
                }

                current = child;
            }

            transform = current;
            return true;
        }

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
