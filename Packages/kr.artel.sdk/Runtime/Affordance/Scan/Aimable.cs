using System;
using UnityEngine;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// Whether an object is one a pointer could be sent at, and what kind of thing it is.
    /// </summary>
    /// <remarks>
    /// The report asks a different question from this one, and the two answers do not contain each
    /// other. The report keeps what carries evidence or inspector wiring, because that is what a
    /// specification can be written from — a controller with no picture belongs there and cannot be
    /// aimed at. This keeps what occupies the screen or can receive input, because that is what an
    /// agent can act on — a background sprite belongs here and says nothing a specification wants.
    /// Measured on the sample game: twenty-seven and thirty-one, overlapping but neither inside the
    /// other. One walk, two questions.
    ///
    /// Asked by what a thing has rather than by what it is wired to, which is the opposite of the
    /// report's rule and deliberately so. A button whose handler was attached in code carries no
    /// persistent call and would be invisible to the other question, while a person can plainly see
    /// it and press it. What can be done to a screen is not limited to what somebody wired in the
    /// inspector.
    ///
    /// Matched on type names for the same reason the rest of this assembly does: it is not built
    /// against uGUI, and a project without that package must still compile.
    /// </remarks>
    internal static class Aimable
    {
        /// <summary>Whether a pointer sent here would land on something.</summary>
        /// <remarks>
        /// Four ways to be on screen, and a thing needs only one. The empty parents, the audio
        /// sources, the camera and the event system have none of them — they are the scaffolding a
        /// scene is built with rather than anything a player sees, and listing them costs an agent
        /// its attention for nothing. On the sample game that is forty-six of seventy-seven objects.
        ///
        /// A collider counts even with nothing drawn on it, because <c>OnMouseDown</c> is delivered
        /// by the engine from the collider and not from the picture. An invisible one is still what
        /// a click lands on first.
        /// </remarks>
        internal static bool Is(Component[] components)
        {
            if (components == null)
            {
                return false;
            }

            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType();

                if (Derives(type, "UnityEngine.UI.Graphic") ||
                    Derives(type, "UnityEngine.Renderer") ||
                    Derives(type, "UnityEngine.UI.Selectable") ||
                    Derives(type, "UnityEngine.Collider") ||
                    Derives(type, "UnityEngine.Collider2D"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>What kind of thing this is, in the words the action protocol already uses.</summary>
        /// <remarks>
        /// Ordered so that the more specific answer wins. A button is drawn with an image and often
        /// carries a label, and answering "image" for it would be true and useless — the caller is
        /// asking what it can do here, and the answer that lets it do something is the one to give.
        ///
        /// Falls through to <c>block</c> rather than to nothing. An object with only a collider is
        /// something a pointer can reach and nothing this can name, and saying so is better than
        /// dropping it: the agent can still aim at the area it covers.
        /// </remarks>
        internal static string KindOf(Component[] components)
        {
            var kind = "block";

            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType();

                if (Derives(type, "UnityEngine.UI.Selectable"))
                {
                    // Text entry is a kind of selectable, so it has to be asked about first or every
                    // input field would answer "button" and refuse the text it was meant to take.
                    if (Derives(type, "UnityEngine.UI.InputField") ||
                        Derives(type, "TMPro.TMP_InputField"))
                    {
                        return "editText";
                    }

                    if (Derives(type, "UnityEngine.UI.Button"))
                    {
                        return "button";
                    }
                }

                if (Derives(type, "UnityEngine.UI.Text") || Derives(type, "TMPro.TMP_Text"))
                {
                    kind = "text";
                }
                else if (Derives(type, "UnityEngine.UI.Image") && kind == "block")
                {
                    kind = "image";
                }
                else if (Derives(type, "UnityEngine.SpriteRenderer") && kind == "block")
                {
                    kind = "sprite";
                }
            }

            return kind;
        }

        /// <summary>
        /// Whether a person could press or type into this right now.
        /// </summary>
        /// <remarks>
        /// Three ways to be unreachable and all of them have to be asked. A disabled component, a
        /// selectable switched off in the inspector, and a parent group that blocks everything below
        /// it look identical to a scan that only reads the first — and an agent told it may press
        /// something it cannot press reports a game broken when the game is fine.
        ///
        /// True for anything that is not a control. A sprite is not interactable in the sense a
        /// button is, but a pointer sent at it still arrives, and answering false would read as
        /// "do not aim here".
        /// </remarks>
        internal static bool Interactable(GameObject subject, Component[] components)
        {
            if (subject == null || !subject.activeInHierarchy)
            {
                return false;
            }

            foreach (var component in components)
            {
                if (component == null || !Derives(component.GetType(), "UnityEngine.UI.Selectable"))
                {
                    continue;
                }

                if (!Enabled(component) || !Reads<bool>(component, "interactable", true))
                {
                    return false;
                }

                // A CanvasGroup anywhere above can switch off everything under it, and the control
                // itself reports nothing about that.
                return !BlockedByGroup(subject.transform);
            }

            return true;
        }

        private static bool BlockedByGroup(Transform from)
        {
            for (var at = from; at != null; at = at.parent)
            {
                foreach (var component in at.GetComponents<Component>())
                {
                    if (component == null || component.GetType().FullName != "UnityEngine.CanvasGroup")
                    {
                        continue;
                    }

                    if (!Reads<bool>(component, "interactable", true))
                    {
                        return true;
                    }

                    // A group that does not pass the question on is the last one that matters.
                    if (!Reads<bool>(component, "ignoreParentGroups", false))
                    {
                        continue;
                    }

                    return false;
                }
            }

            return false;
        }

        private static bool Enabled(Component component)
        {
            return Reads<bool>(component, "enabled", true);
        }

        /// <summary>Reads a property this assembly was not built against, or says what to assume.</summary>
        private static T Reads<T>(Component component, string property, T whenUnknown)
        {
            try
            {
                var found = component.GetType().GetProperty(property);
                var value = found == null ? null : found.GetValue(component, null);

                return value is T typed ? typed : whenUnknown;
            }
            catch (Exception)
            {
                // A property that throws is one component, not a reason to call the object dead.
                return whenUnknown;
            }
        }

        internal static bool Derives(Type type, string baseName)
        {
            for (var at = type; at != null; at = at.BaseType)
            {
                if (at.FullName == baseName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
