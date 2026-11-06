using System.Collections.Generic;
using Artel.Affordances.Scan;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests
{
    /// <summary>
    /// scene walk 전후로 <c>PlayerPrefs</c> 를 담고 되돌리는 <c>PlayerPrefsSnapshot</c> 을 본다(ARTEL-1231).
    /// </summary>
    /// <remarks>
    /// 실제 <c>PlayerPrefs</c> 에 쓴다. 쓰는 key 는 모두 <see cref="Prefix"/> 로 시작하고 <c>[TearDown]</c> 에서 지운다.
    /// 새 key 를 지우는 경우는 key 를 <c>extraKeys</c> 로도 넘긴다 — 저장소를 나열하지 못하는 플랫폼에서도 같은 것을
    /// 보게 하려는 것이다.
    /// </remarks>
    public sealed class PlayerPrefsSnapshotTests
    {
        private const string Prefix = "artel-test-1231.";
        private const string Stage = Prefix + "MaxStageId";
        private const string Volume = Prefix + "Volume";
        private const string Name = Prefix + "Name";
        private const string Added = Prefix + "TutorialFlag";
        private const string Own = "Artel." + Prefix + "Token";

        private static readonly string[] All = { Stage, Volume, Name, Added, Own };

        [SetUp]
        public void SetUp()
        {
            Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Clear();
        }

        private static void Clear()
        {
            foreach (var key in All)
            {
                PlayerPrefs.DeleteKey(key);
            }

            PlayerPrefs.Save();
        }

        [Test]
        public void ValuesTheWalkChangedGoBackToWhatTheyWere()
        {
            PlayerPrefs.SetInt(Stage, 0);
            PlayerPrefs.SetFloat(Volume, 0.25f);
            PlayerPrefs.SetString(Name, "before");

            var snapshot = PlayerPrefsSnapshot.Take(new[] { Stage, Volume, Name });

            PlayerPrefs.SetInt(Stage, 4);
            PlayerPrefs.SetFloat(Volume, 1f);
            PlayerPrefs.SetString(Name, "after");

            Assert.AreEqual(3, snapshot.Restore());
            Assert.AreEqual(0, PlayerPrefs.GetInt(Stage, -1));
            Assert.AreEqual(0.25f, PlayerPrefs.GetFloat(Volume, -1f));
            Assert.AreEqual("before", PlayerPrefs.GetString(Name, "missing"));
        }

        [Test]
        public void AKeyTheWalkCreatedIsDeleted()
        {
            var snapshot = PlayerPrefsSnapshot.Take(new[] { Added });

            PlayerPrefs.SetInt(Added, 1);

            Assert.AreEqual(1, snapshot.Restore());
            Assert.IsFalse(PlayerPrefs.HasKey(Added));
        }

        [Test]
        public void AKeyTheWalkDeletedComesBack()
        {
            PlayerPrefs.SetString(Name, "saved");
            var snapshot = PlayerPrefsSnapshot.Take(new[] { Name });

            PlayerPrefs.DeleteKey(Name);

            Assert.AreEqual(1, snapshot.Restore());
            Assert.AreEqual("saved", PlayerPrefs.GetString(Name, "missing"));
        }

        [Test]
        public void AKeyWhoseTypeChangedGetsItsOldTypeBack()
        {
            PlayerPrefs.SetInt(Stage, 2);
            var snapshot = PlayerPrefsSnapshot.Take(new[] { Stage });

            PlayerPrefs.DeleteKey(Stage);
            PlayerPrefs.SetString(Stage, "two");

            snapshot.Restore();

            Assert.AreEqual(2, PlayerPrefs.GetInt(Stage, -1));
            Assert.AreEqual("missing", PlayerPrefs.GetString(Stage, "missing"));
        }

        [Test]
        public void NothingChangedMeansNothingIsWritten()
        {
            PlayerPrefs.SetInt(Stage, 3);
            var snapshot = PlayerPrefsSnapshot.Take(new[] { Stage });

            Assert.AreEqual(0, snapshot.Restore());
            Assert.AreEqual(3, PlayerPrefs.GetInt(Stage, -1));
        }

        [Test]
        public void TheSdksOwnKeysAreLeftAsTheWalkLeftThem()
        {
            PlayerPrefs.SetString(Own, "old-token");
            var snapshot = PlayerPrefsSnapshot.Take(new[] { Own });

            PlayerPrefs.SetString(Own, "refreshed-token");

            Assert.AreEqual(0, snapshot.Restore());
            Assert.AreEqual("refreshed-token", PlayerPrefs.GetString(Own, "missing"));
        }

        [Test]
        public void TheRegistryHashSuffixIsStrippedFromAValueName()
        {
            Assert.AreEqual("MaxStageId", PlayerPrefsKeys.KeyOf("MaxStageId_h1234567890"));
            Assert.AreEqual("Screenmanager Resolution Width",
                PlayerPrefsKeys.KeyOf("Screenmanager Resolution Width_h182942802"));
            Assert.AreEqual("player_hp", PlayerPrefsKeys.KeyOf("player_hp"));
            Assert.AreEqual("level_h", PlayerPrefsKeys.KeyOf("level_h"));
        }

        [Test]
        public void SavedEffectsInTheEvidenceNameTheirKeys()
        {
            const string document =
                "[{\"effects\":[{\"kind\":\"saved\",\"category\":\"state\",\"target\":\"MaxStageId\",\"detail\":\"4\"}," +
                "{\"kind\":\"saved\",\"category\":\"state\",\"target\":\"*\",\"detail\":\"deleted\"}," +
                "{\"kind\":\"saved\",\"category\":\"state\",\"target\":\"(not a literal)\",\"detail\":\"1\"}," +
                "{\"kind\":\"write\",\"category\":\"state\",\"target\":\"Player.hp\",\"detail\":\"1\"}]}]";

            var keys = new List<string>();
            PlayerPrefsSnapshot.KeysIn(document, keys);

            CollectionAssert.AreEqual(new[] { "MaxStageId" }, keys);
        }
    }
}
