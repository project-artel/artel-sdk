using System;
using global::UnityEngine;

namespace Artel.NativeInput
{
    internal static class OsKeyCodes
    {
        internal static ushort Windows(KeyCode key)
        {
            if (key >= KeyCode.A && key <= KeyCode.Z) return (ushort)(0x41 + key - KeyCode.A);
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return (ushort)(0x30 + key - KeyCode.Alpha0);
            if (key >= KeyCode.F1 && key <= KeyCode.F15) return (ushort)(0x70 + key - KeyCode.F1);
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) return (ushort)(0x60 + key - KeyCode.Keypad0);
            switch (key)
            {
                case KeyCode.Backspace: return 0x08;
                case KeyCode.Tab: return 0x09;
                case KeyCode.Clear: return 0x0c;
                case KeyCode.Return: case KeyCode.KeypadEnter: return 0x0d;
                case KeyCode.Pause: return 0x13;
                case KeyCode.CapsLock: return 0x14;
                case KeyCode.Escape: return 0x1b;
                case KeyCode.Space: return 0x20;
                case KeyCode.PageUp: return 0x21;
                case KeyCode.PageDown: return 0x22;
                case KeyCode.End: return 0x23;
                case KeyCode.Home: return 0x24;
                case KeyCode.LeftArrow: return 0x25;
                case KeyCode.UpArrow: return 0x26;
                case KeyCode.RightArrow: return 0x27;
                case KeyCode.DownArrow: return 0x28;
                case KeyCode.Print: return 0x2c;
                case KeyCode.Insert: return 0x2d;
                case KeyCode.Delete: return 0x2e;
                case KeyCode.LeftCommand: case KeyCode.LeftWindows: return 0x5b;
                case KeyCode.RightCommand: case KeyCode.RightWindows: return 0x5c;
                case KeyCode.Menu: return 0x5d;
                case KeyCode.KeypadMultiply: return 0x6a;
                case KeyCode.KeypadPlus: return 0x6b;
                case KeyCode.KeypadMinus: return 0x6d;
                case KeyCode.KeypadPeriod: return 0x6e;
                case KeyCode.KeypadDivide: return 0x6f;
                case KeyCode.Numlock: return 0x90;
                case KeyCode.ScrollLock: return 0x91;
                case KeyCode.LeftShift: return 0xa0;
                case KeyCode.RightShift: return 0xa1;
                case KeyCode.LeftControl: return 0xa2;
                case KeyCode.RightControl: return 0xa3;
                case KeyCode.LeftAlt: return 0xa4;
                case KeyCode.RightAlt: case KeyCode.AltGr: return 0xa5;
                case KeyCode.Semicolon: return 0xba;
                case KeyCode.Equals: return 0xbb;
                case KeyCode.Comma: return 0xbc;
                case KeyCode.Minus: return 0xbd;
                case KeyCode.Period: return 0xbe;
                case KeyCode.Slash: return 0xbf;
                case KeyCode.BackQuote: return 0xc0;
                case KeyCode.LeftBracket: return 0xdb;
                case KeyCode.Backslash: return 0xdc;
                case KeyCode.RightBracket: return 0xdd;
                case KeyCode.Quote: return 0xde;
                default: throw Unsupported(key);
            }
        }

        internal static ushort Mac(KeyCode key)
        {
            // macOS virtual key는 문자 코드가 아니라 ANSI 키 위치다.
            switch (key)
            {
                case KeyCode.A: return 0;
                case KeyCode.S: return 1;
                case KeyCode.D: return 2;
                case KeyCode.F: return 3;
                case KeyCode.H: return 4;
                case KeyCode.G: return 5;
                case KeyCode.Z: return 6;
                case KeyCode.X: return 7;
                case KeyCode.C: return 8;
                case KeyCode.V: return 9;
                case KeyCode.B: return 11;
                case KeyCode.Q: return 12;
                case KeyCode.W: return 13;
                case KeyCode.E: return 14;
                case KeyCode.R: return 15;
                case KeyCode.Y: return 16;
                case KeyCode.T: return 17;
                case KeyCode.Alpha1: return 18;
                case KeyCode.Alpha2: return 19;
                case KeyCode.Alpha3: return 20;
                case KeyCode.Alpha4: return 21;
                case KeyCode.Alpha6: return 22;
                case KeyCode.Alpha5: return 23;
                case KeyCode.Equals: return 24;
                case KeyCode.Alpha9: return 25;
                case KeyCode.Alpha7: return 26;
                case KeyCode.Minus: return 27;
                case KeyCode.Alpha8: return 28;
                case KeyCode.Alpha0: return 29;
                case KeyCode.RightBracket: return 30;
                case KeyCode.O: return 31;
                case KeyCode.U: return 32;
                case KeyCode.LeftBracket: return 33;
                case KeyCode.I: return 34;
                case KeyCode.P: return 35;
                case KeyCode.Return: return 36;
                case KeyCode.L: return 37;
                case KeyCode.J: return 38;
                case KeyCode.Quote: return 39;
                case KeyCode.K: return 40;
                case KeyCode.Semicolon: return 41;
                case KeyCode.Backslash: return 42;
                case KeyCode.Comma: return 43;
                case KeyCode.Slash: return 44;
                case KeyCode.N: return 45;
                case KeyCode.M: return 46;
                case KeyCode.Period: return 47;
                case KeyCode.Tab: return 48;
                case KeyCode.Space: return 49;
                case KeyCode.BackQuote: return 50;
                case KeyCode.Backspace: return 51;
                case KeyCode.Escape: return 53;
                case KeyCode.RightCommand: case KeyCode.RightWindows: return 54;
                case KeyCode.LeftCommand: case KeyCode.LeftWindows: return 55;
                case KeyCode.LeftShift: return 56;
                case KeyCode.CapsLock: return 57;
                case KeyCode.LeftAlt: return 58;
                case KeyCode.LeftControl: return 59;
                case KeyCode.RightShift: return 60;
                case KeyCode.RightAlt: case KeyCode.AltGr: return 61;
                case KeyCode.RightControl: return 62;
                case KeyCode.F5: return 96;
                case KeyCode.F6: return 97;
                case KeyCode.F7: return 98;
                case KeyCode.F3: return 99;
                case KeyCode.F8: return 100;
                case KeyCode.F9: return 101;
                case KeyCode.F11: return 103;
                case KeyCode.F13: return 105;
                case KeyCode.F14: return 107;
                case KeyCode.F10: return 109;
                case KeyCode.F12: return 111;
                case KeyCode.F15: return 113;
                case KeyCode.Help: case KeyCode.Insert: return 114;
                case KeyCode.Home: return 115;
                case KeyCode.PageUp: return 116;
                case KeyCode.Delete: return 117;
                case KeyCode.F4: return 118;
                case KeyCode.End: return 119;
                case KeyCode.F2: return 120;
                case KeyCode.PageDown: return 121;
                case KeyCode.F1: return 122;
                case KeyCode.LeftArrow: return 123;
                case KeyCode.RightArrow: return 124;
                case KeyCode.DownArrow: return 125;
                case KeyCode.UpArrow: return 126;
                case KeyCode.KeypadPeriod: return 65;
                case KeyCode.KeypadMultiply: return 67;
                case KeyCode.KeypadPlus: return 69;
                case KeyCode.Clear: case KeyCode.Numlock: return 71;
                case KeyCode.KeypadDivide: return 75;
                case KeyCode.KeypadEnter: return 76;
                case KeyCode.KeypadMinus: return 78;
                case KeyCode.KeypadEquals: return 81;
                case KeyCode.Keypad0: return 82;
                case KeyCode.Keypad1: return 83;
                case KeyCode.Keypad2: return 84;
                case KeyCode.Keypad3: return 85;
                case KeyCode.Keypad4: return 86;
                case KeyCode.Keypad5: return 87;
                case KeyCode.Keypad6: return 88;
                case KeyCode.Keypad7: return 89;
                case KeyCode.Keypad8: return 91;
                case KeyCode.Keypad9: return 92;
                default: throw Unsupported(key);
            }
        }

        internal static UIntPtr X11(KeyCode key)
        {
            if (key >= KeyCode.A && key <= KeyCode.Z) return new UIntPtr((uint)(0x61 + key - KeyCode.A));
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return new UIntPtr((uint)(0x30 + key - KeyCode.Alpha0));
            if (key >= KeyCode.F1 && key <= KeyCode.F15) return new UIntPtr((uint)(0xffbe + key - KeyCode.F1));
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) return new UIntPtr((uint)(0xffb0 + key - KeyCode.Keypad0));
            uint symbol;
            switch (key)
            {
                case KeyCode.Backspace: symbol = 0xff08; break;
                case KeyCode.Tab: symbol = 0xff09; break;
                case KeyCode.Clear: symbol = 0xff0b; break;
                case KeyCode.Return: symbol = 0xff0d; break;
                case KeyCode.Pause: symbol = 0xff13; break;
                case KeyCode.ScrollLock: symbol = 0xff14; break;
                case KeyCode.Escape: symbol = 0xff1b; break;
                case KeyCode.Home: symbol = 0xff50; break;
                case KeyCode.LeftArrow: symbol = 0xff51; break;
                case KeyCode.UpArrow: symbol = 0xff52; break;
                case KeyCode.RightArrow: symbol = 0xff53; break;
                case KeyCode.DownArrow: symbol = 0xff54; break;
                case KeyCode.PageUp: symbol = 0xff55; break;
                case KeyCode.PageDown: symbol = 0xff56; break;
                case KeyCode.End: symbol = 0xff57; break;
                case KeyCode.Print: symbol = 0xff61; break;
                case KeyCode.Insert: symbol = 0xff63; break;
                case KeyCode.Menu: symbol = 0xff67; break;
                case KeyCode.Numlock: symbol = 0xff7f; break;
                case KeyCode.KeypadEnter: symbol = 0xff8d; break;
                case KeyCode.KeypadMultiply: symbol = 0xffaa; break;
                case KeyCode.KeypadPlus: symbol = 0xffab; break;
                case KeyCode.KeypadMinus: symbol = 0xffad; break;
                case KeyCode.KeypadPeriod: symbol = 0xffae; break;
                case KeyCode.KeypadDivide: symbol = 0xffaf; break;
                case KeyCode.KeypadEquals: symbol = 0xffbd; break;
                case KeyCode.LeftShift: symbol = 0xffe1; break;
                case KeyCode.RightShift: symbol = 0xffe2; break;
                case KeyCode.LeftControl: symbol = 0xffe3; break;
                case KeyCode.RightControl: symbol = 0xffe4; break;
                case KeyCode.CapsLock: symbol = 0xffe5; break;
                case KeyCode.LeftAlt: symbol = 0xffe9; break;
                case KeyCode.RightAlt: symbol = 0xffea; break;
                case KeyCode.AltGr: symbol = 0xfe03; break;
                case KeyCode.LeftCommand: case KeyCode.LeftWindows: symbol = 0xffeb; break;
                case KeyCode.RightCommand: case KeyCode.RightWindows: symbol = 0xffec; break;
                case KeyCode.Delete: symbol = 0xffff; break;
                case KeyCode.Space: case KeyCode.Quote: case KeyCode.Comma: case KeyCode.Minus:
                case KeyCode.Period: case KeyCode.Slash: case KeyCode.Semicolon: case KeyCode.Equals:
                case KeyCode.LeftBracket: case KeyCode.Backslash: case KeyCode.RightBracket: case KeyCode.BackQuote:
                    symbol = (uint)key; break;
                default: throw Unsupported(key);
            }
            return new UIntPtr(symbol);
        }

        private static InvalidOperationException Unsupported(KeyCode key)
        {
            return new InvalidOperationException("OS keyboard input does not support KeyCode: " + key);
        }
    }
}
