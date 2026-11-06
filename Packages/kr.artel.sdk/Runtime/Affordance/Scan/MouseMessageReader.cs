using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// collider 가 붙은 객체 위의 <c>OnMouse*</c> handler 를 그 객체를 누르는 <c>calls</c> 항목으로 적는다(ARTEL-1227).
    /// </summary>
    /// <remarks>
    /// Unity 는 <c>OnMouseDown</c>, <c>OnMouseUp</c>, <c>OnMouseUpAsButton</c>, <c>OnMouseDrag</c> 를 마우스가 그
    /// 객체의 <c>Collider</c> 나 <c>Collider2D</c> 위에 있을 때만 부른다. 그러니 이 handler 는 그 객체를 누르는
    /// 조작이고, 버튼의 <c>onClick</c> 과 같은 자리에 둘 수 있다. 예전에는 handler 의 evidence 만 있고 누를 자리가
    /// 없어 interaction 이 <c>none</c> 이 됐다 — WordVenture 의 카드 drag 와 적 클릭이 그래서 TC step 이 되지 못했다.
    ///
    /// <c>calls</c> 항목 하나로 적는다. <c>event</c> 는 메시지 이름이고 <c>targetType</c> 은 컴포넌트의 runtime type,
    /// <c>targetPath</c> 는 그 객체 자신의 경로다. 문서 모양은 그대로이고, 소비자는 버튼의 persistent call 과 같은 방법으로 이것을
    /// handler 의 evidence 에 잇는다. <c>OnMouseEnter</c> 와 <c>OnMouseOver</c> 처럼 누르지 않아도 불리는 메시지는
    /// 적지 않는다.
    /// </remarks>
    internal static class MouseMessageReader
    {
        internal static readonly string[] Messages =
        {
            "OnMouseDown", "OnMouseUp", "OnMouseUpAsButton", "OnMouseDrag"
        };

        private const BindingFlags Declared =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly Dictionary<Type, string[]> Known = new Dictionary<Type, string[]>();

        internal static void Read(Component component, List<PersistentCall> into)
        {
            if (!(component is MonoBehaviour))
            {
                return;
            }

            var handled = HandledBy(component.GetType());

            if (handled.Length == 0 || !HasCollider(component.gameObject))
            {
                return;
            }

            var path = ScenePath.Of(component.transform);
            var type = component.GetType().FullName;

            foreach (var message in handled)
            {
                into.Add(new PersistentCall { Event = message, TargetType = type, TargetPath = path, Method = message });
            }
        }

        /// <summary>이 type 이 받는 마우스 메시지. 부모 class 에 선언된 것도 Unity 가 부르므로 함께 센다.</summary>
        internal static string[] HandledBy(Type type)
        {
            if (Known.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var found = new List<string>();

            foreach (var message in Messages)
            {
                for (var at = type; at != null && at != typeof(MonoBehaviour); at = at.BaseType)
                {
                    if (at.GetMethod(message, Declared, null, Type.EmptyTypes, null) != null)
                    {
                        found.Add(message);
                        break;
                    }
                }
            }

            var answer = found.ToArray();
            Known[type] = answer;
            return answer;
        }

        /// <summary>
        /// 객체에 3D 또는 2D collider 가 있는가.
        /// </summary>
        /// <remarks>
        /// type 이름으로 본다. 이 assembly 가 physics module 에 기대지 않게 하려는 것이다. 꺼진 collider 도 센다 — scan 은
        /// 꺼진 객체도 읽고, 게임이 그것을 켤 때 함께 켜는 경우가 흔하다.
        /// </remarks>
        internal static bool HasCollider(GameObject subject)
        {
            foreach (var component in subject.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                for (var at = component.GetType(); at != null; at = at.BaseType)
                {
                    if (at.FullName == "UnityEngine.Collider" || at.FullName == "UnityEngine.Collider2D")
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
