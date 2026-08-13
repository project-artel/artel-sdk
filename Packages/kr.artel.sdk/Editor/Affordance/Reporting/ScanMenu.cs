using System;
using System.IO;
using Artel.Affordances.Scan;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Artel.Affordances.Editor
{
    /// <summary>
    /// Asks for a scan by hand.
    /// </summary>
    /// <remarks>
    /// Scenes are read as they load, so the report fills in by itself while the game is played. Two
    /// things still need asking for: reading the screen again after the game has been driven
    /// somewhere the loader could not reach on its own, and going to every scene in the build
    /// without playing through to each one.
    ///
    /// Play mode is where these are worth using. Outside it the hierarchy shows what was saved
    /// rather than what runs, so a label reads as its unreplaced placeholder.
    /// </remarks>
    internal static class ScanMenu
    {
        [MenuItem("Artel/Scan Loaded Scenes", false, 0)]
        private static void Capture()
        {
            var path = AffordanceBootstrap.CaptureNow();

            if (path == null)
            {
                Debug.LogWarning("[Artel] The report could not be written. See the warning above.");
                return;
            }

            Warn();
            Debug.Log("[Artel] " + AffordanceReport.SceneCount + " scenes in the report: " + path);
        }

        [MenuItem("Artel/Walk All Build Scenes", false, 1)]
        private static void Walk()
        {
            if (!AffordanceBootstrap.WalkAllScenes())
            {
                return;
            }

            Debug.Log("[Artel] Walking every scene in Build Settings. " +
                      "The game in progress is discarded and the starting scene is restored at the end.");
        }

        /// <summary>
        /// Reads every scene file in the project, whether or not the build knows about it.
        /// </summary>
        /// <remarks>
        /// A project that loads its scenes by address has no reason to register them in Build
        /// Settings, and the more recently a project was started the more likely that is. Chop Chop
        /// has one scene registered and fifty on disk; walking the build settings there described
        /// one screen and called it the game.
        ///
        /// Outside play mode, so this reads what was saved rather than what runs — a label shows the
        /// placeholder that was authored, not the text the game puts in it during <c>Start</c>. That
        /// is the price of reaching every screen without playing to each one, and it is said in the
        /// warning rather than left to be discovered.
        /// </remarks>
        [MenuItem("Artel/Read Every Scene In The Project", false, 2)]
        private static void ReadAll()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[Artel] Not during play — this opens scenes in the editor, " +
                                 "which would end the run. Use Artel / Walk All Build Scenes instead.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var opened = EditorSceneManager.GetActiveScene().path;
            var read = 0;

            AffordanceBootstrap.Forget();

            foreach (var guid in AssetDatabase.FindAssets("t:Scene"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                // Only what the project itself holds. A scene inside an imported package is the
                // package author's, not something this game's player can reach.
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    SceneEvidenceScan.CaptureLoaded();
                    read++;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Artel] " + path + " would not open: " + exception.Message);
                }
            }

            if (!string.IsNullOrEmpty(opened))
            {
                EditorSceneManager.OpenScene(opened, OpenSceneMode.Single);
            }

            Warn();
            Debug.Log("[Artel] " + read + " scenes read: " + AffordanceBootstrap.Save());
        }

        /// <summary>
        /// Starts and stops the live channel from the editor.
        /// </summary>
        /// <remarks>
        /// Watching is deliberately not automatic — reading a game's values ten times a second is a
        /// cost that belongs to whoever asked for the channel, and most projects only ever want the
        /// report. That leaves nothing in the editor able to start it, so anybody trying the channel
        /// by hand had to write a script to call one method. This is that method with a menu on it.
        ///
        /// Play mode only, and said rather than silently ignored: nothing holds a value until the
        /// game runs, and a channel started against a stopped editor would report a scene's saved
        /// state as though it were what a tester is looking at.
        /// </remarks>
        [MenuItem("Artel/Watch Live State", false, 10)]
        private static void Watch()
        {
            if (AffordanceBootstrap.Watching)
            {
                AffordanceBootstrap.StopWatching();
                Debug.Log("[Artel] Stopped watching.");
                return;
            }

            if (!AffordanceBootstrap.WatchLiveState())
            {
                return;
            }

            Debug.Log("[Artel] Watching. Readings go to " + Artel.Affordances.Live.PulseFile.Path);
        }

        [MenuItem("Artel/Watch Live State", true)]
        private static bool CanWatch()
        {
            Menu.SetChecked("Artel/Watch Live State", AffordanceBootstrap.Watching);
            return Application.isPlaying;
        }

        [MenuItem("Artel/Reveal Readings", false, 22)]
        private static void RevealReadings()
        {
            var path = Artel.Affordances.Live.PulseFile.Path;

            if (!File.Exists(path))
            {
                Debug.LogWarning("[Artel] No readings yet. Enter play mode and run Artel / Watch Live State.");
                return;
            }

            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Artel/Forget Everything Scanned", false, 20)]
        private static void Forget()
        {
            AffordanceBootstrap.Forget();
            Debug.Log("[Artel] The report is empty again. It fills back up as scenes load.");
        }

        [MenuItem("Artel/Reveal Report", false, 21)]
        private static void Reveal()
        {
            var path = AffordanceBootstrap.ReportPath;

            if (!File.Exists(path))
            {
                Debug.LogWarning("[Artel] Nothing written yet. Enter play mode, or run Artel / Scan Loaded Scenes.");
                return;
            }

            EditorUtility.RevealInFinder(path);
        }

        private static void Warn()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning(
                    "[Artel] Scanned outside play mode. Fields read as their saved values, " +
                    "not as what the game sets during Awake and Start.");
            }
        }
    }
}
