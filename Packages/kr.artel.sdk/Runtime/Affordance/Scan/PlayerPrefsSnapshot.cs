using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// scene walk 전의 <c>PlayerPrefs</c> 를 담아 두었다가, walk 이 끝나면 그 값으로 되돌린다(ARTEL-1231).
    /// </summary>
    /// <remarks>
    /// walk 은 scene 을 하나씩 load 하면서 게임 코드를 실행하고, 게임 코드는 그 사이에 세이브를 쓴다. gyemong 에서는
    /// walk 한 번으로 <c>MaxStageId=4</c>, <c>CurrentStageId=3</c>, <c>TutorialFlag=1</c> 이 저장돼 새 플레이어의
    /// 타이틀에 이어하기 버튼이 나타났다.
    ///
    /// <c>PlayerPrefs</c> API 는 key 를 나열하지 못한다. 그래서 key 목록은 저장소에서 직접 읽는다.
    /// Windows 는 registry 의 value 이름이 곧 key 다. 다른 플랫폼에서는 evidence 가 적은 <c>PlayerPrefs</c> key 만
    /// 담고, 그렇다고 <see cref="Enumerated"/> 로 알린다. 값은 어느 플랫폼이든 API 로 읽고 API 로 되쓴다 —
    /// registry 를 직접 고치면 Unity 가 들고 있는 값과 어긋날 수 있다.
    ///
    /// SDK 자신의 key(<c>Artel.</c> 접두사)는 담지도 되돌리지도 않는다. 로그인 token 처럼 walk 중에 SDK 가
    /// 정당하게 바꾼 값을 walk 전 값으로 덮으면 SDK 가 자기 session 을 잃는다.
    /// </remarks>
    internal sealed class PlayerPrefsSnapshot
    {
        internal const string OwnPrefix = "Artel.";

        private readonly Dictionary<string, Stored> values;
        private readonly HashSet<string> knownKeys;

        private PlayerPrefsSnapshot(Dictionary<string, Stored> values, HashSet<string> knownKeys, bool enumerated)
        {
            this.values = values;
            this.knownKeys = knownKeys;
            Enumerated = enumerated;
        }

        /// <summary>key 목록을 저장소에서 직접 읽었으면 참. 거짓이면 evidence 가 적은 key 만 담았다.</summary>
        internal bool Enumerated { get; }

        /// <summary>담아 둔 key 수. SDK 자신의 key 는 세지 않는다.</summary>
        internal int Count => values.Count;

        /// <summary>지금의 <c>PlayerPrefs</c> 를 담는다.</summary>
        /// <param name="extraKeys">저장소를 나열하지 못할 때 대신 쓸 key. 보통 evidence 가 적은 key 다.</param>
        internal static PlayerPrefsSnapshot Take(IEnumerable<string> extraKeys)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var enumerated = PlayerPrefsKeys.TryList(keys);

            if (extraKeys != null)
            {
                foreach (var key in extraKeys)
                {
                    if (!string.IsNullOrEmpty(key))
                    {
                        keys.Add(key);
                    }
                }
            }

            var values = new Dictionary<string, Stored>(StringComparer.Ordinal);

            foreach (var key in keys)
            {
                if (IsOwn(key) || !PlayerPrefs.HasKey(key))
                {
                    continue;
                }

                values[key] = Stored.Read(key);
            }

            return new PlayerPrefsSnapshot(values, keys, enumerated);
        }

        /// <summary>
        /// 담아 둔 값으로 되돌리고, 바꾸거나 지운 key 수를 돌려준다.
        /// </summary>
        /// <remarks>
        /// walk 중에 새로 생긴 key 는 지운다. 그 목록도 담을 때와 같은 방법으로 다시 읽으므로, 저장소를 나열하지
        /// 못하는 플랫폼에서는 evidence 가 적지 않은 새 key 가 남을 수 있다.
        /// </remarks>
        internal int Restore()
        {
            var touched = 0;
            var now = new HashSet<string>(knownKeys, StringComparer.Ordinal);
            PlayerPrefsKeys.TryList(now);

            foreach (var key in now)
            {
                if (IsOwn(key) || values.ContainsKey(key) || !PlayerPrefs.HasKey(key))
                {
                    continue;
                }

                PlayerPrefs.DeleteKey(key);
                touched++;
            }

            foreach (var pair in values)
            {
                if (PlayerPrefs.HasKey(pair.Key) && pair.Value.Equals(Stored.Read(pair.Key)))
                {
                    continue;
                }

                pair.Value.Write(pair.Key);
                touched++;
            }

            if (touched > 0)
            {
                PlayerPrefs.Save();
            }

            return touched;
        }

        /// <summary>evidence 가 <c>PlayerPrefs</c> 에 쓴다고 적은 key 들.</summary>
        /// <remarks>
        /// 저장소를 나열하지 못하는 플랫폼에서 쓸 대신의 목록이다. 분석기는 <c>SetInt</c> 같은 호출을
        /// <c>"kind":"saved"</c> effect 로 적고 key 가 문자열 상수일 때 그 값을 <c>target</c> 에 둔다. 문서를
        /// 파싱하지 않고 그 effect 의 앞부분만 찾는다 — 필드 순서는 <c>EvidenceJson</c> 이 고정한다.
        /// </remarks>
        internal static List<string> EvidenceKeys()
        {
            var keys = new List<string>();

            foreach (var document in AffordanceCatalog.Everything().Values)
            {
                KeysIn(document, keys);
            }

            return keys;
        }

        private const string SavedMarker = "\"kind\":\"saved\",\"category\":\"state\",\"target\":\"";

        internal static void KeysIn(string document, List<string> into)
        {
            var at = document.IndexOf(SavedMarker, StringComparison.Ordinal);

            while (at >= 0)
            {
                var from = at + SavedMarker.Length;
                var end = document.IndexOf('"', from);

                if (end > from)
                {
                    var key = document.Substring(from, end - from);

                    if (key != "*" && !key.StartsWith("(", StringComparison.Ordinal) && !into.Contains(key))
                    {
                        into.Add(key);
                    }
                }

                at = document.IndexOf(SavedMarker, from, StringComparison.Ordinal);
            }
        }

        internal static bool IsOwn(string key)
        {
            return key.StartsWith(OwnPrefix, StringComparison.Ordinal);
        }

        /// <summary>key 하나의 값과 type.</summary>
        /// <remarks>
        /// <c>PlayerPrefs</c> 는 int, float, string 이 key 이름 공간 하나를 나눠 쓰고, type 이 다른 getter 는 기본값을
        /// 돌려준다. 그래서 기본값을 두 번 바꿔 물어 type 을 가린다: 서로 다른 기본값 두 개에 같은 답이 오면 그 type
        /// 으로 저장된 값이다.
        /// </remarks>
        private readonly struct Stored : IEquatable<Stored>
        {
            private const string MissingText = "\u0001artel-missing";

            private readonly char kind;
            private readonly int integer;
            private readonly float real;
            private readonly string text;

            private Stored(char kind, int integer, float real, string text)
            {
                this.kind = kind;
                this.integer = integer;
                this.real = real;
                this.text = text;
            }

            internal static Stored Read(string key)
            {
                var text = PlayerPrefs.GetString(key, MissingText);

                if (!string.Equals(text, MissingText, StringComparison.Ordinal))
                {
                    return new Stored('s', 0, 0f, text);
                }

                var low = PlayerPrefs.GetInt(key, int.MinValue);
                var high = PlayerPrefs.GetInt(key, int.MaxValue);

                if (low == high)
                {
                    return new Stored('i', low, 0f, null);
                }

                return new Stored('f', 0, PlayerPrefs.GetFloat(key), null);
            }

            internal void Write(string key)
            {
                // type 이 바뀐 경우를 위해 먼저 지운다. 같은 이름의 int 위에 string 을 쓰면 플랫폼에 따라 둘이 함께 남는다.
                PlayerPrefs.DeleteKey(key);

                switch (kind)
                {
                    case 's':
                        PlayerPrefs.SetString(key, text);
                        break;
                    case 'i':
                        PlayerPrefs.SetInt(key, integer);
                        break;
                    default:
                        PlayerPrefs.SetFloat(key, real);
                        break;
                }
            }

            public bool Equals(Stored other)
            {
                return kind == other.kind && integer == other.integer &&
                       real.Equals(other.real) && string.Equals(text, other.text, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is Stored other && Equals(other);
            }

            public override int GetHashCode()
            {
                return kind.GetHashCode() ^ integer ^ real.GetHashCode() ^ (text == null ? 0 : text.GetHashCode());
            }
        }
    }

    /// <summary>저장소에서 <c>PlayerPrefs</c> key 이름을 읽는다. 읽을 수 없는 플랫폼에서는 거짓을 돌려준다.</summary>
    internal static class PlayerPrefsKeys
    {
        /// <summary>key 이름을 <paramref name="into"/> 에 더한다. 저장소를 나열할 수 있었으면 참.</summary>
        internal static bool TryList(ICollection<string> into)
        {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            try
            {
                return WindowsRegistry.List(SubKey(), into);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Artel] The PlayerPrefs registry key could not be listed: " + exception.Message);
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// registry value 이름에서 <c>PlayerPrefs</c> key 를 꺼낸다.
        /// </summary>
        /// <remarks>
        /// Unity 는 key 이름 뒤에 <c>_h</c> 와 이름의 hash 를 붙여 value 이름으로 쓴다. <c>MaxStageId</c> 는
        /// <c>MaxStageId_h1234567890</c> 으로 저장된다. 뒤에 붙은 것이 그 모양이 아니면 이름을 그대로 둔다.
        /// </remarks>
        internal static string KeyOf(string valueName)
        {
            if (string.IsNullOrEmpty(valueName))
            {
                return valueName;
            }

            var mark = valueName.LastIndexOf("_h", StringComparison.Ordinal);

            if (mark <= 0 || mark + 2 >= valueName.Length)
            {
                return valueName;
            }

            for (var index = mark + 2; index < valueName.Length; index++)
            {
                if (!char.IsDigit(valueName[index]))
                {
                    return valueName;
                }
            }

            return valueName.Substring(0, mark);
        }

        private static string SubKey()
        {
            var tail = Application.companyName + "\\" + Application.productName;
            return Application.isEditor ? "Software\\Unity\\UnityEditor\\" + tail : "Software\\" + tail;
        }

#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        private static class WindowsRegistry
        {
            private const int KeyRead = 0x20019;
            private const int Success = 0;
            private const int NoMoreItems = 259;
            private const int FileNotFound = 2;
            private const int MaxValueName = 16384;
            private static readonly UIntPtr CurrentUser = new UIntPtr(0x80000001u);

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegOpenKeyExW")]
            private static extern int RegOpenKeyEx(
                UIntPtr key, string subKey, int options, int access, out UIntPtr opened);

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegEnumValueW")]
            private static extern int RegEnumValue(
                UIntPtr key, int index, StringBuilder name, ref int nameLength,
                IntPtr reserved, IntPtr type, IntPtr data, IntPtr dataLength);

            [DllImport("advapi32.dll")]
            private static extern int RegCloseKey(UIntPtr key);

            internal static bool List(string subKey, ICollection<string> into)
            {
                var opened = RegOpenKeyEx(CurrentUser, subKey, 0, KeyRead, out var handle);

                // 아직 아무것도 저장하지 않은 게임에는 key 자체가 없다. 나열은 성공했고 key 가 0 개다.
                if (opened == FileNotFound)
                {
                    return true;
                }

                if (opened != Success)
                {
                    return false;
                }

                try
                {
                    var name = new StringBuilder(MaxValueName);

                    for (var index = 0; ; index++)
                    {
                        name.Length = 0;
                        var length = MaxValueName;
                        var result = RegEnumValue(
                            handle, index, name, ref length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

                        if (result == NoMoreItems)
                        {
                            return true;
                        }

                        if (result != Success)
                        {
                            return false;
                        }

                        into.Add(KeyOf(name.ToString(0, length)));
                    }
                }
                finally
                {
                    RegCloseKey(handle);
                }
            }
        }
#endif
    }
}
