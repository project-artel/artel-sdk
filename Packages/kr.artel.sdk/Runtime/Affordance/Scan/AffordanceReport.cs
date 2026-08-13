using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// Everything seen so far, across every scene that has been visited.
    /// </summary>
    /// <remarks>
    /// A game is only ever showing one screen, so a scan is only ever a report about one. Writing
    /// each scan over the last left a file describing whichever scene happened to load most
    /// recently — true, and useless for a specification that has to cover the game.
    ///
    /// Keyed by scene so that visiting a screen twice corrects the entry rather than duplicating
    /// it. A scene reached later in the game, with different state behind it, should describe the
    /// screen as it was found the last time it was actually there.
    /// </remarks>
    public static class AffordanceReport
    {
        /// <summary>
        /// Six: <c>label</c> stopped meaning what it meant.
        /// </summary>
        /// <remarks>
        /// Every version before this one grew. Six is the first that narrowed: <c>label</c> was the
        /// one thing an object showed and is now what is written on something a player can press,
        /// so a reader that knew the old meaning and is handed a new document reads an enemy's
        /// remaining health as a control's name. Sixteen of the sample game's twenty-two were
        /// exactly that.
        ///
        /// A number that a reader refuses when it does not recognise it is the right place to say
        /// so. Refusing loudly is what should happen here — the alternative was leaving it at five
        /// and letting readers that cannot tell the difference carry on quietly getting it wrong.
        ///
        /// <c>capabilities</c> is beside it and does a different job: the number says which
        /// generation a document belongs to, and the list says which promises it makes, so a later
        /// addition that changes nothing's meaning can be announced without shutting the door on
        /// anyone. Six also brings <c>build</c>, <c>selector</c>, <c>visuals</c> and
        /// <c>persistentObjects</c>, all of which only add.
        ///
        /// Two moved evidence out of the components into a table of its own; three added what each
        /// component's inspector fields point at.
        ///
        /// Four adds <c>unplaced</c> beside <c>types</c>. Everything a reader knew stays where it
        /// was and means what it meant — <c>types</c> is still only what was met on a GameObject —
        /// which is the point of a second table rather than a flag inside the first. A reader that
        /// does not know about <c>unplaced</c> cannot be misled by it, and one that does gets the
        /// rules of types the run never reached along with what would have to happen for them to
        /// exist.
        ///
        /// Five adds <c>calledBy</c> next to <c>createdBy</c>, and <c>unread</c> on a condition
        /// nobody could read. Both are additive and a reader that ignores them reads what it read
        /// before — the version moves anyway, because a reader that keys off the number should be
        /// told the shape grew rather than discover it.
        /// </remarks>
        public const int SchemaVersion = 6;

        /// <summary>How many scenes are held before older ones stop being replaced.</summary>
        private const int MaxScenes = 256;

        /// <summary>
        /// How many bytes of never-placed evidence the report will carry.
        /// </summary>
        /// <remarks>
        /// A budget rather than a count, because what costs is the evidence and not the number of
        /// types. Measured on the sample project, two components belonging to an unrelated package
        /// were two megabytes on their own while the whole of the game's own unplaced evidence was a
        /// tenth of that — the analyser bakes every assembly it is given and cannot know which of
        /// them is the game.
        ///
        /// Spent on the entries that are worth the most first: the ones something in a scene is known
        /// to make, then the small ones, so the budget buys as many types as it can. What did not fit
        /// is counted in the gaps.
        /// </remarks>
        private const int UnplacedBudget = 512 * 1024;

        private const int MaxMakers = 8;

        /// <summary>What the analyser writes a call's target as: assembly, type, method, signature.</summary>
        private const string TargetMarker = "\"targetId\":\"";

        private static readonly Dictionary<string, List<string>> Makers =
            new Dictionary<string, List<string>>(System.StringComparer.Ordinal);

        /// <summary>
        /// Types an assembly carries evidence for that no scene was found holding.
        /// </summary>
        /// <remarks>
        /// Asked at the end for the same reason the dangling wiring is: which types were met is only
        /// settled once the walk is over.
        ///
        /// Decided by comparing documents, not names. The catalogue's keys are the names the types
        /// had when they were compiled, and what the scan met may have been renamed since — a build
        /// that is obfuscated would otherwise report every type it met as never placed. Two entries
        /// for one type carry the same document, whatever it is called by then.
        /// </remarks>
        private static List<KeyValuePair<string, string>> Unplaced()
        {
            var placed = new HashSet<string>(Types.Values, System.StringComparer.Ordinal);
            var missing = new List<KeyValuePair<string, string>>();

            foreach (var pair in AffordanceCatalog.Everything())
            {
                if (!Types.ContainsKey(pair.Key) && !placed.Contains(pair.Value))
                {
                    missing.Add(pair);
                }
            }

            // Ordered by what the budget should buy first — something in a scene is known to make
            // it, then whichever is smallest — and by name last so two runs agree byte for byte.
            missing.Sort((left, right) =>
            {
                var madeLeft = Makers.ContainsKey(left.Key) ? 0 : 1;
                var madeRight = Makers.ContainsKey(right.Key) ? 0 : 1;

                if (madeLeft != madeRight)
                {
                    return madeLeft - madeRight;
                }

                return left.Value.Length != right.Value.Length
                    ? left.Value.Length - right.Value.Length
                    : string.CompareOrdinal(left.Key, right.Key);
            });

            return missing;
        }

        /// <summary>
        /// Which types call into each type, read off the evidence the analyser already baked.
        /// </summary>
        /// <remarks>
        /// A type that no scene holds, that nothing makes, and that nothing calls is dead code. The
        /// first two were already known and were not enough: the sample game carries a whole
        /// superseded namespace whose types read exactly like types the run simply had not got to,
        /// and their rules were being written down as the game's rules.
        ///
        /// Read out of the document text rather than by parsing it. The call targets are the only
        /// thing being looked for, the documents are held as strings anyway, and a parser here would
        /// be a second reader of a format that already has one.
        ///
        /// Names, not documents — unlike <see cref="Unplaced"/>, which compares documents because a
        /// renamed type must still be recognised. Both sides here come out of the same baked
        /// evidence, so both carry whatever names the compiler saw and they match each other.
        ///
        /// Being called does not make a type alive: its caller may be dead too. That is why the
        /// callers are listed rather than reduced to a flag — the reader can see whether they are
        /// themselves in this same table.
        /// </remarks>
        private static Dictionary<string, List<string>> Callers()
        {
            var callers = new Dictionary<string, List<string>>(System.StringComparer.Ordinal);

            foreach (var pair in AffordanceCatalog.Everything())
            {
                var document = pair.Value;
                var at = document.IndexOf(TargetMarker, System.StringComparison.Ordinal);

                while (at >= 0)
                {
                    var from = at + TargetMarker.Length;
                    var bar = document.IndexOf('|', from);
                    var end = bar < 0 ? -1 : document.IndexOf('|', bar + 1);

                    if (end > bar)
                    {
                        Calls(callers, document.Substring(bar + 1, end - bar - 1), pair.Key);
                    }

                    at = document.IndexOf(TargetMarker, from, System.StringComparison.Ordinal);
                }
            }

            return callers;
        }

        private static void Calls(
            Dictionary<string, List<string>> callers, string callee, string caller)
        {
            if (callee.Length == 0 || callee == caller)
            {
                return;
            }

            if (!callers.TryGetValue(callee, out var found))
            {
                found = new List<string>();
                callers[callee] = found;
            }

            if (!found.Contains(caller) && found.Count < MaxMakers)
            {
                found.Add(caller);
            }
        }

        /// <summary>
        /// Notes that a prefab held by something in a scene carries this type.
        /// </summary>
        /// <remarks>
        /// The one fact that separates a type nobody ever makes from a type the run has not got to
        /// yet. Without it the report has a list of types it never saw and no way to tell which of
        /// them are dead code — and publishing a dead type's rules as the game's rules is the one
        /// way this table could produce a specification that is simply false.
        /// </remarks>
        internal static void Creates(string carriedType, string ownerType, string field)
        {
            if (string.IsNullOrEmpty(carriedType) || string.IsNullOrEmpty(ownerType))
            {
                return;
            }

            if (!Makers.TryGetValue(carriedType, out var makers))
            {
                makers = new List<string>();
                Makers[carriedType] = makers;
            }

            var maker = ownerType + "." + field;

            if (!makers.Contains(maker) && makers.Count < MaxMakers)
            {
                makers.Add(maker);
            }
        }

        private static readonly List<string> Order = new List<string>();
        private static readonly Dictionary<string, string> Objects = new Dictionary<string, string>();
        private static readonly Dictionary<string, List<string>> Gaps = new Dictionary<string, List<string>>();
        private static readonly Dictionary<string, string> Types = new Dictionary<string, string>();

        /// <summary>Scenes the report has something to say about.</summary>
        public static int SceneCount => Order.Count;

        internal static void Merge(string scene, string objects, List<string> gaps)
        {
            var name = string.IsNullOrEmpty(scene) ? "(unnamed)" : scene;

            if (!Objects.ContainsKey(name))
            {
                if (Order.Count >= MaxScenes)
                {
                    return;
                }

                Order.Add(name);
            }

            Objects[name] = objects;
            Gaps[name] = gaps;
        }

        /// <summary>What the game kept across scene loads, read once for the whole report.</summary>
        internal static void Persistent(string objects, List<string> gaps)
        {
            _persistent = objects ?? string.Empty;
            _persistentRead = true;

            if (gaps == null)
            {
                return;
            }

            foreach (var gap in gaps)
            {
                if (!_persistentGaps.Contains(gap))
                {
                    _persistentGaps.Add(gap);
                }
            }
        }

        private static string _persistent = string.Empty;
        private static bool _persistentRead;
        private static readonly List<string> _persistentGaps = new List<string>();

        /// <summary>Adds one more thing to say about a scene already read.</summary>
        internal static void Note(string scene, string gap)
        {
            var name = string.IsNullOrEmpty(scene) ? "(unnamed)" : scene;

            if (Gaps.TryGetValue(name, out var already) && !already.Contains(gap))
            {
                already.Add(gap);
            }
        }

        /// <summary>
        /// Records what a type's evidence says, once, however many of it a scene holds.
        /// </summary>
        /// <remarks>
        /// Told the first time the type is met and not asked again. The evidence is baked onto the
        /// type at compile time and cannot differ between two instances of it.
        /// </remarks>
        internal static bool Knows(string type)
        {
            return Types.ContainsKey(type);
        }

        internal static void Learn(string type, string evidenceArray)
        {
            if (!string.IsNullOrEmpty(type) && !Types.ContainsKey(type))
            {
                Types[type] = evidenceArray;
            }
        }

        /// <summary>
        /// Notes a type that a scene's wiring calls into.
        /// </summary>
        /// <remarks>
        /// Whether it has any evidence cannot be answered here — the type may not have been met yet,
        /// and a later scene may be where it lives. Asked at the end instead, when every scene that
        /// is going to be visited has been.
        /// </remarks>
        internal static void Wired(string type)
        {
            if (!string.IsNullOrEmpty(type))
            {
                WiredTo.Add(type);
            }
        }

        private static readonly HashSet<string> WiredTo = new HashSet<string>();

        private static int _unplacedOmitted;

        /// <summary>Starts again, so a walk does not carry answers from a previous one.</summary>
        public static void Forget()
        {
            Order.Clear();
            Objects.Clear();
            Gaps.Clear();
            Types.Clear();
            WiredTo.Clear();
            Makers.Clear();
            _unplacedOmitted = 0;
            _persistent = string.Empty;
            _persistentRead = false;
            _persistentGaps.Clear();
            SerializedReferences.Forget();
        }

        /// <summary>
        /// Whether anything had run by the time the scene was read.
        /// </summary>
        /// <remarks>
        /// Most of a report is about code and holds whenever it is read. What an object was showing
        /// does not: an editor walk opens a scene and reads it as it was saved, while a player has
        /// been through <c>Awake</c>, <c>Start</c> and however much play came before the walk. The
        /// same field means "what the scene says" in one and "what it said at that moment" in the
        /// other — an enemy's label is its authored <c>20</c> in one and its remaining health in the
        /// other.
        ///
        /// Said in the document rather than left to whoever named the file, because a reader holding
        /// one report has no other way to know, and reading a moment as a rule is how a test gets
        /// written against a number that will not be there next time.
        /// </remarks>
        private static string Capture()
        {
            if (!Application.isEditor)
            {
                return "player";
            }

            return Application.isPlaying ? "editor-play" : "editor";
        }

        /// <summary>
        /// What made this document, said so that two of them can be told apart.
        /// </summary>
        /// <remarks>
        /// A report that does not say where it came from cannot be argued with. Two files disagree
        /// and nobody can tell whether the game changed, the analyser changed, or one of them was
        /// taken from a different build — which is the position a reader was in until now, and the
        /// reason a review of these documents had to say it could not establish their provenance.
        ///
        /// No clock and no session number, on purpose. A time does not answer the question — "when"
        /// does not say what was analysed — and it would put a difference into every pair of files
        /// that a reader then has to look past. <c>evidence</c> answers it instead: a fingerprint of
        /// the baked documents themselves, so the same game read by the same analyser gives the same
        /// value and a change to either gives another. That is the number two files should be
        /// compared by.
        ///
        /// It is worth saying what this does not make stable. Two scans of one unchanged game still
        /// differ, because a scene reference is written with the instance id Unity gave it and those
        /// are handed out afresh each session. The evidence — everything read out of the code — is
        /// the same bytes both times, which is how the Mono and IL2CPP builds were shown to agree;
        /// what the scene half writes about the same object can differ in that one field.
        /// </remarks>
        /// <summary>
        /// What the fields in this document are promised to mean.
        /// </summary>
        /// <remarks>
        /// The version number could not carry this. A reader refuses a number it does not know, so
        /// raising it to say one field's meaning had narrowed would have shut the door on every
        /// reader at once — and leaving it alone said the shape had only grown, which was not true
        /// of <c>label</c>. It went from "the one thing this object showed" to "what is written on
        /// something a player can press", and a document written before that change looks exactly
        /// like one written after.
        ///
        /// Nor can one field stand in for another. <c>build</c> arrived a commit before the roles
        /// did, so a report carrying it may still mean the old <c>label</c>; a reader inferring the
        /// contract from what is present would get that pair wrong.
        ///
        /// So each promise is named and a reader asks for the one it needs. What a run happened to
        /// find is not one of these: a player scan that kept nothing across a load still says
        /// <c>persistent-objects-v1</c>, because the claim is about what the field would have meant
        /// had there been any.
        /// </remarks>
        private static void Promises(StringBuilder text)
        {
            text.Append("\"capabilities\":[");

            for (var index = 0; index < Promised.Length; index++)
            {
                if (index > 0)
                {
                    text.Append(',');
                }

                Json.String(text, Promised[index]);
            }

            text.Append(']');
        }

        private static readonly string[] Promised =
        {
            // `build` is present and says what made this document.
            "build-info-v1",

            // Every object carries `selector`, unique within its scene for this reading.
            "selector-v1",

            // `visuals[]` gives every text and picture a role, and `label` and `sprite` name a
            // control or are absent — they are no longer whatever the object happened to show.
            "visual-roles-v1",

            // `persistentObjects` holds what the game kept across scene loads, and the gap that
            // said nobody looked is only written when nobody could.
            "persistent-objects-v1"
        };

        private static void Built(StringBuilder text)
        {
            text.Append("\"build\":{");
            Json.Property(text, "unity", Application.unityVersion);
            text.Append(',');
            Json.Property(text, "platform", Application.platform.ToString());
            text.Append(',');
            Json.Property(text, "backend", Backend());
            text.Append(',');
            Json.Property(text, "development", Debug.isDebugBuild);
            text.Append(',');
            Json.Property(text, "sdk", PackageVersion);
            text.Append(',');
            Json.Property(text, "evidence", Fingerprint());
            text.Append('}');
        }

        /// <summary>Kept in step with `package.json` by hand; there is nowhere else to read it from.</summary>
        private const string PackageVersion = "0.1.0";

        private static string Backend()
        {
#if ENABLE_IL2CPP
            return "il2cpp";
#elif ENABLE_MONO
            return "mono";
#else
            return "unknown";
#endif
        }

        /// <summary>
        /// One number standing for every document that was baked onto this game.
        /// </summary>
        /// <remarks>
        /// Sorted before it is read, so the order the assemblies happen to load in does not change
        /// the answer. Not a security digest and not claimed as one — it is here to tell two
        /// analyses apart, and a cheap mixing function does that.
        /// </remarks>
        private static string Fingerprint()
        {
            var named = new List<string>(AffordanceCatalog.Everything().Keys);
            named.Sort(System.StringComparer.Ordinal);

            var everything = AffordanceCatalog.Everything();
            var hash = 14695981039346656037UL;

            foreach (var name in named)
            {
                hash = Mixed(hash, name);
                hash = Mixed(hash, everything[name]);
            }

            return hash.ToString("x16");
        }

        private static ulong Mixed(ulong hash, string text)
        {
            foreach (var letter in text)
            {
                hash = (hash ^ letter) * 1099511628211UL;
            }

            return (hash ^ '\n') * 1099511628211UL;
        }

        public static string Compose()
        {
            // Worked out once: the table below writes it and the gap list counts it.
            var missing = Unplaced();
            var callers = Callers();

            var text = new StringBuilder(16384);
            text.Append("{\"schema\":").Append(SchemaVersion).Append(",\"capture\":");
            Json.String(text, Capture());
            text.Append(',');
            Promises(text);
            text.Append(',');
            Built(text);
            text.Append(",\"scenes\":[");

            for (var index = 0; index < Order.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(',');
                }

                Json.String(text, Order[index]);
            }

            text.Append("],\"types\":{");

            // Sorted so that two runs over the same game produce the same bytes. Scene order
            // follows where the game was walked; this has no such order to follow.
            var named = new List<string>(Types.Keys);
            named.Sort(System.StringComparer.Ordinal);

            for (var index = 0; index < named.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(',');
                }

                Json.String(text, named[index]);
                text.Append(':').Append(Types[named[index]]);
            }

            // Everything the assemblies know about that no scene was found holding. Its own table
            // rather than a flag inside the one above, so that nothing which reads `types` as "what
            // is on screen" is quietly made wrong by it.
            text.Append("},\"unplaced\":{");

            var spent = 0;
            var written = 0;

            for (var index = 0; index < missing.Count; index++)
            {
                var entry = missing[index];

                if (spent > 0 && spent + entry.Value.Length > UnplacedBudget)
                {
                    continue;
                }

                spent += entry.Value.Length;

                if (written > 0)
                {
                    text.Append(',');
                }

                written++;
                Json.String(text, entry.Key);
                text.Append(":{\"evidence\":").Append(entry.Value);

                // Who would have to make one. A prefab held by something that *is* in a scene is a
                // way in; nothing at all is the shape of dead code, and the two must not read alike.
                text.Append(",\"createdBy\":[");

                if (Makers.TryGetValue(entry.Key, out var makers))
                {
                    for (var maker = 0; maker < makers.Count; maker++)
                    {
                        if (maker > 0)
                        {
                            text.Append(',');
                        }

                        Json.String(text, makers[maker]);
                    }
                }

                text.Append("],\"calledBy\":[");

                if (callers.TryGetValue(entry.Key, out var calling))
                {
                    for (var caller = 0; caller < calling.Count; caller++)
                    {
                        if (caller > 0)
                        {
                            text.Append(',');
                        }

                        Json.String(text, calling[caller]);
                    }
                }

                text.Append("]}");
            }

            _unplacedOmitted = missing.Count - written;

            text.Append("},\"objects\":[");

            var wrote = false;

            foreach (var scene in Order)
            {
                var objects = Objects[scene];

                if (string.IsNullOrEmpty(objects))
                {
                    continue;
                }

                if (wrote)
                {
                    text.Append(',');
                }

                text.Append(objects);
                wrote = true;
            }

            text.Append("],\"persistentObjects\":[").Append(_persistent).Append("],\"gaps\":[");

            var said = new HashSet<string>();
            var first = true;

            foreach (var scene in Order)
            {
                foreach (var gap in Gaps[scene])
                {
                    // Scoped by scene. A gap that applies to one screen and not another is a
                    // different fact from one that applies everywhere, and merging them loses which.
                    var scoped = scene + ":" + gap;

                    if (!said.Add(scoped))
                    {
                        continue;
                    }

                    if (!first)
                    {
                        text.Append(',');
                    }

                    Json.String(text, scoped);
                    first = false;
                }
            }

            // Said once about the report rather than once per screen. Objects kept across scene
            // loads are in no screen and in all of them, and a walk that never ran cannot have
            // reached them.
            if (!_persistentRead)
            {
                if (!first)
                {
                    text.Append(',');
                }

                Json.String(text, "dont-destroy-on-load-not-walked");
                first = false;
            }

            foreach (var gap in _persistentGaps)
            {
                if (!first)
                {
                    text.Append(',');
                }

                Json.String(text, "persistent:" + gap);
                first = false;
            }

            // The count stays in the gaps because it is a fact about the report as a whole. The
            // rules themselves went into a table of their own, above.
            if (missing.Count > 0)
            {
                if (!first)
                {
                    text.Append(',');
                }

                Json.String(text, "evidence-never-placed-count:" + missing.Count);
                first = false;
            }

            if (_unplacedOmitted > 0)
            {
                text.Append(',');
                Json.String(text, "unplaced-evidence-omitted:" + _unplacedOmitted);
            }

            // A button wired to a method on a type that has no evidence is a dead end that looks
            // like a live one: the call is in the report, the thing it calls is not. Said here
            // rather than per scene because whether a type is known is only settled once the walk
            // is over — the object carrying it may be in a scene visited later, or in none of them.
            var dangling = new List<string>();

            foreach (var type in WiredTo)
            {
                if (!Types.ContainsKey(type))
                {
                    dangling.Add("wired-target-has-no-evidence:" + type);
                }
            }

            dangling.Sort(System.StringComparer.Ordinal);

            foreach (var gap in dangling)
            {
                if (!first)
                {
                    text.Append(',');
                }

                Json.String(text, gap);
                first = false;
            }

            text.Append("]}");
            return text.ToString();
        }
    }
}
