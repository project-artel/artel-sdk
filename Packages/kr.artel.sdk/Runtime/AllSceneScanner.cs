using System;
using System.Collections;
using System.Collections.Generic;
using Artel.Protocol.Dto;
using Artel.Protocol.Mapping;
using UnityEngine.SceneManagement;

namespace Artel
{
    /// <summary>
    /// Walks every scene listed in Build Settings and scans each one.
    /// </summary>
    internal sealed class AllSceneScanner
    {
        private readonly SceneScanner scanner;

        public AllSceneScanner(SceneScanner scanner)
        {
            this.scanner = scanner;
        }

        public IEnumerator ScanAll(Action<List<ScannedSceneDto>> completed)
        {
            var scanned = new List<ScannedSceneDto>();
            var originalScene = SceneManager.GetActiveScene();

            for (var buildIndex = 0; buildIndex < SceneManager.sceneCountInBuildSettings; buildIndex++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(buildIndex);
                var scene = SceneManager.GetSceneByPath(path);

                // Scenes are loaded additively rather than singly: a single load destroys the
                // running scene, and this SDK lives in it — the coroutine driving this walk would
                // die halfway through. It also means a scene the game already has open is scanned
                // in place instead of being duplicated and torn back down.
                var wasAlreadyLoaded = scene.IsValid() && scene.isLoaded;
                if (!wasAlreadyLoaded)
                {
                    yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
                    scene = SceneManager.GetSceneByPath(path);

                    // Awake and OnEnable have run by now, but Start has not. Anything a scene
                    // fills in there — most UI text — is missing without this frame.
                    yield return null;
                }

                SceneManager.SetActiveScene(scene);
                scanned.Add(new ScannedSceneDto
                {
                    BuildIndex = buildIndex,
                    Path = path,

                    // The scan result's pending actions are deliberately left uncommitted. These
                    // scenes are being visited, not played, and dropping their recorded actions
                    // here would hide them from the next GAME_STATE.
                    Scene = SceneSnapshotMapper.ToDto(scanner.Scan().Scene)
                });

                if (!wasAlreadyLoaded)
                {
                    yield return SceneManager.UnloadSceneAsync(scene);
                }
            }

            SceneManager.SetActiveScene(originalScene);

            // Target ids come from whichever scene was scanned last, and that scene is now
            // unloaded. Rescan so button_click and enter_text address live objects again.
            scanner.Scan();
            completed(scanned);
        }
    }
}
