using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// Visits every scene in the build so the report covers the game rather than the screen.
    /// </summary>
    /// <remarks>
    /// Playing through to reach a screen is the honest way to describe it, and it is not something
    /// that can be asked for every scene of every game. This drives the loading itself: each scene
    /// in the build settings is brought up in turn, given a moment for <c>Awake</c> and
    /// <c>Start</c> to run, read, and left behind.
    ///
    /// Only ever started deliberately. It replaces whatever is on screen and discards the run in
    /// progress, so it is a thing to ask for, not a thing to have happen. Loaded singly rather than
    /// added alongside: a walk that stalls part way through leaves nothing mounted on top of the
    /// game, which is how the previous version of this failed.
    /// </remarks>
    internal sealed class SceneWalk : MonoBehaviour
    {
        /// <summary>Frames given to a scene before it is read.</summary>
        /// <remarks>
        /// One for the load to complete and one for the first <c>Update</c>, by which point
        /// <c>Awake</c> and <c>Start</c> have run and a label shows the text the game put in it
        /// rather than the placeholder that was saved with the scene.
        /// </remarks>
        private const int SettleFrames = 2;

        /// <summary>Seconds one scene may take to come up before the walk moves on.</summary>
        private const float PatiencePerScene = 30f;

        private static SceneWalk _walking;

        internal static bool InProgress => _walking != null;

        internal static bool Begin()
        {
            if (_walking != null)
            {
                return false;
            }

            var carrier = new GameObject("Artel Scene Walk") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(carrier);

            _walking = carrier.AddComponent<SceneWalk>();
            _walking.StartCoroutine(_walking.Visit());
            return true;
        }

        private IEnumerator Visit()
        {
            var count = SceneManager.sceneCountInBuildSettings;
            var addressed = Addressed();

            if (count == 0 && addressed.Count == 0)
            {
                // Two different facts that used to read the same. A project with an empty Build
                // Settings and no way to enumerate its addresses is not a project without scenes.
                Debug.LogWarning(ExtraScenes.Available
                    ? "[Artel] No scenes in Build Settings and none reachable by address."
                    : "[Artel] No scenes in Build Settings. If this game loads its scenes by " +
                      "address, install Addressables support and walk again.");

                AffordanceReport.Merge("(walk)", string.Empty, new List<string>
                {
                    ExtraScenes.Available ? "no-scenes-anywhere" : "no-scenes-in-build-settings"
                });

                Finish();
                yield break;
            }

            // Where to put the game back. Taken by build index because a scene can share its name
            // with another in a different folder.
            var origin = SceneManager.GetActiveScene().buildIndex;

            for (var index = 0; index < count; index++)
            {
                Debug.Log("[Artel] Walking scene " + (index + 1) + " of " + count + ".");
                yield return Read(index);
            }

            for (var index = 0; index < addressed.Count; index++)
            {
                Debug.Log("[Artel] Walking addressed scene " + (index + 1) + " of " + addressed.Count + ".");
                yield return Read(addressed[index]);
            }

            if (origin >= 0)
            {
                yield return Load(origin);
            }

            // Read last, and once. What the game kept across scene loads is whatever it had
            // accumulated by the end of the walk, and this carrier lives in that same scene — which
            // is the only handle on it a package that installs itself has.
            SceneEvidenceScan.CapturePersistent(gameObject.scene);

            Debug.Log("[Artel] Walk finished. " + AffordanceReport.SceneCount + " scenes in the report: " +
                      AffordanceBootstrap.Save());

            Finish();
        }

        /// <summary>The addresses of scenes that are not in the build settings, or none.</summary>
        private static List<string> Addressed()
        {
            if (!ExtraScenes.Available)
            {
                return new List<string>();
            }

            try
            {
                return ExtraScenes.List() ?? new List<string>();
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[Artel] The address catalogue could not be read: " + exception.Message);
                return new List<string>();
            }
        }

        /// <summary>
        /// Brings up one addressed scene alone and reads it.
        /// </summary>
        /// <remarks>
        /// Alone is not how the game plays it. Scenes loaded by address are usually meant to be
        /// added on top of a manager scene that is already up, and one raised by itself may come up
        /// half-built or empty. So the reading is kept and marked rather than thrown away — a screen
        /// described from an incomplete load is still the only account of that screen there is, as
        /// long as nobody reads it as the whole one.
        /// </remarks>
        private IEnumerator Read(string address)
        {
            IEnumerator loading;

            try
            {
                loading = ExtraScenes.Load(address);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[Artel] " + address + " would not load: " + exception.Message);
                yield break;
            }

            if (loading != null)
            {
                yield return loading;
            }

            for (var frame = 0; frame < SettleFrames; frame++)
            {
                yield return null;
            }

            SceneEvidenceScan.CaptureLoaded();
            AffordanceReport.Note(SceneManager.GetActiveScene().name, "scene-loaded-alone");
        }

        private IEnumerator Read(int buildIndex)
        {
            yield return Load(buildIndex);

            var scene = SceneManager.GetActiveScene();

            if (scene.buildIndex != buildIndex)
            {
                // The game refused to stay where it was put — a title screen that sends itself
                // onward, or a scene that failed to come up. Said rather than recorded under the
                // name of whatever it landed on.
                AffordanceReport.Merge("build:" + buildIndex, string.Empty,
                    new System.Collections.Generic.List<string> { "scene-would-not-stay" });
                yield break;
            }

            SceneEvidenceScan.Capture(scene);
        }

        private IEnumerator Load(int buildIndex)
        {
            AsyncOperation loading;

            try
            {
                loading = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[Artel] Scene " + buildIndex + " would not load: " + exception.Message);
                yield break;
            }

            if (loading == null)
            {
                yield break;
            }

            var waited = 0f;

            while (!loading.isDone)
            {
                waited += Time.unscaledDeltaTime;

                if (waited > PatiencePerScene)
                {
                    // Bounded because a scene that never finishes would otherwise hold the walk for
                    // as long as the editor is open, and the game is already unusable by then.
                    Debug.LogWarning("[Artel] Scene " + buildIndex + " did not finish loading in " +
                                     PatiencePerScene + "s. Moving on.");
                    yield break;
                }

                yield return null;
            }

            for (var frame = 0; frame < SettleFrames; frame++)
            {
                yield return null;
            }
        }

        private void Finish()
        {
            _walking = null;
            Destroy(gameObject);
        }
    }
}
