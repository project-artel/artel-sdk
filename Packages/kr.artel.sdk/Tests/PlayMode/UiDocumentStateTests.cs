using System.Collections;
using Artel.Affordances.Live;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Artel.Tests
{
    /// <summary><c>UIDocument</c> 의 트리가 객체 항목의 <c>uitk</c> 로 요약되는지(ARTEL-1284). 테스트 프로젝트의 UiToolkitSample 씬을 쓴다.</summary>
    public sealed class UiDocumentStateTests
    {
        private const string SceneName = "UiToolkitSample";
        private static int teardowns;
        private Scene sample;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);
            sample = SceneManager.GetSceneByName(SceneName);
            SceneManager.SetActiveScene(sample);
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UiDocumentState.Forget();
            SceneManager.SetActiveScene(SceneManager.CreateScene("after uitk state " + ++teardowns));
            yield return SceneManager.UnloadSceneAsync(sample);
        }

        [UnityTest]
        public IEnumerator 문서의_이름_있는_element_와_글자를_uitk_로_싣고_그_객체는_순회에_든다()
        {
            var document = Object.FindObjectOfType<UIDocument>();
            Assert.That(document, Is.Not.Null, "the scene has no UIDocument");
            yield return null;

            var fragment = UiDocumentState.Of(document.gameObject);

            Assert.That(fragment, Does.StartWith(",\"uitk\":["));
            Assert.That(fragment, Does.Contain("\"name\":\"play-button\""));
            Assert.That(fragment, Does.Contain("\"name\":\"status-label\""));
            Assert.That(fragment, Does.Contain("\"type\":\"Button\""));
            Assert.That(fragment, Does.Contain("\"enabled\":true"));
            Assert.That(UiDocumentState.CanCarry(document.gameObject), Is.True);

            var bare = new GameObject("bare");
            Assert.That(UiDocumentState.Of(bare), Is.Null);
            Object.DestroyImmediate(bare);
        }
    }
}
