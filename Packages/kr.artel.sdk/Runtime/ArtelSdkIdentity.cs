using System;
using UnityEngine;

namespace Artel
{
    /// <summary>
    /// 이 설치본의 <c>sdkId</c>. <see cref="ArtelIdentityStore"/> 의 파일에 둔다(ARTEL-1232).
    /// </summary>
    internal static class ArtelSdkIdentity
    {
        /// <summary>파일로 옮기기 전에 쓰던 자리. 남아 있으면 처음 읽을 때 옮기고 지운다.</summary>
        private const string LegacyPlayerPrefsKey = ArtelOwnedPlayerPrefs.SdkId;

        public static string LoadOrCreate()
        {
            var storedId = ArtelIdentityStore.ReadSdkId();
            if (Guid.TryParse(storedId, out _))
            {
                return storedId;
            }

            var legacyId = PlayerPrefs.GetString(LegacyPlayerPrefsKey, string.Empty);
            var sdkId = Guid.TryParse(legacyId, out _) ? legacyId : Guid.NewGuid().ToString("D");

            if (ArtelIdentityStore.WriteSdkId(sdkId))
            {
                PlayerPrefs.DeleteKey(LegacyPlayerPrefsKey);
            }
            else
            {
                PlayerPrefs.SetString(LegacyPlayerPrefsKey, sdkId);
            }

            PlayerPrefs.Save();
            return sdkId;
        }

        public static string ResetAndCreate()
        {
            ArtelIdentityStore.WriteSdkId(null);
            PlayerPrefs.DeleteKey(LegacyPlayerPrefsKey);
            return LoadOrCreate();
        }
    }
}
