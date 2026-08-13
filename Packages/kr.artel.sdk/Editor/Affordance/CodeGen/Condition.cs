using System.Collections.Generic;
using System.Text;

namespace Artel.Affordances.CodeGen
{
    internal enum ConditionKind
    {
        /// <summary>Nothing had to be true.</summary>
        Always,

        /// <summary>A comparison the code made.</summary>
        Test,

        /// <summary>An input the player gave.</summary>
        Gesture,

        /// <summary>Something on the way here that could not be read.</summary>
        Unknown,

        /// <summary>All of these.</summary>
        Every,

        /// <summary>Any one of these.</summary>
        Either
    }

    /// <summary>
    /// What had to be true to arrive somewhere.
    /// </summary>
    /// <remarks>
    /// A tree rather than a list, because a list can only mean "and". Code reaching one place two
    /// ways — <c>position == 4 || position == 5</c> — flattens into a list that says the field held
    /// both values at once, which no state satisfies. A specification built from that describes an
    /// action nobody can perform.
    ///
    /// Kept nested rather than expanded into a sum of products. The branches share their ancestors,
    /// so the tree stays about the size of the method while expanding it would not.
    /// </remarks>
    internal sealed class Condition
    {
        private string _key;

        internal ConditionKind Kind { get; private set; }
        internal Precondition Test { get; private set; }
        internal InputRead Gesture { get; private set; }
        internal string Reason { get; private set; }

        /// <summary>The shape of the thing that defeated the read, for counting.</summary>
        /// <remarks>
        /// A count of unread conditions says how much is missing; it does not say what to build
        /// next. Whether the eighty-nine here are locals the walk refuses to follow, operators it
        /// has no word for, or calls it cannot see into decides three different pieces of work, and
        /// until this field existed the answer was a guess. Diagnostic only — nothing composes on
        /// it, and a reader that ignores it reads exactly what it read before.
        /// </remarks>
        internal string Unread { get; private set; }

        /// <summary>Where going round again starts, or -1.</summary>
        internal int LoopsBackTo { get; private set; } = -1;
        internal List<Condition> Parts { get; private set; }

        internal static readonly Condition Always = new Condition { Kind = ConditionKind.Always };

        internal static Condition FromTest(Precondition test)
        {
            return new Condition { Kind = ConditionKind.Test, Test = test };
        }

        internal static Condition FromGesture(InputRead gesture)
        {
            return new Condition { Kind = ConditionKind.Gesture, Gesture = gesture };
        }

        internal static Condition Unreadable(string reason, string unread = null)
        {
            return new Condition { Kind = ConditionKind.Unknown, Reason = reason, Unread = unread };
        }

        /// <summary>
        /// A condition that could not be read because getting here means going round again.
        /// </summary>
        /// <remarks>
        /// The offset is where round again starts. Saying only "loop" left a reader who wanted to
        /// join two things that happen on different turns of it with nothing but arithmetic on
        /// offsets — which is a cause the report never established. The edge is one the graph
        /// already found; it was being thrown away at the moment of giving up.
        /// </remarks>
        internal static Condition Looping(int backTo)
        {
            return new Condition
            {
                Kind = ConditionKind.Unknown,
                Reason = "loop",
                LoopsBackTo = backTo
            };
        }

        internal static Condition Every(IEnumerable<Condition> parts)
        {
            var gathered = new List<Condition>();

            foreach (var part in parts)
            {
                if (part == null || part.Kind == ConditionKind.Always)
                {
                    continue;
                }

                if (part.Kind == ConditionKind.Every)
                {
                    AddDistinct(gathered, part.Parts);
                    continue;
                }

                AddDistinct(gathered, part);
            }

            DropImplied(gathered);

            if (gathered.Count == 0) return Always;
            if (gathered.Count == 1) return gathered[0];

            return new Condition { Kind = ConditionKind.Every, Parts = gathered };
        }

        internal static Condition Either(IEnumerable<Condition> parts)
        {
            var gathered = new List<Condition>();

            foreach (var raw in parts)
            {
                var part = WithoutShortCircuit(raw);

                if (part == null)
                {
                    continue;
                }

                // One way in that needed nothing makes the whole choice unconditional.
                if (part.Kind == ConditionKind.Always)
                {
                    return Always;
                }

                if (part.Kind == ConditionKind.Either)
                {
                    AddDistinct(gathered, part.Parts);
                    continue;
                }

                AddDistinct(gathered, part);
            }

            if (gathered.Count == 0) return Always;
            if (gathered.Count == 1) return gathered[0];

            return new Condition { Kind = ConditionKind.Either, Parts = gathered };
        }

        /// <summary>
        /// One way into a choice, with the marks left by short-circuit evaluation taken off.
        /// </summary>
        /// <remarks>
        /// <c>GetKey(Left) || GetKey(Right)</c> only tests the right key when the left one was not
        /// pressed, so the way in through the right key carries <c>no Left</c> with it. That is
        /// true, and it is a fact about how C# evaluates <c>||</c> rather than about the game — read
        /// as a specification it says to press Right while carefully not pressing Left.
        ///
        /// Only under a choice. An absent input at the top of an <c>and</c> is a real rule —
        /// <c>if (!Input.GetKey(Shift))</c> is something the game means — and is left alone. And
        /// dropping a requirement only ever makes a way in easier, so the choice this belongs to
        /// still holds wherever it held before.
        /// </remarks>
        private static Condition WithoutShortCircuit(Condition way)
        {
            if (way == null)
            {
                return null;
            }

            if (way.Kind == ConditionKind.Gesture)
            {
                return way.Gesture.Absent ? Always : way;
            }

            if (way.Kind != ConditionKind.Every)
            {
                return way;
            }

            List<Condition> kept = null;

            for (var index = 0; index < way.Parts.Count; index++)
            {
                var part = way.Parts[index];
                var absent = part.Kind == ConditionKind.Gesture && part.Gesture.Absent;

                if (absent && kept == null)
                {
                    kept = new List<Condition>(way.Parts.GetRange(0, index));
                    continue;
                }

                if (!absent)
                {
                    kept?.Add(part);
                }
            }

            return kept == null ? way : Every(kept);
        }

        /// <summary>
        /// Whether every comparison in here is about the caller's own object, or about nothing.
        /// </summary>
        /// <remarks>
        /// An input is not a comparison and has no subject, so it never stands in the way. A
        /// comparison whose subject could not be worked out does — not knowing whose <c>count</c>
        /// this is means not knowing whether it may be read beside somebody else's.
        /// </remarks>
        /// <summary>
        /// The same condition said where the caller stands, or null when it cannot be.
        /// </summary>
        /// <remarks>
        /// A callee's condition is about the callee's object and says something else beside the
        /// caller's terms — which is why it is refused rather than composed. Refusing is right only
        /// while the two cannot be brought into one set of words. When the caller called it on a
        /// thing it can name, they can: <c>CombineZone.spellCards.Count</c> read where the card is
        /// dragged is <c>DraggableCard.combineZone.spellCards.Count</c>, and that sentence is about
        /// the caller's own object, which is what the composing rule wants.
        ///
        /// The swap is on the head of the name, and only when the head is the callee's own type.
        /// Every name here is written from what it was read out of, so a term of the callee's
        /// <c>this</c> begins with that type; one that does not begin with it is about something
        /// else and is left where it is — which makes the whole condition unsayable here, so
        /// nothing is returned.
        ///
        /// Nothing is dropped and nothing is guessed. Either the whole condition can be said in the
        /// caller's words or none of it is offered, because a half-translated sentence reads as one
        /// object's account while being two.
        /// </remarks>
        internal Condition ReadFrom(Binding binding)
        {
            if (binding == null || !binding.Anything)
            {
                return null;
            }

            switch (Kind)
            {
                case ConditionKind.Test:
                {
                    string head;
                    string term;
                    string standing;

                    if (Test.Context == "this")
                    {
                        if (binding.Receiver == null)
                        {
                            return null;
                        }

                        head = binding.Owner;
                        term = binding.Receiver;
                        standing = binding.ReceiverWhere;
                    }
                    else if (Test.Context != null && Test.Context.StartsWith("arg:", System.StringComparison.Ordinal))
                    {
                        // A term about a parameter is about whatever the caller put in it.
                        head = HeadOf(Test.Left);

                        if (head == null || binding.Passed == null ||
                            !binding.Passed.TryGetValue(head, out term))
                        {
                            return null;
                        }

                        standing = binding.PassedWhere != null &&
                                   binding.PassedWhere.TryGetValue(head, out var whose)
                            ? whose
                            : null;
                    }
                    else
                    {
                        // Static or subjectless terms mean the same wherever they are read.
                        return Test.Context == "static" || Test.Context == null ? this : null;
                    }

                    var left = Swapped(Test.Left, head, term);
                    var right = Swapped(Test.Right, head, term);

                    if (left == null || right == null)
                    {
                        return null;
                    }

                    return FromTest(new Precondition
                    {
                        Left = left,
                        Operator = Test.Operator,
                        Right = right,
                        Context = standing,
                        SubjectLost = standing == null ? Test.SubjectLost : null,
                        Offset = Test.Offset
                    });
                }

                case ConditionKind.Every:
                case ConditionKind.Either:
                {
                    var moved = new List<Condition>(Parts.Count);

                    foreach (var part in Parts)
                    {
                        var said = part.ReadFrom(binding);

                        if (said == null)
                        {
                            return null;
                        }

                        moved.Add(said);
                    }

                    return Kind == ConditionKind.Every ? Every(moved) : Either(moved);
                }

                default:
                    // Always, a gesture, or something unread. None of them names an object.
                    return this;
            }
        }

        /// <summary>One term said from where the caller stands, or null when it cannot be.</summary>
        internal static string Swapped(string term, string owner, string receiver)
        {
            if (term == null || owner == null || receiver == null)
            {
                return null;
            }

            if (term == owner)
            {
                return receiver;
            }

            // A number, a string, `null` — nothing that names an object of the callee's.
            if (!term.StartsWith(owner + ".", System.StringComparison.Ordinal))
            {
                return term.IndexOf('.') < 0 ? term : null;
            }

            return receiver + term.Substring(owner.Length);
        }

        /// <summary>The first name in a term, which is the thing the rest hangs off.</summary>
        private static string HeadOf(string term)
        {
            if (string.IsNullOrEmpty(term))
            {
                return null;
            }

            var dot = term.IndexOf('.');
            return dot < 0 ? term : term.Substring(0, dot);
        }

        internal bool AboutSelfOnly()
        {
            switch (Kind)
            {
                case ConditionKind.Test:
                    return Test.Context == "this" || Test.Context == "static";

                case ConditionKind.Every:
                case ConditionKind.Either:
                    foreach (var part in Parts)
                    {
                        if (!part.AboutSelfOnly())
                        {
                            return false;
                        }
                    }

                    return true;

                default:
                    // Always, a gesture, or something unread. None of them names an object.
                    return true;
            }
        }

        /// <summary>
        /// The same condition with everything but the inputs dropped.
        /// </summary>
        /// <remarks>
        /// What comes out is implied by what went in, which is the only property that matters: it
        /// may say less than the truth but never something the truth does not.
        ///
        /// That is why the two ways of joining are not treated alike. Every part of an <c>and</c>
        /// had to hold, so keeping the inputs out of any of them is still true. An <c>or</c> only
        /// promises that *one* way was taken, so its inputs can only be kept when **every** way has
        /// one — otherwise the way with no input is a way in that this would deny.
        ///
        /// This exists because an input is the one thing in a condition that does not belong to an
        /// object. A caller's <c>count &gt; 0</c> is about the caller's <c>count</c> and means
        /// something else beside the callee's terms; a caller's <c>Space was pressed</c> is about
        /// the keyboard and means the same thing everywhere. So it is the one part that can be sent
        /// down a call edge before the edges are able to carry receivers.
        /// </remarks>
        internal Condition InputsOnly()
        {
            switch (Kind)
            {
                case ConditionKind.Gesture:
                    return this;

                case ConditionKind.Every:
                {
                    var kept = new List<Condition>();

                    foreach (var part in Parts)
                    {
                        var inputs = part.InputsOnly();

                        if (inputs.Kind != ConditionKind.Always)
                        {
                            kept.Add(inputs);
                        }
                    }

                    return kept.Count == 0 ? Always : Every(kept);
                }

                case ConditionKind.Either:
                {
                    var kept = new List<Condition>();

                    foreach (var part in Parts)
                    {
                        var inputs = part.InputsOnly();

                        if (inputs.Kind == ConditionKind.Always)
                        {
                            return Always;
                        }

                        kept.Add(inputs);
                    }

                    return kept.Count == 0 ? Always : Either(kept);
                }

                default:
                    // A test, an unknown, or nothing at all. None of them is an input.
                    return Always;
            }
        }

        private static void AddDistinct(List<Condition> gathered, Condition part)
        {
            foreach (var existing in gathered)
            {
                if (existing.Key == part.Key)
                {
                    return;
                }
            }

            gathered.Add(part);
        }

        private static void AddDistinct(List<Condition> gathered, List<Condition> parts)
        {
            foreach (var part in parts)
            {
                AddDistinct(gathered, part);
            }
        }

        /// <summary>
        /// Removes what the rest already says.
        /// </summary>
        /// <remarks>
        /// An <c>else if</c> chain leaves every earlier test behind as its negation, so reaching the
        /// fourth arm carries three <c>!=</c> clauses that <c>== 3</c> already implies. They are true,
        /// and they bury the one clause that matters.
        /// </remarks>
        private static void DropImplied(List<Condition> parts)
        {
            for (var i = parts.Count - 1; i >= 0; i--)
            {
                var candidate = parts[i];

                if (candidate.Kind != ConditionKind.Test || candidate.Test.Operator != "!=")
                {
                    continue;
                }

                foreach (var other in parts)
                {
                    if (other.Kind != ConditionKind.Test ||
                        other.Test.Operator != "==" ||
                        other.Test.Left != candidate.Test.Left ||
                        other.Test.Right == candidate.Test.Right)
                    {
                        continue;
                    }

                    parts.RemoveAt(i);
                    break;
                }
            }
        }

        /// <summary>
        /// A canonical form, so two conditions saying the same thing compare equal.
        /// </summary>
        /// <remarks>
        /// A test is told apart by where it was read as well as by what it says. Two reads that come
        /// out with the same words are not the same fact — <c>spellCards.Count == 1</c> and
        /// <c>magicTypeCards.Count == 1</c> arrived as one sentence while the name of a call was
        /// written against its declaring type, and one of them was dropped as a repeat. What went
        /// out was a precondition with half of itself missing and nothing saying so, which is worse
        /// than an unread one: a specification built on it asks for one card where the game wants
        /// two.
        ///
        /// The offset stays out of the sentence a person reads. It is here, where sameness is
        /// decided, and the writing-out is left alone.
        /// </remarks>
        internal string Key
        {
            get
            {
                if (_key != null)
                {
                    return _key;
                }

                switch (Kind)
                {
                    case ConditionKind.Always:
                        _key = "T";
                        break;
                    case ConditionKind.Test:
                        _key = "t:" + Test + "@" + Test.Offset;
                        break;
                    case ConditionKind.Gesture:
                        _key = "g:" + Gesture;
                        break;
                    case ConditionKind.Unknown:
                        _key = "?:" + Reason;
                        break;
                    default:
                        var keys = new List<string>(Parts.Count);
                        foreach (var part in Parts)
                        {
                            keys.Add(part.Key);
                        }

                        keys.Sort(System.StringComparer.Ordinal);
                        _key = (Kind == ConditionKind.Every ? "&(" : "|(") +
                               string.Join(",", keys) + ")";
                        break;
                }

                return _key;
            }
        }

        internal void CollectGestures(List<InputRead> into, HashSet<Condition> seen)
        {
            if (!seen.Add(this))
            {
                return;
            }

            if (Kind == ConditionKind.Gesture)
            {
                // An input that had to be absent is a precondition, not a way to trigger this.
                // Listing it as one would offer a key that does the opposite of what it says.
                if (Gesture.Absent)
                {
                    return;
                }

                foreach (var existing in into)
                {
                    if (existing.ToString() == Gesture.ToString())
                    {
                        return;
                    }
                }

                into.Add(Gesture);
                return;
            }

            if (Parts == null)
            {
                return;
            }

            foreach (var part in Parts)
            {
                part.CollectGestures(into, seen);
            }
        }

        internal bool HasUnknown(HashSet<Condition> seen)
        {
            if (!seen.Add(this))
            {
                return false;
            }

            if (Kind == ConditionKind.Unknown)
            {
                return true;
            }

            if (Parts == null)
            {
                return false;
            }

            foreach (var part in Parts)
            {
                if (part.HasUnknown(seen))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Writes the condition out, stopping if it runs long.
        /// </summary>
        /// <remarks>
        /// The tree shares its branches; writing it out does not. A budget keeps a shape that is
        /// compact in memory from becoming a page of text, and the marker says where it stopped so
        /// the result is short rather than quietly partial.
        /// </remarks>
        internal void Write(StringBuilder text, ref int budget)
        {
            if (budget-- <= 0)
            {
                text.Append('…');
                return;
            }

            switch (Kind)
            {
                case ConditionKind.Always:
                    text.Append("always");
                    return;

                case ConditionKind.Test:
                    text.Append(Test);
                    return;

                case ConditionKind.Gesture:
                    text.Append(Gesture);
                    return;

                case ConditionKind.Unknown:
                    text.Append('<').Append(Reason).Append('>');
                    return;
            }

            var joiner = Kind == ConditionKind.Every ? " and " : " or ";

            for (var index = 0; index < Parts.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(joiner);
                }

                var part = Parts[index];
                var wrap = part.Kind == ConditionKind.Every || part.Kind == ConditionKind.Either;

                if (wrap) text.Append('(');
                part.Write(text, ref budget);
                if (wrap) text.Append(')');

                if (budget <= 0)
                {
                    return;
                }
            }
        }
    }
}
