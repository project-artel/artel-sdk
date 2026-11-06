using System.Collections.Generic;
using System.Text.RegularExpressions;
using Artel.Affordances.Scan;
using Artel.Tests.Tracking;
using NUnit.Framework;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Artel.Tests
{
    /// <summary>
    /// 방문한 scene 이 <c>DontDestroyOnLoad</c> 로 내보낸 root 가 그 scene 의 이름과 <c>persistent: true</c> 로 적히는지
    /// 본다(ARTEL-1228).
    /// </summary>
    /// <remarks>
    /// edit mode 에서는 <c>DontDestroyOnLoad</c> 도, 저장하지 않은 scene 옆에 scene 을 하나 더 여는 것도 안 된다. 그래서
    /// 같은 scene 의 root 하나를 내보낸 것으로 넘긴다. 그 root 는 scene 의 것으로 한 번, 내보낸 것으로 한 번 적히고, 테스트는
    /// 뒤의 것만 본다. scan 이 받는 것은 root 목록뿐이라 결과는 같다. 근거를 굽지 않은 assembly 라 persistent listener 로
    /// 보고에 실린다.
    /// </remarks>
    public sealed class SceneEvidenceScanCarriedOffTests
    {
        private readonly List<GameObject> made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var subject in made)
            {
                if (subject != null)
                {
                    Object.DestroyImmediate(subject);
                }
            }

            made.Clear();
            AffordanceReport.Forget();
        }

        private GameObject Wired(string name)
        {
            var button = new GameObject(name).AddComponent<Button>();
            var listener = new GameObject(name + " Listener").AddComponent<TrackedFixtureBehaviour>();
            made.Add(button.gameObject);
            made.Add(listener.gameObject);
            UnityEventTools.AddPersistentListener(button.onClick, listener.Ping);
            return button.gameObject;
        }

        [Test]
        public void ARootTheSceneSentAwayIsWrittenUnderThatSceneAsPersistent()
        {
            var visited = SceneManager.GetActiveScene();
            var tutorial = Wired("Tutorial Button");

            SceneEvidenceScan.Capture(visited, new List<GameObject> { tutorial });
            var document = AffordanceReport.Compose();

            StringAssert.IsMatch(
                "\"path\":\"Tutorial Button\",\"selector\":\"[^\"]*\",\"scene\":\"" +
                Regex.Escape(visited.name) + "\",\"active\":true,\"persistent\":true",
                document,
                "내보낸 root 가 방문한 scene 이름과 persistent 로 적히지 않았다.");
        }

        [Test]
        public void ObjectsStillInTheSceneAreNotMarkedPersistent()
        {
            Wired("Plain Button");

            SceneEvidenceScan.Capture(SceneManager.GetActiveScene(), new List<GameObject>());
            var document = AffordanceReport.Compose();

            StringAssert.Contains("\"path\":\"Plain Button\"", document);
            StringAssert.DoesNotContain("\"persistent\":true", document);
        }
    }
}
