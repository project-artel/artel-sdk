using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Artel.Protocol.Dto;
using global::UnityEngine;

namespace Artel
{
    /// <summary>
    /// 게임 코드가 어떤 키와 Input Manager 축을 읽는지. 키는 빌드 때 위버가 호출 지점에서 모은 것이고, 축 이름은 그것에
    /// 실행 중 proxy 가 받은 이름을 더한다.
    /// </summary>
    /// <remarks>
    /// 키 명령이 게임에 닿지 않는 경우를 성공으로 보고하지 않으려고 둔다(ARTEL-1412). 가상 키는 <c>GetKey*</c> 에만 닿고
    /// <c>GetAxis</c>·<c>GetButton*</c> 에는 닿지 않는다(ARTEL-176·292). 그래서 게임이 어디서도 읽지 않는 키를 축을 읽는
    /// 게임에 보내면, 축 이름과 <c>set_button</c>·<c>set_axis</c> 를 알려 주는 실패로 바꾼다.
    ///
    /// "읽지 않는다"를 실행 중 기록으로 정하지 않는다. 그 키를 읽는 코드가 아직 돌지 않았을 뿐일 수 있다. 위버가 본 호출
    /// 지점은 빌드 전체에 대한 사실이다. 축 이름만은 실행 중 기록을 더한다. <c>CrossPlatformInputManager</c> 처럼 이름을
    /// 변수로 넘기는 코드는 빌드 기록에 이름이 남지 않는다.
    ///
    /// <c>ArtelInput.ResetVirtualKeyboard</c> 가 지우지 않는다. 빌드 기록은 assembly 가 로드될 때 한 번만 들어온다.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class InputReadManifest
    {
        private static readonly Dictionary<string, AssemblyReads> Declared =
            new Dictionary<string, AssemblyReads>(StringComparer.Ordinal);

        private static readonly SortedSet<string> SeenAxes = new SortedSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 위버가 만든 <c>Artel.Generated.InputReadRegistration.Register()</c> 가 부른다.
        /// </summary>
        /// <param name="assemblyName">읽는 코드가 든 assembly. 같은 이름이 다시 오면 덮어쓴다(Domain Reload 를 끈 재진입).</param>
        /// <param name="keyCodes">리터럴 KeyCode 값을 쉼표로 이은 것.</param>
        /// <param name="keyNames">리터럴 문자열 키 이름을 줄바꿈으로 이은 것.</param>
        /// <param name="readsUnknownKeys">키를 변수로 읽거나 <c>anyKey</c> 를 읽는다. 어떤 키든 읽힐 수 있다.</param>
        /// <param name="axisNames">리터럴 축 이름을 줄바꿈으로 이은 것.</param>
        /// <param name="readsUnknownAxes">축 이름을 변수로 넘긴다.</param>
        public static void Declare(
            string assemblyName, string keyCodes, string keyNames, bool readsUnknownKeys, string axisNames, bool readsUnknownAxes)
        {
            var reads = new AssemblyReads { ReadsUnknownKeys = readsUnknownKeys, ReadsUnknownAxes = readsUnknownAxes };

            foreach (var value in Split(keyCodes, ','))
            {
                if (int.TryParse(value, out var number) && Enum.IsDefined(typeof(KeyCode), number))
                    reads.Keys.Add((KeyCode)number);
                else
                    reads.ReadsUnknownKeys = true;
            }

            foreach (var name in Split(keyNames, '\n'))
            {
                // legacy 의 문자열 키 이름은 "left shift" 같은 꼴도 받는다. KeyCode 이름으로 풀리지 않으면 어느 키인지 모르므로
                // 판단을 거둔다.
                // "1" 같은 숫자는 Enum.TryParse 가 값으로 받는다(KeyCode 1, Alpha1 아님). 이름만 받는다.
                if (!char.IsDigit(name[0]) && Enum.TryParse(name, true, out KeyCode key) && key != KeyCode.None &&
                    Enum.IsDefined(typeof(KeyCode), key))
                    reads.Keys.Add(key);
                else
                    reads.ReadsUnknownKeys = true;
            }

            foreach (var name in Split(axisNames, '\n')) reads.Axes.Add(name);

            Declared[assemblyName ?? string.Empty] = reads;
        }

        internal static void SawAxis(string axisName)
        {
            if (!string.IsNullOrEmpty(axisName)) SeenAxes.Add(axisName);
        }

        /// <summary>
        /// 이 키 명령이 게임에 닿지 않는다고 확실히 말할 수 있을 때의 실패 문구. 그렇지 않으면 null 이다.
        /// </summary>
        /// <param name="key">보내려는 키.</param>
        /// <param name="keyboardAdapterRegistered">Input System 키보드 adapter 가 있다. 그러면 키가 그쪽으로도 가므로 판단하지 않는다.</param>
        internal static string KeyCommandError(KeyCode key, bool keyboardAdapterRegistered)
        {
            if (keyboardAdapterRegistered || MouseButtonKeyCode.TryGetButton(key, out _) || Declared.Count == 0)
                return null;

            var readsAxes = false;
            var axes = new SortedSet<string>(SeenAxes, StringComparer.Ordinal);

            foreach (var reads in Declared.Values)
            {
                if (reads.ReadsUnknownKeys || reads.Keys.Contains(key)) return null;
                readsAxes |= reads.ReadsUnknownAxes || reads.Axes.Count > 0;
                axes.UnionWith(reads.Axes);
            }

            if (!readsAxes && axes.Count == 0) return null;

            var named = axes.Count > 0
                ? "(" + string.Join(", ", axes) + ")"
                : "whose names the game computes at run time";
            return "The game never reads " + key + " through Input.GetKey; it reads Input Manager axes " + named +
                   ". Key commands do not reach axes. Send set_button [\"<axis>\", true] or set_axis [\"<axis>\", <value>] instead.";
        }

        /// <summary>
        /// `list_inputs` 의 키와 축 부분을 채운다. 키는 빌드 기록만, 축 이름은 빌드 기록에 런 중 본 이름을 더한다.
        /// </summary>
        internal static void Describe(InputListDto into)
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            var axes = new SortedSet<string>(SeenAxes, StringComparer.Ordinal);
            foreach (var reads in Declared.Values)
            {
                foreach (var key in reads.Keys) keys.Add(key.ToString());
                axes.UnionWith(reads.Axes);
                into.OtherKeys |= reads.ReadsUnknownKeys;
            }

            into.Keys = new List<string>(keys);
            into.Axes = new List<string>(axes);
        }

        /// <summary>테스트가 위빙된 assembly 의 기록과 섞이지 않게 비운다.</summary>
        internal static void ClearForTests()
        {
            Declared.Clear();
            SeenAxes.Clear();
        }

        private static IEnumerable<string> Split(string joined, char separator)
        {
            return string.IsNullOrEmpty(joined)
                ? Enumerable.Empty<string>()
                : joined.Split(separator).Where(part => part.Length > 0);
        }

        private sealed class AssemblyReads
        {
            internal readonly HashSet<KeyCode> Keys = new HashSet<KeyCode>();
            internal readonly HashSet<string> Axes = new HashSet<string>(StringComparer.Ordinal);
            internal bool ReadsUnknownKeys;
            internal bool ReadsUnknownAxes;
        }
    }
}
