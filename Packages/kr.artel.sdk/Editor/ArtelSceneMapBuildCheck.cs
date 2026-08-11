using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Artel.Editor
{
    /// <summary>
    /// Fails the build when the scene map is missing or no longer matches the scenes being built.
    /// </summary>
    /// <remarks>
    /// It only checks. Producing the map means opening scenes, and whether a build callback may
    /// do that is not something this project has been able to confirm — so exporting stays a
    /// menu command a person runs, and this hook's job is to make sure they did.
    ///
    /// A build that ships the SDK without a map ships a <c>scan_all_scenes</c> that cannot
    /// answer. Failing here is louder than discovering it from a QA run.
    /// </remarks>
    // Public, not internal: Unity instantiates build callbacks by reflection and does not reach
    // non-public constructors. The assembly is not auto-referenced, so nothing else can see it.
    public sealed class ArtelSceneMapBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return 0; } }

        public void OnPreprocessBuild(BuildReport report)
        {
            var buildScenes = ReadBuildScenes();
            if (buildScenes.Count == 0)
            {
                // Nothing to map, and Unity has its own clearer complaint about a build with no
                // scenes. Standing in front of it with an Artel error would only confuse.
                return;
            }

            var mapExists = File.Exists(SceneMap.AssetPath);
            var mapJson = mapExists ? File.ReadAllText(SceneMap.AssetPath) : null;
            var mapWrittenUtc = mapExists
                ? File.GetLastWriteTimeUtc(SceneMap.AssetPath)
                : (DateTime?)null;

            if (ArtelSceneMapFreshness.IsUpToDate(mapJson, mapWrittenUtc, buildScenes, out var problem))
            {
                return;
            }

            throw new BuildFailedException("[Artel] " + problem);
        }

        /// <summary>
        /// The enabled entries of Build Settings, in order.
        /// </summary>
        /// <remarks>
        /// This has to come out in the same order the exporter walks, which reaches the same list
        /// through <c>SceneUtility.GetScenePathByBuildIndex</c>. Build indices are handed to the
        /// enabled entries in this order, so the two agree — disabled entries get no index and are
        /// not in the map either.
        /// </remarks>
        private static List<ArtelSceneMapFreshness.BuildScene> ReadBuildScenes()
        {
            var scenes = new List<ArtelSceneMapFreshness.BuildScene>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled)
                {
                    continue;
                }

                scenes.Add(new ArtelSceneMapFreshness.BuildScene(
                    scene.path, File.GetLastWriteTimeUtc(scene.path)));
            }

            return scenes;
        }
    }
}
