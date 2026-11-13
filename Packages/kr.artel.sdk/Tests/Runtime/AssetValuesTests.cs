using System;
using System.Collections.Generic;
using System.Text;
using Artel.Affordances.Scan;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Artel.Tests
{
    /// <summary>ScriptableObject 의 직렬화된 값이 문서의 <c>assets</c> 에 실리는지 본다(ARTEL-1226).</summary>
    public sealed class AssetValuesTests
    {
        private readonly List<Object> made = new List<Object>();
        private Func<Type, bool> wasGameType;

        [SetUp]
        public void SetUp()
        {
            wasGameType = AssetValues.IsGameType;
            AssetValues.IsGameType = type => type.Namespace == typeof(AssetValuesTests).Namespace;
            AffordanceReport.Forget();
        }

        [TearDown]
        public void TearDown()
        {
            AssetValues.IsGameType = wasGameType;
            AffordanceReport.Forget();

            foreach (var asset in made)
            {
                Object.DestroyImmediate(asset);
            }

            made.Clear();
        }

        private T Make<T>(string name) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            asset.name = name;
            made.Add(asset);
            return asset;
        }

        private static string Composed()
        {
            var text = new StringBuilder();
            AssetValues.Compose(text);
            return text.ToString();
        }

        [Test]
        public void ANestedTableIsWrittenWithItsNumbers()
        {
            var table = Make<AffinityTableFixture>("Magic Affinity Table");
            table.table = new AffinityRowsFixture
            {
                rows = new List<AffinityRowFixture>
                {
                    new AffinityRowFixture { targets = new List<float> { 1f, 0.5f } },
                    new AffinityRowFixture { targets = new List<float> { 2f, 1f } }
                }
            };

            AssetValues.Read(table);

            StringAssert.Contains(
                "\"name\":\"Magic Affinity Table\",\"type\":\"" + typeof(AffinityTableFixture).FullName +
                "\",\"values\":{\"table\":{\"rows\":[{\"targets\":[1,0.5]},{\"targets\":[2,1]}]}},\"truncated\":false",
                Composed());
        }

        [Test]
        public void EnumsAreNamedAndAReferencedAssetIsReadToo()
        {
            var enemies = Make<EnemyListFixture>("EnemyDataContainer");
            enemies.enemies = new List<EnemyFixture>
            {
                new EnemyFixture { name = "Lich", maxHp = 80, type = ElementFixture.Undead }
            };

            var waves = Make<WaveListFixture>("BossBattleWaveData");
            waves.container = enemies;
            waves.counts = new[] { 2, 3 };

            AssetValues.Read(waves);
            var document = Composed();

            StringAssert.Contains("\"enemies\":[{\"maxHp\":80,\"name\":\"Lich\",\"type\":\"Undead\"}]", document);
            StringAssert.Contains("\"container\":{\"ref\":\"EnemyDataContainer\",\"id\":", document);
            StringAssert.Contains("\"counts\":[2,3]", document);
            Assert.That(AssetValues.Count, Is.EqualTo(2));
        }

        [Test]
        public void MoreThanTwoHundredValuesAreCutAndSaySo()
        {
            var big = Make<WaveListFixture>("Big");
            big.counts = new int[500];

            AssetValues.Read(big);

            StringAssert.Contains("\"truncated\":true", Composed());
        }

        [Test]
        public void AnAssetOutsideTheGameIsNotRead()
        {
            AssetValues.IsGameType = type => false;
            AssetValues.Read(Make<WaveListFixture>("Engine"));

            Assert.That(AssetValues.Count, Is.EqualTo(0));
            Assert.That(Composed(), Is.EqualTo("\"assets\":{}"));
        }

        [Test]
        public void TheReportCarriesTheAssetsAndPromisesThem()
        {
            AssetValues.Read(Make<WaveListFixture>("Waves"));
            var document = AffordanceReport.Compose();

            StringAssert.Contains("\"asset-values-v1\"", document);
            StringAssert.Contains("\"assets\":{\"", document);
        }
    }

    public enum ElementFixture
    {
        Fire,
        Undead
    }

    [Serializable]
    public class AffinityRowFixture
    {
        public List<float> targets = new List<float>();
    }

    [Serializable]
    public class AffinityRowsFixture
    {
        public List<AffinityRowFixture> rows = new List<AffinityRowFixture>();
    }

    public class AffinityTableFixture : ScriptableObject
    {
        public AffinityRowsFixture table = new AffinityRowsFixture();
    }

    [Serializable]
    public struct EnemyFixture
    {
        public string name;
        public int maxHp;
        public ElementFixture type;
    }

    public class EnemyListFixture : ScriptableObject
    {
        public List<EnemyFixture> enemies = new List<EnemyFixture>();
    }

    public class WaveListFixture : ScriptableObject
    {
        public EnemyListFixture container;
        public int[] counts = new int[0];
    }
}
