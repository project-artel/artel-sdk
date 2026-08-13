using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// Starts the scan without anything being placed in a scene.
    /// </summary>
    /// <remarks>
    /// Booting itself is what keeps the promise that installing the package is the whole
    /// integration. Asking a game team to drop a manager object into every scene is a change to
    /// their scenes, which is the thing this is not allowed to require.
    ///
    /// Every scene that loads is read and added to the report, so simply playing the game builds up
    /// an account of everywhere it has been. Reaching the screens nobody walked to is what
    /// <see cref="WalkAllScenes"/> is for.
    ///
    /// Compiled only under the discovery define, so a shipping build has none of this in it.
    /// </remarks>
    public static class AffordanceBootstrap
    {
        private const string FileName = "artel-affordances.json";

        /// <summary>Where the report is written.</summary>
        public static string ReportPath => Path.Combine(Application.persistentDataPath, FileName);

        /// <remarks>
        /// Nothing is subscribed to in a released game. The define this assembly compiles behind
        /// says the tooling may exist; this says it should actually do something, and the two are
        /// different questions — the first is a project's decision and stays set for as long as
        /// they are working on the game, the second is true only while someone is developing.
        ///
        /// Asked as an <c>#if</c> rather than a runtime test so a shipping player holds no
        /// subscription, no callback, and no reason to have loaded any of this. The same pair of
        /// symbols is read from the other side in
        /// <c>AffordanceILPostProcessor.IsDiscoveryBuild</c>, which decides whether the evidence
        /// this reads was ever baked. Change one and change the other: this one cannot share a
        /// constant with it, because a preprocessor test is evaluated where its own assembly is
        /// compiled and cannot read a value from anywhere.
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            SceneManager.sceneLoaded += (scene, mode) => Capture(scene);
            Debug.Log("[Artel] Discovery is on. The report is written to " + ReportPath);
#endif
        }

        private static void Capture(Scene scene)
        {
            // Read after the scene is up rather than from the editor's saved state. The stored
            // values are what a field held before anything ran, so text a component fills in during
            // Awake still reads as its placeholder.
            try
            {
                SceneEvidenceScan.Capture(scene);

                // Not written during a walk. The walk saves once at the end, and writing the file
                // on every one of a dozen scene loads is a dozen times the work for the same answer.
                if (!SceneWalk.InProgress)
                {
                    Save();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Artel] Reading " + scene.name + " failed: " + exception.Message);
            }
        }

        /// <summary>Reads every loaded scene now and writes the report.</summary>
        public static string CaptureNow()
        {
            SceneEvidenceScan.CaptureLoaded();
            return Save();
        }

        /// <summary>
        /// Visits every scene in the build settings and reads each one.
        /// </summary>
        /// <remarks>
        /// Discards the run in progress, so it is offered rather than done. Returns false when a
        /// walk is already going, since two of them would fight over which scene is loaded.
        /// </remarks>
        public static bool WalkAllScenes()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Artel] A walk needs play mode: scenes are loaded as the game loads them.");
                return false;
            }

            return SceneWalk.Begin();
        }

        /// <summary>
        /// Starts sending the live values of everything the evidence names.
        /// </summary>
        /// <remarks>
        /// The report says what has to be true; this says what is true now, which is what a
        /// specification needs before it can be run rather than read. Nothing has to be marked in the
        /// game for it — the analysis wrote down the member behind every condition and every effect
        /// while it was reading them, and that list is what gets watched.
        ///
        /// Offered rather than done. Reading a hundred fields ten times a second is a cost that
        /// belongs to whoever wants the channel, and most of the projects that install this package
        /// only ever want the report. Returns false when it is already going.
        ///
        /// Without a sink the readings go to a file beside the report, so the channel can be watched
        /// before anything is listening for it.
        /// </remarks>
        public static bool WatchLiveState(Live.IPulseSink sink = null)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Artel] Watching needs play mode: nothing holds a value until the game runs.");
                return false;
            }

            // A watch that ended without anyone stopping it — play mode left, the carrier destroyed,
            // nothing asked to close the file. With domain reload on, the statics go and this is
            // already null; with it off they survive and the handle would be held by a watch that
            // no longer exists. Closing it here costs nothing and is the difference between the
            // channel starting and the channel refusing.
            var stale = _ours;
            _ours = null;
            stale?.Dispose();

            var ours = sink == null ? Live.PulseFile.Open() : null;
            var destination = sink ?? ours;

            if (destination == null)
            {
                return false;
            }

            if (!Live.Pulse.Begin(destination))
            {
                (ours as System.IDisposable)?.Dispose();
                return false;
            }

            _ours = ours;

            Debug.Log("[Artel] Watching " + Live.WatchList.All().Count + " members named by the evidence" +
                      (sink == null ? ". Readings go to " + Live.PulseFile.Path : "."));
            return true;
        }

        /// <summary>Whether the live channel is running.</summary>
        /// <remarks>
        /// Said here because the beat itself is internal to this assembly and the editor menu that
        /// offers the channel lives in another one. A caller outside has no other way to ask, and
        /// one that cannot ask has to keep its own answer — which is the pair that drifts.
        /// </remarks>
        public static bool Watching => Live.Pulse.InProgress;

        /// <summary>
        /// What was opened here rather than handed in, so that only that is closed again.
        /// </summary>
        /// <remarks>
        /// A caller that brings its own sink keeps it — closing something this package did not open
        /// is a decision the caller never asked for. The default file is ours, and leaving it open
        /// after watching stops is what makes the next start fail: the file is still held, opening
        /// it again is a sharing violation, and the game runs with no channel at all rather than
        /// with one that complains. Measured in the editor by turning watching off and on again.
        /// </remarks>
        private static Live.PulseFile _ours;

        /// <summary>Stops sending live values.</summary>
        public static void StopWatching()
        {
            Live.Pulse.Stop();

            // After the beat is gone, so nothing is mid-send into a closed file.
            var ours = _ours;
            _ours = null;
            ours?.Dispose();
        }

        /// <summary>Throws away everything gathered so far.</summary>
        public static void Forget()
        {
            AffordanceReport.Forget();
        }

        /// <summary>Writes the report, and returns where it went or null if it could not be written.</summary>
        public static string Save()
        {
            try
            {
                File.WriteAllText(ReportPath, AffordanceReport.Compose());
                return ReportPath;
            }
            catch (Exception exception)
            {
                // Never take the game down over a report. Discovery is a side activity and a
                // read-only or full disk is not the game's problem.
                Debug.LogWarning("[Artel] Could not write " + ReportPath + ": " + exception.Message);
                return null;
            }
        }
    }
}
