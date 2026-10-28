using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Artel.Affordances.Scan;
using UnityEngine;

namespace Artel.Affordances.Live
{
    /// <summary>
    /// uGUI 컴포넌트가 지금 어떤 상태인지. 눌리는지, 켜져 있는지, 어디까지 찼는지.
    /// </summary>
    /// <remarks>
    /// <see cref="Legible"/> 과 같은 틀이다. 타입은 이름으로 맞추고 값은 리플렉션으로 읽어, 이 어셈블리가 uGUI 나 TMP 를
    /// 참조하지 않는다. 글자가 화면이 말하는 것이라면 이것은 화면이 받아 주는 것이다 — 흐려진 버튼, 꺼진 토글, 반쯤 찬
    /// 게이지는 테스트 케이스가 묻는 것인데 어느 멤버에도 없다.
    ///
    /// 객체 항목에 <c>ui</c> 하나로 싣고 장부가 통째로 비교한다. 연속값(slider·fillAmount·alpha)은 셋째 자리에서 끊는다.
    /// </remarks>
    internal static class UiState
    {
        private sealed class Reading
        {
            internal string Family;
            internal string Property;
            internal string Key;
        }

        private static readonly Reading[] Readings =
        {
            new Reading { Family = "UnityEngine.UI.Selectable", Property = "interactable", Key = "interactable" },
            new Reading { Family = "UnityEngine.UI.Toggle", Property = "isOn", Key = "isOn" },
            new Reading { Family = "UnityEngine.UI.Slider", Property = "value", Key = "value" },
            new Reading { Family = "UnityEngine.UI.Scrollbar", Property = "value", Key = "value" },
            new Reading { Family = "UnityEngine.UI.Dropdown", Property = "value", Key = "value" },
            new Reading { Family = "TMPro.TMP_Dropdown", Property = "value", Key = "value" },
            new Reading { Family = "UnityEngine.UI.Image", Property = "fillAmount", Key = "fill" },
            new Reading { Family = "UnityEngine.UI.Image", Property = "sprite", Key = "sprite" },
            new Reading { Family = "UnityEngine.CanvasGroup", Property = "alpha", Key = "alpha" },
            new Reading { Family = "UnityEngine.CanvasGroup", Property = "interactable", Key = "groupInteractable" },
            new Reading { Family = "UnityEngine.CanvasGroup", Property = "blocksRaycasts", Key = "blocksRaycasts" }
        };

        /// <summary>이것을 가진 객체는 입력을 받는 객체다. <see cref="Worth"/> 가 상한 안에서 넣는다.</summary>
        private const string Pressable = "UnityEngine.UI.Selectable";

        private const int MaxRemembered = 2048;

        private static readonly Dictionary<Type, List<KeyValuePair<Reading, PropertyInfo>>> Answered =
            new Dictionary<Type, List<KeyValuePair<Reading, PropertyInfo>>>();

        private static readonly Dictionary<Type, bool> PressableAnswered = new Dictionary<Type, bool>();

        private static readonly List<Component> Holding = new List<Component>();

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        internal static bool CanCarry(GameObject subject)
        {
            if (subject == null)
            {
                return false;
            }

            try
            {
                subject.GetComponents(Holding);
            }
            catch (Exception)
            {
                Holding.Clear();
                return false;
            }

            var answer = false;

            foreach (var component in Holding)
            {
                if (component != null && IsPressable(component.GetType()))
                {
                    answer = true;
                    break;
                }
            }

            Holding.Clear();
            return answer;
        }

        /// <summary><c>,"ui":{...}</c> 조각, 또는 이 객체에 uGUI 상태가 없으면 null.</summary>
        internal static string Of(GameObject subject)
        {
            if (subject == null)
            {
                return null;
            }

            try
            {
                subject.GetComponents(Holding);
            }
            catch (Exception)
            {
                Holding.Clear();
                return null;
            }

            StringBuilder text = null;

            foreach (var component in Holding)
            {
                if (component == null)
                {
                    continue;
                }

                foreach (var pair in ReadersFor(component.GetType()))
                {
                    object held;

                    try
                    {
                        held = pair.Value.GetValue(component);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (text == null)
                    {
                        text = new StringBuilder(96);
                        text.Append(",\"ui\":{");
                    }
                    else
                    {
                        text.Append(',');
                    }

                    Json.String(text, pair.Key.Key);
                    text.Append(':');
                    Value(text, held);
                }
            }

            Holding.Clear();

            if (text == null)
            {
                return null;
            }

            text.Append('}');
            return text.ToString();
        }

        internal static void Value(StringBuilder text, object held)
        {
            switch (held)
            {
                case null:
                    text.Append("null");
                    return;

                case bool flag:
                    text.Append(flag ? "true" : "false");
                    return;

                case int number:
                    text.Append(number.ToString(Invariant));
                    return;

                case float number:
                    text.Append(Math.Round(number, 3).ToString("0.###", Invariant));
                    return;

                case double number:
                    text.Append(Math.Round(number, 3).ToString("0.###", Invariant));
                    return;

                case string words:
                    Json.String(text, words);
                    return;

                case Enum member:
                    Json.String(text, member.ToString());
                    return;

                case UnityEngine.Object reference:
                    Json.String(text, reference == null ? null : reference.name);
                    return;
            }

            Json.String(text, held.ToString());
        }

        internal static void Forget()
        {
            Answered.Clear();
            PressableAnswered.Clear();
        }

        private static bool IsPressable(Type type)
        {
            if (PressableAnswered.TryGetValue(type, out var already))
            {
                return already;
            }

            if (PressableAnswered.Count >= MaxRemembered)
            {
                PressableAnswered.Clear();
            }

            var answer = InFamily(type, Pressable);
            PressableAnswered[type] = answer;
            return answer;
        }

        private static List<KeyValuePair<Reading, PropertyInfo>> ReadersFor(Type type)
        {
            if (Answered.TryGetValue(type, out var already))
            {
                return already;
            }

            if (Answered.Count >= MaxRemembered)
            {
                Answered.Clear();
            }

            var readers = new List<KeyValuePair<Reading, PropertyInfo>>();

            foreach (var reading in Readings)
            {
                if (!InFamily(type, reading.Family))
                {
                    continue;
                }

                PropertyInfo property;

                try
                {
                    property = type.GetProperty(reading.Property, BindingFlags.Public | BindingFlags.Instance);
                }
                catch (Exception)
                {
                    continue;
                }

                if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
                {
                    readers.Add(new KeyValuePair<Reading, PropertyInfo>(reading, property));
                }
            }

            Answered[type] = readers;
            return readers;
        }

        internal static bool InFamily(Type type, string family)
        {
            for (var walking = type; walking != null; walking = walking.BaseType)
            {
                if (walking.FullName == family)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
