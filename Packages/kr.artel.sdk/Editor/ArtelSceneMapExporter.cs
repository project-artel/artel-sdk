using System.Collections.Generic;
using System.IO;
using Artel.Protocol.Dto;
using Artel.Protocol.Mapping;
using Artel.Serialization;
using Artel.Tracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel.Editor
{
    /// <summary>
    /// Writes the scene map <c>scan_all_scenes</c> answers from, scanning every build scene from
    /// the Editor with the game stopped. Nothing in the scanned scenes runs, so this has none of
    /// the side effects the runtime walk has to clean up after.
    /// </summary>
    internal static class ArtelSceneMapExporter
    {
        [MenuItem("Artel/Export Scene Map…")]
        private static void Export()
        {
            // EditorSceneManager.OpenScene is rejected during play mode, which is the whole
            // reason this is a menu command rather than a branch inside scan_all_scenes.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "Artel",
                    "Stop play mode first. Send scan_all_scenes [\"live\"] to map scenes while the game runs.",
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

            var json = Scan(sceneCount);

            // Written through the filesystem rather than AssetDatabase, which has no API for
            // creating a text asset from a string, then imported by the refresh below.
            Directory.CreateDirectory(Path.GetDirectoryName(SceneMap.AssetPath));
            File.WriteAllText(SceneMap.AssetPath, json);
            AssetDatabase.Refresh();

            Debug.Log("[Artel] Scene map written to " + SceneMap.AssetPath + ".");
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

                        // Full, always. The map is baked once and whoever reads it cannot ask for
                        // more later, so it carries the wider of the two scans: every serialized
                        // field, inactive objects, and each button's wired-up onClick.
                        Scene = SceneSnapshotMapper.ToDto(scanner.Scan(SceneScanOptions.Full).Scene)
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

            return new NewtonsoftJsonCodec().Serialize(new SceneMapDocumentDto
            {
                FormatVersion = SceneMap.FormatVersion,
                Scenes = scenes
            });
        }
    }
}
