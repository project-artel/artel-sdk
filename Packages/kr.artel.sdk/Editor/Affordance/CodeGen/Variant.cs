using System.Collections.Generic;
using System.Text;

namespace Artel.Affordances.CodeGen
{
    /// <summary>Something a player does.</summary>
    internal sealed class InputRead
    {
        internal string Gesture;
        internal string Name;
        internal string Phase;

        /// <summary>
        /// True when arriving here meant this input was *not* given.
        /// </summary>
        /// <remarks>
        /// A short-circuited <c>||</c> only tests its second key when the first was not pressed, so
        /// half the ways through a pair of keys carry a gesture that must be absent. Read without
        /// this, the two keys read as needed together when either one will do.
        /// </remarks>
        internal bool Absent;
        internal int Offset;

        public override string ToString()
        {
            var text = Phase == null ? Gesture + ":" + Name : Gesture + ":" + Name + " (" + Phase + ")";
            return Absent ? "no " + text : text;
        }
    }

    /// <summary>Something that had to be true to get here.</summary>
    internal sealed class Precondition
    {
        internal string Left;
        internal string Operator;
        internal string Right;

        /// <summary>
        /// Why the subject came out unknown, for counting. Diagnostic only.
        /// </summary>
        /// <remarks>
        /// The same source read twice gives two answers — thirty-nine of these in an editor scan
        /// and none in a development build — and a count alone does not say whether that is one
        /// cause or five. Nothing composes on it and a reader that ignores it reads what it read
        /// before.
        /// </remarks>
        internal string SubjectLost;

        /// <summary>
        /// Whose terms this is written in — <c>this</c>, <c>arg:N</c>, <c>static</c>, or unknown.
        /// </summary>
        /// <remarks>
        /// Without it a condition is a sentence with no subject, and two of them cannot be put side
        /// by side. With it, a callee's condition about <c>this</c> can be rewritten in the caller's
        /// terms once the caller says what it called the method on.
        /// </remarks>
        internal string Context;

        internal int Offset;

        /// <summary>
        /// Where the left side can be read back while the game runs, when it is somewhere.
        /// </summary>
        /// <remarks>
        /// A condition is what a tester has to arrange, and arranging it starts with seeing what is
        /// there now. The report has said <c>MapMove.position == 0</c> for as long as it has existed
        /// and nothing could look at <c>position</c>, so every such row was a rule with no way to
        /// check its own premise.
        ///
        /// Null for most of them and that is not a defect: a condition on a call or on a local is
        /// about a value that only exists while the method runs. Counted rather than approximated.
        /// </remarks>
        internal WatchTarget Watch;

        public override string ToString()
        {
            return Left + " " + Operator + " " + Right;
        }
    }

    /// <summary>Something the game does about it.</summary>
    internal sealed class Outcome
    {
        internal string Kind;
        internal string Category;
        internal string Target;
        internal string Detail;

        /// <summary>
        /// The values the target could have been, when it is a local written in several places.
        /// </summary>
        /// <remarks>
        /// Beside <c>Target</c>, never instead of it. The target is what the source called the
        /// thing; this is what the source put in it, and the two answer different questions.
        /// </remarks>
        internal System.Collections.Generic.List<string> TargetCandidates;

        /// <summary>
        /// Where the thing this changed can be read back, when it is a field.
        /// </summary>
        /// <remarks>
        /// The other half of the same need. A condition says what to arrange and an effect says what
        /// to check afterwards, and both are unrunnable until somebody can see the value. An effect
        /// on a field is also the surest watch target there is — the report has already established
        /// that something writes it, so it is not a value that merely happens to sit there.
        /// </remarks>
        internal WatchTarget Watch;

        /// <summary>
        /// Where the value came from, when that is somewhere too.
        /// </summary>
        /// <remarks>
        /// <c>character.transform.position = battle2.transform.position</c> names two objects and
        /// only one of them is what changed. Watching the changed one alone says where the cursor is
        /// and never where it was going, so "the cursor has arrived at <c>battle2</c>" — which is the
        /// whole of what the specification row checks — cannot be answered.
        ///
        /// It matters most beside a screen recording. The video can see something arrive somewhere;
        /// it cannot know the somewhere is called <c>battle2</c>, and a destination nobody is
        /// watching has no position to compare against.
        /// </remarks>
        internal WatchTarget WatchSource;

        /// <summary>
        /// A name the game passed to an animator, when it was written out in the code.
        /// </summary>
        /// <remarks>
        /// Unity does not give a state's name back at runtime. <c>AnimatorStateInfo</c> carries a
        /// hash and nothing that turns it into words, so a reading of an animator can say the state
        /// changed and not which state it changed to — which is precisely the half a screen
        /// recording already supplies, and precisely not the half it cannot.
        ///
        /// It can be asked, though. <c>IsName</c> answers whether the current state is called
        /// something, so a reading that knows the candidates can name the state by trying them. The
        /// candidates are in the code: <c>SetTrigger("Death")</c> wrote one down.
        ///
        /// A trigger's name and a state's name are not the same thing, and the games that use one
        /// for the other are following a convention rather than a rule. So the answer is only ever
        /// given when <c>IsName</c> says yes, and the hash is written beside it either way.
        /// </remarks>
        internal string AnimatorName;

        internal int Offset;

        public override string ToString()
        {
            return Detail == null ? Kind + " " + Target : Kind + " " + Target + " " + Detail;
        }
    }

    /// <summary>A same-assembly call made under this case's condition.</summary>
    internal sealed class CallEdge
    {
        internal string TargetId;
        internal string Target;

        /// <summary>
        /// What the call was made on, and with what.
        /// </summary>
        /// <remarks>
        /// Two buttons that both call <c>Raise</c> are the same edge until the receiver says which
        /// field each of them called it on. The same lack is what stops a callee's condition from
        /// being composed into its caller's — <c>count &gt; 0</c> is about somebody, and the edge
        /// never said who.
        /// </remarks>
        internal string Receiver;

        /// <summary>Whose object the receiver was, in the caller's own terms.</summary>
        internal string ReceiverWhere;

        internal string Arguments;

        internal int Offset;
    }

    /// <summary>Another way the same case is reached.</summary>
    internal sealed class Arrival
    {
        internal string Entry;
        internal string EntryId;
        internal string TriggerKind;
        internal List<string> CallPath;
    }

    /// <summary>A method hung on something that will call it later.</summary>
    internal sealed class Subscription
    {
        /// <summary>The field or property the handler was attached to, when it could be named.</summary>
        internal string Channel;

        /// <summary>The type of that channel — what a publisher of the same type could reach.</summary>
        internal string ChannelType;

        /// <summary>Which member of it: the event's name, or the field's.</summary>
        internal string Member;

        internal string Handler;
        internal string HandlerId;
        internal int Offset;
    }

    /// <summary>
    /// One input, what had to be true, and what changed.
    /// </summary>
    /// <remarks>
    /// The same key is a different variant in each branch that handles it. A direction key at one
    /// place on the map moves somewhere else than the same key one step along, and a specification
    /// that collapsed them would describe neither.
    /// </remarks>
    internal sealed class Variant
    {
        /// <summary>How much of a condition is written out before it is cut short.</summary>
        private const int WriteBudget = 40;

        internal string Method;
        internal string MethodId;

        /// <summary>
        /// Whether the condition here is the whole account of how a player reaches these effects.
        /// </summary>
        /// <remarks>
        /// A record found down a call path carries its own method's condition, which says nothing
        /// about what had to be true to make the call in the first place. Until that is composed in,
        /// the record is a step on the way rather than something a test could be written from, and
        /// the two must not look alike to whoever reads this.
        /// </remarks>
        internal string RecordKind = "candidate";

        /// <summary>The Unity entry point from which this evidence was reached.</summary>
        internal string Entry;
        internal string EntryId;

        /// <summary>How execution enters the root: Unity event, lifecycle, or code input.</summary>
        internal string TriggerKind;

        /// <summary>Every same-assembly call followed from the entry to this method.</summary>
        internal readonly List<string> CallPath = new List<string>();

        /// <summary>The type this gets baked onto, when it is one a GameObject can carry.</summary>
        internal Mono.Cecil.TypeDefinition Owner;

        internal Condition When = Condition.Always;
        internal readonly List<InputRead> Inputs = new List<InputRead>();
        internal readonly List<Outcome> Outcomes = new List<Outcome>();
        internal readonly List<CallEdge> Calls = new List<CallEdge>();
        internal readonly List<Subscription> Handles = new List<Subscription>();

        /// <summary>
        /// The other entry points that reach this same case.
        /// </summary>
        /// <remarks>
        /// One helper called from six places used to be six records saying the same thing. The first
        /// way in keeps <see cref="Entry"/> and <see cref="CallPath"/> so that nothing reading this
        /// has to change; the rest are here.
        /// </remarks>
        internal readonly List<Arrival> AlsoReachedBy = new List<Arrival>();

        /// <summary>
        /// Where this method was handed over rather than called, or -1.
        /// </summary>
        /// <remarks>
        /// An offset in the last method of <see cref="CallPath"/> before this one. It is the only
        /// thing that puts a handed-over body in order beside the effects around it — the calls in
        /// that method already carry offsets, and without this one the wait between two of them has
        /// no place to go.
        /// </remarks>
        internal int HandedAt = -1;

        /// <summary>Which method on <see cref="CallPath"/> that offset belongs to, or -1.</summary>
        /// <remarks>
        /// Said so the offset can travel. Without it the number only made sense when the hand-over
        /// was the last step, so it was dropped the moment an ordinary call followed — one record
        /// in four kept it, and every drag-and-drop in the sample game lost the ordering it needed.
        /// With the index the reader knows which body the offset is an offset into, and nothing
        /// has to be guessed from position in the path.
        /// </remarks>
        internal int HandedIn = -1;

        /// <summary>
        /// What took the method that was handed over.
        /// </summary>
        /// <remarks>
        /// Where a predicate went is what says whether anything waits on it. Named rather than
        /// judged — <c>UnityEngine.WaitUntil</c> is written down and what waiting means is the
        /// reader's to know.
        /// </remarks>
        internal string HandedTo;

        /// <summary>
        /// Where control comes back to when this runs more than once, or -1.
        /// </summary>
        /// <remarks>
        /// It was only ever said when the loop defeated a read, which had it disappear exactly as
        /// the reading got better: once the counter could be named the condition resolved, the
        /// walk never gave up, and the edge went unmentioned. Going round again is a fact about
        /// the code, not a failure to read it, so it is said whether or not anything else could be.
        ///
        /// Two shapes carry it and both mean the same place. A block control returns *to* says its
        /// own offset; a block that jumps *back* says where it jumps. Anything that only sits
        /// between them says nothing — naming a loop it merely belongs to would take a graph
        /// question this does not ask.
        /// </remarks>
        internal int LoopsBackTo = -1;

        /// <summary>Specific reasons this evidence must not be treated as exhaustive.</summary>
        internal readonly List<string> Gaps = new List<string>();

        /// <summary>True when part of the way here could not be read.</summary>
        internal bool Incomplete;

        internal void AddGap(string gap)
        {
            if (!string.IsNullOrEmpty(gap) && !Gaps.Contains(gap))
            {
                Gaps.Add(gap);
                Incomplete = true;
            }
        }

        public override string ToString()
        {
            var text = new StringBuilder();
            text.Append(Method).Append("  when ");

            var budget = WriteBudget;
            When.Write(text, ref budget);

            text.Append("  -> ").Append(string.Join(", ", Outcomes));
            return text.ToString();
        }
    }
}
