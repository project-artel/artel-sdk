using System.Collections.Generic;
using System.IO;
using Artel.Protocol.Dto;
using Artel.Protocol.Mapping;
using Artel.Serialization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel.Editor
{
    /// <summary>
    /// Builds the same scene map the <c>scan_all_scenes</c> action produces, but from the Editor
    /// with the game stopped. Nothing in the scanned scenes runs, so the walk has none of the
    /// side effects the runtime one carries.
    /// </summary>
    internal static class ArtelSceneMapExporter
    {
        [MenuItem("Artel/Export Scene Map…")]
        private static void Export()
        {
            // EditorSceneManager.OpenScene is rejected during play mode, which is the whole
            // reason this is a separate command rather than a branch inside scan_all_scenes.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "Artel",
                    "Stop play mode first. Use the scan_all_scenes action to map scenes while the game runs.",
                    "OK");
                return;
            }

            var sceneCount = SceneManager.sceneCountInBuildSettings;
            if (sceneCount == 0)
            {
                EditorUtility.DisplayDialog("Artel", "Build Settings lists no scenes.", "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var destination = EditorUtility.SaveFilePanel(
                "Export Artel scene map",
                "",
                "artel-scene-map.json",
                "json");
            if (string.IsNullOrEmpty(destination))
            {
                return;
            }

            var json = Scan(sceneCount);
            File.WriteAllText(destination, json);
            Debug.Log("[Artel] Scene map written to " + destination + ".");
        }

        private static string Scan(int sceneCount)
        {
            var scanner = new SceneScanner();
            var scenes = new List<ScannedSceneDto>(sceneCount);

            // Restores whatever the user had open, including multi-scene setups and unsaved
            // scene ordering, once the walk is done.
            var setup = EditorSceneManager.GetSceneManagerSetup();

            try
            {
                for (var buildIndex = 0; buildIndex < sceneCount; buildIndex++)
                {
                    var path = SceneUtility.GetScenePathByBuildIndex(buildIndex);
                    EditorUtility.DisplayProgressBar(
                        "Artel",
                        "Scanning " + path,
                        (float)buildIndex / sceneCount);

                    // Single, not additive: with nothing running there is no reason to keep the
                    // previous scene around, and one scene at a time keeps the active scene
                    // unambiguous for the scanner.
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    scenes.Add(new ScannedSceneDto
                    {
                        BuildIndex = buildIndex,
                        Path = path,
                        Scene = SceneSnapshotMapper.ToDto(scanner.Scan().Scene)
                    });
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (setup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                }
            }

            return new NewtonsoftJsonCodec().Serialize(new AllScenesMessageDto
            {
                Type = "ALL_SCENES",

                // The runtime id is a per-session message counter. An exported file is not part
                // of any session, so it carries none.
                Id = 0,
                Scenes = scenes
            });
        }
    }
}
