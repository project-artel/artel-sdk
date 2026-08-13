using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>Reading values and names back out of instructions.</summary>
    internal static class IlReading
    {
        internal static bool TryConstant(Instruction instruction, out int value)
        {
            value = 0;

            if (instruction == null)
            {
                return false;
            }

            switch (instruction.OpCode.Code)
            {
                case Code.Ldc_I4_M1: value = -1; return true;
                case Code.Ldc_I4_0: value = 0; return true;
                case Code.Ldc_I4_1: value = 1; return true;
                case Code.Ldc_I4_2: value = 2; return true;
                case Code.Ldc_I4_3: value = 3; return true;
                case Code.Ldc_I4_4: value = 4; return true;
                case Code.Ldc_I4_5: value = 5; return true;
                case Code.Ldc_I4_6: value = 6; return true;
                case Code.Ldc_I4_7: value = 7; return true;
                case Code.Ldc_I4_8: value = 8; return true;
                case Code.Ldc_I4_S: value = (sbyte)instruction.Operand; return true;
                case Code.Ldc_I4: value = (int)instruction.Operand; return true;
                default: return false;
            }
        }

        /// <summary>
        /// A short name for whatever an instruction puts on the stack.
        /// </summary>
        /// <remarks>
        /// Null when the value is something this cannot name — a computed expression, a local whose
        /// history is not tracked. Callers treat null as an unread condition and say so rather than
        /// inventing a plausible one.
        ///
        /// The bound is the first instruction of the block being read, and nothing is read past it.
        /// Without one, arguments are not read at all: a call's arguments are the instructions
        /// before it, and where control can arrive from more than one place the instructions before
        /// it belong to whichever path happens to be written above.
        /// </remarks>
        internal static string Describe(Instruction instruction)
        {
            return Describe(instruction, null);
        }

        /// <summary>
        /// The object a call was made on, followed down to the field holding it.
        /// </summary>
        /// <remarks>
        /// <c>MapMove.character.transform.position = MapMove.battle2.transform.position</c> is the
        /// sample game moving its map cursor, and both halves of it are a field with
        /// <c>.transform</c> on the end. Read as a receiver the answer is a call; read one hop
        /// further it is <c>character</c>, which is a place a value can be read back from while the
        /// game runs.
        ///
        /// This matters more now that a screen recording is watched beside the readings. The video
        /// shows a sprite arriving somewhere and cannot say that the sprite is <c>wordHead</c> or
        /// that the somewhere is <c>battle2</c>; those are names, and naming them is what turns two
        /// unrelated observations into one fact.
        ///
        /// Only <c>transform</c> and <c>gameObject</c> are stepped through, and only those. They are
        /// the two accessors that answer with the same object in another guise, so the field behind
        /// them is genuinely the thing that moved. Any other getter may hand back something else
        /// entirely — <c>list.First().position</c> would root to <c>list</c>, and a list has no
        /// position — so the walk stops and the value is left unwatched, which is the truthful
        /// answer rather than a plausible one.
        /// </remarks>
        internal static Instruction Rooted(
            MethodReference method, Instruction call, Instruction boundary, MethodDefinition within)
        {
            if (method == null || !method.HasThis || call == null || boundary == null)
            {
                return null;
            }

            return RootedAt(Receiving(method, call, boundary), boundary, within);
        }

        /// <summary>The same walk, begun at an instruction rather than at a call's receiver.</summary>
        /// <remarks>
        /// A tweening library moves a transform that was passed to it rather than called on, so what
        /// has to be rooted is an argument. Same walk, different starting point — and keeping them
        /// one walk is what stops the two shapes from disagreeing about which field moved.
        /// </remarks>
        internal static Instruction RootedAt(
            Instruction from, Instruction boundary, MethodDefinition within)
        {
            var at = Holding(from, within);

            for (var depth = 0; depth < MaxReceiverDepth && at != null; depth++)
            {
                if (at.OpCode.Code != Code.Call && at.OpCode.Code != Code.Callvirt)
                {
                    return at;
                }

                if (!(at.Operand is MethodReference getter) ||
                    !getter.HasThis || getter.Parameters.Count != 0 ||
                    (getter.Name != "get_transform" && getter.Name != "get_gameObject"))
                {
                    return at;
                }

                at = Holding(Receiving(getter, at, boundary), within);
            }

            return at;
        }

        /// <summary>
        /// Where <see cref="Describe"/> ends up: the instruction that actually holds the value.
        /// </summary>
        /// <remarks>
        /// The same follow through singly-written locals, and only that. Naming a value and finding
        /// somewhere to read it back are the same walk, so a member watched at runtime has to be the
        /// one the sentence is about — <c>MapMove.position</c> named through two copies in a
        /// debugging build is still that field, and a watcher pointed at the local would be watching
        /// something that stops existing the moment the method returns.
        ///
        /// Kept beside <see cref="Describe"/> rather than folded into it because they answer
        /// different questions and only one of them can fail politely. A name that cannot be read is
        /// an unread condition; an instruction that is not a field is simply not somewhere to look,
        /// which is an ordinary and frequent answer.
        /// </remarks>
        internal static Instruction Holding(Instruction instruction, MethodDefinition within)
        {
            for (var depth = 0; depth < MaxReceiverDepth; depth++)
            {
                var stored = StoredOnce(instruction, within);

                if (stored == null)
                {
                    return instruction;
                }

                instruction = stored;
            }

            return instruction;
        }

        internal static string Describe(Instruction instruction, Instruction boundary)
        {
            return Describe(instruction, boundary, null);
        }

        /// <summary>
        /// The same naming, able to see through a local that can only hold one thing.
        /// </summary>
        /// <remarks>
        /// An optimised compiler puts a value in a local and reads it back where a debugging one
        /// fetches it again, so the same source left twenty conditions unnamed in an editor scan
        /// and readable in a development build. Following a local is refused in general — it may
        /// have been assigned out of sight — and allowed when the method stores it in exactly one
        /// place, because then there is nowhere else it could have come from.
        /// </remarks>
        internal static string Describe(
            Instruction instruction, Instruction boundary, MethodDefinition within)
        {
            return Describe(instruction, boundary, within, 0);
        }

        private static string Describe(
            Instruction instruction, Instruction boundary, MethodDefinition within, int depth)
        {
            var stored = StoredOnce(instruction, within);

            if (stored != null)
            {
                // Followed on, not stopped after one. A debugging compiler copies a switch's
                // subject through two locals before testing it — `ldarg.1; stloc.1; ldloc.1;
                // stloc.0` — and stopping at the first left the sample game's five map screens and
                // five word positions as switches on nothing anybody could name. Each step is still
                // a local its method writes exactly once, which is the whole of the safety and does
                // not weaken by being applied twice; the depth is what keeps a field assigned from
                // itself from going round.
                return Describe(stored, boundary, depth + 1 < MaxReceiverDepth ? within : null, depth + 1);
            }

            if (instruction == null)
            {
                return null;
            }

            if (TryConstant(instruction, out var number))
            {
                return number.ToString();
            }

            switch (instruction.OpCode.Code)
            {
                case Code.Ldstr:
                    return "\"" + instruction.Operand + "\"";

                case Code.Ldnull:
                    return "null";

                case Code.Ldfld:
                case Code.Ldsfld:
                {
                    var field = instruction.Operand as FieldReference;

                    return WhichScene(field, within, boundary, depth) ?? FieldName(field);
                }

                case Code.Ldc_I8:
                case Code.Ldc_R4:
                case Code.Ldc_R8:
                    return Convert.ToString(
                        instruction.Operand, System.Globalization.CultureInfo.InvariantCulture);

                case Code.Ldarg_0:
                case Code.Ldarg_1:
                case Code.Ldarg_2:
                case Code.Ldarg_3:
                case Code.Ldarg:
                case Code.Ldarg_S:
                    return ArgumentName(instruction, within);

                case Code.Ldloc_0:
                case Code.Ldloc_1:
                case Code.Ldloc_2:
                case Code.Ldloc_3:
                case Code.Ldloc:
                case Code.Ldloc_S:
                case Code.Ldloca:
                case Code.Ldloca_S:
                    return LocalName(instruction, within);

                case Code.Call:
                case Code.Callvirt:
                    return CallName(
                        instruction.Operand as MethodReference, instruction, boundary, within, depth);

                default:
                    return Arithmetic(instruction, boundary, 0, within);
            }
        }

        /// <summary>
        /// The object a method was called on, or the name a parameter was declared with.
        /// </summary>
        /// <remarks>
        /// Left unread until now, and the cost was two different things. A condition comparing a
        /// parameter had no left-hand side at all and was reported as unread, which is a rule nobody
        /// can write down. And <c>Destroy(this)</c> came out as a target nobody could name — while
        /// the singleton plumbing that wants to recognise it has been looking for the word
        /// <c>this</c> the whole time, so an <c>Awake</c> that destroys the second copy of itself
        /// was arriving as a feature.
        ///
        /// The name is the one in the assembly's own metadata, so an obfuscated build gives back
        /// whatever it kept — and nothing, rather than a guess, when it kept nothing.
        ///
        /// Naming a parameter says what the comparison is about, not who could arrange it. That is
        /// the subject's job, and <see cref="Where"/> answers it with <c>arg:N</c> as it always did.
        /// </remarks>
        private static string ArgumentName(Instruction instruction, MethodDefinition within)
        {
            if (instruction.Operand is ParameterDefinition declared)
            {
                return string.IsNullOrEmpty(declared.Name) ? null : declared.Name;
            }

            if (within == null)
            {
                return null;
            }

            int index;

            switch (instruction.OpCode.Code)
            {
                case Code.Ldarg_0: index = 0; break;
                case Code.Ldarg_1: index = 1; break;
                case Code.Ldarg_2: index = 2; break;
                case Code.Ldarg_3: index = 3; break;
                default: return null;
            }

            if (within.HasThis)
            {
                if (index == 0)
                {
                    return "this";
                }

                index--;
            }

            if (index >= within.Parameters.Count)
            {
                return null;
            }

            var parameter = within.Parameters[index];

            return string.IsNullOrEmpty(parameter.Name) ? null : parameter.Name;
        }

        /// <summary>
        /// The name the source gave a local, when the assembly still carries it.
        /// </summary>
        /// <remarks>
        /// Named, not followed. Following a local is what this refuses in general and allows only
        /// when the method writes it once; naming it asks a different question, and the answer is
        /// written down in the symbols rather than worked out.
        ///
        /// It is the counter of a <c>for</c> loop that made this worth having. A loop's own test is
        /// <c>i &lt; cards.Count</c>, and <c>i</c> is written twice — once at zero and once at
        /// itself plus one — so it is exactly the shape the one-store rule refuses. The test came
        /// out as an unread condition and took the whole record with it: thirteen of the sample
        /// game's records said nothing at all except that a loop was involved.
        ///
        /// A local says nothing about whose it is, so the subject is still lost and the report still
        /// says so. What is gained is the sentence: "the number gone through is less than the number
        /// of cards" is a rule someone can read, where "unread condition" is not.
        ///
        /// Nothing is claimed when the symbols are not there. A release build is not baked at all
        /// and an obfuscated one gives back whatever it kept, which may be nothing — and nothing is
        /// what this then says. Names the compiler invented for itself are left alone.
        /// </remarks>
        private static string LocalName(Instruction instruction, MethodDefinition within)
        {
            if (within == null || !within.HasBody || !IsLoadingLocal(instruction, out var slot))
            {
                return null;
            }

            var variables = within.Body.Variables;

            if (slot >= variables.Count || within.DebugInformation == null)
            {
                return null;
            }

            if (!within.DebugInformation.TryGetName(variables[slot], out var name) ||
                string.IsNullOrEmpty(name) || name.StartsWith("<", StringComparison.Ordinal))
            {
                return null;
            }

            return name;
        }

        /// <summary>
        /// A value the code worked out, written the way the source wrote it.
        /// </summary>
        /// <remarks>
        /// Conditions in a game loop compare something computed, not something stored: a slide ends
        /// when the distance travelled since it began divided by its length reaches one. Refusing to
        /// name the sum left every one of those as an unread condition — on Trash Dash three hundred
        /// and sixty-seven of them — and an unread condition is a rule nobody can write down.
        ///
        /// Only what is on the stack at that moment. A value the code put in a local and read back
        /// is not followed, because a local may have been assigned somewhere this cannot see and a
        /// condition bound to the wrong assignment is the expensive kind of wrong. That is a
        /// separate piece of work and it is not done here.
        ///
        /// Bounded by depth as well as by the block. A long expression makes a long sentence, and
        /// past a few levels the sentence stops being one anybody reads.
        /// </remarks>
        private static string Arithmetic(
            Instruction instruction, Instruction boundary, int depth, MethodDefinition within)
        {
            if (depth >= MaxArithmeticDepth || boundary == null)
            {
                return null;
            }

            var symbol = Operator(instruction.OpCode.Code);

            if (symbol == null)
            {
                return Negation(instruction, boundary, depth, within);
            }

            var rightAt = Preceding(instruction, boundary);
            var leftAt = Under(rightAt, boundary);

            var right = Read(rightAt, boundary, depth + 1, within);
            var left = Read(leftAt, boundary, depth + 1, within);

            return left == null || right == null ? null : "(" + left + " " + symbol + " " + right + ")";
        }

        /// <summary>A unary operation, which is one operand rather than two.</summary>
        private static string Negation(
            Instruction instruction, Instruction boundary, int depth, MethodDefinition within)
        {
            if (instruction.OpCode.Code != Code.Neg)
            {
                return null;
            }

            var value = Read(Preceding(instruction, boundary), boundary, depth + 1, within);
            return value == null ? null : "-" + value;
        }

        /// <summary>Names an operand, going one level deeper into a sum if it is one.</summary>
        private static string Read(
            Instruction instruction, Instruction boundary, int depth, MethodDefinition within)
        {
            if (instruction == null)
            {
                return null;
            }

            return Operator(instruction.OpCode.Code) != null || instruction.OpCode.Code == Code.Neg
                ? Arithmetic(instruction, boundary, depth, within)
                : Describe(instruction, boundary, within);
        }

        private static string Operator(Code code)
        {
            switch (code)
            {
                case Code.Add: case Code.Add_Ovf: case Code.Add_Ovf_Un: return "+";
                case Code.Sub: case Code.Sub_Ovf: case Code.Sub_Ovf_Un: return "-";
                case Code.Mul: case Code.Mul_Ovf: case Code.Mul_Ovf_Un: return "*";
                case Code.Div: case Code.Div_Un: return "/";
                case Code.Rem: case Code.Rem_Un: return "%";
                default: return null;
            }
        }

        /// <summary>How many levels of a computed value are written out.</summary>
        private const int MaxArithmeticDepth = 4;

        /// <summary>
        /// Names the answer a call gave, with whatever of its arguments can be read.
        /// </summary>
        /// <remarks>
        /// Conditions in real code test what a method returned as often as they test a field —
        /// whether a save exists, whether a list is empty. Refusing to name those left the branch
        /// they guard reported as an unread condition, which in the sample game meant the fact that
        /// decides which scene loads was the one fact missing.
        ///
        /// Arguments used to be left off on the grounds that a signature on every condition costs
        /// more than it says. That was answered by measurement: <c>Component.CompareTag()</c> is a
        /// hundred and five conditions in the sample game and every one of them reads the same, so
        /// the tag-based half of its combat rules arrived as one repeated sentence. What is wanted
        /// is not the signature but the argument — and an argument that cannot be read is written
        /// as <c>_</c>, so a condition never claims to know one it does not.
        ///
        /// Written against what the call was made on, and only against the declaring type when that
        /// cannot be read. The type is the same for every object of it, so two lists on one object
        /// were both <c>List`1.Count</c> and a reader had no way to tell which — the sample game's
        /// combine button needs one spell card and one element card, and the two conditions arrived
        /// as one sentence. The receiver was never missing; it was simply not asked for, and
        /// <see cref="Receiver"/> has been asking for it on call edges all along.
        ///
        /// A receiver is itself a value with a receiver, so this walks. Bounded, because a name is
        /// for reading and past a few links it stops being one.
        /// </remarks>
        private static string CallName(
            MethodReference method, Instruction call, Instruction boundary, MethodDefinition within,
            int depth)
        {
            if (method == null || method.ReturnType.MetadataType == MetadataType.Void)
            {
                return null;
            }

            var owner = Owner(method, call, boundary, within, depth) ?? method.DeclaringType?.Name;

            if (owner == null)
            {
                return null;
            }

            var arguments = Arguments(method, call, boundary);

            if (method.Name.StartsWith("get_", StringComparison.Ordinal))
            {
                var property = owner + "." + method.Name.Substring(4);

                // An indexer is a getter with parameters, and it is read as one in the source too.
                // Written with the brackets rather than as get_Item so that a name the compiler
                // invented does not end up in a specification.
                return method.Parameters.Count == 0
                    ? property
                    : property + "[" + (arguments ?? Unread(method.Parameters.Count)) + "]";
            }

            return owner + "." + method.Name + "(" + (arguments ?? "") + ")";
        }

        /// <summary>
        /// What a call was made on, named, or null when it is not worth a name.
        /// </summary>
        /// <remarks>
        /// A static call has no receiver, and one made on <c>this</c> is left to the declaring type
        /// on purpose: a field of <c>this</c> is already written that way (<c>CombineZone.spellCards</c>,
        /// not <c>this.spellCards</c>) and the subject is carried by the condition's own
        /// <c>context</c>. So nothing moves for the ordinary case, and what moves is exactly the
        /// case where two objects were sharing one name.
        /// </remarks>
        private static string Owner(
            MethodReference method, Instruction call, Instruction boundary, MethodDefinition within,
            int depth)
        {
            if (!method.HasThis || depth >= MaxReceiverDepth)
            {
                return null;
            }

            return Describe(Receiving(method, call, boundary), boundary, within, depth + 1);
        }

        /// <summary>How many receivers deep a name is written before it stops being readable.</summary>
        private const int MaxReceiverDepth = 3;

        /// <summary>
        /// The arguments a call was given, as far back as the stack can be followed.
        /// </summary>
        /// <remarks>
        /// Read from the last argument backwards, because the last one is the only position that is
        /// certain without any analysis: whatever the instruction before a call left on the stack is
        /// its final argument. Every step further back has to skip over what the argument it just
        /// read consumed, which is what <see cref="Under"/> does, and it stops the moment it meets
        /// an instruction whose effect on the stack is not known.
        ///
        /// Null when nothing at all could be read, so that a call with unreadable arguments still
        /// reads as it always did rather than as an empty argument list.
        /// </remarks>
        internal static string Arguments(MethodReference method, Instruction call, Instruction boundary)
        {
            var names = ArgumentsRead(method, call, boundary, null);

            if (names == null)
            {
                return null;
            }

            for (var index = 0; index < names.Length; index++)
            {
                if (names[index] == null)
                {
                    names[index] = "_";
                }
            }

            return string.Join(", ", names);
        }

        /// <summary>One argument by position, or null if that one could not be read.</summary>
        /// <remarks>
        /// For an extension method the object being acted on is argument zero rather than a receiver,
        /// so naming what a call changed means asking for a single position. Reading the whole list
        /// and taking one of it rather than walking to that position directly: the walk has to pass
        /// over every later argument anyway, and doing it twice invites the two to disagree.
        /// </remarks>
        internal static string ArgumentAt(
            MethodReference method, Instruction call, Instruction boundary, int index)
        {
            return ArgumentAt(method, call, boundary, index, null);
        }

        internal static string ArgumentAt(
            MethodReference method, Instruction call, Instruction boundary, int index,
            MethodDefinition within)
        {
            var names = ArgumentsRead(method, call, boundary, within);

            return names != null && index >= 0 && index < names.Length ? names[index] : null;
        }

        /// <summary>The instruction that produced one of a call's arguments.</summary>
        internal static Instruction ArgumentFrom(
            MethodReference method, Instruction call, Instruction boundary, int index)
        {
            var count = method?.Parameters.Count ?? 0;

            if (count == 0 || call == null || boundary == null || index < 0 || index >= count)
            {
                return null;
            }

            var at = Preceding(call, boundary);

            for (var slot = count - 1; slot > index && at != null; slot--)
            {
                at = Under(at, boundary);
            }

            return at;
        }

        /// <summary>
        /// Every value a local is written with, when it is written more than once.
        /// </summary>
        /// <remarks>
        /// A local written once is that value and is read as it (see <see cref="StoredOnce"/>).
        /// Written five times it is none of them, and until now the report said so by saying
        /// nothing — the sample game picks a spell prefab in five switch arms and instantiates it
        /// after they join, so what was made came out as `(not a simple target)` and later, once
        /// locals had names, as `prefabToInstantiate`. Both are honest and neither is an answer.
        ///
        /// Five names is an answer of a different kind: not which one, but which five. A reader can
        /// say the spell that was cast is one of these and go looking, where before it had a word
        /// the game invented for a variable.
        ///
        /// All or nothing. If one of the stores cannot be named the set is not returned at all — a
        /// list missing a member reads like a complete one, and a reader would rule out the value
        /// that is actually there. That is the failure this is meant to prevent, not cause.
        ///
        /// Bounded, because a local assigned in twenty places is not being chosen between; it is
        /// being accumulated into, and listing twenty names says nothing about what was made.
        /// </remarks>
        internal static List<string> Candidates(
            Instruction instruction, Instruction boundary, MethodDefinition within, int most)
        {
            if (within == null || !within.HasBody || !IsLoadingLocal(instruction, out var slot))
            {
                return null;
            }

            var stores = new List<Instruction>();

            foreach (var candidate in within.Body.Instructions)
            {
                if (IsStoringLocal(candidate, out var stored) && stored == slot)
                {
                    stores.Add(candidate);
                }
            }

            if (stores.Count < 2 || stores.Count > most)
            {
                return null;
            }

            var named = new List<string>();

            foreach (var store in stores)
            {
                var value = Describe(store.Previous, boundary, null, 0);

                if (value == null)
                {
                    return null;
                }

                if (!named.Contains(value))
                {
                    named.Add(value);
                }
            }

            return named;
        }

        private static string[] ArgumentsRead(
            MethodReference method, Instruction call, Instruction boundary, MethodDefinition within)
        {
            var count = method?.Parameters.Count ?? 0;

            if (count == 0 || call == null || boundary == null)
            {
                return null;
            }

            var names = new string[count];
            var read = false;
            var at = Preceding(call, boundary);

            for (var index = count - 1; index >= 0 && at != null; index--)
            {
                names[index] = Argument(at, method.Parameters[index].ParameterType, boundary, within);
                read |= names[index] != null;
                at = Under(at, boundary);
            }

            return read ? names : null;
        }

        /// <summary>
        /// What a call was made on.
        /// </summary>
        /// <remarks>
        /// The receiver sits under every argument, so getting to it means skipping each of them in
        /// turn. It is the half of a call edge that says which object the call was about — two
        /// buttons calling <c>Raise</c> on two different channel fields are two different wirings,
        /// and without this they were the same line.
        /// </remarks>
        internal static string Receiver(MethodReference method, Instruction call, Instruction boundary)
        {
            return Receiver(method, call, boundary, null);
        }

        internal static string Receiver(
            MethodReference method, Instruction call, Instruction boundary, MethodDefinition within)
        {
            if (method == null || !method.HasThis || call == null || boundary == null)
            {
                return null;
            }

            return Describe(Receiving(method, call, boundary), boundary, within);
        }

        /// <summary>Where a call's receiver came from, in the caller's own terms.</summary>
        internal static string ReceiverWhere(
            MethodReference method, Instruction call, Instruction boundary, bool hasThis)
        {
            if (method == null || !method.HasThis || call == null || boundary == null)
            {
                return null;
            }

            return Where(Receiving(method, call, boundary), boundary, hasThis);
        }

        /// <summary>
        /// Whose value this is — the object a name is written against.
        /// </summary>
        /// <remarks>
        /// <c>count &gt; 0</c> is not a fact until it says whose <c>count</c>. That is the whole
        /// reason a callee's condition cannot be put beside its caller's: read next to the caller's
        /// terms it becomes a claim about the caller's object, and a wrong precondition is worse
        /// than a missing one.
        ///
        /// Found by walking down to what the expression was ultimately read from. A field of a field
        /// of <c>this</c> is still about <c>this</c>; a field of an argument is about whatever was
        /// passed. Anything the walk cannot follow says so, and nothing is composed on a maybe.
        /// </remarks>
        internal static string Where(Instruction instruction, Instruction boundary, bool hasThis)
        {
            return Where(instruction, boundary, hasThis, out _);
        }

        internal static string Where(
            Instruction instruction, Instruction boundary, bool hasThis, out Instruction stoppedAt)
        {
            return Where(instruction, boundary, hasThis, null, out stoppedAt);
        }

        /// <summary>
        /// The same walk, saying where it gave up.
        /// </summary>
        /// <remarks>
        /// The operand a condition started from is not where the subject was lost — a call names
        /// itself while the thing that defeated the walk is somewhere down its receiver. Counting
        /// the starting point told us a call was involved and nothing more.
        /// </remarks>
        internal static string Where(
            Instruction instruction,
            Instruction boundary,
            bool hasThis,
            MethodDefinition within,
            out Instruction stoppedAt)
        {
            stoppedAt = instruction;

            for (var step = 0; step < 32 && instruction != null; step++)
            {
                stoppedAt = instruction;

                // A local written in one place is the value written there, and whose that value is
                // is the same question one step further back. Naming already saw through such a
                // local; the subject did not, so a debug build could say `MapMove.StagePosition`
                // and in the same breath refuse to say whose it was.
                //
                // Followed once. Asking the rest without `within` keeps a local named through
                // another local from chaining, which is where one store stops being the whole of
                // the safety.
                var stored = StoredOnce(instruction, within);

                if (stored != null)
                {
                    instruction = stored;
                    within = null;
                    continue;
                }

                switch (instruction.OpCode.Code)
                {
                    case Code.Ldarg_0:
                        return hasThis ? "this" : "arg:0";

                    case Code.Ldarg_1: return hasThis ? "arg:0" : "arg:1";
                    case Code.Ldarg_2: return hasThis ? "arg:1" : "arg:2";
                    case Code.Ldarg_3: return hasThis ? "arg:2" : "arg:3";

                    case Code.Ldarg:
                    case Code.Ldarg_S:
                    {
                        var parameter = instruction.Operand as ParameterDefinition;
                        return parameter == null ? null : "arg:" + parameter.Index;
                    }

                    case Code.Ldsfld:
                    case Code.Ldstr:
                    case Code.Ldnull:

                    // A number too wide for the small-integer opcodes is still a number, and a
                    // condition comparing a field with -10 is about the field. Left out, the
                    // literal named no object, nothing agreed with it, and the whole comparison
                    // lost its subject — `Vector3.x < -10` was unusable for want of reading -10.
                    case Code.Ldc_I8:
                    case Code.Ldc_R4:
                    case Code.Ldc_R8:
                        return "static";

                    default:
                        if (TryConstant(instruction, out _))
                        {
                            return "static";
                        }

                        // A sum is about whatever both of its sides are about. Without this, reading
                        // an expression that used to be unreadable made the condition *less*
                        // composable than when nobody could read it at all — the atom went from
                        // "names no object" to "names an object nobody worked out".
                        if (Operator(instruction.OpCode.Code) != null)
                        {
                            var rightSide = Preceding(instruction, boundary);

                            return Agreeing(
                                Where(Under(rightSide, boundary), boundary, hasThis),
                                Where(rightSide, boundary, hasThis));
                        }

                        if (instruction.OpCode.Code == Code.Neg)
                        {
                            instruction = Preceding(instruction, boundary);
                            continue;
                        }

                        // Down to whatever this was read from. One input means the thing it was read
                        // from; more than one, or none that can be followed, ends the walk.
                        if (Consumes(instruction) != 1)
                        {
                            var call = instruction.Operand as MethodReference;

                            if ((instruction.OpCode.Code == Code.Call ||
                                 instruction.OpCode.Code == Code.Callvirt) && call != null)
                            {
                                if (!call.HasThis)
                                {
                                    return "static";
                                }

                                instruction = Receiving(call, instruction, boundary);
                                continue;
                            }

                            return null;
                        }

                        instruction = Preceding(instruction, boundary);
                        continue;
                }
            }

            return null;
        }

        /// <summary>
        /// The one object two sides are both about, or null when there is not one.
        /// </summary>
        /// <remarks>
        /// A side made only of constants agrees with anything, which is the ordinary shape — a field
        /// of <c>this</c> divided by a number.
        /// </remarks>
        internal static string Agreeing(string left, string right)
        {
            if (left == null || right == null)
            {
                return null;
            }

            if (left == "static") return right;
            if (right == "static") return left;

            return left == right ? left : null;
        }

        /// <summary>The instruction that produced a call's receiver.</summary>
        private static Instruction Receiving(MethodReference method, Instruction call, Instruction boundary)
        {
            var at = Preceding(call, boundary);

            for (var index = 0; index < method.Parameters.Count && at != null; index++)
            {
                at = Under(at, boundary);
            }

            return at;
        }

        /// <summary>A place for each argument that could not be read.</summary>
        private static string Unread(int count)
        {
            var places = new string[count];

            for (var index = 0; index < count; index++)
            {
                places[index] = "_";
            }

            return string.Join(", ", places);
        }

        /// <summary>
        /// One argument, named as the source would have written it where that is knowable.
        /// </summary>
        /// <remarks>
        /// A flag and an enum both arrive as a number, and the number on its own is unreadable —
        /// <c>SetActive(0)</c> and <c>Play(4)</c> say nothing. The parameter's own type is what
        /// turns them back into <c>false</c> and the member's name.
        /// </remarks>
        private static string Argument(
            Instruction instruction, TypeReference parameter, Instruction boundary,
            MethodDefinition within)
        {
            if (instruction == null)
            {
                return null;
            }

            if (TryConstant(instruction, out var number))
            {
                if (parameter?.MetadataType == MetadataType.Boolean)
                {
                    return number == 0 ? "false" : "true";
                }

                // Resolved only for a value type, so that an int argument does not pay for a type
                // load on every condition. An enum is a value type in a signature.
                return parameter?.MetadataType == MetadataType.ValueType
                    ? EnumName(parameter, number)
                    : number.ToString();
            }

            switch (instruction.OpCode.Code)
            {
                case Code.Ldc_I8:
                case Code.Ldc_R4:
                case Code.Ldc_R8:
                    return Convert.ToString(instruction.Operand, System.Globalization.CultureInfo.InvariantCulture);

                case Code.Box:
                    // An enum compared through Equals or passed as an object arrives boxed, and the
                    // number underneath means nothing without the type the box names.
                    return Argument(
                        Preceding(instruction, boundary), instruction.Operand as TypeReference, boundary,
                        within);

                default:
                    return Describe(instruction, boundary, within);
            }
        }

        /// <summary>
        /// The instruction before, with a debug build's padding stepped over, and never past the
        /// start of the block being read.
        /// </summary>
        /// <remarks>
        /// The bound is what makes reading backwards sound. A block begins where control can arrive
        /// from more than one place, so the instruction before a block's first is only one of the
        /// ways the value could have been produced. Stepping over that boundary reads the tail of
        /// whichever path happens to be written above, and a short-circuited <c>&amp;&amp;</c> puts
        /// a literal <c>0</c> there — the first attempt at this reported the map's unlock rule as
        /// <c>0 != 0</c>, which is worse than reporting nothing.
        /// </remarks>
        internal static Instruction Preceding(Instruction instruction, Instruction boundary)
        {
            if (instruction == null || instruction == boundary)
            {
                return null;
            }

            var previous = instruction.Previous;

            // A prefix — constrained., volatile., readonly. — is written as an instruction of its
            // own but leaves the stack alone, and stopping at one hid every argument of a method
            // called on a value type. Enum.Equals is the common case and it is a comparison.
            while (previous != null &&
                   (previous.OpCode.Code == Code.Nop || previous.OpCode.OpCodeType == OpCodeType.Prefix))
            {
                if (previous == boundary)
                {
                    return null;
                }

                previous = previous.Previous;
            }

            return previous;
        }

        /// <summary>
        /// What produced the value sitting under the one an instruction produced.
        /// </summary>
        /// <remarks>
        /// Stepping one instruction back is only the same thing as stepping one stack slot back for
        /// an instruction that consumes nothing. <c>ldfld</c> eats an object reference,
        /// <c>op_Equality</c> eats two arguments, and a reader that ignores that names the wrong
        /// operand rather than none — <c>a == b.Count</c> would be read as <c>b == b.Count</c>.
        ///
        /// So each input is skipped by the same rule, recursively, and anything whose effect on the
        /// stack is not in the table below stops the walk. Refusing there is the point: the caller
        /// reports the condition as unread, which is the honest answer.
        /// </remarks>
        internal static Instruction Under(Instruction instruction, Instruction boundary)
        {
            var eaten = Consumes(instruction);

            if (eaten < 0)
            {
                return null;
            }

            var cursor = Preceding(instruction, boundary);

            for (var index = 0; index < eaten && cursor != null; index++)
            {
                cursor = Under(cursor, boundary);
            }

            return cursor;
        }

        /// <summary>How many stack slots an instruction eats, or -1 when that is not known here.</summary>
        private static int Consumes(Instruction instruction)
        {
            if (instruction == null)
            {
                return -1;
            }

            if (TryConstant(instruction, out _))
            {
                return 0;
            }

            switch (instruction.OpCode.Code)
            {
                case Code.Ldstr:
                case Code.Ldnull:
                case Code.Ldc_I8:
                case Code.Ldc_R4:
                case Code.Ldc_R8:
                case Code.Ldarg_0:
                case Code.Ldarg_1:
                case Code.Ldarg_2:
                case Code.Ldarg_3:
                case Code.Ldarg:
                case Code.Ldarg_S:
                case Code.Ldarga:
                case Code.Ldarga_S:
                case Code.Ldloc_0:
                case Code.Ldloc_1:
                case Code.Ldloc_2:
                case Code.Ldloc_3:
                case Code.Ldloc:
                case Code.Ldloc_S:
                case Code.Ldloca:
                case Code.Ldloca_S:
                case Code.Ldsfld:
                case Code.Ldsflda:
                case Code.Ldtoken:
                case Code.Ldftn:
                case Code.Sizeof:
                    return 0;

                case Code.Ldfld:
                case Code.Ldflda:
                case Code.Ldlen:
                case Code.Ldobj:
                case Code.Ldvirtftn:
                case Code.Newarr:
                case Code.Box:
                case Code.Unbox:
                case Code.Unbox_Any:
                case Code.Castclass:
                case Code.Isinst:
                case Code.Neg:
                case Code.Not:
                    return 1;

                case Code.Add:
                case Code.Sub:
                case Code.Mul:
                case Code.Div:
                case Code.Rem:
                case Code.And:
                case Code.Or:
                case Code.Xor:
                case Code.Shl:
                case Code.Shr:
                case Code.Shr_Un:
                case Code.Ceq:
                case Code.Clt:
                case Code.Clt_Un:
                case Code.Cgt:
                case Code.Cgt_Un:
                    return 2;

                case Code.Call:
                case Code.Callvirt:
                    return instruction.Operand is MethodReference called
                        ? called.Parameters.Count + (called.HasThis ? 1 : 0)
                        : -1;

                case Code.Newobj:
                    return instruction.Operand is MethodReference constructor
                        ? constructor.Parameters.Count
                        : -1;

                default:
                    return ByName(instruction.OpCode.Name);
            }
        }

        /// <summary>
        /// The families that are too long to list one by one.
        /// </summary>
        /// <remarks>
        /// <c>conv.*</c> replaces the value it is given, <c>ldind.*</c> replaces an address with what
        /// is at it, and <c>ldelem.*</c> eats an array and an index. Anything else is unknown, and
        /// unknown stops the walk.
        /// </remarks>
        private static int ByName(string opcode)
        {
            if (opcode == null)
            {
                return -1;
            }

            if (opcode.StartsWith("conv.", StringComparison.Ordinal) ||
                opcode.StartsWith("ldind.", StringComparison.Ordinal))
            {
                return 1;
            }

            return opcode.StartsWith("ldelem", StringComparison.Ordinal) ? 2 : -1;
        }

        private const string BackingSuffix = ">k__BackingField";

        /// <summary>
        /// Names a field, or refuses to when it is the compiler's own bookkeeping.
        /// </summary>
        /// <remarks>
        /// A coroutine or a lambda is compiled into a type of its own, and the fields on it —
        /// <c>&lt;&gt;1__state</c>, <c>&lt;&gt;4__this</c>, the captured locals of a display class —
        /// are plumbing. Reported as effects they read as the game changing something, and in the
        /// sample game they were a seventh of everything the analysis claimed to have found.
        ///
        /// Refused by the type that declares them rather than by their own names, because one
        /// pattern of angle-bracketed name is not plumbing: the field behind an auto-property is
        /// declared on the game's own type and holds the game's own state. Dropping those by name
        /// would lose every <c>public int Score { get; set; }</c> in a codebase.
        /// </remarks>
        internal static string FieldName(FieldReference field)
        {
            var declaring = field?.DeclaringType;

            if (declaring == null)
            {
                return null;
            }

            if (declaring.Name.StartsWith("<", StringComparison.Ordinal))
            {
                return Hoisted(field.Name);
            }

            var name = field.Name;

            if (!name.StartsWith("<", StringComparison.Ordinal))
            {
                return declaring.Name + "." + name;
            }

            if (!name.EndsWith(BackingSuffix, StringComparison.Ordinal))
            {
                return null;
            }

            // Named as the property, which is what the source says and what a specification would
            // have to write.
            return declaring.Name + "." + name.Substring(1, name.Length - BackingSuffix.Length - 1);
        }

        /// <summary>
        /// A local the compiler moved onto a coroutine, named as the source named it.
        /// </summary>
        /// <remarks>
        /// A <c>for</c> counter inside a coroutine is not a local by the time it is read: it lives
        /// across a yield, so it is a field on the generated type. Refusing the whole type refused
        /// it with the plumbing, and the sample game's story screen lost the one term that says
        /// when the loop is over — without it "press a key and the map opens" is promised for every
        /// press rather than the last.
        ///
        /// The two are told apart by what is inside the brackets. Plumbing has nothing there
        /// (<c>&lt;&gt;1__state</c>, <c>&lt;&gt;4__this</c>, <c>&lt;&gt;t__builder</c>) because
        /// there was no source name to keep; a hoisted local has its own (<c>&lt;i&gt;5__1</c>).
        /// So this is not a guess about what a field means — it is the name the source wrote,
        /// read back out of where the compiler put it.
        ///
        /// No type name in front of it. The declaring type is a name nobody wrote and nobody could
        /// look up, and <c>&lt;StoryTelling&gt;d__8.i</c> says less than <c>i</c> does.
        /// </remarks>
        private static string Hoisted(string name)
        {
            if (name == null || !name.StartsWith("<", StringComparison.Ordinal))
            {
                return null;
            }

            var close = name.IndexOf('>');

            return close > 1 ? name.Substring(1, close - 1) : null;
        }

        /// <summary>
        /// What a local holds when the method writes it in exactly one place.
        /// </summary>
        /// <remarks>
        /// Returns the instruction that produced the stored value, so naming it is the same work
        /// as naming anything else. One store is the whole of the safety: however many times it
        /// runs, there is no other assignment this read could have seen, so nothing is being
        /// guessed about which one it was. More than one and the question comes back, and the
        /// answer is still no.
        ///
        /// Both what a value is called and whose it is come through here, and they have to come
        /// through together. An optimised compiler puts the value in a local and reads it back
        /// where a debugging one fetches it again, so the same source read one way in an editor
        /// scan and another in a development build; letting only the name see through the local
        /// left conditions that said <c>MapMove.StagePosition == 0</c> and refused to say whose.
        /// </remarks>
        private static Instruction StoredOnce(Instruction instruction, MethodDefinition within)
        {
            if (within == null || !within.HasBody || !IsLoadingLocal(instruction, out var slot))
            {
                return null;
            }

            Instruction only = null;

            foreach (var candidate in within.Body.Instructions)
            {
                if (!IsStoringLocal(candidate, out var stored) || stored != slot)
                {
                    continue;
                }

                if (only != null)
                {
                    return null;
                }

                only = candidate;
            }

            // The value is what came before the store. Reading it is bounded by nothing here
            // because the store is wherever the method put it, not wherever this read is.
            return only?.Previous;
        }

        /// <summary>
        /// The name a field is written by, when the field is nothing but a copy of which scene is
        /// running.
        /// </summary>
        /// <remarks>
        /// A controller that keeps <c>sceneName = SceneManager.GetActiveScene().name</c> and guards
        /// its whole body with <c>sceneName == "GameClearScene"</c> reads, without this, as a
        /// condition about a string nobody can evaluate. The sample game puts that one controller on
        /// two screens, so half of what it says is about a screen it is not on — and a specification
        /// that cannot see the guard promises the game-over screen everything the clear screen does.
        /// Whoever knows which scene an object was found in can settle it; nobody could settle
        /// <c>GameClearController.sceneName</c>.
        ///
        /// Only this one shape. The general rule — name a singly-written field by whatever was
        /// written to it — was tried and measured, and it is wrong: a local's one store sits in the
        /// same method as the read and a field's need not run first. <c>flag = true</c> is the only
        /// write to <c>flag</c>, and reading the field as <c>1</c> turned every test of it into
        /// <c>1 == 0</c>, which reads as a branch that can never be taken while the game takes it
        /// every time. It cost eighty-four good names besides — <c>onPushArea1</c> says more than
        /// the <c>Array.Exists()</c> that filled it.
        ///
        /// The active scene survives that objection because it is not a value: it is the same
        /// expression wherever it is read, before the write as after. So this names the expression
        /// and stops, and what it comes to is the reader's business.
        ///
        /// Private, so the only writer C# allows is the type itself, and not serialized, so there is
        /// no authored value sitting under the one store.
        /// </remarks>
        private static string WhichScene(
            FieldReference field, MethodDefinition within, Instruction boundary, int depth)
        {
            if (depth >= MaxReceiverDepth)
            {
                return null;
            }

            var held = WrittenOnce(field, within, out var wroteIt);

            if (held == null)
            {
                return null;
            }

            var named = Describe(held, boundary, wroteIt, depth + 1);

            return named == ActiveScene ? named : null;
        }

        /// <summary>The one expression a field may be read as, because it never comes to anything else.</summary>
        private const string ActiveScene = "SceneManager.GetActiveScene().name";

        /// <summary>What a type's field is written from, when the type writes it in exactly one place.</summary>
        private static Instruction WrittenOnce(
            FieldReference field, MethodDefinition within, out MethodDefinition wroteIt)
        {
            wroteIt = null;

            var owner = within?.DeclaringType;

            // Only the type being read, so that naming a field never resolves a reference into an
            // assembly this was not asked to look at.
            if (field?.DeclaringType == null || owner == null ||
                field.DeclaringType.FullName != owner.FullName)
            {
                return null;
            }

            FieldDefinition declared = null;

            foreach (var candidate in owner.Fields)
            {
                if (candidate.Name == field.Name)
                {
                    declared = candidate;
                    break;
                }
            }

            if (declared == null || !declared.IsPrivate || IsSerialized(declared))
            {
                return null;
            }

            Instruction only = null;

            foreach (var method in owner.Methods)
            {
                if (!method.HasBody)
                {
                    continue;
                }

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode.Code != Code.Stfld && instruction.OpCode.Code != Code.Stsfld)
                    {
                        continue;
                    }

                    if (!(instruction.Operand is FieldReference stored) || stored.Name != field.Name ||
                        stored.DeclaringType == null ||
                        stored.DeclaringType.FullName != owner.FullName)
                    {
                        continue;
                    }

                    if (only != null)
                    {
                        return null;
                    }

                    only = instruction;
                    wroteIt = method;
                }
            }

            if (only == null)
            {
                wroteIt = null;
                return null;
            }

            return only.Previous;
        }

        /// <summary>Whether the inspector could have put a value here before any code ran.</summary>
        private static bool IsSerialized(FieldDefinition field)
        {
            if (!field.HasCustomAttributes)
            {
                return false;
            }

            foreach (var attribute in field.CustomAttributes)
            {
                if (attribute.AttributeType != null &&
                    attribute.AttributeType.Name == "SerializeField")
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLoadingLocal(Instruction instruction, out int slot)
        {
            slot = -1;

            if (instruction == null)
            {
                return false;
            }

            switch (instruction.OpCode.Code)
            {
                case Code.Ldloc_0: slot = 0; return true;
                case Code.Ldloc_1: slot = 1; return true;
                case Code.Ldloc_2: slot = 2; return true;
                case Code.Ldloc_3: slot = 3; return true;
                case Code.Ldloc:
                case Code.Ldloc_S:
                case Code.Ldloca:
                case Code.Ldloca_S:
                    slot = (instruction.Operand as VariableDefinition)?.Index ?? -1;
                    return slot >= 0;
                default: return false;
            }
        }

        private static bool IsStoringLocal(Instruction instruction, out int slot)
        {
            slot = -1;

            if (instruction == null)
            {
                return false;
            }

            switch (instruction.OpCode.Code)
            {
                case Code.Stloc_0: slot = 0; return true;
                case Code.Stloc_1: slot = 1; return true;
                case Code.Stloc_2: slot = 2; return true;
                case Code.Stloc_3: slot = 3; return true;
                case Code.Stloc:
                case Code.Stloc_S:
                    slot = (instruction.Operand as VariableDefinition)?.Index ?? -1;
                    return slot >= 0;
                default: return false;
            }
        }

        /// <summary>Names a property read as the property rather than as its getter.</summary>
        private static string PropertyName(MethodReference method)
        {
            if (method == null || !method.Name.StartsWith("get_", StringComparison.Ordinal))
            {
                return null;
            }

            return method.DeclaringType.Name + "." + method.Name.Substring(4);
        }

        internal static TypeDefinition SafeResolve(TypeReference reference)
        {
            try
            {
                return reference?.Resolve();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The name an enum gives to one of its values.
        /// </summary>
        /// <remarks>
        /// The number is what the instruction carries; the name lives in the enum's own metadata, in
        /// whichever assembly defines it. Falls back to the number, which is still usable and is
        /// visibly not a name.
        /// </remarks>
        internal static string EnumName(TypeReference enumType, int value)
        {
            var definition = SafeResolve(enumType);

            if (definition == null || !definition.IsEnum)
            {
                return value.ToString();
            }

            foreach (var field in definition.Fields)
            {
                if (field.HasConstant && field.Constant is int constant && constant == value)
                {
                    return field.Name;
                }
            }

            return value.ToString();
        }
    }
}
