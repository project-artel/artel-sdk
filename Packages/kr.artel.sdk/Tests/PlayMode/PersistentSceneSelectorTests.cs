using System.Collections;
using Artel.Affordances.Scan;
using Artel.Pointer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Artel.Tests
{
    /// <summary>
    /// <c>DontDestroyOnLoad</c> 씬의 객체도 selector 로 풀리는지(ARTEL-1287). pulse 는 그 객체를 보고하는데 액션은
    /// "없다" 고 답하던 자리다.
    /// </summary>
    public sealed class PersistentSceneSelectorTests
    {
        private GameObject carrier;

        [TearDown]
        public void TearDown()
        {
            if (carrier != null)
            {
                Object.DestroyImmediate(carrier);
            }
        }

        [UnityTest]
        public IEnumerator DontDestroyOnLoad_씬의_객체가_그_씬의_root_번호로_풀린다()
        {
            carrier = new GameObject("Artel persistent probe");
            var child = new GameObject("Child").transform;
            child.SetParent(carrier.transform);
            Object.DontDestroyOnLoad(carrier);
            yield return null;

            var persistent = carrier.scene;
            Assert.That(persistent, Is.Not.EqualTo(SceneManager.GetActiveScene()), "DontDestroyOnLoad 씬으로 옮겨져야 한다.");

            var rootIndex = System.Array.IndexOf(persistent.GetRootGameObjects(), carrier);
            Assert.That(rootIndex, Is.GreaterThanOrEqualTo(0));
            var selector = ScenePath.SelectorOf(child, rootIndex);

            Assert.That(ScenePathSelectorResolver.TryResolve(selector, out var found, out var error), Is.True, error);
            Assert.That(found, Is.EqualTo(child));
        }

        [UnityTest]
        public IEnumerator 활성_씬에_같은_selector_가_있으면_활성_씬이_이긴다()
        {
            // 활성 씬의 root 0 과 같은 이름·번호를 DontDestroyOnLoad 에도 만든다. 전과 같은 답(활성 씬)이어야 한다.
            var active = SceneManager.GetActiveScene();
            var roots = active.GetRootGameObjects();
            Assume.That(roots.Length, Is.GreaterThan(0), "활성 씬에 root 가 있어야 비교할 수 있다.");
            var first = roots[0];

            carrier = new GameObject(first.name);
            Object.DontDestroyOnLoad(carrier);
            yield return null;

            var selector = ScenePath.SelectorOf(first.transform, 0);
            var persistentIndex = System.Array.IndexOf(carrier.scene.GetRootGameObjects(), carrier);

            Assert.That(ScenePathSelectorResolver.TryResolve(selector, out var found, out _), Is.True);
            Assert.That(found.gameObject, Is.EqualTo(first));
            Assume.That(persistentIndex == 0, Is.True, "번호까지 같을 때만 뜻이 있는 검사다.");
        }

        [Test]
        public void 어느_씬에도_없으면_전과_같은_문장으로_거절한다()
        {
            Assert.That(ScenePathSelectorResolver.TryResolve("Nope[99]/Child[0]", out _, out var error), Is.False);
            Assert.That(error, Is.EqualTo("Nothing at selector: Nope[99]/Child[0]"));
        }
    }
}
