using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// A member whose live value decides something, named exactly enough to read it back.
    /// </summary>
    /// <remarks>
    /// The report says what had to be true — <c>MapMove.position == 0</c> — and a test cannot be run
    /// from that until somebody can see what <c>position</c> holds right now. The other SDK asked the
    /// game to mark those members by hand, which puts the burden on the game and gets it wrong in the
    /// two usual ways: a field nobody marked is invisible, and marking everything makes every frame
    /// look like a change.
    ///
    /// Nothing has to be marked here. The analysis already walked the instruction that produced the
    /// left-hand side of every condition, and a field is right there in its operand. So the list of
    /// what to watch is a by-product of reading the conditions, and it is exactly as long as the
    /// conditions require — measured on the sample game, the whole of it is a couple of hundred
    /// members out of a game with thousands.
    ///
    /// Written as parts rather than as the sentence the report shows. <c>MapMove.position</c> is a
    /// thing to read to a person; <c>(WordVenture.Map.MapMove, position)</c> is a thing reflection can
    /// find, and turning the first back into the second at runtime would be parsing our own prose.
    ///
    /// Only fields. A condition on <c>spellCards.Count</c> or <c>CompareTag("Spell")</c> is produced
    /// by a call, and calling it to find out is not watching — it is playing the game. Those are left
    /// out and counted, rather than approximated by watching whatever the call was made on.
    /// </remarks>
    internal sealed class WatchTarget
    {
        private const string BackingSuffix = ">k__BackingField";

        /// <summary>The type that declares it, by the name it had when this was compiled.</summary>
        internal string Declaring;

        internal string Member;

        /// <summary>
        /// The property this field stands behind, when a compiler made it.
        /// </summary>
        /// <remarks>
        /// An automatic property is a field called <c>&lt;Instance&gt;k__BackingField</c>, and that
        /// is the name reflection needs. It is not the name anything else uses: the evidence says
        /// <c>StageDataSingleton.Instance</c>, so a reader joining a reading to a condition on the
        /// member name would find nothing. Both are written — the one to look it up by, and the one
        /// everybody else calls it.
        /// </remarks>
        internal string Property;

        /// <summary>What kind of value comes back, so a reader knows what it is comparing.</summary>
        internal string Type;

        /// <summary>
        /// True when there is no instance to find it on.
        /// </summary>
        /// <remarks>
        /// The difference decides where the value can be carried. A scan that walks GameObjects has
        /// somewhere to put an instance field and nowhere to put a static one — which is how the
        /// other SDK came to have no answer for <c>MapMove.StagePosition</c>, the field the sample
        /// game's whole map screen turns on.
        /// </remarks>
        internal bool Static;

        /// <summary>What makes two of these the same one.</summary>
        internal string Key => Declaring + "::" + Member;

        /// <summary>
        /// What was read off the field, when the value tested is not the field itself.
        /// </summary>
        /// <remarks>
        /// <c>spellCards.Count == 1</c> is about a list's size, and the list is the field. Watching
        /// the field answers it — a reading writes a collection as its count — but only if the two
        /// ends agree about which of the two numbers is being compared. Written down rather than
        /// left to be inferred from the type.
        ///
        /// Null when the field itself is the value, which is most of them.
        /// </remarks>
        internal string Via;

        /// <summary>
        /// The field a value was read off, when the value is not itself somewhere.
        /// </summary>
        /// <remarks>
        /// A condition on <c>CombineZone.spellCards.Count</c> is produced by a call, so asked
        /// directly there is nowhere to look and the condition goes unwatched — which left four
        /// specification rows unable to check their own premise while the list they are about sat in
        /// a field the whole time.
        ///
        /// Accepted only for a property read taking nothing, on something that roots to a field. A
        /// getter with arguments is a question with an answer that depends on the question, and a
        /// receiver that is not a field is not somewhere to look. What is read is written into
        /// <see cref="Via"/> so nothing has to guess which of the field's numbers was meant.
        /// </remarks>
        internal static WatchTarget ReadOff(
            Instruction from, Instruction boundary, MethodDefinition within)
        {
            if (from == null ||
                (from.OpCode.Code != Code.Call && from.OpCode.Code != Code.Callvirt) ||
                !(from.Operand is MethodReference read) ||
                !read.HasThis || read.Parameters.Count != 0 ||
                !read.Name.StartsWith("get_", System.StringComparison.Ordinal))
            {
                return null;
            }

            var target = From(IlReading.Rooted(read, from, boundary, within));

            if (target == null)
            {
                return null;
            }

            // `transform` and `gameObject` are stepped through on the way to the field, so a read of
            // one of them has already been answered by the field itself and saying it again would
            // describe the object as a property of itself.
            var name = read.Name.Substring(4);
            target.Via = name == "transform" || name == "gameObject" ? null : name;
            return target;
        }

        /// <summary>The property name a compiler-generated backing field belongs to, or null.</summary>
        private static string Behind(string name)
        {
            return name != null && name.Length > BackingSuffix.Length + 1 &&
                   name[0] == '<' && name.EndsWith(BackingSuffix, System.StringComparison.Ordinal)
                ? name.Substring(1, name.Length - BackingSuffix.Length - 1)
                : null;
        }

        /// <summary>
        /// The field an instruction reads, or null when it does not read one.
        /// </summary>
        /// <remarks>
        /// The instruction handed in is the one that produced the value being tested, which for a
        /// chain like <c>this.zone.spellCards</c> is the last field in it. That last one is the
        /// answer: it is what holds the value, and the ones before it are the way there.
        ///
        /// Null for everything else, deliberately. A call, an argument, a local and an arithmetic
        /// result are all values the report can name and none of them is a place to look, so
        /// guessing one would put a member in the list that nothing can read.
        /// </remarks>
        internal static WatchTarget From(Instruction instruction)
        {
            if (instruction == null)
            {
                return null;
            }

            if (instruction.OpCode.Code != Code.Ldfld &&
                instruction.OpCode.Code != Code.Ldsfld &&
                instruction.OpCode.Code != Code.Ldflda &&
                instruction.OpCode.Code != Code.Ldsflda)
            {
                return null;
            }

            return instruction.Operand is FieldReference read
                ? Of(read, instruction.OpCode.Code == Code.Ldsfld || instruction.OpCode.Code == Code.Ldsflda)
                : null;
        }

        /// <summary>The same target, named from a field the caller already has.</summary>
        /// <remarks>
        /// An effect writes a field and the instruction that does it carries the reference outright,
        /// so there is nothing to walk. Whether it stands alone is the caller's to say — a property
        /// setter that assigns a field is a write to that field, and what the opcode says at that
        /// point is about the call, not about the field behind it.
        /// </remarks>
        internal static WatchTarget Of(FieldReference field, bool isStatic)
        {
            if (field == null)
            {
                return null;
            }

            var declaring = field.DeclaringType?.FullName;

            if (string.IsNullOrEmpty(declaring) || string.IsNullOrEmpty(field.Name))
            {
                return null;
            }

            return new WatchTarget
            {
                Declaring = declaring,
                Member = field.Name,
                Property = Behind(field.Name),
                Type = field.FieldType?.FullName,
                Static = isStatic
            };
        }
    }
}
