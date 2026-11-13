using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using KeyCode = UnityEngine.KeyCode;

namespace Artel
{
    // 프로토콜의 키 이름은 KeyCode 다. Input System 장치에 넣을 때만 Key 로 옮긴다.
    internal static class KeyCodeToKey
    {
        // 이름이 다른 것만 적는다. 대소문자만 다른 이름(Numlock, BackQuote)과 Key 에도 있는 별칭(LeftCommand, AltGr)은
        // Enum.TryParse 가 찾는다. KeyCode 는 별칭끼리 값이 같아서 값이 아니라 이름으로 적는다.
        private static readonly Dictionary<string, string> RenamedKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Return", "Enter" },
            { "Alpha0", "Digit0" }, { "Alpha1", "Digit1" }, { "Alpha2", "Digit2" }, { "Alpha3", "Digit3" },
            { "Alpha4", "Digit4" }, { "Alpha5", "Digit5" }, { "Alpha6", "Digit6" }, { "Alpha7", "Digit7" },
            { "Alpha8", "Digit8" }, { "Alpha9", "Digit9" },
            { "Keypad0", "Numpad0" }, { "Keypad1", "Numpad1" }, { "Keypad2", "Numpad2" }, { "Keypad3", "Numpad3" },
            { "Keypad4", "Numpad4" }, { "Keypad5", "Numpad5" }, { "Keypad6", "Numpad6" }, { "Keypad7", "Numpad7" },
            { "Keypad8", "Numpad8" }, { "Keypad9", "Numpad9" },
            { "KeypadPeriod", "NumpadPeriod" }, { "KeypadDivide", "NumpadDivide" },
            { "KeypadMultiply", "NumpadMultiply" }, { "KeypadMinus", "NumpadMinus" }, { "KeypadPlus", "NumpadPlus" },
            { "KeypadEnter", "NumpadEnter" }, { "KeypadEquals", "NumpadEquals" },
            { "LeftControl", "LeftCtrl" }, { "RightControl", "RightCtrl" },
            { "Print", "PrintScreen" }, { "Menu", "ContextMenu" }
        };

        // 이름이 Key 에도 있지만 같은 키가 아닌 것. Key.None 은 키가 아니다.
        private static readonly HashSet<string> NotKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "None" };

        // `<Keyboard>/shift`·`ctrl`·`alt` 는 양쪽 키를 묶은 synthetic control 이라 `KeyControl` 도 `Key` 도 아니다. 왼쪽 키 하나만
        // 눌러도 켜지므로 왼쪽 키로 읽는다. binding 창이 "Shift [Keyboard]" 로 고르는 기본 경로다.
        private static readonly Dictionary<string, Key> EitherSideKeys = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase)
        {
            { "shift", Key.LeftShift }, { "ctrl", Key.LeftCtrl }, { "alt", Key.LeftAlt }
        };

        private static Dictionary<Key, string> keyCodeNames;

        /// <summary>
        /// Input System 키보드 binding 경로(`<Keyboard>/upArrow`)가 가리키는 키의 KeyCode 이름. 키보드 키가 아니면 false 다.
        /// </summary>
        /// <remarks>
        /// `keyboard` 는 SDK 가 키를 넣는 가상 장치다. 그 장치에서 control 을 찾아 `KeyControl.keyCode` 를 읽으므로, 배열에 따라
        /// 바뀌는 경로(`#(a)`)도 SDK 가 누를 키로 풀린다. 사람의 키보드로 풀면 배열(AZERTY 등)에 따라 다른 키가 나와, 그 키를
        /// 눌러도 action 이 켜지지 않는다. 가상 장치가 아직 없으면 경로 끝 이름을 `Key` 로 읽고, `#(a)` 같은 경로는 빠진다. 숫자
        /// 키의 control 이름은 `1` 이라 `Digit1` 로 읽는다.
        /// </remarks>
        internal static bool TryKeyCodeNameOfPath(string path, Keyboard keyboard, out string keyCodeName)
        {
            keyCodeName = null;
            if (string.IsNullOrEmpty(path) || !path.StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase))
                return false;
            var key = Key.None;
            if (keyboard != null && InputControlPath.TryFindControl(keyboard, path) is KeyControl control)
            {
                key = control.keyCode;
            }
            else
            {
                var name = path.Substring(path.LastIndexOf('/') + 1);
                if (name.Length == 1 && char.IsDigit(name[0])) name = "Digit" + name;
                if (!EitherSideKeys.TryGetValue(name, out key) && !Enum.TryParse(name, true, out key)) return false;
            }
            return KeyCodeNames().TryGetValue(key, out keyCodeName);
        }

        // Key → 그 Key 로 옮겨지는 첫 KeyCode 의 이름. Enter 는 Return, Digit1 은 Alpha1 이 된다.
        private static Dictionary<Key, string> KeyCodeNames()
        {
            if (keyCodeNames != null) return keyCodeNames;
            var names = new Dictionary<Key, string>();
            foreach (KeyCode keyCode in Enum.GetValues(typeof(KeyCode)))
                if (TryConvert(keyCode, out var key) && !names.ContainsKey(key))
                    names[key] = keyCode.ToString();
            return keyCodeNames = names;
        }

        internal static bool TryConvert(KeyCode keyCode, out Key key)
        {
            key = Key.None;
            if (MouseButtonKeyCode.TryGetButton(keyCode, out _)) return false;
            var name = keyCode.ToString();
            return TryConvert(name, out key);
        }

        internal static bool TryConvert(string keyCodeName, out Key key)
        {
            key = Key.None;
            if (string.IsNullOrEmpty(keyCodeName) || NotKeys.Contains(keyCodeName)) return false;
            if (RenamedKeys.TryGetValue(keyCodeName, out var renamed)) keyCodeName = renamed;
            // 숫자 문자열은 Enum.TryParse 가 값으로 받아들인다. 이름만 받는다.
            if (keyCodeName.Length > 0 && char.IsDigit(keyCodeName[0])) return false;
            return Enum.TryParse(keyCodeName, true, out key) && key != Key.None && Enum.IsDefined(typeof(Key), key);
        }
    }
}
