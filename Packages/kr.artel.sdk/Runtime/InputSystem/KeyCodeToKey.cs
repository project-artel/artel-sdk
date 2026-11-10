using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
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
