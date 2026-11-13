using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Artel.Auth;
using Newtonsoft.Json;
using UnityEngine;

namespace Artel
{
    /// <summary>
    /// 이 설치본의 <c>sdkId</c> 와 서버마다의 instance id 를 <c>Application.persistentDataPath/artel/identity.json</c> 에 둔다(ARTEL-1232).
    /// </summary>
    /// <remarks>
    /// 전에는 <c>PlayerPrefs</c> 에 있었다. 게임이 <c>PlayerPrefs.DeleteAll()</c> 을 부르면 함께 지워져 다음 실행이 새 game
    /// instance 로 등록됐다 (gyemong 의 <c>CinematicEvent</c>, 6 → 10). 게임은 SDK 의 파일을 모르므로 지울 일이 없다.
    ///
    /// 예전 <c>PlayerPrefs</c> 값이 있으면 처음 읽을 때 파일로 옮기고 지운다. 파일을 쓰지 못하는 환경(읽기 전용 디스크 같은
    /// 것)에서는 경고를 남기고 예전처럼 <c>PlayerPrefs</c> 에 둔다 — 식별자를 잃는 것보다 지워질 수 있는 자리에 두는 편이 낫다.
    ///
    /// 쓰기는 <see cref="SecretFile.Replace"/> 로 한다. 같은 사람이 게임을 둘 띄우면 두 프로세스가 같은 파일을 쓴다.
    /// </remarks>
    internal static class ArtelIdentityStore
    {
        /// <summary>테스트가 실제 설치본의 파일을 건드리지 않게 경로를 바꾸는 자리. null 이면 기본 경로.</summary>
        internal static string PathOverride { get; set; }

        /// <summary>테스트가 파일을 지운 뒤 기억해 둔 값을 버리게 한다.</summary>
        internal static void ForgetCache()
        {
            cached = null;
            cachedPath = null;
        }

        private static bool warnedUnwritable;

        /// <summary>
        /// 마지막으로 읽거나 쓴 문서와 그 경로. overlay 가 <c>CanConnect</c> 를 그릴 때마다 instance id 를 묻는데, 그때마다
        /// 디스크를 읽지 않으려고 둔다. 다른 프로세스가 그사이 파일을 바꿔도 이 프로세스는 자기가 아는 값을 쓴다.
        /// </summary>
        private static IdentityDocument cached;
        private static string cachedPath;

        internal static string FilePath =>
            PathOverride ?? Path.Combine(Application.persistentDataPath, "artel", "identity.json");

        /// <summary>파일의 <c>sdkId</c>. 없으면 null.</summary>
        public static string ReadSdkId()
        {
            return Read().SdkId;
        }

        /// <summary><paramref name="sdkId"/> 를 파일에 쓰고, 쓰였으면 true.</summary>
        public static bool WriteSdkId(string sdkId)
        {
            var document = Read();
            document.SdkId = sdkId;
            return TryWrite(document);
        }

        /// <summary>
        /// <paramref name="scope"/> 서버의 instance id. 파일에 없고 <paramref name="legacyKey"/> 에 있으면 파일로 옮긴다.
        /// </summary>
        public static string ReadInstanceId(string scope, string legacyKey)
        {
            var document = Read();
            if (document.InstanceIds.TryGetValue(scope, out var stored) && !string.IsNullOrWhiteSpace(stored))
            {
                return stored.Trim();
            }

            var legacy = PlayerPrefs.GetString(legacyKey, string.Empty);
            if (string.IsNullOrWhiteSpace(legacy))
            {
                return null;
            }

            legacy = legacy.Trim();
            document.InstanceIds[scope] = legacy;
            if (TryWrite(document))
            {
                PlayerPrefs.DeleteKey(legacyKey);
                PlayerPrefs.Save();
            }

            return legacy;
        }

        public static void WriteInstanceId(string scope, string legacyKey, string instanceId)
        {
            var document = Read();
            document.InstanceIds[scope] = instanceId;
            if (TryWrite(document))
            {
                PlayerPrefs.DeleteKey(legacyKey);
            }
            else
            {
                PlayerPrefs.SetString(legacyKey, instanceId);
            }

            PlayerPrefs.Save();
        }

        public static void DeleteInstanceId(string scope, string legacyKey)
        {
            var document = Read();
            if (document.InstanceIds.Remove(scope))
            {
                TryWrite(document);
            }

            PlayerPrefs.DeleteKey(legacyKey);
        }

        private static IdentityDocument Read()
        {
            var path = FilePath;
            if (cached != null && cachedPath == path)
            {
                return cached.Copy();
            }

            var document = ReadFile(path);
            cached = document;
            cachedPath = path;
            return document.Copy();
        }

        private static IdentityDocument ReadFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return new IdentityDocument();
                }

                var document = JsonConvert.DeserializeObject<IdentityDocument>(File.ReadAllText(path, Encoding.UTF8));
                if (document == null)
                {
                    return new IdentityDocument();
                }

                document.InstanceIds = document.InstanceIds ?? new Dictionary<string, string>();
                return document;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                              exception is JsonException)
            {
                // 깨진 파일은 없는 것으로 본다. 다음 쓰기가 온전한 파일로 덮는다.
                Debug.LogWarning("[Artel] The identity file could not be read: " + exception.Message);
                return new IdentityDocument();
            }
        }

        private static bool TryWrite(IdentityDocument document)
        {
            try
            {
                var path = FilePath;
                SecretFile.Replace(path, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(document)));
                cached = document.Copy();
                cachedPath = path;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                if (!warnedUnwritable)
                {
                    warnedUnwritable = true;
                    Debug.LogWarning("[Artel] The identity file could not be written, so PlayerPrefs keeps it: " +
                                     exception.Message);
                }

                return false;
            }
        }

        private sealed class IdentityDocument
        {
            [JsonProperty("sdkId")]
            public string SdkId { get; set; }

            /// <summary>서버 주소의 SHA-256 (<c>ArtelSdkSession</c> 의 scope) → instance id.</summary>
            [JsonProperty("instanceIds")]
            public Dictionary<string, string> InstanceIds { get; set; } = new Dictionary<string, string>();

            public IdentityDocument Copy()
            {
                return new IdentityDocument
                {
                    SdkId = SdkId,
                    InstanceIds = new Dictionary<string, string>(InstanceIds)
                };
            }
        }
    }
}
