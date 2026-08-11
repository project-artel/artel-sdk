using System.Collections.Generic;
using Artel.Protocol.Dto;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Artel.Tests.Protocol
{
    public sealed class SceneMapTests
    {
        [Test]
        public void Parse_ReadsBackWhatTheExporterWrites()
        {
            var json = JsonConvert.SerializeObject(new SceneMapDocumentDto
            {
                FormatVersion = SceneMap.FormatVersion,
                Scenes = new List<ScannedSceneDto>
                {
                    new ScannedSceneDto
                    {
                        BuildIndex = 0,
                        Path = "Assets/Scenes/Lobby.unity",
                        Scene = new SceneDto { Id = 1, Type = "scene", Name = "Lobby" }
                    },
                    new ScannedSceneDto
                    {
                        BuildIndex = 1,
                        Path = "Assets/Scenes/Game.unity",
                        Scene = new SceneDto { Id = 2, Type = "scene", Name = "Game" }
                    }
                }
            });

            Assert.That(SceneMap.TryParse(json, out var scenes, out var error), Is.True, error);
            Assert.That(scenes, Has.Count.EqualTo(2));
            Assert.That(scenes[1].BuildIndex, Is.EqualTo(1));
            Assert.That(scenes[1].Path, Is.EqualTo("Assets/Scenes/Game.unity"));
            Assert.That(scenes[1].Scene.Name, Is.EqualTo("Game"));
        }

        [Test]
        public void Serialize_KeepsTheFileShapeSeparateFromTheMessage()
        {
            var root = JObject.Parse(JsonConvert.SerializeObject(new SceneMapDocumentDto
            {
                FormatVersion = SceneMap.FormatVersion,
                Scenes = new List<ScannedSceneDto>
                {
                    new ScannedSceneDto
                    {
                        BuildIndex = 0,
                        Path = "Assets/Scenes/Lobby.unity",
                        Scene = new SceneDto { Id = 1, Type = "scene", Name = "Lobby" }
                    }
                }
            }));

            Assert.That((int)root["formatVersion"], Is.EqualTo(SceneMap.FormatVersion));
            Assert.That(root["scenes"], Has.Count.EqualTo(1));

            // A file is not a session, so it carries none of the message envelope. The runtime
            // stamps type and id when it sends the scenes on.
            Assert.That(root["type"], Is.Null);
            Assert.That(root["id"], Is.Null);
        }

        [Test]
        public void Parse_RejectsAMapFromADifferentFormatVersion()
        {
            var json = JsonConvert.SerializeObject(new SceneMapDocumentDto
            {
                FormatVersion = SceneMap.FormatVersion + 1,
                Scenes = new List<ScannedSceneDto>()
            });

            Assert.That(SceneMap.TryParse(json, out var scenes, out var error), Is.False);
            Assert.That(scenes, Is.Null);

            // The whole point of the version is that a stale map reads back clean otherwise —
            // the codec ignores members it does not know — so the message has to name the cause.
            Assert.That(error, Does.Contain("format v" + (SceneMap.FormatVersion + 1)));
            Assert.That(error, Does.Contain("Export Scene Map"));
        }

        [Test]
        public void ScanMode_ReadsTheMapUnlessTheCallerAsksForTheLiveWalk()
        {
            Assert.That(ArtelManager.TryReadScanMode(null, out var none), Is.True);
            Assert.That(none, Is.False);

            Assert.That(ArtelManager.TryReadScanMode(new List<object>(), out var empty), Is.True);
            Assert.That(empty, Is.False);

            Assert.That(
                ArtelManager.TryReadScanMode(new List<object> { "live" }, out var live), Is.True);
            Assert.That(live, Is.True);
        }

        [Test]
        public void ScanMode_StillAcceptsTheDetailModesItNoLongerVariesOn()
        {
            // The map is baked once, so a reader cannot pick how much of it to get. Both spellings
            // keep working rather than starting to fail on callers written before the map existed.
            Assert.That(
                ArtelManager.TryReadScanMode(new List<object> { "default" }, out var standard),
                Is.True);
            Assert.That(standard, Is.False);

            Assert.That(
                ArtelManager.TryReadScanMode(new List<object> { "full" }, out var full), Is.True);
            Assert.That(full, Is.False);
        }

        [Test]
        public void ScanMode_RejectsUnknownAndOverlongParameters()
        {
            Assert.That(
                ArtelManager.TryReadScanMode(new List<object> { "sideways" }, out _), Is.False);
            Assert.That(ArtelManager.TryReadScanMode(new List<object> { 1 }, out _), Is.False);
            Assert.That(
                ArtelManager.TryReadScanMode(new List<object> { "live", "full" }, out _), Is.False);
        }

        [Test]
        public void Parse_RejectsEmptyAndMalformedText()
        {
            Assert.That(SceneMap.TryParse(null, out _, out var missing), Is.False);
            Assert.That(missing, Does.Contain("empty"));

            Assert.That(SceneMap.TryParse("   ", out _, out var blank), Is.False);
            Assert.That(blank, Does.Contain("empty"));

            Assert.That(SceneMap.TryParse("{\"scenes\":", out _, out var truncated), Is.False);
            Assert.That(truncated, Does.Contain("not valid JSON"));

            Assert.That(
                SceneMap.TryParse("{\"formatVersion\":1,\"scenes\":null}", out _, out var nulled),
                Is.False);
            Assert.That(nulled, Does.Contain("no scenes"));
        }
    }
}
