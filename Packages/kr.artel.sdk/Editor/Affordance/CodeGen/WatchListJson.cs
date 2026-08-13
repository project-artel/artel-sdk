using System;
using System.Collections.Generic;
using System.Text;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// The whole of what an assembly's evidence asks somebody to look at, written once.
    /// </summary>
    /// <remarks>
    /// Per assembly rather than per type because that is the shape of the question. A watcher does
    /// not start from a component and ask what to read on it; it starts with nothing and asks what
    /// in this game is worth reading at all, and the answer includes members of types no GameObject
    /// carries — a static field on a map controller decides five screens and hangs off nothing.
    ///
    /// It is a union and it is small. Every condition in the sample game names one of about seventy
    /// distinct values and every effect one of about a hundred and fifty, and most of both are the
    /// same handful said again in another branch. What comes out is short enough to read every poll
    /// and short enough for a person to check by eye, which is the property that matters: a list
    /// nobody can audit is a list nobody can trust.
    ///
    /// Sorted, so that analysing the same assembly twice gives the same bytes. The evidence resource
    /// is deflated without a timestamp for the same reason, and a set iterated in hash order would
    /// have quietly undone it.
    /// </remarks>
    internal static class WatchListJson
    {
        /// <summary>
        /// One: members named by conditions and by effects, as declaring type and member.
        /// </summary>
        /// <remarks>
        /// Its own number rather than the evidence document's. The two are written by the same pass
        /// and read by different readers for different reasons, and a watcher that understands this
        /// list has no opinion at all about what a record looks like.
        /// </remarks>
        internal const int SchemaVersion = 1;

        /// <summary>How many members are written before the rest are left out and said to be.</summary>
        /// <remarks>
        /// A bound because this is read every poll. Two hundred is the sample game entire; a game
        /// that wants more than a thousand distinct values watched at once is asking for a dump, and
        /// a dump is the thing this exists to avoid. What is dropped is said, so a short list is
        /// never mistaken for a complete one.
        /// </remarks>
        private const int MaxMembers = 1024;

        /// <summary>
        /// What the analysis found to watch, and what it refused to.
        /// </summary>
        /// <remarks>
        /// The refusals are counted and not named. A condition on <c>spellCards.Count</c> or on
        /// <c>CompareTag("Spell")</c> is produced by a call, and there is no member behind it to
        /// read — calling it to find out would be playing the game rather than watching it. The
        /// count is what says how much of the report a watcher can check its own premises for; the
        /// individual ones are already in the evidence, spelled out, where they belong.
        /// </remarks>
        internal sealed class Result
        {
            internal string Document;
            internal int Watched;
            internal int Unwatchable;
        }

        internal static Result Write(IEnumerable<Variant> variants)
        {
            var found = new Dictionary<string, WatchTarget>(StringComparer.Ordinal);
            var names = new List<string>();
            var unwatchable = 0;

            var offers = new Dictionary<string, Offer>(StringComparer.Ordinal);

            foreach (var variant in variants)
            {
                Taking(variant, offers);

                Gather(variant.When, found, ref unwatchable);

                foreach (var outcome in variant.Outcomes)
                {
                    Take(outcome.Watch, found, ref unwatchable);

                    // Not counted as a refusal when it is absent. Most effects put a value somewhere
                    // rather than move it from somewhere, and having no source is the ordinary case
                    // rather than a thing that could not be read.
                    if (outcome.WatchSource != null)
                    {
                        Take(outcome.WatchSource, found, ref unwatchable);
                    }

                    if (outcome.AnimatorName != null && !names.Contains(outcome.AnimatorName))
                    {
                        names.Add(outcome.AnimatorName);
                    }
                }
            }

            var keys = new List<string>(found.Keys);
            keys.Sort(StringComparer.Ordinal);

            var text = new StringBuilder(1024);
            text.Append("{\"schema\":").Append(SchemaVersion).Append(",\"watch\":[");

            var written = 0;

            foreach (var key in keys)
            {
                if (written >= MaxMembers)
                {
                    break;
                }

                if (written > 0)
                {
                    text.Append(',');
                }

                var target = found[key];
                text.Append('{');
                Property(text, "declaring", target.Declaring);
                text.Append(',');
                Property(text, "member", target.Member);
                text.Append(',');

                if (target.Property != null)
                {
                    Property(text, "property", target.Property);
                    text.Append(',');
                }

                if (target.Via != null)
                {
                    Property(text, "via", target.Via);
                    text.Append(',');
                }

                Property(text, "type", target.Type);
                text.Append(",\"static\":").Append(target.Static ? "true" : "false");
                text.Append('}');
                written++;
            }

            text.Append(']');

            // Every name the code hands an animator, so a reading can ask whether the state is
            // called one of them. Unity answers `IsName` and nothing that turns a hash into words,
            // so without this list a reading can say an animator changed and never say to what.
            names.Sort(StringComparer.Ordinal);
            text.Append(",\"animatorNames\":[");

            for (var at = 0; at < names.Count; at++)
            {
                if (at > 0)
                {
                    text.Append(',');
                }

                EvidenceJson.String(text, names[at]);
            }

            text.Append(']');

            Offers(text, offers);

            text.Append(",\"unwatchable\":").Append(unwatchable);

            if (keys.Count > written)
            {
                text.Append(",\"dropped\":").Append(keys.Count - written);
            }

            text.Append('}');

            return new Result { Document = text.ToString(), Watched = written, Unwatchable = unwatchable };
        }

        /// <summary>
        /// The ways a player can set a type going, gathered per type rather than per case.
        /// </summary>
        /// <remarks>
        /// A reading says what the game holds. It does not say what a tester may do next, and
        /// without that an agent reading the channel has the whole state of a screen and no idea
        /// which of the things on it will answer to anything.
        ///
        /// Buttons the scan already finds, because a persistent call is wiring a tester can see. The
        /// two it cannot see are here: which keys mean something on this screen, and which objects
        /// answer a pointer. Both are known only from the compiled code — a key is a literal inside
        /// a method, and a drag handler is a method name the engine calls — and both are useless in
        /// the abstract. Knowing the game reads <c>RightArrow</c> somewhere is not knowing that
        /// pressing it now would do anything.
        ///
        /// So they are gathered against the type that carries them. A reading walks the objects that
        /// are actually on screen, and a type that is not on one of them offers nothing.
        /// </remarks>
        private sealed class Offer
        {
            internal readonly List<string> Keys = new List<string>();
            internal readonly List<string> Pointers = new List<string>();
        }

        /// <summary>
        /// Engine messages that a tester can cause with a pointer.
        /// </summary>
        /// <remarks>
        /// A subset of the messages the analysis follows, and deliberately not all of them.
        /// <c>OnTriggerEnter2D</c> is an entry point and is reached by a projectile arriving, not by
        /// anybody doing anything — offering it as an input would put a step in a test that no
        /// tester can carry out.
        /// </remarks>
        private static readonly HashSet<string> Pointered = new HashSet<string>(StringComparer.Ordinal)
        {
            "OnMouseDown", "OnMouseUp", "OnMouseUpAsButton", "OnMouseDrag",
            "OnMouseEnter", "OnMouseExit", "OnMouseOver",
            "OnPointerClick", "OnPointerDown", "OnPointerUp", "OnPointerEnter", "OnPointerExit",
            "OnBeginDrag", "OnDrag", "OnEndDrag", "OnDrop", "OnScroll"
        };

        private static void Taking(Variant variant, Dictionary<string, Offer> offers)
        {
            var owner = variant.Owner == null ? null : variant.Owner.FullName;

            if (owner == null)
            {
                // A case on a type no GameObject carries cannot be offered against anything a
                // reading walks. Counted nowhere, because what it would have offered is not an
                // input a tester was ever going to be given.
                return;
            }

            if (!offers.TryGetValue(owner, out var offer))
            {
                offer = new Offer();
                offers[owner] = offer;
            }

            var gestures = new List<InputRead>();
            variant.When.CollectGestures(gestures, new HashSet<Condition>());

            foreach (var gesture in gestures)
            {
                var said = gesture.ToString();

                if (!offer.Keys.Contains(said))
                {
                    offer.Keys.Add(said);
                }
            }

            var entry = Method(variant.EntryId);

            if (entry != null && Pointered.Contains(entry) && !offer.Pointers.Contains(entry))
            {
                offer.Pointers.Add(entry);
            }
        }

        /// <summary>The method's own name out of an id the writer made as assembly|type|name|signature.</summary>
        private static string Method(string entryId)
        {
            if (entryId == null)
            {
                return null;
            }

            var parts = entryId.Split('|');

            return parts.Length < 3 ? null : parts[2];
        }

        private static void Offers(StringBuilder text, Dictionary<string, Offer> offers)
        {
            var owners = new List<string>(offers.Keys);
            owners.Sort(StringComparer.Ordinal);

            text.Append(",\"inputs\":[");

            var written = 0;

            foreach (var owner in owners)
            {
                var offer = offers[owner];

                if (offer.Keys.Count == 0 && offer.Pointers.Count == 0)
                {
                    continue;
                }

                if (written > 0)
                {
                    text.Append(',');
                }

                text.Append('{');
                Property(text, "declaring", owner);
                text.Append(",\"keys\":[");
                Flat(text, offer.Keys);
                text.Append("],\"pointers\":[");
                Flat(text, offer.Pointers);
                text.Append("]}");
                written++;
            }

            text.Append(']');
        }

        private static void Flat(StringBuilder text, List<string> said)
        {
            said.Sort(StringComparer.Ordinal);

            for (var at = 0; at < said.Count; at++)
            {
                if (at > 0)
                {
                    text.Append(',');
                }

                EvidenceJson.String(text, said[at]);
            }
        }

        private static void Gather(
            Condition condition, Dictionary<string, WatchTarget> found, ref int unwatchable)
        {
            if (condition == null)
            {
                return;
            }

            if (condition.Kind == ConditionKind.Test)
            {
                Take(condition.Test?.Watch, found, ref unwatchable);
                return;
            }

            if (condition.Parts == null)
            {
                return;
            }

            foreach (var part in condition.Parts)
            {
                Gather(part, found, ref unwatchable);
            }
        }

        private static void Take(
            WatchTarget target, Dictionary<string, WatchTarget> found, ref int unwatchable)
        {
            if (target == null)
            {
                unwatchable++;
                return;
            }

            // The same member reached two ways is one thing to read. A field tested in eight branches
            // is eight conditions and one place to look, and the difference between those two numbers
            // is the whole reason this list is short.
            //
            // Keeping whichever entry says what was read off the field. A list watched both as
            // itself and for its count is one member to read either way, and the count is the part a
            // reader would otherwise have to work out for itself.
            if (!found.TryGetValue(target.Key, out var already) || already.Via == null)
            {
                found[target.Key] = target;
            }
        }

        private static void Property(StringBuilder text, string name, string value)
        {
            EvidenceJson.String(text, name);
            text.Append(':');
            EvidenceJson.String(text, value);
        }
    }
}
