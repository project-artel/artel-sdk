using System;
using System.Collections;
using Artel.Tracking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Artel.Tests
{
    /// <summary>
    /// 연결이 끊기면 ArtelManager 는 scan_all_scenes 의 iterator 를 Dispose 한다. 그때 워크가 로드한
    /// 씬이 게임에 남지 않는지 본다. 테스트 project 의 Build Settings 0번 씬(UiToolkitSample)을 쓴다.
    /// </summary>
    public sealed class AllSceneScannerCancellationTests
    {
        private const int MaxWaitFrames = 300;

        private string scenePath;

        [SetUp]
        public void SetUp()
        {
            Assert.That(SceneManager.sceneCountInBuildSettings, Is.GreaterThan(0),
                "테스트 project 의 Build Settings 에 씬이 없습니다.");
            scenePath = SceneUtility.GetScenePathByBuildIndex(0);
            Assert.That(SceneManager.GetSceneByPath(scenePath).isLoaded, Is.False,
                "다른 테스트가 " + scenePath + " 를 언로드하지 않았습니다.");

            // 저장된 sample 씬의 StandaloneInputModule 은 Input System 만 켠 project 에서 매 frame
            // 예외를 낸다. 이 테스트는 입력을 쓰지 않으므로 Update 전에 지운다.
            SceneManager.sceneLoaded += RemoveLegacyInputModules;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= RemoveLegacyInputModules;

            var scene = SceneManager.GetSceneByPath(scenePath);
            if (scene.IsValid() && scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        [UnityTest]
        public IEnumerator Load_중에_취소되면_load가_끝난_뒤_씬을_언로드한다()
        {
            var completed = false;
            var walk = new AllSceneScanner(new SceneScanner())
                .ScanAll(SceneScanOptions.Default, _ => completed = true);

            Assert.That(walk.MoveNext(), Is.True);
            var load = walk.Current as AsyncOperation;
            Assert.That(load, Is.Not.Null, "첫 yield 는 LoadSceneAsync 여야 합니다.");
            Assert.That(load.isDone, Is.False);

            ((IDisposable)walk).Dispose();
            yield return load;
            yield return WaitUntilSceneIsGone();

            Assert.That(SceneManager.GetSceneByPath(scenePath).isLoaded, Is.False);
            Assert.That(completed, Is.False);
        }

        [UnityTest]
        public IEnumerator Settle_대기_중에_취소되면_씬을_언로드한다()
        {
            var completed = false;
            var walk = new AllSceneScanner(new SceneScanner())
                .ScanAll(SceneScanOptions.Default, _ => completed = true);

            Assert.That(walk.MoveNext(), Is.True);
            yield return walk.Current;
            Assert.That(walk.MoveNext(), Is.True);
            Assert.That(walk.Current, Is.InstanceOf<WaitForSecondsRealtime>());
            Assert.That(SceneManager.GetSceneByPath(scenePath).isLoaded, Is.True);

            ((IDisposable)walk).Dispose();
            yield return WaitUntilSceneIsGone();

            Assert.That(SceneManager.GetSceneByPath(scenePath).isLoaded, Is.False);
            Assert.That(completed, Is.False);
        }

        private IEnumerator WaitUntilSceneIsGone()
        {
            for (var frame = 0; frame < MaxWaitFrames && SceneManager.GetSceneByPath(scenePath).IsValid(); frame++)
            {
                yield return null;
            }
        }

        private static void RemoveLegacyInputModules(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects())
            foreach (var legacy in root.GetComponentsInChildren<StandaloneInputModule>(true))
            {
                Object.DestroyImmediate(legacy);
            }
        }
    }
}
