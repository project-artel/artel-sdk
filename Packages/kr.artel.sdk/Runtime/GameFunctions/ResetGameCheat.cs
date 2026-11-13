#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using Artel.Affordances.Scan;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel
{
    internal sealed class ResetGameCheat
    {
        private readonly GameFunctionActions actions;
        private readonly Func<string> releaseOsInput;
        private readonly Action restoreTimeScale;
        private readonly Action releaseVirtualInput;
        private readonly Action scan;
        private readonly int startupSceneBuildIndex;
        private readonly string startupScenePath;

        public ResetGameCheat(GameFunctionActions actions, Func<string> releaseOsInput, Action restoreTimeScale,
            Action releaseVirtualInput, Action scan)
        {
            var startupScene = SceneManager.GetActiveScene();
            startupSceneBuildIndex = startupScene.buildIndex;
            startupScenePath = startupScene.path;
            this.actions = actions;
            this.releaseOsInput = releaseOsInput;
            this.restoreTimeScale = restoreTimeScale;
            this.releaseVirtualInput = releaseVirtualInput;
            this.scan = scan;
        }

        public GameFunction Function => new GameFunction
        {
            Kind = GameFunctionKind.Cheat,
            Name = BuiltInCheats.ResetGame,
            Description = "Clears game preferences and static state, then reloads the startup scene.",
            Call = (_, __) => ReloadStartupScene(),
            Condition = _ => startupSceneBuildIndex < 0
                ? "the scene the game started in is not in Build Settings: " + startupScenePath
                : null
        };

        public GameFunctionCall Wrap(GameFunctionCall call) =>
            (target, args) => Run(call, target, args);

        private IEnumerator Run(GameFunctionCall call, object target, object[] args)
        {
            actions.EndRun(BuiltInCheats.ResetGame);
            var error = releaseOsInput();
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            restoreTimeScale();
            releaseVirtualInput();
            try
            {
                var result = call(target, args);
                if (result is IEnumerator routine)
                {
                    yield return routine;
                }
                else if (result is string message)
                {
                    yield return message;
                }

                yield return new WaitForSecondsRealtime(AllSceneScanner.SettleSeconds);
            }
            finally
            {
                scan();
            }
        }

        private IEnumerator ReloadStartupScene()
        {
            DoomPersistentObjects();
            yield return UnloadEveryScene();
            var outcome = GameStatics.ResetBeforeSceneLoad();
            Debug.Log($"[Artel] reset_game reset {outcome.Fields} static field(s) in {outcome.Types} game type(s)" +
                      (outcome.Failed.Count > 0 ? "; left as they were: " + string.Join("; ", outcome.Failed) : ""));

            ArtelOwnedPlayerPrefs.DeleteAllExceptOwn();
            yield return SceneManager.LoadSceneAsync(startupSceneBuildIndex, LoadSceneMode.Single);
            GameStatics.RunAfterSceneLoad();
        }

        /// <summary>
        /// 열린 씬을 모두 내린다. 빈 자리 씬 하나를 남겨 두고 내리며, 시작 씬을 <see cref="LoadSceneMode.Single"/> 로 열 때 그것도 내려간다.
        /// </summary>
        private static IEnumerator UnloadEveryScene()
        {
            var holding = SceneManager.CreateScene("Artel reset " + Time.frameCount);
            var opened = new List<Scene>();
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (scene != holding)
                {
                    opened.Add(scene);
                }
            }

            SceneManager.SetActiveScene(holding);
            foreach (var scene in opened)
            {
                var unloading = SceneManager.UnloadSceneAsync(scene);
                if (unloading != null)
                {
                    yield return unloading;
                }
            }
        }

        /// <summary>
        /// SDK 가 제 일을 하려고 띄운 영구 오브젝트(연결 · 라이브 판독 등)인지. 게임의 것이 아니므로 리셋이 지우지 않는다.
        /// </summary>
        /// <remarks>
        /// <see cref="ArtelManager"/> 만 살리면 라이브 판독을 보내는 <c>Artel Pulse</c> 가 첫 리셋에 함께 지워지고, 그 뒤의 런은 화면이
        /// 무엇인지 모르는 채로 좌표로만 조작하게 된다.
        /// </remarks>
        internal static bool IsSdkOwned(GameObject root)
        {
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var space = component == null ? null : component.GetType().Namespace;
                if (space == "Artel" || (space != null && space.StartsWith("Artel.", StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }

        private static void DoomPersistentObjects()
        {
            var doomed = SceneManager.GetActiveScene();
            var dropped = new List<string>();
            foreach (var root in StraySpawnTracker.DontDestroyOnLoadScene().GetRootGameObjects())
            {
                if (IsSdkOwned(root))
                {
                    continue;
                }

                SceneManager.MoveGameObjectToScene(root, doomed);
                dropped.Add(root.name);
            }

            if (dropped.Count > 0)
            {
                // Named, because a game whose bootstrap lives outside the start scene loses these
                // for good — the one way reset_game can leave it worse off than it found it.
                Debug.Log("[Artel] reset_game dropped persistent object(s): " +
                          string.Join(", ", dropped));
            }
        }

    }
}
#endif
