using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace Artel.Affordances.Live
{
    /// <summary>One member the evidence asks to be read while the game runs.</summary>
    internal sealed class Watched
    {
        internal string Declaring;
        internal string Member;

        /// <summary>What the analysis said the value was, before anything read it.</summary>
        /// <remarks>
        /// Carried so that a value can be reported as what it is rather than as what it prints as.
        /// A bool comes off a field as <c>True</c> and an int as <c>1</c>, and the report has spent
        /// a long time learning not to let those two look alike.
        /// </remarks>
        internal string Type;

        internal bool Static;

        /// <summary>
        /// What everything other than reflection calls this, when a compiler renamed it.
        /// </summary>
        /// <remarks>
        /// An automatic property is a field called <c>&lt;Instance&gt;k__BackingField</c>. That name
        /// is what finds it and nothing else uses it — the evidence says
        /// <c>StageDataSingleton.Instance</c> — so a reading naming the field would not join to the
        /// condition it answers.
        /// </remarks>
        internal string Property;

        /// <summary>What was read off the field, when the field is not itself the value.</summary>
        internal string Via;

        /// <summary>The field itself, once reflection has been asked. Null until then.</summary>
        internal FieldInfo Field;

        /// <summary>The type it lives on, for finding the instances that carry it.</summary>
        internal Type Owner;

        /// <summary>
        /// Whether a condition or an effect named this member, or it is merely readable.
        /// </summary>
        /// <remarks>
        /// Both are read and both go out; the difference is what a reader should do with them. A
        /// member the evidence asked for is one some specification row turns on, and a reader
        /// checking that row wants exactly those. The rest are carried for the rows nobody has
        /// written yet — see <see cref="Readable"/> for why they are carried at all — and a reader
        /// that treated them alike would be reading a game's whole state where it meant to read a
        /// premise.
        ///
        /// True for everything the watch list itself holds, since that list is nothing but what was
        /// asked for.
        /// </remarks>
        internal bool Asked = true;

        /// <summary>What names this member apart from any other.</summary>
        internal string Key => Declaring + "::" + Member;
    }

    /// <summary>
    /// What to look at while the game runs, as the analysis worked it out.
    /// </summary>
    /// <remarks>
    /// The other SDK asked the game to mark its own fields. That put the decision in the wrong place
    /// twice over: a field nobody thought to mark is invisible however much the report turns on it,
    /// and the escape hatch — read every serialized field — makes an idle animation look like a
    /// state change, which is why it was never used on the live path.
    ///
    /// Nothing is marked here. The analysis already read the instruction behind every condition and
    /// every effect, so the members worth watching fell out of work that was being done anyway, and
    /// the list is exactly as long as the evidence requires rather than as long as the game is.
    ///
    /// Read once. Resolving a name to a field is reflection, and reflection on a hundred members
    /// every poll would cost more than reading them.
    /// </remarks>
    internal static class WatchList
    {
        private const string ResourceName = "kr.artel.affordance.watch";

        private static List<Watched> _resolved;
        private static List<string> _animatorNames;
        private static Dictionary<string, Offer> _offers;

        /// <summary>
        /// The ways a player can set a type going, when it is on something in the scene.
        /// </summary>
        /// <remarks>
        /// A reading says what the game holds and, without this, nothing about what may be done to
        /// it next. The scan finds buttons on its own — a persistent call is wiring anybody can
        /// see — and these are the two it cannot: which keys mean something here, and which objects
        /// answer a pointer. Both are literals and method names inside compiled code.
        ///
        /// Keyed by the type rather than the scene, because what makes a key meaningful is that
        /// something reading it is on screen now. A reading walks the objects that are there and
        /// asks each of their components, so a screen the type is absent from offers nothing without
        /// anyone having to work out which screen this is.
        /// </remarks>
        internal sealed class Offer
        {
            internal readonly List<string> Keys = new List<string>();
            internal readonly List<string> Pointers = new List<string>();
        }

        /// <summary>What this type answers to, or null.</summary>
        internal static Offer OfferedBy(string declaring)
        {
            All();

            if (declaring == null || _offers == null)
            {
                return null;
            }

            return _offers.TryGetValue(declaring, out var offer) ? offer : null;
        }

        /// <summary>
        /// Every name the game's code hands an animator.
        /// </summary>
        /// <remarks>
        /// Unity gives back a hash for the state an animator is in and nothing that turns it into
        /// words, so a reading can say the state changed and not to what — the half a recording of
        /// the screen already shows, and not the half it cannot. <c>IsName</c> answers the question
        /// the other way round, so a reading that knows the candidates can name the state by asking.
        /// </remarks>
        internal static IReadOnlyList<string> AnimatorNames
        {
            get
            {
                All();
                return _animatorNames;
            }
        }

        /// <summary>How many the analysis named but reflection could not find.</summary>
        /// <remarks>
        /// Obfuscation is the ordinary cause: the list holds the names the code had when it was
        /// compiled and the assembly ships with different ones. Said rather than skipped, because a
        /// watcher reporting eleven of two hundred members with no explanation looks like a game
        /// that has almost no state.
        /// </remarks>
        internal static int Unresolved { get; private set; }

        /// <summary>How many values the analysis had nowhere to read, summed over assemblies.</summary>
        internal static int Unwatchable { get; private set; }

        internal static IReadOnlyList<Watched> All()
        {
            if (_resolved != null)
            {
                return _resolved;
            }

            _resolved = new List<Watched>();
            _animatorNames = new List<string>();
            _offers = new Dictionary<string, Offer>(StringComparer.Ordinal);
            Unresolved = 0;
            Unwatchable = 0;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Read(assembly, _resolved);
                }
                catch (Exception)
                {
                    // A dynamic assembly, or one whose resources will not open. Skipping it makes
                    // the list shorter, never wrong.
                }
            }

            return _resolved;
        }

        internal static void Forget()
        {
            _resolved = null;
        }

        private static void Read(Assembly assembly, List<Watched> into)
        {
            using (var packed = assembly.GetManifestResourceStream(ResourceName))
            {
                if (packed == null)
                {
                    return;
                }

                string text;

                using (var expanded = new DeflateStream(packed, CompressionMode.Decompress))
                using (var reader = new StreamReader(expanded, Encoding.UTF8))
                {
                    text = reader.ReadToEnd();
                }

                Unwatchable += Number(text, "\"unwatchable\":");
                Names(text, _animatorNames);

                foreach (var entry in Entries(text, "watch"))
                {
                    Resolve(assembly, entry, into);
                }

                foreach (var entry in Entries(text, "inputs"))
                {
                    Offered(entry);
                }
            }
        }

        /// <summary>
        /// Finds each object in the <c>watch</c> array, without a JSON parser.
        /// </summary>
        /// <remarks>
        /// The document is written by the same package that reads it, one field shape, no nesting
        /// inside the array elements and no strings holding braces — a type name cannot contain one.
        /// Bringing a parser into the runtime assembly for that would be weight on every game that
        /// ships this, and the writer is fifty lines away.
        /// </remarks>
        private static IEnumerable<string> Entries(string text, string array)
        {
            var start = text.IndexOf("\"" + array + "\":[", StringComparison.Ordinal);

            if (start < 0)
            {
                yield break;
            }

            var index = start;

            while (true)
            {
                var open = text.IndexOf('{', index);

                if (open < 0)
                {
                    yield break;
                }

                var close = text.IndexOf('}', open);

                if (close < 0)
                {
                    yield break;
                }

                yield return text.Substring(open + 1, close - open - 1);
                index = close + 1;

                // Stopping at the array's own end rather than running to the next one. Told by what
                // follows an entry — a comma or the closing bracket, and nothing else — because a
                // bracket count would be wrong the moment a field holds a generic type name, which
                // carries brackets of its own.
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                {
                    index++;
                }

                if (index >= text.Length || text[index] == ']')
                {
                    yield break;
                }
            }
        }

        private static void Resolve(Assembly assembly, string entry, List<Watched> into)
        {
            var declaring = Text(entry, "\"declaring\":\"");
            var member = Text(entry, "\"member\":\"");

            if (declaring == null || member == null)
            {
                return;
            }

            var owner = assembly.GetType(declaring, false);

            if (owner == null)
            {
                Unresolved++;
                return;
            }

            // Private and inherited both. A game's state is mostly private, and a condition on a
            // field a base class declares is still a condition on this component.
            var field = owner.GetField(
                member,
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);

            if (field == null)
            {
                Unresolved++;
                return;
            }

            into.Add(new Watched
            {
                Declaring = declaring,
                Member = member,
                Property = Text(entry, "\"property\":\""),
                Via = Walkable(field.FieldType, Text(entry, "\"via\":\"")),
                Type = Text(entry, "\"type\":\""),
                Static = entry.Contains("\"static\":true"),
                Field = field,
                Owner = owner
            });
        }

        /// <summary>
        /// The path, when it can actually be walked from what the field holds — otherwise nothing.
        /// </summary>
        /// <remarks>
        /// Settled once here rather than asked on every reading, because whether a type has a member
        /// does not change while the game runs, and a reading that answered differently from the
        /// name beside it would be worse than either answer.
        ///
        /// Dropped rather than reported when it will not walk. The evidence strips <c>transform</c>
        /// on the way to a field, so a destination written as
        /// <c>MapMove.battle1.transform.position</c> arrives here as <c>position</c> on a
        /// <c>GameObject</c>, which has no such member — while the coordinates that row wants are
        /// already what a reference is written as. Calling that unreadable took thirteen rows'
        /// destinations away; leaving the field to answer for itself is what this did before there
        /// were paths at all.
        ///
        /// Judged against the declared type. A field holding something more derived may offer more
        /// than this can see, and the cost of that is a path not taken rather than a wrong value.
        /// </remarks>
        private static string Walkable(Type from, string path)
        {
            if (path == null || from == null)
            {
                return null;
            }

            const BindingFlags Flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var step in path.Split('.'))
            {
                var field = from.GetField(step, Flags);

                if (field != null)
                {
                    from = field.FieldType;
                    continue;
                }

                var property = from.GetProperty(step, Flags);

                if (property == null || !property.CanRead ||
                    property.GetIndexParameters().Length != 0)
                {
                    return null;
                }

                from = property.PropertyType;
            }

            return path;
        }

        /// <summary>One type's offered inputs, out of an entry of the <c>inputs</c> array.</summary>
        private static void Offered(string entry)
        {
            var declaring = Text(entry, "\"declaring\":\"");

            if (declaring == null)
            {
                return;
            }

            if (!_offers.TryGetValue(declaring, out var offer))
            {
                offer = new Offer();
                _offers[declaring] = offer;
            }

            Listed(entry, "\"keys\":[", offer.Keys);
            Listed(entry, "\"pointers\":[", offer.Pointers);
        }

        /// <summary>
        /// A flat array of strings sitting inside one entry, up to that array's own end.
        /// </summary>
        /// <remarks>
        /// Bounded to the array rather than run to the document's end, which is what
        /// <see cref="Names"/> can do because it reads the only array of its name. Two of these sit
        /// side by side in one entry, so the first would otherwise swallow the second.
        /// </remarks>
        private static void Listed(string entry, string key, List<string> into)
        {
            var start = entry.IndexOf(key, StringComparison.Ordinal);

            if (start < 0)
            {
                return;
            }

            var index = start + key.Length;
            var end = entry.IndexOf(']', index);

            if (end < 0)
            {
                return;
            }

            while (index < end)
            {
                var open = entry.IndexOf('"', index);

                if (open < 0 || open > end)
                {
                    return;
                }

                var close = entry.IndexOf('"', open + 1);

                if (close < 0 || close > end)
                {
                    return;
                }

                var said = entry.Substring(open + 1, close - open - 1);

                if (said.Length > 0 && !into.Contains(said))
                {
                    into.Add(said);
                }

                index = close + 1;
            }
        }

        /// <summary>Reads the flat string array the writer put the animator names in.</summary>
        private static void Names(string text, List<string> into)
        {
            const string key = "\"animatorNames\":[";

            var start = text.IndexOf(key, StringComparison.Ordinal);

            if (start < 0)
            {
                return;
            }

            // Past the key's own closing quote, not at it. Starting on the key meant the first pair
            // of quotes found was the key's own and its own name became the first entry — measured,
            // the list came out as `:[` and `,`, so every state went unnamed and the reason was
            // invisible because an unnamed state is also what a game that names them differently
            // produces.
            var index = start + key.Length - 1;
            var end = text.IndexOf(']', index);

            while (index < end)
            {
                var open = text.IndexOf('"', index);

                if (open < 0 || open > end)
                {
                    return;
                }

                var close = text.IndexOf('"', open + 1);

                if (close < 0 || close > end)
                {
                    return;
                }

                var name = text.Substring(open + 1, close - open - 1);

                if (name.Length > 0 && !into.Contains(name))
                {
                    into.Add(name);
                }

                index = close + 1;
            }
        }

        private static string Text(string entry, string key)
        {
            var at = entry.IndexOf(key, StringComparison.Ordinal);

            if (at < 0)
            {
                return null;
            }

            var from = at + key.Length;
            var to = entry.IndexOf('"', from);
            return to < 0 ? null : entry.Substring(from, to - from);
        }

        private static int Number(string text, string key)
        {
            var at = text.IndexOf(key, StringComparison.Ordinal);

            if (at < 0)
            {
                return 0;
            }

            var from = at + key.Length;
            var to = from;

            while (to < text.Length && (char.IsDigit(text[to]) || text[to] == '-'))
            {
                to++;
            }

            return int.TryParse(text.Substring(from, to - from), out var value) ? value : 0;
        }
    }
}
