using System.Collections.Generic;
using System.Text;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// Folds together the cases that differ only in where they were reached from.
    /// </summary>
    /// <remarks>
    /// One helper called from six entry points produces six records saying the same thing about the
    /// same method. Between a fifth and a third of every report measured was that — nineteen percent
    /// of WordVenture, twenty-four of Trash Dash, thirty of Chop Chop — and a reader has to work out
    /// that they are the same before it can say anything about them.
    ///
    /// Only where everything else is identical: the condition, the effects, the calls, the
    /// subscriptions and the gaps. Two cases that agree on all of those are one fact, and the
    /// several ways to it are the thing that differs.
    ///
    /// The first way in stays where it was, under the names it always had, so nothing that reads
    /// this has to change to keep working. The others go in a list beside it. That does mean a
    /// reader who ignores the list now sees fewer ways to the same effect than it used to, which is
    /// why the schema number moves.
    /// </remarks>
    internal static class DuplicateVariants
    {
        /// <summary>Between fields, so that two values cannot run together into a third.</summary>
        private const char Separator = '\u001f';

        internal static int Fold(List<Variant> variants)
        {
            var byIdentity = new Dictionary<string, Variant>(variants.Count);
            var kept = new List<Variant>(variants.Count);
            var folded = 0;

            foreach (var variant in variants)
            {
                var identity = Identity(variant);

                if (!byIdentity.TryGetValue(identity, out var already))
                {
                    byIdentity[identity] = variant;
                    kept.Add(variant);
                    continue;
                }

                already.AlsoReachedBy.Add(new Arrival
                {
                    Entry = variant.Entry,
                    EntryId = variant.EntryId,
                    TriggerKind = variant.TriggerKind,
                    CallPath = variant.CallPath
                });

                folded++;
            }

            variants.Clear();
            variants.AddRange(kept);
            return folded;
        }

        /// <summary>
        /// Everything about a case except how it was reached.
        /// </summary>
        /// <remarks>
        /// Built from the same values that are written out rather than from object identity, because
        /// two cases found down two different paths are two objects that may still say exactly the
        /// same thing.
        /// </remarks>
        private static string Identity(Variant variant)
        {
            var key = new StringBuilder(256);

            key.Append(variant.Owner?.FullName).Append(Separator)
                .Append(variant.MethodId).Append(Separator)
                .Append(variant.RecordKind).Append(Separator)
                .Append(variant.When.Key).Append(Separator);

            foreach (var outcome in variant.Outcomes)
            {
                key.Append(outcome.Kind).Append(':').Append(outcome.Category).Append(':')
                    .Append(outcome.Target).Append(':').Append(outcome.Detail).Append(':')
                    .Append(outcome.Offset).Append(Separator);
            }

            key.Append(Separator);

            foreach (var call in variant.Calls)
            {
                key.Append(call.TargetId).Append(':').Append(call.Receiver).Append(':')
                    .Append(call.Arguments).Append(':').Append(call.Offset).Append(Separator);
            }

            key.Append(Separator);

            foreach (var handled in variant.Handles)
            {
                key.Append(handled.HandlerId).Append(':').Append(handled.Channel).Append(':')
                    .Append(handled.Offset).Append(Separator);
            }

            key.Append(Separator);

            foreach (var gap in variant.Gaps)
            {
                key.Append(gap).Append(Separator);
            }

            return key.ToString();
        }
    }
}
