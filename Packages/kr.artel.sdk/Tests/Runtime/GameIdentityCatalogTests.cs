using System;
using System.Collections.Generic;
using System.Linq;
using Artel.Protocol.Dto;
using Artel.Serialization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Artel.Tests
{
    public sealed class GameIdentityCatalogTests
    {
        private class IdentityParent
        {
            [ArtelMetadata(Kind = "ordinal", Base = 0, Identifies = "stage", Role = "current",
                Meaning = "현재 stage", ChangesWhen = "stage 진입 시")]
            public int Stage;

            [ArtelMetadata(Kind = "id", Identifies = "enemy wave", Role = "other",
                Meaning = "선택한 wave", ChangesWhen = "wave 선택 시")]
            private string Wave => throw new InvalidOperationException("getter must not run");
        }

        private sealed class IdentityChild : IdentityParent { }

        private sealed class MetadataNames
        {
            [ArtelMetadata(Kind = "ordinal", Base = 0, Identifies = "stage", Role = "current",
                Meaning = "현재 stage", ChangesWhen = "stage 진입 시")]
            public int Current;

#pragma warning disable CS0618
            [ArtelGameIdentity(Kind = "ordinal", Base = 0, Identifies = "stage", Role = "current",
                Meaning = "현재 stage", ChangesWhen = "stage 진입 시")]
            private int Legacy => throw new InvalidOperationException("getter must not run");
#pragma warning restore CS0618
        }

        [Test]
        public void ArtelMetadata와_기존_attribute가_같은_필드를_전송한다()
        {
            Assert.That(GameIdentityCatalog.TryCollect(new[] { typeof(MetadataNames) },
                out var catalog, out var error), Is.True, error);
            var wire = JObject.Parse(new NewtonsoftJsonCodec().Serialize(catalog));
            Assert.That((string)wire["type"], Is.EqualTo("GAME_IDENTITY_CATALOG"));
            var entries = (JArray)wire["identities"];
            Assert.That(entries.Count, Is.EqualTo(2));
            Assert.That((string)entries[0]["name"], Is.EqualTo("MetadataNames.Current"));
            Assert.That((string)entries[1]["name"], Is.EqualTo("MetadataNames.Legacy"));
            var current = (JObject)entries[0];
            var legacy = (JObject)entries[1];
            current.Remove("name");
            legacy.Remove("name");
            Assert.That(JToken.DeepEquals(current, legacy), Is.True);
            Assert.That((int)legacy["base"], Is.Zero);
        }

        [Test]
        public void Field와_property의_선언을_값을_읽지_않고_수집한다()
        {
            Assert.That(GameIdentityCatalog.TryCollect(new[] { typeof(IdentityChild), typeof(IdentityParent) },
                out var catalog, out var error), Is.True, error);
            Assert.That(catalog.Identities.Count, Is.EqualTo(2));
            Assert.That(catalog.Identities[0].Name, Is.EqualTo("IdentityParent.Stage"));
            Assert.That(catalog.Identities[0].Base, Is.EqualTo(0));
            Assert.That(catalog.Identities[1].Name, Is.EqualTo("IdentityParent.Wave"));
            Assert.That(catalog.Identities[1].Base, Is.Null);
        }

        [Test]
        public void 같은_Type_member가_두_번_나오면_전체_수집을_거절한다()
        {
            Assert.That(GameIdentityCatalog.TryCollect(new[] { typeof(IdentityParent), typeof(IdentityParent) },
                out var catalog, out var error), Is.False);
            Assert.That(catalog, Is.Null);
            Assert.That(error, Does.Contain("Duplicate variable metadata name"));
        }

        [Test]
        public void Type_수집이_중간에_실패하면_부분_목록을_반환하지_않는다()
        {
            Assert.That(GameIdentityCatalog.TryCollect(IncompleteTypes(), out var catalog, out var error), Is.False);
            Assert.That(catalog, Is.Null);
            Assert.That(error, Is.EqualTo("incomplete scan"));
        }

        private static IEnumerable<Type> IncompleteTypes()
        {
            yield return typeof(IdentityParent);
            throw new TypeLoadException("incomplete scan");
        }

        [Test]
        public void 선언이_없으면_빈_catalog를_반환한다()
        {
            Assert.That(GameIdentityCatalog.TryCollect(new[] { typeof(IdentityChild) }, out var catalog, out _), Is.True);
            var wire = JObject.Parse(new NewtonsoftJsonCodec().Serialize(catalog));
            Assert.That((string)wire["type"], Is.EqualTo("GAME_IDENTITY_CATALOG"));
            Assert.That(((JArray)wire["identities"]).Count, Is.Zero);
            Assert.That(wire.Property("gameBuildId"), Is.Null);
        }

        [Test]
        public void Wire에_선언_필드와_null_base를_보낸다()
        {
            GameIdentityCatalog.TryCollect(new[] { typeof(IdentityParent) }, out var catalog, out _);
            var wire = JObject.Parse(new NewtonsoftJsonCodec().Serialize(catalog));
            var entry = (JObject)wire["identities"][1];
            Assert.That(entry.Properties().Count(), Is.EqualTo(7));
            Assert.That(entry["base"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That((string)entry["kind"], Is.EqualTo("id"));
            Assert.That((string)entry["meaning"], Is.EqualTo("선택한 wave"));
            Assert.That((string)entry["changesWhen"], Is.EqualTo("wave 선택 시"));
            Assert.That((string)entry["identifies"], Is.EqualTo("enemy wave"));
            Assert.That((string)entry["role"], Is.EqualTo("other"));
        }

        [TestCase("kind")]
        [TestCase("base")]
        [TestCase("name")]
        [TestCase("meaning")]
        [TestCase("changesWhen")]
        [TestCase("identifies")]
        [TestCase("role")]
        public void 서버_규칙을_벗어난_필드를_거절한다(string field)
        {
            var entry = ValidEntry();
            switch (field)
            {
                case "kind": entry.Kind = "index"; break;
                case "base": entry.Base = -1; break;
                case "name": entry.Name = new string('a', 201) + ".Stage"; break;
                case "meaning": entry.Meaning = " "; break;
                case "changesWhen": entry.ChangesWhen = new string('a', 501); break;
                case "identifies": entry.Identifies = "Stage"; break;
                case "role": entry.Role = "latest"; break;
            }
            Assert.Throws<InvalidOperationException>(() => GameIdentityCatalog.Validate(entry));
        }

        [TestCase("ordinal")]
        [TestCase("enum")]
        [TestCase("id")]
        [TestCase("flag")]
        [TestCase("text")]
        public void 서버가_지원하는_kind를_허용한다(string kind)
        {
            var entry = ValidEntry();
            entry.Kind = kind;
            entry.Base = null;
            Assert.DoesNotThrow(() => GameIdentityCatalog.Validate(entry));
        }

        [Test]
        public void 문자열_상한과_base_1을_허용한다()
        {
            var entry = ValidEntry();
            entry.Name = new string('a', 198) + ".b";
            entry.Meaning = new string('a', 500);
            entry.ChangesWhen = new string('a', 500);
            entry.Identifies = new string('a', 60);
            entry.Base = 1;
            Assert.DoesNotThrow(() => GameIdentityCatalog.Validate(entry));
        }

        private static GameIdentityEntryDto ValidEntry() => new GameIdentityEntryDto
        {
            Name = "StageManager.Stage", Kind = "ordinal", Base = 0, Identifies = "stage", Role = "current",
            Meaning = "현재 stage", ChangesWhen = "stage 진입 시"
        };
    }
}
