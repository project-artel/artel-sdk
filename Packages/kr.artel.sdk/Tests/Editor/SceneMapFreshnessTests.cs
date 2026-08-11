using System;
using System.Collections.Generic;
using Artel.Editor;
using Artel.Protocol.Dto;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Artel.Tests.EditorTools
{
    public sealed class SceneMapFreshnessTests
    {
        private static readonly DateTime Exported = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void UpToDate_WhenTheMapCoversTheSameScenesAndOutlivesThem()
        {
            var upToDate = ArtelSceneMapFreshness.IsUpToDate(
                MapOf("Assets/Scenes/Lobby.unity", "Assets/Scenes/Game.unity"),
                Exported,
                ScenesOf(
                    (path: "Assets/Scenes/Lobby.unity", written: Exported.AddHours(-1)),
                    (path: "Assets/Scenes/Game.unity", written: Exported.AddHours(-2))),
                out var problem);

            Assert.That(upToDate, Is.True, problem);
        }

        [Test]
        public void UpToDate_WhenASceneWasSavedInTheSameMomentAsTheExport()
        {
            // The export opens and reads each scene, so a scene stamped exactly at the map's time
            // is one the export saw. Only a strictly later stamp means it changed afterwards.
            var upToDate = ArtelSceneMapFreshness.IsUpToDate(
                MapOf("Assets/Scenes/Lobby.unity"),
                Exported,
                ScenesOf((path: "Assets/Scenes/Lobby.unity", written: Exported)),
                out var problem);

            Assert.That(upToDate, Is.True, problem);
        }

        [Test]
        public void Stale_WhenThereIsNoMapAtAll()
        {
            var upToDate = ArtelSceneMapFreshness.IsUpToDate(
                null,
                null,
                ScenesOf((path: "Assets/Scenes/Lobby.unity", written: Exported)),
                out var problem);

            Assert.That(upToDate, Is.False);
            Assert.That(problem, Does.Contain(SceneMap.AssetPath));
            Assert.That(problem, Does.Contain("Export Scene Map"));
        }

        [Test]
        public void Stale_WhenTheMapWasWrittenByADifferentFormatVersion()
        {
            var json = JsonConvert.SerializeObject(new SceneMapDocumentDto
            {
                FormatVersion = SceneMap.FormatVersion + 1,
                Scenes = new List<ScannedSceneDto>()
            });

            var upToDate = ArtelSceneMapFreshness.IsUpToDate(
                json,
                Exported,
                ScenesOf((path: "Assets/Scenes/Lobby.unity", written: Exported.AddHours(-1))),
                out var problem);

            // Upgrading the SDK touches nobody's scene assets, so timestamps alone would let a
            // map written against an older scan shape through.
            Assert.That(upToDate, Is.False);
            Assert.That(problem, Does.Contain("format v" + (SceneMap.FormatVersion + 1)));
        }

        [Test]
        public void Stale_WhenBuildSettingsGainedAScene()
        {
            var upToDate = ArtelSceneMapFreshness.IsUpToDate(
                MapOf("Assets/Scenes/Lobby.unity"),
                Exported,
                ScenesOf(
                    (path: "Assets/Scenes/Lobby.unity", written: Exported.AddHours(-1)),
                    (path: "Assets/Scenes/Game.unity", written: Exported.AddHours(-1))),
                out var problem);

            Assert.That(upToDate, Is.False);
            Assert.That(problem, Does.Contain("covers 1 scene(s)"));
            Assert.That(problem, Does.Contain("lists 2"));
        }

        [Test]
        public void Stale_WhenTheSameScenesWereReordered()
        {
            // Build index is part of what the map reports, so the same scenes in another order
            // describe a different build.
            var upToDate = ArtelSceneMapFreshness.IsUpToDate(
                MapOf("Assets/Scenes/Lobby.unity", "Assets/Scenes/Game.unity"),
                Exported,
                ScenesOf(
                    (path: "Assets/Scenes/Game.unity", written: Exported.AddHours(-1)),
                    (path: "Assets/Scenes/Lobby.unity", written: Exported.AddHours(-1))),
                out var problem);

            Assert.That(upToDate, Is.False);
            Assert.That(problem, Does.Contain("build index 0"));
            Assert.That(problem, Does.Contain("Assets/Scenes/Game.unity"));
        }

        [Test]
        public void Stale_WhenASceneChangedAfterTheExport()
        {
            var upToDate = ArtelSceneMapFreshness.IsUpToDate(
                MapOf("Assets/Scenes/Lobby.unity", "Assets/Scenes/Game.unity"),
                Exported,
                ScenesOf(
                    (path: "Assets/Scenes/Lobby.unity", written: Exported.AddHours(-1)),
                    (path: "Assets/Scenes/Game.unity", written: Exported.AddSeconds(1))),
                out var problem);

            Assert.That(upToDate, Is.False);
            Assert.That(problem, Does.Contain("Assets/Scenes/Game.unity"));
        }

        private static string MapOf(params string[] paths)
        {
            var scenes = new List<ScannedSceneDto>();
            for (var i = 0; i < paths.Length; i++)
            {
                scenes.Add(new ScannedSceneDto
                {
                    BuildIndex = i,
                    Path = paths[i],
                    Scene = new SceneDto { Id = i, Type = "scene", Name = "scene " + i }
                });
            }

            return JsonConvert.SerializeObject(new SceneMapDocumentDto
            {
                FormatVersion = SceneMap.FormatVersion,
                Scenes = scenes
            });
        }

        private static List<ArtelSceneMapFreshness.BuildScene> ScenesOf(
            params (string path, DateTime written)[] scenes)
        {
            var built = new List<ArtelSceneMapFreshness.BuildScene>();
            foreach (var scene in scenes)
            {
                built.Add(new ArtelSceneMapFreshness.BuildScene(scene.path, scene.written));
            }

            return built;
        }
    }
}
