using System.Collections.Generic;
using Artel.Protocol.Dto;
using Artel.Serialization;
using Newtonsoft.Json;
using UnityEngine;

namespace Artel
{
    /// <summary>
    /// The build-time scene map: where it lives, and how to read it back.
    /// </summary>
    /// <remarks>
    /// The map is what <c>scan_all_scenes</c> answers from. It is produced in the Editor with the
    /// game stopped, so nothing in the mapped scenes ever runs — which is the whole point of it
    /// existing. Both the Editor that writes it and the runtime that reads it come through here,
    /// so the path and the format version have exactly one definition.
    /// </remarks>
    internal static class SceneMap
    {
        /// <summary>
        /// Bumped by hand whenever the shape of a scanned scene changes.
        /// </summary>
        /// <remarks>
        /// The build check compares this against what the map on disk carries. Without it a map
        /// exported before the change reads back clean — the codec ignores members it does not
        /// know — and the scan quietly reports less than it used to. File timestamps cannot catch
        /// that: upgrading the SDK does not touch anyone's scene assets.
        /// </remarks>
        public const int FormatVersion = 1;

        /// <summary>The name <see cref="Resources.Load(string)"/> takes: no folder, no extension.</summary>
        public const string ResourceName = "ArtelSceneMap";

        /// <summary>
        /// Where the exporter writes it. Under <c>Resources</c> because that is what puts a file
        /// into the build and within reach of <see cref="Resources.Load(string)"/>, and in the
        /// consuming project's <c>Assets</c> rather than in this package, which is read-only to
        /// the games that install it.
        /// </summary>
        public const string AssetPath = "Assets/Resources/" + ResourceName + ".json";

        private static readonly IJsonCodec Codec = new NewtonsoftJsonCodec();

        /// <param name="error">Why the map could not be read, phrased for whoever has to fix it.</param>
        public static bool TryLoad(out List<ScannedSceneDto> scenes, out string error)
        {
            var asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null)
            {
                scenes = null;
                error =
                    "No scene map in this build (" + AssetPath + "). " +
                    "Run Artel ▸ Export Scene Map… and rebuild, " +
                    "or send scan_all_scenes [\"live\"] to walk the scenes instead.";
                return false;
            }

            // Copied out and released rather than left in the Resources cache. The map covers
            // every scene at full detail, so it is the largest thing this SDK ever loads, and it
            // is read once per scan_all_scenes — not often enough to be worth holding.
            var json = asset.text;
            Resources.UnloadAsset(asset);

            return TryParse(json, out scenes, out error);
        }

        public static bool TryParse(string json, out List<ScannedSceneDto> scenes, out string error)
        {
            scenes = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "The scene map at " + AssetPath + " is empty.";
                return false;
            }

            SceneMapDocumentDto document;
            try
            {
                document = Codec.Deserialize<SceneMapDocumentDto>(json);
            }
            catch (JsonException exception)
            {
                error = "The scene map at " + AssetPath + " is not valid JSON: " + exception.Message;
                return false;
            }

            if (document == null)
            {
                error = "The scene map at " + AssetPath + " is empty.";
                return false;
            }

            // Version before contents: a map from a format that moved or renamed things would
            // otherwise be reported as merely empty, which sends the reader looking in the wrong
            // place.
            if (document.FormatVersion != FormatVersion)
            {
                error =
                    "The scene map at " + AssetPath + " is format v" + document.FormatVersion +
                    " and this SDK reads v" + FormatVersion +
                    ". Run Artel ▸ Export Scene Map… again.";
                return false;
            }

            if (document.Scenes == null)
            {
                error = "The scene map at " + AssetPath + " carries no scenes.";
                return false;
            }

            scenes = document.Scenes;
            error = null;
            return true;
        }
    }
}
