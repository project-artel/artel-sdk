using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace Artel.Affordances.Scan
{
    /// <summary>One method the inspector was told to call, and what it hangs off.</summary>
    internal struct PersistentCall
    {
        internal string Event;
        internal string TargetType;
        internal string TargetPath;
        internal string Method;
    }

    /// <summary>
    /// Reads the wiring a designer did in the inspector.
    /// </summary>
    /// <remarks>
    /// This is the half the compiled code cannot know. A button handler's body says what happens;
    /// nothing in it says which button, or that there is a button at all. The link lives in the
    /// scene as a serialised persistent call, and joining the two is the whole point of scanning at
    /// runtime rather than reading the assembly alone.
    ///
    /// Only persistent calls are visible. A listener added with <c>AddListener</c> in code is not
    /// serialised and cannot be counted, let alone named — that gap is reported rather than hidden.
    /// </remarks>
    internal static class PersistentCallReader
    {
        private const BindingFlags Fields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>How far up a component's base types to look for event fields.</summary>
        private const int MaxDepth = 16;

        private static readonly Dictionary<Type, FieldInfo[]> Known = new Dictionary<Type, FieldInfo[]>();

        internal static void Read(Component component, List<PersistentCall> into)
        {
            foreach (var field in EventFieldsOf(component.GetType()))
            {
                UnityEventBase wiring;

                try
                {
                    wiring = field.GetValue(component) as UnityEventBase;
                }
                catch (Exception)
                {
                    continue;
                }

                if (wiring == null)
                {
                    continue;
                }

                Read(field.Name, wiring, into);
            }
        }

        private static void Read(string name, UnityEventBase wiring, List<PersistentCall> into)
        {
            int count;

            try
            {
                count = wiring.GetPersistentEventCount();
            }
            catch (Exception)
            {
                return;
            }

            for (var index = 0; index < count; index++)
            {
                try
                {
                    var target = wiring.GetPersistentTarget(index);

                    into.Add(new PersistentCall
                    {
                        Event = name,
                        TargetType = target == null ? null : target.GetType().FullName,
                        TargetPath = target is Component component ? ScenePath.Of(component.transform) : null,
                        Method = wiring.GetPersistentMethodName(index)
                    });
                }
                catch (Exception)
                {
                    // One unreadable entry, not a reason to abandon the rest of the event.
                }
            }
        }

        /// <summary>
        /// The event-shaped fields on a type, including the private ones the engine declares.
        /// </summary>
        /// <remarks>
        /// A Button exposes <c>onClick</c> as a property, and the serialised state behind it is a
        /// private field of the class that declares it. Reflection has to be told to look at private
        /// members and has to walk the base types itself, since asking a derived type for
        /// non-public members does not reach the ones its parents declare.
        /// </remarks>
        private static FieldInfo[] EventFieldsOf(Type type)
        {
            if (Known.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var found = new List<FieldInfo>();
            var current = type;

            for (var depth = 0; depth < MaxDepth && current != null; depth++)
            {
                try
                {
                    foreach (var field in current.GetFields(Fields))
                    {
                        if (typeof(UnityEventBase).IsAssignableFrom(field.FieldType))
                        {
                            found.Add(field);
                        }
                    }
                }
                catch (Exception)
                {
                    break;
                }

                current = current.BaseType;
            }

            var fields = found.ToArray();
            Known[type] = fields;
            return fields;
        }

        internal static void Forget()
        {
            Known.Clear();
        }
    }
}
