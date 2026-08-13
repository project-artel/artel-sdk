using System;
using System.Collections.Generic;
using Artel.Affordances.Scan;
using UnityEngine;

namespace Artel.Affordances.Live
{
    /// <summary>
    /// Whether an object is one a test could act on — the report's rule, asked ten times a second.
    /// </summary>
    /// <remarks>
    /// The rule belongs to the scan and is copied here rather than reinvented: an object counts when
    /// one of its components carries baked evidence, or has an inspector-wired call. That is what
    /// makes the report's list forty-three objects instead of a thousand, and it is what puts
    /// <c>Canvas/ExitButton</c> in it — a Button whose <c>onClick</c> points at a method.
    ///
    /// It has to be the same rule. The specification was written from the report's own walk, so a
    /// reading that draws the line anywhere else reports rows as uncheckable that the package could
    /// answer, or fills every reading with scenery. There is one place the two are allowed to
    /// differ, and it is in this direction only: an object holding a watched member is written even
    /// if nothing else about it qualifies, because a value nobody can find is worse than a row of
    /// scenery.
    ///
    /// Answered once per object and remembered. Reading a component's UnityEvent fields is
    /// reflection, and the scan pays it once per scene where this would pay it on every beat.
    /// Remembered against the object rather than its type: two Buttons of one type are wired
    /// differently, and one of them may point at nothing.
    /// </remarks>
    internal static class Worth
    {
        /// <summary>
        /// How many answers are kept before the lot is dropped and worked out again.
        /// </summary>
        /// <remarks>
        /// A game that spawns and destroys for an hour would otherwise grow a row here for every
        /// object it ever made. Dropping all of them costs one expensive walk and cannot give a
        /// wrong answer, which is the trade to make when the alternative is a leak.
        /// </remarks>
        private const int MaxRemembered = 4096;

        private static readonly Dictionary<int, bool> Answered = new Dictionary<int, bool>();

        internal static bool Writing(GameObject subject, Dictionary<Type, List<Watched>> byOwner)
        {
            if (subject == null)
            {
                return false;
            }

            var id = subject.GetInstanceID();

            if (Answered.TryGetValue(id, out var already))
            {
                return already;
            }

            if (Answered.Count >= MaxRemembered)
            {
                Answered.Clear();
            }

            var answer = Ask(subject, byOwner);
            Answered[id] = answer;
            return answer;
        }

        private static bool Ask(GameObject subject, Dictionary<Type, List<Watched>> byOwner)
        {
            Component[] components;

            try
            {
                components = subject.GetComponents<Component>();
            }
            catch (Exception)
            {
                // The scan reports this as a gap against the scene. Here it is simply an object
                // nothing can be said about.
                return false;
            }

            var calls = new List<PersistentCall>();

            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType();

                if (byOwner.ContainsKey(type) || AffordanceCatalog.For(type) != null)
                {
                    return true;
                }

                calls.Clear();

                try
                {
                    PersistentCallReader.Read(component, calls);
                }
                catch (Exception)
                {
                    continue;
                }

                if (calls.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        internal static void Forget()
        {
            Answered.Clear();
        }
    }
}
