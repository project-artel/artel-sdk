using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Artel.Affordances.Scan;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel.Affordances.Live
{
    /// <summary>
    /// What the watched members hold right now, written the way the report names them.
    /// </summary>
    /// <remarks>
    /// The report says <c>MapMove.position == 0</c> and until now nothing could see what
    /// <c>position</c> held, so every row of a specification was a rule with no way to check its own
    /// premise. This is the other side of that sentence.
    ///
    /// Two things are kept apart on purpose. A static field has one value and no owner, and an
    /// instance field has one value per object that carries it — five spawned enemies are five
    /// answers to <c>hp</c>, and folding them into one would be the same mistake as writing a
    /// condition about two objects in one sentence. Statics are written as a list of their own;
    /// instance values are written under the path of the object holding them, and never averaged,
    /// summed or picked from.
    ///
    /// Nothing here interprets. A value is written as the field gave it and typed as the analysis
    /// declared it, and what <c>flag == 1</c> means is a question for whoever reads this.
    /// </remarks>
    internal static class LiveState
    {
        /// <summary>How many objects one watched member may be found on before the rest are dropped.</summary>
        /// <remarks>
        /// A pooled projectile can exist in the hundreds, and a payload that grows with the pool is
        /// one the change gate cannot use — it would differ every poll for reasons no condition
        /// mentions. What is dropped is said on the member rather than left to be inferred from a
        /// count that looks complete.
        /// </remarks>
        private const int MaxHolders = 16;

        /// <summary>
        /// Reads every watched member and writes one document.
        /// </summary>
        /// <remarks>
        /// The scene is walked once and every component is offered to every watched type, rather
        /// than each type being searched for on its own. A game with a hundred watched members
        /// would otherwise walk the hierarchy a hundred times per poll, and the walk is the
        /// expensive half.
        /// </remarks>
        internal static string Compose(
            long reading, Scene persistent, Restless restless, Dictionary<string, string> since,
            bool repair, out bool settled)
        {
            var watched = WatchList.All();
            var now = new Dictionary<string, string>(since.Count, StringComparer.Ordinal);
            var moved = new List<string>();
            var byOwner = new Dictionary<Type, List<Watched>>();
            var statics = new List<Watched>();

            foreach (var member in watched)
            {
                if (member.Static)
                {
                    statics.Add(member);
                    continue;
                }

                if (!byOwner.TryGetValue(member.Owner, out var list))
                {
                    list = new List<Watched>();
                    byOwner[member.Owner] = list;
                }

                list.Add(member);
            }

            var active = SceneManager.GetActiveScene();
            var text = new StringBuilder(4096);

            text.Append("{\"schema\":").Append(Pulse.SchemaVersion);

            // Said so a reading can be put beside the frame it describes. A specification is checked
            // against the screen and against this at once, and without a place in time the two are
            // two accounts of a game with no way to tell which moment either belongs to. The frame
            // is the one the game itself counts, so anything else reading the same frame agrees.
            text.Append(",\"reading\":").Append(reading);
            text.Append(",\"frame\":").Append(Time.frameCount);

            text.Append(",\"scene\":");
            Json.String(text, active.IsValid() ? active.name : null);

            // The first reading has nothing to be a difference from, and a change of screen replaces
            // everything on it anyway — so the full state costs almost nothing extra there and gives
            // a reader a point it can be sure of. Every other reading carries only what moved.
            //
            // And after a reading that could not be delivered. A reader that missed a difference is
            // wrong about that value until something happens to move it again, which a full reading
            // repairs and another difference cannot. Sending the lost document again would be the
            // flood the sink is already unhappy about; sending the whole state once is not.
            since.TryGetValue("scene", out var was);

            var everything = repair || since.Count == 0 ||
                             was != (active.IsValid() ? active.name : null);

            var ledger = new Ledger
            {
                Restless = restless, Since = since, Now = now, Moved = moved, Everything = everything
            };

            // The screen the tester is on is part of what a reading claims, so a change of screen is
            // news even when every value on it happens to read the same.
            ledger.Say("scene", active.IsValid() ? active.name : null);

            text.Append(",\"statics\":[");
            Statics(text, statics, ledger);
            text.Append(']');

            var showing = new Bin();
            var hidden = new Bin();
            var truncated = Objects(persistent, byOwner, ledger, showing, hidden);

            showing.WriteTo(text, "active");
            hidden.WriteTo(text, "deactive");

            // Said so a reader knows which kind of reading it is holding. Without it, a delta and a
            // full reading look alike, and a reader that mistook one for the other would either
            // discard state it still needs or keep state that is gone.
            text.Append(",\"whole\":").Append(everything ? "true" : "false");

            text.Append(",\"watching\":").Append(watched.Count);
            text.Append(",\"unresolved\":").Append(WatchList.Unresolved);
            text.Append(",\"unwatchable\":").Append(WatchList.Unwatchable);

            if (truncated > 0)
            {
                text.Append(",\"gaps\":[\"holder-limit:").Append(truncated).Append("\"]");
            }

            // What is different from the reading before this one. Written even when it is empty,
            // because an empty list and a missing field are different claims — the first says the
            // values were compared and none had moved, which is what the first reading of a run
            // cannot say.
            //
            // This is the half that makes a restless value findable. A run whose readings nearly all
            // go out is a run with something moving that no condition mentions, and without this a
            // reader can see that it is happening and never which member is doing it. Said on every
            // reading rather than counted at the end, so it is noticed on the first one.
            // Gone is a change. A key the reading before this one had and this one does not is an
            // object that was destroyed or a screen that was left, and comparing only what is here
            // now would report the busiest moment in a game — everything being torn down — as
            // nothing having happened.
            foreach (var pair in since)
            {
                if (!now.ContainsKey(pair.Key))
                {
                    moved.Add(pair.Key);
                }
            }

            text.Append(",\"changed\":[");
            moved.Sort(StringComparer.Ordinal);

            for (var at = 0; at < moved.Count; at++)
            {
                if (at > 0)
                {
                    text.Append(',');
                }

                Json.String(text, moved[at]);
            }

            text.Append("]}");

            settled = moved.Count == 0;

            since.Clear();

            foreach (var pair in now)
            {
                since[pair.Key] = pair.Value;
            }

            return text.ToString();
        }

        /// <summary>What this reading said, what the one before it said, and the difference.</summary>
        /// <summary>
        /// One of the two lists a reading's objects are sorted into.
        /// </summary>
        /// <remarks>
        /// Switched-on and switched-off are told apart by which list an object arrives in rather
        /// than by a field on it. A row of the sample game's specification is <em>the continue
        /// button is shown disabled</em>, so the switched-off ones have to be carried; carrying them
        /// mixed in with the rest makes a reader filter a list to answer the question the channel
        /// was asked. Sorting is the same work done once, here.
        /// </remarks>
        private sealed class Bin
        {
            private readonly StringBuilder _text = new StringBuilder(1024);
            private int _written;

            internal void Add(StringBuilder said)
            {
                if (_written > 0)
                {
                    _text.Append(',');
                }

                _text.Append(said);
                _written++;
            }

            internal void WriteTo(StringBuilder text, string name)
            {
                text.Append(",\"").Append(name).Append("\":[").Append(_text).Append(']');
            }
        }

        private sealed class Ledger
        {
            internal Restless Restless;
            internal Dictionary<string, string> Since;
            internal Dictionary<string, string> Now;
            internal List<string> Moved;

            /// <summary>
            /// Records one value under its own name and notes whether it is new.
            /// </summary>
            /// <remarks>
            /// Keyed by where the value lives rather than by what it is called, so the same field on
            /// two objects is two entries. A member that appears on five spawned enemies moving
            /// independently is five things that can each be seen to have moved, which is the only
            /// form in which that fact is any use.
            /// </remarks>
            internal bool Say(string key, string value)
            {
                Now[key] = value;

                if (Since.TryGetValue(key, out var before) && before == value)
                {
                    return false;
                }

                Moved.Add(key);
                return true;
            }

            /// <summary>
            /// Whether this reading carries everything rather than only what moved.
            /// </summary>
            /// <remarks>
            /// A reading that carries only differences is the whole point — the watch list now holds
            /// everything readable rather than everything the evidence asked for, and sending a
            /// game's entire state ten times a second to say that none of it moved is the cost that
            /// widening would otherwise have bought.
            ///
            /// But a reader that has only ever seen differences has never been told what the values
            /// are, and one that missed a reading is wrong about them until something happens to
            /// move each one. So the whole state goes out at points a reader can be counted on to
            /// have: the first reading, and every change of screen. A scene change is the natural
            /// one — everything on the screen is replaced anyway, so the full reading costs almost
            /// nothing extra there, and it is the boundary a specification is written against.
            /// </remarks>
            internal bool Everything;

            /// <summary>Says the value, and answers whether it belongs in this reading.</summary>
            internal bool Keep(string key, string value)
            {
                return Say(key, value) | Everything;
            }
        }

        private static void Statics(StringBuilder text, List<Watched> statics, Ledger ledger)
        {
            var written = 0;

            foreach (var member in statics)
            {
                var said = new StringBuilder(96);

                said.Append('{');
                Json.Property(said, "declaring", member.Declaring);
                said.Append(',');
                Json.Property(said, "member", Named(member));
                said.Append(',');
                Json.Property(said, "type", member.Type);
                said.Append(',');

                if (!Value(said, member, null, ledger, member.Key))
                {
                    continue;
                }

                said.Append('}');

                if (written > 0)
                {
                    text.Append(',');
                }

                text.Append(said);
                written++;
            }
        }

        /// <summary>
        /// Every object a test could act on, under the path it sits at.
        /// </summary>
        /// <remarks>
        /// The same objects the report has, decided the same way. That is not a convenience: the
        /// specification was written from the report's own walk of this game, so an object the walk
        /// wrote down is one some row may name — and a reading narrower than that walk reports the
        /// premise of a row as missing when the package had it all along.
        ///
        /// It was narrower. This used to visit only the objects carrying a member the evidence
        /// names, which left <c>Canvas/ExitButton</c> and three more buttons out of every reading
        /// while the report listed them with their paths and their switched-on state. Six rows were
        /// unanswerable for no reason but that.
        ///
        /// So the watch list decides what to *read*, never what to *visit*. Its whole job is the
        /// values a walk cannot see — a private field, a static that hangs off no object — and
        /// deciding the walk with it was two jobs given to one thing.
        ///
        /// Every loaded scene and the persistent one. A game that puts its interface on top of a
        /// manager scene is playing both, and what a game keeps across scene loads is filed
        /// somewhere Unity does not count among the loaded scenes at all.
        ///
        /// Scenes that are not loaded are not read: there is nothing to read, and a tester is not in
        /// them. Inactive objects inside a loaded scene are read, because whether the continue button
        /// is switched on is the whole of what one row checks — and it is the one thing a recording
        /// of that screen cannot answer, since an absent button and a switched-off one look the same.
        /// </remarks>
        private static int Objects(
            Scene persistent, Dictionary<Type, List<Watched>> byOwner, Ledger ledger,
            Bin showing, Bin hidden)
        {
            var seen = new Dictionary<Type, int>();
            var dropped = 0;

            for (var at = 0; at < SceneManager.sceneCount; at++)
            {
                var scene = SceneManager.GetSceneAt(at);

                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                dropped += In(scene, byOwner, seen, ledger, showing, hidden);
            }

            // What the game kept across scene loads. Unity does not count it among the loaded
            // scenes, so a walk of those alone misses it — and this is where a game puts the things
            // that outlive a screen. The sample game keeps its stage number there, which
            // twenty-six specification rows test.
            //
            // It is not another screen's data. These objects are in this play session, alive right
            // now, and the only reason they need saying twice is that Unity files them apart.
            if (persistent.IsValid() && persistent.isLoaded)
            {
                dropped += In(persistent, byOwner, seen, ledger, showing, hidden);
            }

            return dropped;
        }

        private static int In(
            Scene scene,
            Dictionary<Type, List<Watched>> byOwner,
            Dictionary<Type, int> seen,
            Ledger ledger,
            Bin showing,
            Bin hidden)
        {
            var dropped = 0;
            var roots = scene.GetRootGameObjects();

            for (var index = 0; index < roots.Length; index++)
            {
                if (roots[index] == null || roots[index].hideFlags != HideFlags.None)
                {
                    // The pulse's own carrier lives in a scene like anything else. Reporting it
                    // would be reporting the instrument rather than the game.
                    continue;
                }

                foreach (var transform in roots[index].GetComponentsInChildren<Transform>(true))
                {
                    if (transform == null)
                    {
                        continue;
                    }

                    if (!Worth.Writing(transform.gameObject, byOwner))
                    {
                        continue;
                    }

                    var kind = transform.gameObject.GetType();
                    seen.TryGetValue(kind, out var already);
                    seen[kind] = already + 1;

                    if (already >= MaxHolders * MaxHolders)
                    {
                        dropped++;
                        continue;
                    }

                    var said = new StringBuilder(256);

                    if (!Object(said, transform, scene, index, byOwner, ledger))
                    {
                        continue;
                    }

                    // Which bin it goes in is the statement, so the object does not also carry a
                    // flag saying the same thing. A reader holding a difference knows the object is
                    // switched off because of where it arrived, and an object that says nothing this
                    // reading stays wherever it was last put — which is right, because a change of
                    // that is itself a difference and would have brought it here.
                    (transform.gameObject.activeInHierarchy ? showing : hidden).Add(said);
                }
            }

            return dropped;
        }

        /// <summary>Writes one object: where it is, whether it is showing, and what it holds.</summary>
        /// <remarks>
        /// One record per object rather than per component, which is the shape the report already
        /// uses. An object is what a row names and what a tester acts on; that two of its components
        /// each hold a watched field is an arrangement inside it.
        /// </remarks>
        /// <returns>True when anything about this object belongs in the reading.</returns>
        private static bool Object(
            StringBuilder into,
            Transform transform,
            Scene scene,
            int rootIndex,
            Dictionary<Type, List<Watched>> byOwner,
            Ledger ledger)
        {
            var selector = ScenePath.SelectorOf(transform, rootIndex);

            // Keyed by the selector rather than the path. Five spawned enemies share one path —
            // `TurnBattleScene/RangedCat(Clone)` five times over — so a path-keyed ledger has them
            // overwriting each other and reports a change every reading for objects that never
            // moved. Measured: that alone was most of what opened the gate on a run.
            var identity = scene.name + "/" + selector;

            // Built aside because whether the object is written at all is only known once its
            // members have been read. An object none of whose values moved is one the reading has
            // nothing to say about, and the whole of what it holds is the wrong price for saying so.
            var text = new StringBuilder(256);

            // Where it is, always. A reader that has never been told the path of a selector cannot
            // act on a delta about it, and these three cost nothing beside the members.
            text.Append('{');
            Json.Property(text, "scene", scene.name);
            text.Append(',');
            Json.Property(text, "path", ScenePath.Of(transform));
            text.Append(',');
            Json.Property(text, "selector", selector);

            // Told to the ledger, not written into the object. Which list it lands in already
            // says it, and saying it twice is two places for one fact to disagree. The ledger still
            // needs it so that switching off is a difference and brings the object to a reader.
            var moved = ledger.Keep(
                identity + "|active",
                transform.gameObject.activeInHierarchy ? "true" : "false");

            moved |= Where(text, transform, ledger, identity);

            moved |= Offered(text, transform, ledger, identity);

            text.Append(",\"members\":[");

            var written = 0;

            // How many of each type have been passed on this object. Nothing stops a GameObject
            // carrying two of one behaviour, and the sample game does — `CombineZone/Zone1` has two
            // `DropZone`s. Without this the two share one entry in the ledger: the second overwrites
            // the first, so the first's value is never what the next reading compares against, and
            // it either moves without being reported or is reported as moving when it did not.
            //
            // The same fault the selector already fixed one level up, where five spawned enemies
            // shared one path and every reading called them all changed. The object was made
            // countable and the components on it were not.
            var counted = new Dictionary<Type, int>();

            foreach (var component in transform.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType();

                counted.TryGetValue(type, out var ordinal);
                counted[type] = ordinal + 1;

                // Only the second and later are marked, so an object carrying one of a type — which
                // is nearly all of them — keys and reads exactly as it did before.
                var among = ordinal == 0 ? string.Empty : ordinal.ToString(Invariant) + "#";

                byOwner.TryGetValue(type, out var named);

                // What the evidence asked for, and what else can be read off the same component.
                // A member nobody asked for is still a member somebody will ask for once a person
                // writes the row the analysis missed.
                var members = Readable.On(type, named);

                if (members == null)
                {
                    continue;
                }

                foreach (var member in members)
                {
                    var said = new StringBuilder(96);

                    said.Append('{');
                    Json.Property(said, "on", type.FullName);
                    said.Append(',');
                    Json.Property(said, "member", Named(member));
                    said.Append(',');
                    Json.Property(said, "type", member.Type);

                    // Said in the document as well as kept in the ledger. A reader given two entries
                    // that name the same type and the same member has no way to tell which of the
                    // object's components each came from.
                    if (ordinal > 0)
                    {
                        said.Append(",\"among\":").Append(ordinal.ToString(Invariant));
                    }

                    if (!member.Asked)
                    {
                        said.Append(",\"asked\":false");
                    }

                    said.Append(',');

                    if (!Value(said, member, component, ledger, identity + "|" + among + member.Key))
                    {
                        continue;
                    }

                    said.Append('}');

                    if (written > 0)
                    {
                        text.Append(',');
                    }

                    text.Append(said);
                    written++;
                }
            }

            text.Append("]}");

            if (written == 0 && !moved)
            {
                return false;
            }

            into.Append(text);
            return true;
        }

        /// <summary>
        /// The value, or the reason there is not one.
        /// </summary>
        /// <remarks>
        /// A field that throws when it is read is not a value of zero. Reflection on a property-like
        /// field of a destroyed object does throw, and reporting the exception as a number would put
        /// a false premise into a specification — which is the one failure this whole package is
        /// arranged to avoid.
        ///
        /// A reference is written as whether it is there, not as what it is. What a
        /// <c>SaveLoadController</c> holds is the game's own data; that a condition compares it with
        /// <c>null</c> is answered entirely by present or absent, and going further would turn a
        /// state channel into a dump of the save file.
        /// </remarks>
        /// <returns>True when the value belongs in this reading — it moved, or everything is going.</returns>
        private static bool Value(
            StringBuilder text, Watched member, Component on, Ledger ledger, string key)
        {
            // Written aside first so the ledger can hold exactly what went out. Comparing the
            // fragment rather than the value it came from means the two can never disagree — a
            // coordinate held still by the deadband reads as unchanged because it *is* the same
            // text, not because a second rule said it should be.
            var said = new StringBuilder(64);

            Read(said, member, on, ledger, key);

            // Said to the ledger whether or not it is written. What the reading carries and what the
            // reading knows are different things: a value left out because it held still still has
            // to be recorded, or the next reading finds it missing and calls that a change.
            if (!ledger.Keep(key, said.ToString()))
            {
                return false;
            }

            text.Append(said);
            return true;
        }

        /// <summary>
        /// What the reading calls a member: the thing asked for, not the field it was found through.
        /// </summary>
        /// <remarks>
        /// The evidence asks for <c>IsStreaming</c> and the place to look is
        /// <c>chatWindowController</c>. Naming the reading after the field leaves a reader holding
        /// <c>chatWindowController = true</c>, which is not a sentence anybody wrote a row against —
        /// and worse, a list read for its size and a list read for itself would both be called by
        /// the list's name with different values under it.
        ///
        /// So the name is the path that was walked. The field stays in front of it, because two
        /// objects can offer the same property and a row names the one it means.
        /// </remarks>
        private static string Named(Watched member)
        {
            var found = member.Property ?? member.Member;

            return member.Via == null ? found : found + "." + member.Via;
        }

        /// <summary>
        /// Walks from a field's value to the thing the evidence actually named.
        /// </summary>
        /// <remarks>
        /// One step per name, each a field or an argument-less property, exactly as the analysis
        /// wrote them down. Nothing is chosen here: the path was settled when the code was read, and
        /// following it is arithmetic.
        ///
        /// A step that is not there is reported by name rather than as a missing value. Obfuscation
        /// renames members, and a reading that answered null would be putting words in the game's
        /// mouth — the same reason an unreadable field says <c>unread</c> instead of zero.
        /// </remarks>
        private static object Along(object from, string path)
        {
            const BindingFlags Flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var step in path.Split('.'))
            {
                if (from == null)
                {
                    return null;
                }

                var type = from.GetType();
                var field = type.GetField(step, Flags);

                if (field != null)
                {
                    from = field.GetValue(from);
                    continue;
                }

                var property = type.GetProperty(step, Flags);

                if (property == null || !property.CanRead ||
                    property.GetIndexParameters().Length != 0)
                {
                    return null;
                }

                from = property.GetValue(from, null);
            }

            return from;
        }

        private static void Read(
            StringBuilder text, Watched member, Component on, Ledger ledger, string key)
        {
            object held;

            try
            {
                held = member.Field.GetValue(on);

                // The evidence did not ask for the field, it asked for something reached from it —
                // a list's count, or what a method that only walks fields would have returned. The
                // path is followed here rather than the method being called, which is the whole
                // difference between watching the game and playing it.
                if (member.Via != null && held != null)
                {
                    held = Along(held, member.Via);
                }
            }
            catch (Exception exception)
            {
                Json.Property(text, "unread", exception.GetType().Name);
                return;
            }

            if (held == null)
            {
                text.Append("\"value\":null");
                return;
            }

            switch (held)
            {
                case bool flag:
                    text.Append("\"value\":").Append(flag ? "true" : "false");
                    return;

                case int number:
                    text.Append("\"value\":").Append(number.ToString(Invariant));
                    return;

                case long number:
                    text.Append("\"value\":").Append(number.ToString(Invariant));
                    return;

                case float number:
                    Number(text, ledger.Restless.Settle(key, number));
                    return;

                case double number:
                    Number(text, number);
                    return;

                case string words:
                    Json.Property(text, "value", words);
                    return;

                case Enum member_:
                    Json.Property(text, "value", member_.ToString());
                    return;
            }

            if (held is UnityEngine.Object reference)
            {
                // Unity overloads equality so a destroyed object is not the same as a missing one,
                // and a condition comparing against null means the overloaded answer.
                if (reference == null)
                {
                    text.Append("\"value\":null");
                    return;
                }

                Held(text, reference, ledger.Restless, key);
                return;
            }

            if (held is System.Collections.ICollection collection)
            {
                // The count and nothing else. Every condition in the sample game that reaches into a
                // collection asks how many are in it, and the contents are the game's own data.
                text.Append("\"count\":").Append(collection.Count.ToString(Invariant));
                return;
            }

            // What it is, rather than that it is. A field holding a plain object used to read as
            // "present", which says only that the reference is not null — and a reference that is
            // never null says the same thing on every reading, so the channel carried the field and
            // told nobody anything.
            //
            // The concrete type is the thing a game keeps in such a field for. A tutorial's current
            // step, a state machine's current state, a strategy, a handler: the class standing there
            // *is* the state. The sample game holds its tutorial position in one — fifteen classes
            // behind one interface — and asking which of them is there answers more than the
            // <c>IsMeetCondition()</c> the evidence could not call, because a name says which step
            // rather than whether one predicate happened to be true.
            //
            // Costs a reflection call that cannot fail and cannot be wrong. The declared type is
            // already on the member; this is what turned up in it.
            text.Append("\"value\":{");
            Json.Property(text, "is", held.GetType().FullName);
            text.Append('}');
        }

        /// <summary>
        /// What a reference points at: which object, and where it is.
        /// </summary>
        /// <remarks>
        /// This used to say <c>"present"</c>, which threw away the thing the channel exists for. The
        /// evidence says the map cursor moves to <c>MapMove.battle2.transform.position</c>, and both
        /// <c>character</c> and <c>battle2</c> are fields — so what is actually being asked is where
        /// two named objects are, and whether one of them has arrived at the other.
        ///
        /// It is the half a screen recording cannot supply. A video shows a sprite finishing
        /// somewhere; it does not know the sprite is called <c>wordHead</c>, does not know the place
        /// is called <c>battle2</c>, and so cannot tell that what it just watched was the thing the
        /// specification named. The path is that name, and the position is what lets the two
        /// accounts be laid over each other.
        ///
        /// Both the path and the position, never one. The path alone cannot say it moved and the
        /// position alone cannot say what moved.
        /// </remarks>
        private static void Held(
            StringBuilder text, UnityEngine.Object reference, Restless restless, string key)
        {
            if (reference is Animator animator)
            {
                Playing(text, animator);
                return;
            }

            if (Showing(text, reference as Component))
            {
                return;
            }

            var transform = reference as Transform
                            ?? (reference as GameObject)?.transform
                            ?? (reference as Component)?.transform;

            if (transform == null)
            {
                // An asset — a sprite, a clip, a ScriptableObject. It is somewhere in the project
                // rather than somewhere on screen, so its name is the whole of what can be said.
                text.Append("\"value\":{");
                Json.Property(text, "name", reference.name);
                text.Append('}');
                return;
            }

            var world = transform.position;

            text.Append("\"value\":{");
            Json.Property(text, "path", ScenePath.Of(transform));
            text.Append(",\"active\":").Append(transform.gameObject.activeInHierarchy ? "true" : "false");
            text.Append(",\"world\":{\"x\":");
            Coordinate(text, restless.Settle(key + "|x", world.x));
            text.Append(",\"y\":");
            Coordinate(text, restless.Settle(key + "|y", world.y));
            text.Append(",\"z\":");
            Coordinate(text, restless.Settle(key + "|z", world.z));
            text.Append("}}");
        }

        /// <summary>
        /// Where the object is, in the game's own world.
        /// </summary>
        /// <remarks>
        /// Held back until now on the grounds that a specification asks whether one object has
        /// arrived where another is, and both of those are named fields the watch list already
        /// reads — so the position of an object nobody's evidence mentions answered no row.
        ///
        /// It is asked for anyway by anything that has to lay a reading over a picture of the
        /// screen. A reader that can see the game cannot join what it sees to what it is told
        /// without somewhere in common, and this is the cheapest one there is.
        ///
        /// Settled through the deadband like any other coordinate. A transform read straight would
        /// differ in its last decimal place for objects sitting exactly where they were, and with a
        /// position on every object rather than only on the watched few that is the whole reading
        /// opening the gate every beat.
        ///
        /// Not on screen and not a rectangle. Where a thing is drawn needs the camera, the canvas it
        /// hangs on and whatever is clipping it, and the reader that wants that is looking at a
        /// capture of the screen already.
        /// </remarks>
        private static bool Where(
            StringBuilder text, Transform transform, Ledger ledger, string identity)
        {
            var world = transform.position;
            var said = new StringBuilder(64);

            said.Append(",\"world\":{\"x\":");
            Coordinate(said, ledger.Restless.Settle(identity + "|wx", world.x));
            said.Append(",\"y\":");
            Coordinate(said, ledger.Restless.Settle(identity + "|wy", world.y));
            said.Append(",\"z\":");
            Coordinate(said, ledger.Restless.Settle(identity + "|wz", world.z));
            said.Append('}');

            var rendered = said.ToString();

            if (!ledger.Keep(identity + "|world", rendered))
            {
                return false;
            }

            text.Append(rendered);
            return true;
        }

        /// <summary>
        /// What a tester can do to this object right now.
        /// </summary>
        /// <remarks>
        /// A reading that says only what the game holds leaves an agent with the whole state of a
        /// screen and no idea which of the things on it will answer to anything. The specification
        /// says press the continue button; the reading has to be where that button is found to be
        /// present, switched on, and wired to something.
        ///
        /// Three kinds and three sources. A click is inspector wiring, read here and now because
        /// the same button can be wired differently on two objects of one type. A key and a pointer
        /// handler are in compiled code, so they were gathered against the type at bake time and are
        /// offered only where that type is on something in the scene — which is what makes
        /// "<c>RightArrow</c> does something" into "<c>RightArrow</c> does something *here*".
        ///
        /// Written on the object rather than once per reading. What a tester needs is not the set of
        /// keys the game reads anywhere, it is the thing they can press and what it is attached to.
        ///
        /// Told to the ledger so that a button appearing, disappearing or being rewired is news. A
        /// screen whose every value held still but whose only button just became unwired has
        /// changed, and a reading that skipped it would be reporting that nothing happened.
        /// </remarks>
        /// <summary>
        /// What each object was found to offer, so the reflection is paid for once.
        /// </summary>
        /// <remarks>
        /// Reading persistent calls is reflection, and the scan already decided it is the kind to
        /// answer once per object and remember. None of what goes in here changes while the game
        /// runs: inspector wiring is serialized data, and which types are on an object is settled
        /// when it is built. Whether the object is <em>showing</em> does change, and that is said
        /// separately.
        ///
        /// Kept against the instance rather than the type, because two buttons of one type are
        /// commonly wired to different methods and one of them may be wired to nothing.
        ///
        /// Dropped whole at a bound, the same trade <see cref="Worth"/> makes: a game that spawns
        /// for an hour would otherwise grow a row for every object it ever made.
        /// </remarks>
        private const int MaxRemembered = 4096;

        private static readonly Dictionary<int, string> Offers = new Dictionary<int, string>();

        /// <returns>True when what this object offers belongs in the reading.</returns>
        private static bool Offered(
            StringBuilder text, Transform transform, Ledger ledger, string identity)
        {
            var id = transform.gameObject.GetInstanceID();

            if (Offers.TryGetValue(id, out var remembered))
            {
                if (remembered.Length == 0)
                {
                    return false;
                }

                if (!ledger.Keep(identity + "|offers", remembered))
                {
                    return false;
                }

                text.Append(remembered);
                return true;
            }

            if (Offers.Count >= MaxRemembered)
            {
                Offers.Clear();
            }

            var said = new StringBuilder(128);
            var calls = new List<PersistentCall>();
            var keys = new List<string>();
            var pointers = new List<string>();

            foreach (var component in transform.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                try
                {
                    PersistentCallReader.Read(component, calls);
                }
                catch (Exception)
                {
                    // One component's wiring, not a reason to lose what the others offer.
                }

                var offer = WatchList.OfferedBy(component.GetType().FullName);

                if (offer == null)
                {
                    continue;
                }

                Add(keys, offer.Keys);
                Add(pointers, offer.Pointers);
            }

            if (calls.Count == 0 && keys.Count == 0 && pointers.Count == 0)
            {
                // Remembered as offering nothing. An object with no wiring and no watched type is
                // the common case, and asking it again every reading is the cost this cache exists
                // to avoid.
                Offers[id] = string.Empty;
                return false;
            }

            said.Append(",\"offers\":{");
            var written = 0;

            if (calls.Count > 0)
            {
                said.Append("\"clicks\":[");

                for (var at = 0; at < calls.Count; at++)
                {
                    if (at > 0)
                    {
                        said.Append(',');
                    }

                    said.Append('{');
                    Json.Property(said, "event", calls[at].Event);
                    said.Append(',');
                    Json.Property(said, "method", calls[at].Method);
                    said.Append(',');
                    Json.Property(said, "on", calls[at].TargetPath);
                    said.Append('}');
                }

                said.Append(']');
                written++;
            }

            written += Flat(said, "keys", keys, written);
            Flat(said, "pointers", pointers, written);

            said.Append('}');

            var rendered = said.ToString();
            Offers[id] = rendered;

            if (!ledger.Keep(identity + "|offers", rendered))
            {
                return false;
            }

            text.Append(rendered);
            return true;
        }

        private static int Flat(StringBuilder text, string name, List<string> offered, int written)
        {
            if (offered.Count == 0)
            {
                return 0;
            }

            if (written > 0)
            {
                text.Append(',');
            }

            offered.Sort(StringComparer.Ordinal);
            text.Append('"').Append(name).Append("\":[");

            for (var at = 0; at < offered.Count; at++)
            {
                if (at > 0)
                {
                    text.Append(',');
                }

                Json.String(text, offered[at]);
            }

            text.Append(']');
            return 1;
        }

        private static void Add(List<string> into, List<string> more)
        {
            foreach (var one in more)
            {
                if (!into.Contains(one))
                {
                    into.Add(one);
                }
            }
        }

        /// <summary>
        /// What a label or a picture is showing, when the reference is one.
        /// </summary>
        /// <remarks>
        /// A field of type <c>TMP_Text</c> was already being watched and already being answered —
        /// with the path and world position of the object the label hangs on, which is the answer
        /// <see cref="Held"/> was built to give. It is the right answer for
        /// <c>MapMove.battle2</c> and the wrong one here: nobody asks where a caption is, they ask
        /// what it says.
        ///
        /// So the reference is asked for its content instead. The path and whether it is showing
        /// stay, because a caption holding the right words while switched off is not the same claim
        /// as one on screen. The world position goes: a caption's coordinates answer no
        /// specification row and are one more value drifting in the last decimal, which is a gate
        /// held open for nothing.
        ///
        /// Matched by type name through <see cref="SceneEvidenceScan"/> rather than compiled
        /// against, for the reason written there — uGUI and TextMeshPro are packages a project may
        /// not have, and this assembly references neither.
        /// </remarks>
        /// <returns>True when the reference was a label or a picture and has been written.</returns>
        private static bool Showing(StringBuilder text, Component component)
        {
            if (component == null)
            {
                return false;
            }

            var shown = SceneEvidenceScan.TextOf(component);
            var role = "label";

            if (shown == null)
            {
                shown = SceneEvidenceScan.SpriteOf(component);
                role = "sprite";
            }

            if (shown == null)
            {
                return false;
            }

            text.Append("\"value\":{");
            Json.Property(text, "path", ScenePath.Of(component.transform));
            text.Append(",\"active\":")
                .Append(component.gameObject.activeInHierarchy ? "true" : "false")
                .Append(',');
            Json.Property(text, role, shown);
            text.Append('}');
            return true;
        }

        /// <summary>
        /// What an animator is doing: the state it is in, named when it can be.
        /// </summary>
        /// <remarks>
        /// The specification says a trigger fires and the screen shows something move; neither on
        /// its own says the moving thing entered the state the row is about. This is what joins
        /// them.
        ///
        /// Unity hands back a hash for the current state and nothing that turns it into words, so
        /// the name is arrived at from the other end — the analysis wrote down every name the code
        /// passes an animator, and <c>IsName</c> answers whether the state is called one of them.
        /// The hash goes out either way, because a state whose name the code never mentions is
        /// still a state that changed and a reader can watch the number move.
        ///
        /// A trigger's name and a state's name are not the same thing. Games commonly use one for
        /// the other and nothing makes them; the name is written only where Unity confirmed it, so
        /// a game that names them differently gets a hash rather than a wrong word.
        ///
        /// The parameters are not read. A trigger is consumed by the state machine within a frame of
        /// being set, so a reading ten times a second would report it as false almost always — a
        /// value that is usually wrong is worse than one that is absent.
        /// </remarks>
        private static void Playing(StringBuilder text, Animator animator)
        {
            text.Append("\"value\":{");
            Json.Property(text, "path", ScenePath.Of(animator.transform));

            AnimatorStateInfo state;

            try
            {
                state = animator.GetCurrentAnimatorStateInfo(0);
            }
            catch (Exception exception)
            {
                // An animator with no controller, or no layer zero. It exists and is doing nothing,
                // which is a different fact from it being absent.
                text.Append(',');
                Json.Property(text, "unread", exception.GetType().Name);
                text.Append('}');
                return;
            }

            text.Append(",\"stateHash\":").Append(state.shortNameHash.ToString(Invariant));

            foreach (var name in WatchList.AnimatorNames)
            {
                if (!state.IsName(name))
                {
                    continue;
                }

                text.Append(',');
                Json.Property(text, "state", name);
                break;
            }

            Parameters(text, animator);
            text.Append('}');
        }

        /// <summary>
        /// The names this animator will answer to.
        /// </summary>
        /// <remarks>
        /// A row saying the <c>Attack</c> trigger fires is written from a <c>SetTrigger("Attack")</c>
        /// in the code, and nothing until now checked that the animator on the object has a
        /// parameter by that name. A misspelling, a controller swapped for another, a trigger
        /// renamed — the code still compiles and the animation silently never plays, which is
        /// exactly the kind of fault a specification exists to catch.
        ///
        /// All of them rather than only the ones the code mentions. Reporting just the matches made
        /// an empty answer mean two different things — this animator has none of those names, or it
        /// has no parameters at all because its controller was never bound — and a reading that
        /// cannot tell those apart is the shape this package refuses everywhere else.
        ///
        /// Names, not values. A trigger is consumed by the state machine within a frame of being
        /// set, so reading one ten times a second reports false almost always, and a value that is
        /// usually wrong is worse than one that is absent. A float parameter driven by movement
        /// would also open the change gate on every beat for a reason no condition mentions. What
        /// the parameters hold is the screen's to show; what they are called is only knowable here.
        /// </remarks>
        private static void Parameters(StringBuilder text, Animator animator)
        {
            AnimatorControllerParameter[] parameters;

            try
            {
                parameters = animator.parameters;
            }
            catch (Exception exception)
            {
                text.Append(',');
                Json.Property(text, "parametersUnread", exception.GetType().Name);
                return;
            }

            if (parameters == null)
            {
                return;
            }

            text.Append(",\"parameters\":[");

            for (var at = 0; at < parameters.Length; at++)
            {
                if (at > 0)
                {
                    text.Append(',');
                }

                Json.String(text, parameters[at].name);
            }

            text.Append(']');
        }

        private static void Coordinate(StringBuilder text, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                text.Append("null");
                return;
            }

            text.Append(Math.Round(value, Decimals).ToString("0.####", Invariant));
        }

        private static readonly System.Globalization.CultureInfo Invariant =
            System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>
        /// How many decimal places a float keeps.
        /// </summary>
        /// <remarks>
        /// The change gate hashes this document, so a raw float turns a breathing idle animation
        /// into a state change and the payload goes out every tick. Rounding is what makes the gate
        /// usable, and four places is finer than any comparison the evidence makes.
        /// </remarks>
        private const int Decimals = 4;

        private static void Number(StringBuilder text, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                // Not a number and not writable as JSON. Said, rather than turned into zero.
                Json.Property(text, "unread", "not-a-number");
                return;
            }

            text.Append("\"value\":").Append(Math.Round(value, Decimals).ToString("0.####", Invariant));
        }
    }
}
