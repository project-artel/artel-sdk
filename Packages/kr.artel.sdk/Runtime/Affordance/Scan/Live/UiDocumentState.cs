using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Artel.Affordances.Scan;
using UnityEngine;

namespace Artel.Affordances.Live
{
    /// <summary>
    /// UI Toolkit 화면이 지금 무엇을 보이는지. <c>UIDocument</c> 의 root 아래 이름이나 글자가 있는 element 들.
    /// </summary>
    /// <remarks>
    /// uGUI 는 element 마다 GameObject 가 있어 씬 걷기가 만나지만, UI Toolkit 은 GameObject 하나(<c>UIDocument</c>) 아래 트리가
    /// 엔진 안에 있다. 그래서 그 객체의 항목에 트리를 요약해 <c>uitk</c> 하나로 싣는다. <see cref="Legible"/> 과 같이 이름으로
    /// 맞추고 리플렉션으로 읽어 UIElements 모듈을 참조하지 않는다 — 그 모듈이 없는 프로젝트도 이 패키지를 쓴다.
    ///
    /// 이름이나 글자가 있는 element 만, 깊이 8, 120 개까지. 레이아웃 컨테이너는 QA 가 묻는 것이 아니다.
    /// </remarks>
    internal static class UiDocumentState
    {
        private const string DocumentType = "UnityEngine.UIElements.UIDocument";
        internal const int MaxDepth = 8;
        internal const int MaxElements = 120;

        private sealed class Readers
        {
            internal PropertyInfo Root;
        }

        private sealed class ElementReaders
        {
            internal PropertyInfo Name;
            internal PropertyInfo ChildCount;
            internal PropertyInfo Item;
            internal PropertyInfo EnabledSelf;
            internal PropertyInfo Visible;
            internal PropertyInfo Text;
        }

        private static readonly Dictionary<Type, Readers> Documents = new Dictionary<Type, Readers>();
        private static readonly Dictionary<Type, ElementReaders> Elements = new Dictionary<Type, ElementReaders>();
        private static readonly List<Component> Holding = new List<Component>();

        internal static bool CanCarry(GameObject subject)
        {
            return DocumentOn(subject) != null;
        }

        /// <summary><c>,"uitk":[...]</c> 조각, 또는 이 객체에 UI Toolkit 문서가 없으면 null.</summary>
        internal static string Of(GameObject subject)
        {
            var document = DocumentOn(subject);

            if (document == null)
            {
                return null;
            }

            object root;

            try
            {
                root = ReadersFor(document.GetType()).Root?.GetValue(document);
            }
            catch (Exception)
            {
                return null;
            }

            if (root == null)
            {
                return null;
            }

            var text = new StringBuilder(256);
            text.Append(",\"uitk\":[");
            var written = 0;
            Walk(root, 0, text, ref written);
            text.Append(']');
            return text.ToString();
        }

        private static void Walk(object element, int depth, StringBuilder text, ref int written)
        {
            if (element == null || depth > MaxDepth || written >= MaxElements)
            {
                return;
            }

            var readers = ElementReadersFor(element.GetType());
            var name = Read(readers.Name, element) as string;
            var said = Read(readers.Text, element) as string;

            if (!string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(said))
            {
                if (written > 0)
                {
                    text.Append(',');
                }

                text.Append('{');
                Json.Property(text, "name", string.IsNullOrEmpty(name) ? null : name);
                text.Append(',');
                Json.Property(text, "type", element.GetType().Name);

                if (!string.IsNullOrEmpty(said))
                {
                    text.Append(',');
                    Json.Property(text, "text", said.Length <= 200 ? said : said.Substring(0, 200) + "…");
                }

                text.Append(",\"enabled\":").Append(Read(readers.EnabledSelf, element) is bool enabled && !enabled ? "false" : "true");
                text.Append(",\"visible\":").Append(Read(readers.Visible, element) is bool visible && !visible ? "false" : "true");
                text.Append('}');
                written++;
            }

            if (!(Read(readers.ChildCount, element) is int count) || readers.Item == null)
            {
                return;
            }

            for (var at = 0; at < count && written < MaxElements; at++)
            {
                object child;

                try
                {
                    child = readers.Item.GetValue(element, new object[] { at });
                }
                catch (Exception)
                {
                    continue;
                }

                Walk(child, depth + 1, text, ref written);
            }
        }

        private static object Read(PropertyInfo property, object on)
        {
            if (property == null)
            {
                return null;
            }

            try
            {
                return property.GetValue(on);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Component DocumentOn(GameObject subject)
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

            Component found = null;

            foreach (var component in Holding)
            {
                if (component != null && component.GetType().FullName == DocumentType)
                {
                    found = component;
                    break;
                }
            }

            Holding.Clear();
            return found;
        }

        private static Readers ReadersFor(Type type)
        {
            if (Documents.TryGetValue(type, out var known))
            {
                return known;
            }

            var made = new Readers { Root = Property(type, "rootVisualElement") };
            Documents[type] = made;
            return made;
        }

        private static ElementReaders ElementReadersFor(Type type)
        {
            if (Elements.TryGetValue(type, out var known))
            {
                return known;
            }

            var made = new ElementReaders
            {
                Name = Property(type, "name"),
                ChildCount = Property(type, "childCount"),
                Item = Indexer(type),
                EnabledSelf = Property(type, "enabledSelf"),
                Visible = Property(type, "visible"),
                Text = Property(type, "text")
            };

            Elements[type] = made;
            return made;
        }

        private static PropertyInfo Property(Type type, string name)
        {
            try
            {
                var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                return property != null && property.CanRead && property.GetIndexParameters().Length == 0 ? property : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static PropertyInfo Indexer(Type type)
        {
            try
            {
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var parameters = property.GetIndexParameters();

                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(int) && property.CanRead)
                    {
                        return property;
                    }
                }
            }
            catch (Exception)
            {
                // 아래에서 null.
            }

            return null;
        }

        internal static void Forget()
        {
            Documents.Clear();
            Elements.Clear();
        }
    }
}
