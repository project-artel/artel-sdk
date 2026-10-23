using System.IO;
using Artel.Auth;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests
{
    /// <summary>
    /// 게임이 <c>PlayerPrefs.DeleteAll()</c> 을 불러도 <c>sdkId</c> 와 instance id 가 남는지(ARTEL-1232).
    /// </summary>
    /// <remarks>
    /// gyemong 의 <c>CinematicEvent</c> 가 <c>DeleteAll()</c> 을 부르자 다음 실행이 새 game instance 로 등록됐다. 이 스위트도
    /// 실제로 <c>DeleteAll()</c> 을 부르므로 버리는 테스트 프로젝트에서만 돌린다(<c>OwnedPlayerPrefsTests</c> 와 같다).
    /// </remarks>
    public sealed class IdentitySurvivesPlayerPrefsWipeTests
    {
        private IdentityFileSandbox identityFile;

        [SetUp]
        public void SetUp()
        {
            identityFile = new IdentityFileSandbox();
            ArtelSecretStore.Current = new PlayerPrefsSecretStore();
        }

        [TearDown]
        public void TearDown()
        {
            ArtelSdkSession.Clear();
            ArtelSecretStore.Current = null;
            identityFile.Dispose();
        }

        [Test]
        public void SdkIdAndInstanceId_SurvivePlayerPrefsDeleteAll()
        {
            var sdkId = ArtelSdkIdentity.LoadOrCreate();
            ArtelSdkSession.SaveInstanceId("instance-6");

            PlayerPrefs.DeleteAll();
            ArtelIdentityStore.ForgetCache();

            Assert.That(ArtelSdkIdentity.LoadOrCreate(), Is.EqualTo(sdkId));
            Assert.That(ArtelSdkSession.LoadInstanceId(), Is.EqualTo("instance-6"));
            Assert.That(File.Exists(identityFile.FilePath), Is.True);
        }

        [Test]
        public void PlayerPrefsValuesFromAnOlderSdk_MoveIntoTheFile()
        {
            var legacySdkId = System.Guid.NewGuid().ToString("D");
            var legacyInstanceKey = ArtelSdkSession.ScopedKey(ArtelOwnedPlayerPrefs.InstanceId);
            PlayerPrefs.SetString(ArtelOwnedPlayerPrefs.SdkId, legacySdkId);
            PlayerPrefs.SetString(legacyInstanceKey, "instance-legacy");

            Assert.That(ArtelSdkIdentity.LoadOrCreate(), Is.EqualTo(legacySdkId));
            Assert.That(ArtelSdkSession.LoadInstanceId(), Is.EqualTo("instance-legacy"));

            Assert.That(PlayerPrefs.HasKey(ArtelOwnedPlayerPrefs.SdkId), Is.False, "moved, not copied");
            Assert.That(PlayerPrefs.HasKey(legacyInstanceKey), Is.False);

            ArtelIdentityStore.ForgetCache();
            Assert.That(ArtelIdentityStore.ReadSdkId(), Is.EqualTo(legacySdkId), "read back from the file");
        }

        [Test]
        public void WithNeitherFileNorPlayerPrefs_ANewIdIsCreatedAndKept()
        {
            PlayerPrefs.DeleteKey(ArtelOwnedPlayerPrefs.SdkId);

            var created = ArtelSdkIdentity.LoadOrCreate();
            ArtelIdentityStore.ForgetCache();

            Assert.That(System.Guid.TryParse(created, out _), Is.True);
            Assert.That(ArtelSdkIdentity.LoadOrCreate(), Is.EqualTo(created));
        }

        [Test]
        public void Logout_ForgetsTheInstanceButKeepsTheSdkId()
        {
            var sdkId = ArtelSdkIdentity.LoadOrCreate();
            ArtelSdkSession.SaveInstanceId("instance-7");

            ArtelSdkSession.Clear();
            ArtelIdentityStore.ForgetCache();

            Assert.That(ArtelSdkSession.TryLoadInstanceId(out _), Is.False);
            Assert.That(ArtelSdkIdentity.LoadOrCreate(), Is.EqualTo(sdkId));
        }
    }
}
