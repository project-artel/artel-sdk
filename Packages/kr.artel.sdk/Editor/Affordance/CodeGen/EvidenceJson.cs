using System.Collections.Generic;
using System.Text;

namespace Artel.Affordances.CodeGen
{
    /// <summary>Writes one bounded, deterministic evidence document for the runtime scanner.</summary>
    internal static class EvidenceJson
    {
        /// <summary>
        /// Six: a record says what reaches it.
        /// </summary>
        /// <remarks>
        /// Three added a receiver and arguments to call edges and wrote out subscriptions. Four
        /// folds together the records that differ only in where they were reached from and puts the
        /// other ways in <c>alsoReachedBy</c>.
        ///
        /// Every field a version-two document had is still in the same place with the same meaning,
        /// so nothing has to be rewritten to read this. The number moves because what a document
        /// *omits* now means something different: a reader that ignores <c>handles</c> concludes a
        /// type subscribes to nothing, and one that ignores <c>alsoReachedBy</c> sees fewer ways to
        /// an effect than there are. The version is the only thing that says which it is holding.
        ///
        /// Six adds <c>calledBy</c>: what reaches a record, which the record's own calls could
        /// never say. It only adds, and it moves with the document that carries it rather than
        /// drifting a version behind — one file should not hold two answers to which shape it is.
        /// </remarks>
        internal const int SchemaVersion = 6;
        private const int MaxItems = 64;
        private const int MaxConditionNodes = 512;
        private const int MaxConditionDepth = 64;

        internal static string Write(Variant variant, out bool truncated)
        {
            return Write(variant, null, out truncated);
        }

        internal static string Write(
            Variant variant, Dictionary<string, List<string>> callers, out bool truncated)
        {
            truncated = false;
            var text = new StringBuilder(1024);
            text.Append('{');
            Property(text, "schema", SchemaVersion);
            text.Append(',');
            Property(text, "owner", variant.Owner?.FullName);
            text.Append(',');
            Property(text, "entry", variant.Entry);
            text.Append(',');
            Property(text, "entryId", variant.EntryId);
            text.Append(',');
            Property(text, "source", variant.Method);
            text.Append(',');
            Property(text, "methodId", variant.MethodId);
            text.Append(',');
            Property(text, "recordKind", variant.RecordKind);
            text.Append(',');
            Property(text, "triggerKind", variant.TriggerKind);
            text.Append(',');
            Property(text, "confidence", Confidence(variant));
            if (variant.LoopsBackTo >= 0)
            {
                text.Append(',');
                Property(text, "loopsBackTo", variant.LoopsBackTo);
            }

            if (variant.HandedAt >= 0)
            {
                text.Append(',');
                Property(text, "handedOverAt", variant.HandedAt);
                text.Append(',');
                Property(text, "handedOverIn", variant.HandedIn);

                // What took it. A predicate given to `WaitUntil` is a coroutine standing still
                // until it comes true; the same predicate given to a list of callbacks is not.
                if (variant.HandedTo != null)
                {
                    text.Append(',');
                    Property(text, "handedOverTo", variant.HandedTo);
                }
            }

            // Who reaches this, which the record's own calls cannot say: they are the edges going
            // out, and a caller with no record of its own leaves none of them.
            if (callers != null && variant.EntryId != null &&
                callers.TryGetValue(variant.EntryId, out var reachedBy))
            {
                text.Append(",\"calledBy\":");
                Strings(text, reachedBy, ref truncated);
            }

            text.Append(",\"callPath\":");
            Strings(text, variant.CallPath, ref truncated);
            text.Append(",\"condition\":");
            var nodes = MaxConditionNodes;
            Condition(text, variant.When, 0, ref nodes, ref truncated);
            text.Append(",\"inputs\":[");
            var inputCount = System.Math.Min(variant.Inputs.Count, MaxItems);
            truncated |= variant.Inputs.Count > MaxItems;
            for (var index = 0; index < inputCount; index++)
            {
                if (index > 0) text.Append(',');
                var input = variant.Inputs[index];
                text.Append('{');
                Property(text, "kind", input.Gesture);
                text.Append(',');
                Property(text, "control", input.Name);
                text.Append(',');
                Property(text, "phase", input.Phase);
                text.Append(',');
                Property(text, "absent", input.Absent);
                text.Append(',');
                Property(text, "offset", input.Offset);
                text.Append('}');
            }
            text.Append(']');
            text.Append(",\"effects\":[");
            var effectCount = System.Math.Min(variant.Outcomes.Count, MaxItems);
            truncated |= variant.Outcomes.Count > MaxItems;
            for (var index = 0; index < effectCount; index++)
            {
                if (index > 0) text.Append(',');
                var effect = variant.Outcomes[index];
                text.Append('{');
                Property(text, "kind", effect.Kind);
                text.Append(',');
                Property(text, "category", effect.Category);
                text.Append(',');
                Property(text, "target", effect.Target);

                // Which values the target could have been. Absent unless the source chose between
                // them, so a reader that sees it knows the single name above is not an answer.
                if (effect.TargetCandidates != null && effect.TargetCandidates.Count > 0)
                {
                    text.Append(",\"targetCandidates\":");
                    Strings(text, effect.TargetCandidates, ref truncated);
                }

                text.Append(',');
                Property(text, "detail", effect.Detail);
                text.Append(',');
                Property(text, "source", variant.Method);
                text.Append(',');
                Property(text, "offset", effect.Offset);
                text.Append('}');
            }
            text.Append(']');
            text.Append(",\"calls\":[");
            var callCount = System.Math.Min(variant.Calls.Count, MaxItems);
            truncated |= variant.Calls.Count > MaxItems;
            for (var index = 0; index < callCount; index++)
            {
                if (index > 0) text.Append(',');
                var call = variant.Calls[index];
                text.Append('{');
                Property(text, "targetId", call.TargetId);
                text.Append(',');
                Property(text, "target", call.Target);
                text.Append(',');
                Property(text, "receiver", call.Receiver);
                text.Append(',');
                Property(text, "receiverWhere", call.ReceiverWhere);
                text.Append(',');
                Property(text, "args", call.Arguments);
                text.Append(',');
                Property(text, "offset", call.Offset);
                text.Append('}');
            }
            text.Append(']');
            text.Append(",\"handles\":[");
            var handleCount = System.Math.Min(variant.Handles.Count, MaxItems);
            truncated |= variant.Handles.Count > MaxItems;
            for (var index = 0; index < handleCount; index++)
            {
                if (index > 0) text.Append(',');
                var handled = variant.Handles[index];
                text.Append('{');
                Property(text, "channel", handled.Channel);
                text.Append(',');
                Property(text, "channelType", handled.ChannelType);
                text.Append(',');
                Property(text, "member", handled.Member);
                text.Append(',');
                Property(text, "handler", handled.Handler);
                text.Append(',');
                Property(text, "handlerId", handled.HandlerId);
                text.Append(',');
                Property(text, "offset", handled.Offset);
                text.Append('}');
            }
            text.Append(']');
            text.Append(",\"alsoReachedBy\":[");
            var arrivalCount = System.Math.Min(variant.AlsoReachedBy.Count, MaxItems);
            truncated |= variant.AlsoReachedBy.Count > MaxItems;
            for (var index = 0; index < arrivalCount; index++)
            {
                if (index > 0) text.Append(',');
                var arrival = variant.AlsoReachedBy[index];
                text.Append('{');
                Property(text, "entry", arrival.Entry);
                text.Append(',');
                Property(text, "entryId", arrival.EntryId);
                text.Append(',');
                Property(text, "triggerKind", arrival.TriggerKind);
                text.Append(",\"callPath\":");
                Strings(text, arrival.CallPath, ref truncated);
                text.Append('}');
            }
            text.Append(']');
            text.Append(",\"gaps\":");
            Strings(text, variant.Gaps, ref truncated);
            text.Append('}');
            return text.ToString();
        }

        private static string Confidence(Variant variant)
        {
            if (variant.Gaps.Count > 0)
            {
                return "partial";
            }

            return variant.CallPath.Count > 1 ? "derived" : "verified";
        }

        private static void Condition(
            StringBuilder text,
            Condition condition,
            int depth,
            ref int nodes,
            ref bool truncated)
        {
            if (condition == null || depth >= MaxConditionDepth || nodes-- <= 0)
            {
                truncated = true;
                text.Append("{\"kind\":\"unknown\",\"reason\":\"serialization-limit\"}");
                return;
            }

            text.Append('{');
            Property(text, "kind", condition.Kind.ToString().ToLowerInvariant());

            if (condition.Kind == ConditionKind.Test)
            {
                text.Append(','); Property(text, "left", condition.Test.Left);
                text.Append(','); Property(text, "operator", condition.Test.Operator);
                text.Append(','); Property(text, "right", condition.Test.Right);
                text.Append(','); Property(text, "context", condition.Test.Context);
                if (condition.Test.SubjectLost != null)
                {
                    text.Append(','); Property(text, "subjectLost", condition.Test.SubjectLost);
                }

                text.Append(','); Property(text, "offset", condition.Test.Offset);
            }
            else if (condition.Kind == ConditionKind.Gesture)
            {
                text.Append(','); Property(text, "input", condition.Gesture.ToString());
                text.Append(','); Property(text, "offset", condition.Gesture.Offset);
            }
            else if (condition.Kind == ConditionKind.Unknown)
            {
                text.Append(','); Property(text, "reason", condition.Reason);

                if (condition.Unread != null)
                {
                    text.Append(','); Property(text, "unread", condition.Unread);
                }

                if (condition.LoopsBackTo >= 0)
                {
                    text.Append(','); Property(text, "loopsBackTo", condition.LoopsBackTo);
                }
            }
            else if (condition.Parts != null)
            {
                text.Append(",\"parts\":[");
                var count = System.Math.Min(condition.Parts.Count, MaxItems);
                truncated |= condition.Parts.Count > MaxItems;
                for (var index = 0; index < count; index++)
                {
                    if (index > 0) text.Append(',');
                    Condition(text, condition.Parts[index], depth + 1, ref nodes, ref truncated);
                }
                text.Append(']');
            }

            text.Append('}');
        }

        private static void Strings(StringBuilder text, IList<string> values, ref bool truncated)
        {
            text.Append('[');
            var count = System.Math.Min(values.Count, MaxItems);
            truncated |= values.Count > MaxItems;
            for (var index = 0; index < count; index++)
            {
                if (index > 0) text.Append(',');
                String(text, values[index]);
            }
            text.Append(']');
        }

        private static void Property(StringBuilder text, string name, string value)
        {
            String(text, name);
            text.Append(':');
            String(text, value);
        }

        private static void Property(StringBuilder text, string name, int value)
        {
            String(text, name);
            text.Append(':').Append(value);
        }

        private static void Property(StringBuilder text, string name, bool value)
        {
            String(text, name);
            text.Append(value ? ":true" : ":false");
        }

        /// <summary>Shared so a second writer cannot disagree with this one about escaping.</summary>
        internal static void String(StringBuilder text, string value)
        {
            if (value == null)
            {
                text.Append("null");
                return;
            }

            text.Append('"');
            foreach (var character in value)
            {
                switch (character)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\f': text.Append("\\f"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (character < 32)
                        {
                            text.Append("\\u").Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            text.Append(character);
                        }
                        break;
                }
            }
            text.Append('"');
        }
    }
}
