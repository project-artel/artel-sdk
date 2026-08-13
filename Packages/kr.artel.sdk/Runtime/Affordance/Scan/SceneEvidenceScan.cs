using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// Joins what the code was found to do with what one scene actually holds.
    /// </summary>
    /// <remarks>
    /// The compiled evidence knows types and methods and nothing about screens. The scene knows
    /// which objects exist, which are switched off, and which button was wired to which method, and
    /// nothing about what any of it does. Neither half is a specification on its own.
    ///
    /// The evidence documents are copied through untouched. Their schema belongs to the analyser
    /// that wrote them and to the agent that reads them; re-parsing here would put a third opinion
    /// in the middle that has to be kept in step with both.
    /// </remarks>
    public static class SceneEvidenceScan
    {
        private const int MaxObjects = 5000;
        private const int MaxComponentsPerObject = 128;
        private const int MaxCallsPerComponent = 64;

        /// <summary>
        /// How far under an object its label is looked for.
        /// </summary>
        /// <remarks>
        /// A caption sits a child or two down. Walking the whole subtree would make a canvas claim
        /// every word on the screen, and the answer to that is not a wrong label but no label —
        /// which is what several words already means here.
        /// </remarks>
        private const int MaxLabelDepth = 3;

        /// <summary>Reads every loaded scene into the report.</summary>
        public static int CaptureLoaded()
        {
            var captured = 0;

            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                if (Capture(SceneManager.GetSceneAt(index)))
                {
                    captured++;
                }
            }

            return captured;
        }

        /// <summary>Reads one scene into the report, replacing anything said about it before.</summary>
        public static bool Capture(Scene scene)
        {
            if (!scene.IsValid())
            {
                return false;
            }

            var gaps = new List<string>();

            if (!scene.isLoaded)
            {
                AffordanceReport.Merge(scene.name, string.Empty, new List<string> { "scene-not-loaded" });
                return false;
            }

            var text = new StringBuilder(4096);
            var objects = 0;
            var first = true;

            var roots = scene.GetRootGameObjects();

            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var root = roots[rootIndex];

                // Inactive objects included on purpose. A menu that is switched off right now is
                // still something the game can show, and leaving it out makes the result true of
                // one moment rather than of the screen.
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (objects >= MaxObjects)
                    {
                        gaps.Add("object-limit");
                        break;
                    }

                    if (Describe(text, transform.gameObject, scene.name, rootIndex, gaps, ref first))
                    {
                        objects++;
                    }
                }
            }

            AffordanceReport.Merge(scene.name, text.ToString(), gaps);
            return true;
        }

        /// <summary>
        /// Reads the objects the game kept across scene loads.
        /// </summary>
        /// <remarks>
        /// They sit in a scene of their own that walking the build settings never reaches, and it is
        /// where a game puts what it does not want to lose — the save controller, the singletons,
        /// the run's progress. Every scene in the report used to carry a gap saying so; a gap is the
        /// right thing to say about something nobody looked at, and the wrong thing to keep saying
        /// once somebody can.
        ///
        /// Kept apart from the scenes rather than copied into each. One of these objects is not in
        /// any screen and is in all of them, and writing it under a scene name would make it look
        /// like something a tester could find there.
        ///
        /// Nothing is here before the game has run. An editor walk opens scenes as they were saved,
        /// so there is no such scene to read and the report says that instead of pretending the
        /// game has none.
        /// </remarks>
        public static bool CapturePersistent(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return false;
            }

            var gaps = new List<string>();
            var text = new StringBuilder(1024);
            var first = true;
            var roots = scene.GetRootGameObjects();

            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var root = roots[rootIndex];

                // The walk's own carrier lives here too. Reporting it would be reporting the
                // instrument rather than the game.
                if (root == null || root.hideFlags != HideFlags.None)
                {
                    continue;
                }

                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    Describe(text, transform.gameObject, scene.name, rootIndex, gaps, ref first);
                }
            }

            AffordanceReport.Persistent(text.ToString(), gaps);
            return true;
        }

        /// <summary>Writes one object, and says whether it was worth writing.</summary>
        private static bool Describe(
            StringBuilder text,
            GameObject subject,
            string scene,
            int rootIndex,
            List<string> gaps,
            ref bool first)
        {
            Component[] components;

            try
            {
                components = subject.GetComponents<Component>();
            }
            catch (Exception)
            {
                gaps.Add("components-unreadable:" + subject.name);
                return false;
            }

            var body = new StringBuilder(256);
            var wrote = false;
            var limit = Math.Min(components.Length, MaxComponentsPerObject);

            if (components.Length > MaxComponentsPerObject)
            {
                gaps.Add("component-limit:" + subject.name);
            }

            for (var index = 0; index < limit; index++)
            {
                var component = components[index];

                if (component == null)
                {
                    // A script whose type no longer exists. Reported because a missing component is
                    // a broken object, and a scan that skips it silently makes the scene look sound.
                    gaps.Add("missing-script:" + subject.name);
                    continue;
                }

                if (Describe(body, component, wrote))
                {
                    wrote = true;
                }
            }

            if (!wrote)
            {
                return false;
            }

            if (!first)
            {
                text.Append(',');
            }

            first = false;

            var path = ScenePath.Of(subject.transform);

            text.Append('{');
            Json.Property(text, "path", path);
            text.Append(',');

            // Five enemies of one kind share a path. Which of the five is a question the path cannot
            // answer and this can.
            Json.Property(text, "selector", ScenePath.SelectorOf(subject.transform, rootIndex));
            text.Append(',');
            Json.Property(text, "scene", scene);
            text.Append(',');
            Json.Property(text, "active", subject.activeInHierarchy);

            var seen = new Showing();
            Gather(subject.transform, 0, seen, false);

            var captions = seen.Only(Caption);
            var pictures = seen.Only(Picture);

            if (seen.Count(Caption) > 1)
            {
                gaps.Add("several-labels:" + path);
            }
            else if (captions != null)
            {
                text.Append(',');
                Json.Property(text, "label", captions.Value);
                text.Append(',');
                Json.Property(text, "labelFrom", captions.From);
            }
            else if (seen.Count(Picture) > 1)
            {
                gaps.Add("several-sprites:" + path);
            }
            else if (pictures != null)
            {
                text.Append(',');
                Json.Property(text, "sprite", pictures.Value);
                text.Append(',');
                Json.Property(text, "spriteFrom", pictures.From);
            }

            if (seen.All.Count > 0)
            {
                text.Append(",\"visuals\":[");

                for (var index = 0; index < seen.All.Count; index++)
                {
                    if (index > 0)
                    {
                        text.Append(',');
                    }

                    var visual = seen.All[index];

                    text.Append('{');
                    Json.Property(text, "role", visual.Role);
                    text.Append(',');
                    Json.Property(text, "value", visual.Value);
                    text.Append(',');
                    Json.Property(text, "from", visual.From);
                    text.Append(',');
                    Json.Property(text, "type", visual.Type);
                    text.Append('}');
                }

                text.Append(']');
            }

            text.Append(",\"components\":[").Append(body).Append("]}");
            return true;
        }

        /// <summary>Words on something a player can press.</summary>
        private const string Caption = "control-caption";

        /// <summary>Words that are there to be read, not to name the thing showing them.</summary>
        private const string Observed = "observed-text";

        /// <summary>The picture drawn on something.</summary>
        private const string Picture = "sprite";

        /// <summary>
        /// What an object shows — the words on it, and failing that the picture drawn on it.
        /// </summary>
        /// <remarks>
        /// An object's name is what a developer called it, and a test step written from it tells a
        /// tester to press something that is not written anywhere on the screen. In the sample game
        /// one button is called <c>Button (Legacy)</c>, which is Unity's own placeholder and says
        /// nothing, and another is called <c>MapSceneButton</c> while it opens the story — a name
        /// that is worse than none because it reads as an answer.
        ///
        /// The words are the point, and in that game none of the buttons has any: every one of them
        /// is a picture. So the sprite's name is taken when there is no text, kept in a field of its
        /// own because an asset's filename is not what the screen says — it is the nearest thing to
        /// it that exists, and it was enough to settle what <c>MapSceneButton</c> is
        /// (<c>Sprite_Start_Button</c>).
        ///
        /// Neither is written as a component. Text and images are not things a test acts on, and
        /// putting them in the report would bury the handful that are under the scenery they are
        /// painted with.
        ///
        /// Several different words under one object and none is taken. Which of them is the label is
        /// a question this cannot answer — a button may carry a caption, a shadow and a count — and
        /// guessing is how a name that reads as an answer gets made in the first place. The same
        /// word twice is one word, so a caption and its drop shadow are not a disagreement.
        ///
        /// All of it is an observation and not a rule: it is what the screen showed while the scan
        /// ran, and a label the game writes at runtime was something else a moment earlier.
        /// </remarks>
        private sealed class Visual
        {
            internal string Role;
            internal string Value;
            internal string From;
            internal string Type;

            /// <summary>Drawn on something a player can press, so it may name the control.</summary>
            internal bool OnControl;
        }

        private sealed class Showing
        {
            internal readonly List<Visual> All = new List<Visual>();

            internal void Add(string role, string value, string from, string type, bool onControl)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return;
                }

                foreach (var seen in All)
                {
                    // The same word twice is one word — a caption and its drop shadow are not two
                    // things showing.
                    if (seen.Role == role && seen.Value == value)
                    {
                        return;
                    }
                }

                All.Add(new Visual
                {
                    Role = role, Value = value, From = from, Type = type, OnControl = onControl
                });
            }

            /// <summary>How many of a role are drawn on the control, which is what may name it.</summary>
            internal int Count(string role)
            {
                var found = 0;

                foreach (var visual in All)
                {
                    if (visual.Role == role && visual.OnControl)
                    {
                        found++;
                    }
                }

                return found;
            }

            internal Visual Only(string role)
            {
                Visual found = null;

                foreach (var visual in All)
                {
                    if (visual.Role != role || !visual.OnControl)
                    {
                        continue;
                    }

                    if (found != null)
                    {
                        return null;
                    }

                    found = visual;
                }

                return found;
            }
        }

        private static void Gather(Transform at, int depth, Showing seen, bool pressable)
        {
            Component[] components;

            try
            {
                components = at.GetComponents<Component>();
            }
            catch (Exception)
            {
                return;
            }

            var path = ScenePath.Of(at);

            // Once something on the way down can be pressed, everything drawn under it is drawn on
            // the thing that is pressed.
            pressable = pressable || Pressable(components);

            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType().FullName;

                seen.Add(pressable ? Caption : Observed, TextOf(component), path, type, pressable);
                seen.Add(Picture, SpriteOf(component), path, type, pressable);
            }

            if (depth >= MaxLabelDepth)
            {
                return;
            }

            for (var index = 0; index < at.childCount; index++)
            {
                Gather(at.GetChild(index), depth + 1, seen, pressable);
            }
        }

        /// <summary>
        /// Whether a player can press this, which is what tells a caption from a readout.
        /// </summary>
        /// <remarks>
        /// The question the report kept answering wrongly was which of an object's words is its
        /// name. An enemy showing <c>20</c> is not an enemy called twenty, and a chat window showing
        /// the speaker's name is not a control called that — but both arrived in the same field as
        /// the combine button's <c>Combine</c>, and nothing downstream could tell them apart. In the
        /// development build sixteen of twenty-two were numbers.
        ///
        /// Answered by what the object is rather than by what the words look like. Text under
        /// something a player can press is written on the thing being pressed, and that is a caption
        /// whatever it says; text anywhere else is something the game is showing at that moment.
        /// Guessing from the shape of the string — "a number is not a name" — would be right here
        /// and wrong on the first button labelled with a number.
        ///
        /// Matched on type names for the same reason the rest of this file does: this assembly is
        /// not built against uGUI, and a project without it must still compile.
        /// </remarks>
        private static bool Pressable(Component[] components)
        {
            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                for (var type = component.GetType(); type != null; type = type.BaseType)
                {
                    if (type.FullName == "UnityEngine.UI.Selectable")
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// The string a text component is showing, read without being built against it.
        /// </summary>
        /// <remarks>
        /// uGUI and TextMeshPro are packages a project may not have, and this assembly references
        /// neither — the same reason the report already names a component by
        /// <c>GetType().FullName</c> rather than by a type it was compiled against. Matching on the
        /// base type covers <c>TextMeshProUGUI</c> and <c>TextMeshPro</c> without naming either, and
        /// both are engine-side types that obfuscation leaves alone.
        /// </remarks>
        internal static string TextOf(Component component)
        {
            if (component == null)
            {
                return null;
            }

            var type = component.GetType();

            if (!IsLabel(type))
            {
                return null;
            }

            try
            {
                var property = type.GetProperty("text");
                var value = property == null ? null : property.GetValue(component, null) as string;

                return value == null ? null : value.Trim();
            }
            catch (Exception)
            {
                // A property that throws is one component, not a reason to lose the object.
                return null;
            }
        }

        private static bool IsLabel(Type type)
        {
            return Derives(type, "UnityEngine.UI.Text") || Derives(type, "TMPro.TMP_Text");
        }

        /// <summary>
        /// The name of the picture drawn on a component, when it is not one Unity supplied.
        /// </summary>
        /// <remarks>
        /// Unity ships a handful of sprites for anyone who has not drawn their own, and a button
        /// left with one of them is a button nobody has named — the same thing <c>Button (Legacy)</c>
        /// says about its object. Reporting <c>UISprite</c> would put a word in a test step that is
        /// not on the screen and not in the game.
        /// </remarks>
        internal static string SpriteOf(Component component)
        {
            if (component == null)
            {
                return null;
            }

            var type = component.GetType();

            if (!Derives(type, "UnityEngine.UI.Image") && !Derives(type, "UnityEngine.SpriteRenderer"))
            {
                return null;
            }

            try
            {
                var property = type.GetProperty("sprite");
                var drawn = property == null ? null : property.GetValue(component, null) as UnityEngine.Object;
                var name = drawn == null ? null : drawn.name;

                return Array.IndexOf(UnitysOwn, name) < 0 ? name : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static readonly string[] UnitysOwn =
        {
            "UISprite", "Background", "Knob", "Checkmark", "DropdownArrow", "InputFieldBackground",
            "UIMask"
        };

        private static bool Derives(Type type, string name)
        {
            for (var at = type; at != null; at = at.BaseType)
            {
                if (at.FullName == name)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Writes one component, and says whether it had anything to say.</summary>
        private static bool Describe(StringBuilder text, Component component, bool needsComma)
        {
            var evidence = AffordanceCatalog.For(component.GetType());
            var calls = new List<PersistentCall>();

            try
            {
                PersistentCallReader.Read(component, calls);
            }
            catch (Exception)
            {
                calls.Clear();
            }

            // Most components are scenery. Writing every transform and sprite would bury the few
            // that a test can act on.
            if (string.IsNullOrEmpty(evidence) && calls.Count == 0)
            {
                return false;
            }

            var refs = new List<Reference>();

            // Only for components worth writing at all. Reading every reference of every sprite and
            // collider in a scene would cost the whole scene to say nothing.
            try
            {
                SerializedReferences.Read(component, refs);
            }
            catch (Exception)
            {
                refs.Clear();
            }

            if (needsComma)
            {
                text.Append(',');
            }

            var type = component.GetType().FullName;

            // The evidence goes in the table under this name, not here. Fifteen slimes of one kind
            // are fifteen places to act on and one thing to know about them.
            Remember(type, evidence);

            text.Append('{');
            Json.Property(text, "type", type);

            text.Append(",\"calls\":[");
            var limit = Math.Min(calls.Count, MaxCallsPerComponent);

            for (var index = 0; index < limit; index++)
            {
                if (index > 0)
                {
                    text.Append(',');
                }

                var call = calls[index];

                // Noted even when this component's own type has evidence. What the wiring points at
                // is a different type from the one that holds the wiring.
                AffordanceReport.Wired(call.TargetType);

                text.Append('{');
                Json.Property(text, "event", call.Event);
                text.Append(',');
                Json.Property(text, "targetType", call.TargetType);
                text.Append(',');
                Json.Property(text, "targetPath", call.TargetPath);
                text.Append(',');
                Json.Property(text, "method", call.Method);
                text.Append('}');
            }

            text.Append("],\"refs\":[");

            for (var index = 0; index < refs.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(',');
                }

                var reference = refs[index];
                text.Append('{');
                Json.Property(text, "field", reference.Field);
                text.Append(',');
                Json.Property(text, "type", reference.Type);
                text.Append(',');
                Json.Property(text, "name", reference.Name);
                text.Append(',');
                Json.Property(text, "id", reference.Id);
                text.Append(',');
                Json.Property(text, "path", reference.Path);
                text.Append(',');

                // A prefab and a scene root used to be written the same way. Said outright now,
                // because only one of the two is somewhere a test can be told to go.
                Json.Property(text, "asset", reference.Asset);
                text.Append(",\"carries\":[");

                if (reference.Carries != null)
                {
                    for (var carried = 0; carried < reference.Carries.Count; carried++)
                    {
                        if (carried > 0)
                        {
                            text.Append(',');
                        }

                        Json.String(text, reference.Carries[carried]);
                    }
                }

                text.Append("]}");

                // Told while the owner is in hand, and followed a step or two further because a
                // prefab is often held through a ScriptableObject. The report needs this the other
                // way round — from a type nobody met back to the field that would make one — and it
                // cannot ask that question once the scene is behind it.
                if (reference.Asset)
                {
                    try
                    {
                        SerializedReferences.Trace(reference.Held, type, reference.Field);
                    }
                    catch (Exception)
                    {
                        // One unwalkable asset is not a reason to stop describing the scene.
                    }
                }
            }

            text.Append("]}");
            return true;
        }

        /// <summary>Puts a type's evidence in the table, the first time that type is met.</summary>
        /// <remarks>
        /// The document arrives already assembled — one array per type, written that way by the
        /// analyser and carried whole. Copied through as it is; quoting it as a string would make
        /// whoever reads this unwrap it again.
        /// </remarks>
        private static void Remember(string type, string evidence)
        {
            if (string.IsNullOrEmpty(evidence) || AffordanceReport.Knows(type))
            {
                return;
            }

            AffordanceReport.Learn(type, evidence);
        }
    }
}
