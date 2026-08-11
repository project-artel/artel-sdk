using System;
using System.Collections.Generic;

namespace Artel.Editor
{
    /// <summary>
    /// Decides whether the scene map on disk still describes the scenes about to be built.
    /// </summary>
    /// <remarks>
    /// Kept free of Unity APIs so the rule itself can be tested. Gathering the values — reading
    /// Build Settings, stat-ing files — is <see cref="ArtelSceneMapBuildCheck"/>'s job.
    /// </remarks>
    internal static class ArtelSceneMapFreshness
    {
        /// <summary>A scene the build will include, and when its asset last changed.</summary>
        internal readonly struct BuildScene
        {
            public BuildScene(string path, DateTime writeTimeUtc)
            {
                Path = path;
                WriteTimeUtc = writeTimeUtc;
            }

            public string Path { get; }
            public DateTime WriteTimeUtc { get; }
        }

        private const string Remedy = " Run Artel ▸ Export Scene Map… before building.";

        /// <param name="mapJson">The map's contents, or null when there is no map.</param>
        /// <param name="mapWriteTimeUtc">When the map was written; null when there is no map.</param>
        /// <param name="problem">What is wrong, phrased for whoever has to fix it.</param>
        public static bool IsUpToDate(
            string mapJson,
            DateTime? mapWriteTimeUtc,
            IReadOnlyList<BuildScene> buildScenes,
            out string problem)
        {
            if (mapWriteTimeUtc == null)
            {
                problem =
                    "This build has no scene map at " + SceneMap.AssetPath +
                    ", so scan_all_scenes would have nothing to answer with." + Remedy;
                return false;
            }

            if (!SceneMap.TryParse(mapJson, out var mapped, out var parseError))
            {
                problem = parseError + Remedy;
                return false;
            }

            if (mapped.Count != buildScenes.Count)
            {
                problem =
                    "The scene map covers " + mapped.Count + " scene(s) but Build Settings lists " +
                    buildScenes.Count + "." + Remedy;
                return false;
            }

            for (var i = 0; i < buildScenes.Count; i++)
            {
                // Build index is part of what the map reports, so a reordering matters as much as
                // a substitution: the same scenes in a different order is a different map.
                if (mapped[i].Path != buildScenes[i].Path)
                {
                    problem =
                        "The scene map has " + mapped[i].Path + " at build index " + i +
                        " but Build Settings has " + buildScenes[i].Path + "." + Remedy;
                    return false;
                }

                if (buildScenes[i].WriteTimeUtc > mapWriteTimeUtc.Value)
                {
                    problem =
                        "The scene map is older than " + buildScenes[i].Path +
                        ", so it no longer describes it." + Remedy;
                    return false;
                }
            }

            problem = null;
            return true;
        }
    }
}
