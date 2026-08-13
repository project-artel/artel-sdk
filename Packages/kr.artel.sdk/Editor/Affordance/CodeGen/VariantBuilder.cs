using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// Turns a graphed method into input, precondition and outcome.
    /// </summary>
    /// <remarks>
    /// Read from the outcome backwards. Every change the code makes sits in some block, and the
    /// decisions that block is control dependent on — followed up through the decisions those
    /// depend on in turn — are the complete account of how a player gets there. Among them, the
    /// ones testing an input say which input; the rest say what else had to be true.
    ///
    /// Read this way <c>A || B</c> needs no special case. Both tests govern the same outcome, so
    /// both appear, which is what the code means.
    /// </remarks>
    internal static class VariantBuilder
    {
        private const string InputType = "UnityEngine.Input";

        private const byte Unvisited = 0;
        private const byte Computing = 1;
        private const byte Settled = 2;

        internal static void Collect(
            MethodDefinition method,
            MethodDefinition entry,
            IReadOnlyList<MethodDefinition> callPath,
            bool callPathTruncated,
            ControlFlowGraph graph,
            ControlDependence dependence,
            List<Variant> variants,
            string triggerKind,
            Condition reachedBy,
            bool sameObject,
            Binding binding)
        {
            // Held across the whole method. Every block's condition is worked out at most once, and
            // blocks high in the method are on the way to most of the others.
            var reached = new Condition[graph.Blocks.Count];
            var state = new byte[graph.Blocks.Count];

            foreach (var block in graph.Blocks)
            {
                if (block.IsExit)
                {
                    continue;
                }

                var outcomes = OutcomesIn(block, method);
                var calls = CallsIn(block, method.Module, graph.HasThis);

                var handles = new List<Subscription>();
                Subscriptions.ReadInto(block, method.Module, handles);

                // An input read with nothing else in the block. A predicate handed to `WaitUntil`
                // is exactly that shape — `() => Input.GetKeyDown(Space)` returns the answer
                // instead of branching on it, so no gesture is ever made of it, and the block is
                // dropped before anyone notices it mentioned a key. The sample game's whole story
                // screen advanced on Space and the report said no key at all.
                //
                // A block that branches is left alone. Its read is already a gesture in the
                // condition of everything it governs, and saying it a second time here would both
                // double it and label it as unbranched, which is the one thing it is not.
                var answered = outcomes.Count == 0 && calls.Count == 0 && handles.Count == 0 &&
                               block.Last?.OpCode.FlowControl != FlowControl.Cond_Branch
                    ? InputIn(block)
                    : null;

                if (answered == null &&
                    outcomes.Count == 0 && calls.Count == 0 && handles.Count == 0)
                {
                    continue;
                }

                var own = Reach(graph, dependence, block.Index, reached, state);
                var derived = !ReferenceEquals(method, entry);

                // Two conditions written against two different receivers must not be run together
                // into one sentence: the callee's `count > 0` is about the callee's object, and
                // beside the caller's own terms it reads as the caller's `count`. That is the one
                // case left alone.
                //
                // Mixing is the whole risk, so anything that does not mix is safe. An unguarded
                // call contributes nothing to compose, leaving the callee's own condition as the
                // complete account of reaching it; a callee with no condition of its own leaves the
                // caller's. Either way exactly one side speaks, and it speaks in its own terms.
                // Two conditions may be run together when they are known to be about the same
                // object. That is the case when every call along the way was made on the caller's
                // own object — `this` is then the same thing at both ends — and both sides say only
                // things about `this` or about nothing at all.
                //
                // Kept as narrow as this on purpose. It was measured that the alternative, guessing,
                // produces sentences that read perfectly and are about the wrong object.
                // Said where the caller stands, when the caller called it on something it can
                // name. The callee's terms then describe the caller's own object and the sentence
                // has one subject, which is the whole of what the rule below asks for.
                var said = binding == null || sameObject ? null : own.ReadFrom(binding);

                if (said != null)
                {
                    own = said;
                }

                var joinable = (sameObject || said != null) &&
                               own.AboutSelfOnly() && reachedBy.AboutSelfOnly();

                var mixes = derived &&
                            !joinable &&
                            reachedBy.Kind != ConditionKind.Always &&
                            own.Kind != ConditionKind.Always;

                var composable = !mixes;

                // Where they would mix, the caller's terms are dropped and only the inputs among
                // them come down. An input is the one part of a condition that is not about an
                // object: the caller's `count > 0` means something else next to the callee's terms,
                // but the caller's `Space was pressed` means the same thing wherever it is written.
                //
                // Without this a game that reads its keys in one method and does the work in
                // another has no inputs anywhere in its evidence. Trash Dash is that game — every
                // key it reads is one call away from every effect it has, and the report named none
                // of them. The condition is still incomplete and still says so; what changes is
                // that it is now incomplete about *what else* had to be true, rather than silent
                // about what the player did.
                var carried = mixes ? reachedBy.InputsOnly() : reachedBy;

                var when = derived
                    ? Condition.Every(new[] { carried, own })
                    : own;

                var variant = new Variant
                {
                    Method = method.FullName,
                    MethodId = MethodIdentity.Of(method),
                    Entry = entry.FullName,
                    EntryId = MethodIdentity.Of(entry),
                    TriggerKind = triggerKind,
                    Owner = Owning(method.DeclaringType) ?? Owning(entry.DeclaringType),
                    When = when
                };

                foreach (var step in callPath)
                {
                    variant.CallPath.Add(step.FullName);
                }

                if (callPathTruncated)
                {
                    variant.AddGap("call-path-limit");
                }

                if (derived && joinable && reachedBy.Kind != ConditionKind.Always &&
                    own.Kind != ConditionKind.Always)
                {
                    // Said because the sentence now has two authors. It is one account of one
                    // object, and which part came from where is no longer visible in it.
                    variant.AddGap("composed-on-same-object");
                }

                if (derived && !composable)
                {
                    variant.AddGap("callee-condition-not-composed");

                    // Said separately because it changes how the condition must be read: the input
                    // in it was given somewhere up the call path, not here.
                    if (carried.Kind != ConditionKind.Always)
                    {
                        variant.AddGap("caller-inputs-carried");
                    }
                }

                var plumbing = SingletonPlumbing.Explains(entry, outcomes);

                if (plumbing)
                {
                    variant.AddGap("singleton-plumbing");
                }

                // Nothing to act on, or no complete account of how to get here. Kept because a call
                // with no effects of its own is how the path to the effects is followed.
                variant.RecordKind = outcomes.Count > 0 && composable && !plumbing ? "candidate" : "flow";

                variant.Outcomes.AddRange(outcomes);
                variant.Calls.AddRange(calls);
                variant.Handles.AddRange(handles);
                variant.LoopsBackTo = GoesRoundAgain(block);
                when.CollectGestures(variant.Inputs, new HashSet<Condition>());

                // Not a condition: nothing here branches on it. The method's answer is the read,
                // and saying so beside the path that handed it over is the whole of what is known.
                if (answered != null)
                {
                    variant.Inputs.Add(answered);
                    variant.AddGap("input-not-branched");
                }
                variant.Incomplete = when.HasUnknown(new HashSet<Condition>());

                if (variant.Incomplete)
                {
                    variant.AddGap("unread-condition");
                }

                variants.Add(variant);
            }
        }

        /// <summary>The condition under which a block runs, worked out once and kept.</summary>
        internal static Condition ReachOf(
            ControlFlowGraph graph,
            ControlDependence dependence,
            int block,
            Condition[] reached,
            byte[] state)
        {
            return Reach(graph, dependence, block, reached, state);
        }

        /// <summary>
        /// The type a result gets baked onto.
        /// </summary>
        /// <remarks>
        /// The compiler puts a coroutine body or a lambda in a nested type of its own, and that
        /// type is not a component. What the scan finds on a GameObject is the behaviour those were
        /// written inside, so that is where the result has to end up.
        /// </remarks>
        private static TypeDefinition Owning(TypeDefinition type)
        {
            var current = type;

            for (var depth = 0; depth < 8 && current != null; depth++)
            {
                if (AnalysisScope.Inspect(current) == TypeVerdict.Behaviour)
                {
                    return current;
                }

                current = current.DeclaringType;
            }

            return null;
        }

        /// <summary>
        /// What had to be true to reach a block.
        /// </summary>
        /// <remarks>
        /// Each way into a block is an alternative, and going one step further back is a further
        /// requirement — so the answer is a choice among ways, each of them the test on that way
        /// together with whatever it took to get to the test.
        ///
        /// Loops make this graph circular: the test at the top of a loop is subject to itself. A
        /// block already being worked out is marked as such, and meeting that mark again puts an
        /// unknown in its place instead of following the circle. Every block moves from unvisited to
        /// computing to settled exactly once, which is what makes this finish rather than what
        /// bounds how long it takes to.
        /// </remarks>
        private static Condition Reach(
            ControlFlowGraph graph,
            ControlDependence dependence,
            int start,
            Condition[] reached,
            byte[] state)
        {
            // An explicit stack. Conditions nest as deeply as a method branches, and a recursive
            // walk of a generated method is how a build turns into a dead editor.
            var pending = new Stack<int>();
            pending.Push(start);

            while (pending.Count > 0)
            {
                var index = pending.Peek();

                if (state[index] == Settled)
                {
                    pending.Pop();
                    continue;
                }

                if (state[index] == Unvisited)
                {
                    state[index] = Computing;
                    var waiting = false;

                    foreach (var governor in dependence.Governing(index))
                    {
                        if (state[governor.Decision] == Unvisited)
                        {
                            pending.Push(governor.Decision);
                            waiting = true;
                        }
                    }

                    if (waiting)
                    {
                        continue;
                    }
                }

                reached[index] = Combine(graph, dependence, index, reached, state);
                state[index] = Settled;
                pending.Pop();
            }

            return reached[start];
        }

        private static Condition Combine(
            ControlFlowGraph graph,
            ControlDependence dependence,
            int index,
            Condition[] reached,
            byte[] state)
        {
            var governors = dependence.Governing(index);

            if (governors.Count == 0)
            {
                // Nothing decides whether this runs.
                return Condition.Always;
            }

            var ways = new List<Condition>(governors.Count);

            foreach (var governor in governors)
            {
                // A governor still being worked out is one this block is inside the loop of, and
                // "you got here by going round again" is not a thing a tester arranges. The test
                // that sends control round is already the `Literal` beside this, and whatever
                // guarded the loop from outside governs this block on its own account — a block
                // reached only when an `if` was taken is control dependent on that `if` whether the
                // loop is in the way or not. So the way round adds nothing, and saying `unknown`
                // for it took the whole condition down with it: twenty-six of the sample game's
                // records read `i < cards.Count and <something nobody could read>`.
                //
                // That the block is in a loop is not lost. It is a fact about the block, worked out
                // from the graph rather than from a failure to write a condition, and the record
                // carries it as `loopsBackTo`.
                var earlier = state[governor.Decision] == Settled
                    ? reached[governor.Decision]
                    : Condition.Always;

                ways.Add(Condition.Every(new[]
                {
                    Literal(
                        graph.Blocks[governor.Decision],
                        graph.Blocks[governor.Taken],
                        graph,
                        dependence,
                        reached,
                        state),
                    earlier
                }));
            }

            return Condition.Either(ways);
        }

        /// <summary>How many ways into a merge will be read before giving up.</summary>
        private const int MaxMergeWays = 4;

        /// <summary>
        /// The value a short-circuit left for the branch to test.
        /// </summary>
        /// <remarks>
        /// A debug build does not leave `(A || B) &amp;&amp; C` on the stack. It computes the answer
        /// in the blocks that made each test, stores it, and branches on the load — so the block
        /// holding the branch begins with the store and there is nothing behind it to read. Six of
        /// the sample game's arrow keys vanished this way in a development build and none of them
        /// did in the editor, because an optimised compiler leaves the value where the walk can
        /// still see it. The two builds were being read as if they said the same thing.
        ///
        /// Read forwards from the ways in rather than backwards past the block. Each way either
        /// pushed a literal — the branch it lost — or pushed the comparison it made, and reaching
        /// that way is a condition already worked out. Nothing here crosses a block boundary to
        /// guess at a value: it asks each block what it left, in that block's own terms.
        ///
        /// Gives up whole rather than in part. A way that cannot be read makes the others a
        /// half-account of when this runs, and a half-account of a condition is the shape of a
        /// precondition that is simply wrong.
        /// </remarks>
        private static Condition Incoming(
            BasicBlock decision,
            BasicBlock taken,
            ControlFlowGraph graph,
            ControlDependence dependence,
            Condition[] reached,
            byte[] state)
        {
            var branch = decision.Last;
            var onTrue = branch.OpCode.Code == Code.Brtrue || branch.OpCode.Code == Code.Brtrue_S;
            var onFalse = branch.OpCode.Code == Code.Brfalse || branch.OpCode.Code == Code.Brfalse_S;

            if (!onTrue && !onFalse)
            {
                return null;
            }

            // Only when the block genuinely has nothing of its own: the store the branch reads is
            // the first thing in it, so the value was made elsewhere.
            var value = Preceding(branch, decision);

            if (!IsLoadLocal(value, out var slot))
            {
                return null;
            }

            var store = Preceding(value, decision);

            if (!IsStoreLocal(store, out var stored) || stored != slot ||
                Preceding(store, decision) != null)
            {
                return null;
            }

            if (decision.Predecessors.Count == 0 || decision.Predecessors.Count > MaxMergeWays)
            {
                return null;
            }

            var branched = ReferenceEquals(taken.First, branch.Operand as Instruction);
            var wantTrue = onTrue ? branched : !branched;
            var ways = new List<Condition>();

            foreach (var from in decision.Predecessors)
            {
                var pushed = Pushed(from);

                if (pushed == null)
                {
                    return null;
                }

                var arriving = Reach(graph, dependence, from.Index, reached, state);

                if (IlReading.TryConstant(pushed, out var literal))
                {
                    // The way that already lost. It cannot be how control got here.
                    if (literal != 0 == wantTrue)
                    {
                        ways.Add(arriving);
                    }

                    continue;
                }

                // The way in made the test itself rather than comparing something: `A || B` with
                // nothing after it leaves the second read on the stack. Read as a comparison it
                // would be "GetKeyDown != 0", which is not a thing a tester can be told to do.
                var read = ReadInput(pushed);

                if (read != null)
                {
                    read.Absent = !wantTrue;
                    ways.Add(Condition.Every(new[] { arriving, Condition.FromGesture(read) }));
                    continue;
                }

                var comparison = ComparisonOperator(pushed, wantTrue, from, out var operands);

                if (comparison == null)
                {
                    return null;
                }

                Operands(operands, from, graph.HasThis, graph.Method, out var left, out var right,
                    out var context, out _, out _, out var watch);

                if (left == null || right == null)
                {
                    return null;
                }

                ways.Add(Condition.Every(new[]
                {
                    arriving,
                    Condition.FromTest(new Precondition
                    {
                        Left = left,
                        Operator = comparison,
                        Right = right,
                        Context = context,
                        Watch = watch,
                        Offset = pushed.Offset
                    })
                }));
            }

            return ways.Count == 0 ? null : Condition.Either(ways);
        }

        /// <summary>
        /// Where control comes back to if this block is part of going round again, or -1.
        /// </summary>
        /// <remarks>
        /// An edge that arrives from later in the method, or leaves for earlier in it, is the
        /// whole test. Reducible is what a C# compiler emits and offsets follow the code, so this
        /// asks about the two blocks at the ends of the edge and nothing else — no dominators, no
        /// loop body worked out. A block merely inside the loop is not claimed, because claiming it
        /// would need the question this deliberately does not ask.
        /// </remarks>
        private static int GoesRoundAgain(BasicBlock block)
        {
            var here = block.First?.Offset ?? -1;

            if (here < 0)
            {
                return -1;
            }

            // Control returns here: something later jumps back to this block.
            foreach (var from in block.Predecessors)
            {
                if ((from.First?.Offset ?? -1) > here)
                {
                    return here;
                }
            }

            // This block is what jumps back.
            var earliest = -1;

            foreach (var to in block.Successors)
            {
                var there = to.First?.Offset ?? -1;

                if (there >= 0 && there <= here && (earliest < 0 || there < earliest))
                {
                    earliest = there;
                }
            }

            return earliest;
        }

        /// <summary>What a block left on the stack for whatever runs next.</summary>
        private static string Shape(Instruction instruction)
        {
            return instruction == null ? "none" : instruction.OpCode.Name;
        }

        /// <summary>
        /// The value a block was handed, when only one of the ways in actually worked it out.
        /// </summary>
        /// <remarks>
        /// A short-circuited <c>&amp;&amp;</c> stores its answer at the top of a block reached two
        /// ways: one way has just compared something, the other jumped straight here with a literal
        /// because the left side already settled it. Reading backwards from the store lands on the
        /// boundary and gives up, which left fifty-five of the development build's conditions
        /// unread — every one of them a block of two ways in beginning with a <c>stloc</c>.
        ///
        /// The literal is not a second answer. It is the shape of the jump the compiler made, and
        /// the test it stands for is already in this block's condition on its own account: the
        /// block is control dependent on the decision that did the short-circuiting. So when one
        /// way in carries a constant and the other carries something that can be read, the one that
        /// can be read is what was tested here.
        ///
        /// Both readable and it is a choice of two values rather than a short circuit — a ternary —
        /// and neither is the answer. Both constant and there was nothing to read either way.
        /// </remarks>
        private static Instruction JoinedAbove(BasicBlock decision)
        {
            if (decision.Predecessors.Count != 2 ||
                !IsStoreLocal(decision.First, out var slot))
            {
                return null;
            }

            // The branch has to be testing the very thing that was stored, or this is some other
            // block that happens to start with a store.
            var value = Preceding(decision.Last, decision);

            if (!IsLoadLocal(value, out var read) || read != slot)
            {
                return null;
            }

            Instruction worked = null;

            foreach (var before in decision.Predecessors)
            {
                var handed = Pushed(before);

                if (handed == null)
                {
                    return null;
                }

                if (IlReading.TryConstant(handed, out _))
                {
                    continue;
                }

                if (worked != null)
                {
                    // Two ways in that both worked something out. Which one this read saw is the
                    // question that is not asked here.
                    return null;
                }

                worked = handed;
            }

            return worked;
        }

        private static Instruction Pushed(BasicBlock from)
        {
            var last = from.Last;

            if (last == null)
            {
                return null;
            }

            // An unconditional jump carries the value made before it; a conditional one decided
            // rather than produced, and there is nothing of its own to take.
            if (last.OpCode.FlowControl == FlowControl.Branch)
            {
                return Preceding(last, from);
            }

            return last.OpCode.FlowControl == FlowControl.Cond_Branch ? null : last;
        }

        /// <summary>What one decision, taken one way, says.</summary>
        private static Condition Literal(
            BasicBlock decision,
            BasicBlock taken,
            ControlFlowGraph graph,
            ControlDependence dependence,
            Condition[] reached,
            byte[] state)
        {
            var branch = decision.Last;

            // Only when the branch tests the input's answer directly. Anywhere else in the block
            // and there is no telling which way means pressed, and a gesture recorded the wrong way
            // round turns a precondition into an instruction to press the opposite key.
            //
            // "Directly" is read through the local a debug build stores the answer in, which is the
            // same test written the way a non-optimised compiler writes it.
            var input = ReadInput(Producer(branch, decision));

            if (input != null)
            {
                var pressedWhenBranched = branch.OpCode.Code == Code.Brtrue ||
                                          branch.OpCode.Code == Code.Brtrue_S;

                var absentWhenBranched = branch.OpCode.Code == Code.Brfalse ||
                                         branch.OpCode.Code == Code.Brfalse_S;

                if (!pressedWhenBranched && !absentWhenBranched)
                {
                    return Condition.Unreadable("input");
                }

                var branched = ReferenceEquals(taken.First, branch.Operand as Instruction);
                input.Absent = pressedWhenBranched ? !branched : branched;

                // The test itself is the gesture. Reading it as a comparison would produce
                // "GetKeyDown != 0", which says nothing a specification could use.
                return Condition.FromGesture(input);
            }

            if (InputIn(decision) != null)
            {
                return Condition.Unreadable("input");
            }

            if (IsResumeDispatch(decision, graph.StateSlot))
            {
                return Condition.Always;
            }

            if (branch.OpCode.Code == Code.Switch)
            {
                return SwitchCase(decision, taken, graph);
            }

            var incoming = Incoming(decision, taken, graph, dependence, reached, state);

            if (incoming != null)
            {
                return incoming;
            }

            var precondition = ReadCondition(decision, taken, graph.HasThis, graph.Method, out var unread);

            return precondition == null
                ? Condition.Unreadable("condition", unread)
                : Condition.FromTest(precondition);
        }

        /// <summary>
        /// Whether this decision is a coroutine picking up where it left off.
        /// </summary>
        /// <remarks>
        /// A coroutine is compiled into a state machine, and the first thing <c>MoveNext</c> does is
        /// branch on which <c>yield</c> it stopped at. Control dependence sees an ordinary decision
        /// and everything after it reads as guarded by something — and since the field it tests is
        /// the compiler's, not the game's, that something comes out as a condition nobody could
        /// read. In the sample game the cast, the wave ending, the dialogue and the turn handover
        /// are all inside coroutines, and all four arrived saying their own conditions were unread.
        ///
        /// It is not an unread condition. It is not a condition. Reported as nothing having to be
        /// true, which is what the resume point means for anyone reading the game rather than the
        /// compiler's rewriting of it — the real conditions are the ones the original code wrote,
        /// and those are still in the blocks that follow.
        /// </remarks>
        private static bool IsResumeDispatch(BasicBlock block, int stateSlot)
        {
            for (var instruction = block.First; instruction != null; instruction = instruction.Next)
            {
                if (instruction.OpCode.Code == Code.Ldfld &&
                    instruction.Operand is FieldReference field &&
                    field.Name == StateField &&
                    field.DeclaringType != null &&
                    field.DeclaringType.Name.StartsWith("<", System.StringComparison.Ordinal))
                {
                    return true;
                }

                // Or a later block of the same dispatch, testing the copy the first one made.
                if (stateSlot >= 0 && IsLoadLocal(instruction, out var slot) && slot == stateSlot)
                {
                    return true;
                }

                if (instruction == block.Last)
                {
                    break;
                }
            }

            return false;
        }

        /// <summary>What the compiler calls the field holding which yield a coroutine stopped at.</summary>
        private const string StateField = "<>1__state";

        private static InputRead InputIn(BasicBlock block)
        {
            for (var instruction = block.First; instruction != null; instruction = instruction.Next)
            {
                var read = ReadInput(instruction);

                if (read != null)
                {
                    return read;
                }

                if (instruction == block.Last)
                {
                    break;
                }
            }

            return null;
        }

        private static InputRead ReadInput(Instruction instruction)
        {
            if (instruction == null ||
                !(instruction.Operand is MethodReference called) ||
                instruction.OpCode.FlowControl != FlowControl.Call ||
                called.DeclaringType?.FullName != InputType)
            {
                return null;
            }

            switch (called.Name)
            {
                case "GetKeyDown": return Key(instruction, called, "down");
                case "GetKey": return Key(instruction, called, "held");
                case "GetKeyUp": return Key(instruction, called, "up");
                case "GetMouseButtonDown": return Mouse(instruction, "down");
                case "GetMouseButton": return Mouse(instruction, "held");
                case "GetMouseButtonUp": return Mouse(instruction, "up");
                case "get_anyKeyDown": return new InputRead { Gesture = "key", Name = "any", Phase = "down", Offset = instruction.Offset };
                case "get_anyKey": return new InputRead { Gesture = "key", Name = "any", Phase = "held", Offset = instruction.Offset };
                default: return null;
            }
        }

        private static InputRead Key(Instruction instruction, MethodReference called, string phase)
        {
            var read = new InputRead { Gesture = "key", Phase = phase, Offset = instruction.Offset };
            var argument = instruction.Previous;

            if (argument != null && argument.OpCode.Code == Code.Ldstr)
            {
                read.Name = argument.Operand as string;
                return read;
            }

            if (IlReading.TryConstant(argument, out var value) && called.Parameters.Count == 1)
            {
                read.Name = IlReading.EnumName(called.Parameters[0].ParameterType, value);
                return read;
            }

            // The key is in a variable. Which key cannot be answered here, and saying so is the
            // whole value of the entry — it is one input the scan does not cover.
            read.Name = "(not a literal)";
            return read;
        }

        private static InputRead Mouse(Instruction instruction, string phase)
        {
            var read = new InputRead { Gesture = "mouse", Phase = phase, Offset = instruction.Offset };
            read.Name = IlReading.TryConstant(instruction.Previous, out var button)
                ? button.ToString()
                : "(not a literal)";
            return read;
        }

        /// <summary>
        /// Direct changes made in this block. Callee effects stay on the callee's own evidence.
        /// </summary>
        /// <remarks>
        /// Keeping only direct effects is deliberate. Copying a callee's outcomes here loses the
        /// callee's conditions and makes mutually exclusive scene loads look simultaneous.
        /// </remarks>
        private static List<Outcome> OutcomesIn(BasicBlock block, MethodDefinition method)
        {
            var outcomes = new List<Outcome>();

            for (var instruction = block.First; instruction != null; instruction = instruction.Next)
            {
                var outcome = OutcomeReader.ReadDirect(instruction, block.First, method);

                if (outcome != null)
                {
                    outcomes.Add(outcome);
                }
                if (instruction == block.Last)
                {
                    break;
                }
            }

            return outcomes;
        }

        private static List<CallEdge> CallsIn(BasicBlock block, ModuleDefinition module, bool hasThis)
        {
            var calls = new List<CallEdge>();

            for (var instruction = block.First; instruction != null; instruction = instruction.Next)
            {
                var callee = CallGraph.CalleeAt(instruction, module);

                if (callee != null)
                {
                    var reference = instruction.Operand as MethodReference;

                    calls.Add(new CallEdge
                    {
                        TargetId = MethodIdentity.Of(callee),
                        Target = callee.FullName,
                        Receiver = IlReading.Receiver(reference, instruction, block.First),
                        ReceiverWhere = IlReading.ReceiverWhere(
                            reference, instruction, block.First, hasThis),
                        Arguments = IlReading.Arguments(reference, instruction, block.First),
                        Offset = instruction.Offset
                    });
                }

                if (instruction == block.Last)
                {
                    break;
                }
            }

            return calls;
        }

        /// <summary>
        /// The comparison a decision makes, stated so that taking this way makes it true.
        /// </summary>
        /// <remarks>
        /// A branch says what sends control to its target; arriving by falling through means the
        /// opposite held. Both edges come through here, so the test is negated for one of them.
        /// </remarks>
        private static Precondition ReadCondition(
            BasicBlock decision, BasicBlock taken, bool hasThis, MethodDefinition method,
            out string unread)
        {
            var branch = decision.Last;
            unread = null;

            if (!(branch.Operand is Instruction target))
            {
                // A switch. Which case was taken is knowable, but not by reading two operands.
                unread = "branch:" + branch.OpCode.Name;
                return null;
            }

            var branched = ReferenceEquals(taken.First, target);
            var comparison = Operator(branch.OpCode.Code, branched);

            if (comparison == null)
            {
                unread = "operator:" + branch.OpCode.Name;
                return null;
            }

            string left;
            string right;
            string context;
            Instruction at;
            string lost = null;
            WatchTarget watch = null;

            if (branch.OpCode.Code == Code.Brtrue || branch.OpCode.Code == Code.Brtrue_S ||
                branch.OpCode.Code == Code.Brfalse || branch.OpCode.Code == Code.Brfalse_S)
            {
                var producer = Producer(branch, decision) ?? JoinedAbove(decision);

                // A debug build tests a comparison it has already made, so the branch says only
                // whether that answer held. The comparison is the condition; reading it as
                // "the answer != 0" would throw away both operands and the operator with them.
                var holds = branch.OpCode.Code == Code.Brtrue || branch.OpCode.Code == Code.Brtrue_S
                    ? branched
                    : !branched;

                var compared = ComparisonOperator(producer, holds, decision, out var operands);

                if (compared != null)
                {
                    Operands(operands, decision, hasThis, method, out left, out right, out context,
                        out at, out lost, out watch);
                    comparison = compared;
                }
                else
                {
                    left = IlReading.Describe(producer, decision.First, method);
                    right = "0";
                    context = IlReading.Where(
                        producer, decision.First, hasThis, method, out var singleStop);
                    lost = context == null ? "single:" + Shape(singleStop) : null;
                    watch = WatchTarget.From(IlReading.Holding(producer, method))
                            ?? WatchTarget.ReadOff(producer, decision.First, method);
                    at = producer;
                }
            }
            else
            {
                Operands(branch, decision, hasThis, method, out left, out right, out context,
                    out at, out lost, out watch);
            }

            if (left == null || right == null)
            {
                unread = "operand:" + (at == null ? "none" : at.OpCode.Name);
                return null;
            }

            return new Precondition
            {
                Left = left,
                Operator = comparison,
                Right = right,
                Context = context,
                SubjectLost = lost,
                Watch = watch,
                Offset = branch.Offset
            };
        }

        /// <summary>
        /// Which case of a switch this way in is.
        /// </summary>
        /// <remarks>
        /// A switch was refused outright — "knowable, but not by reading two operands" — and three
        /// of the sample game's features sat behind one: which background the map shows, which card
        /// a stage rewards, where the character starts. The jump table is right there in the
        /// instruction; the only work is saying what an index means.
        ///
        /// Two things make it more than reading the table. A case can share a block with another
        /// (<c>case 4</c> and <c>case 5</c> doing the same thing), so one way in can mean several
        /// values at once — a choice, not one test. And a switch whose cases do not start at zero is
        /// compiled with the subtraction folded in front of it, so the index is not the value.
        ///
        /// The fall-through is written as the pair of comparisons it actually is. IL compares
        /// unsigned, so a negative value falls through as surely as one past the end, and
        /// <c>&gt;= count</c> alone would be a claim that is false for half the numbers.
        /// </remarks>
        private static Condition SwitchCase(BasicBlock decision, BasicBlock taken, ControlFlowGraph graph)
        {
            if (!(decision.Last.Operand is Instruction[] targets) || targets.Length == 0)
            {
                return Condition.Unreadable("switch");
            }

            var subject = Producer(decision.Last, decision);
            var offset = 0;

            // A switch on cases starting anywhere but zero has the shift folded in front of it.
            if (subject != null && (subject.OpCode.Code == Code.Sub || subject.OpCode.Code == Code.Add))
            {
                var shift = Preceding(subject, decision);

                if (!IlReading.TryConstant(shift, out var amount))
                {
                    return Condition.Unreadable("switch");
                }

                offset = subject.OpCode.Code == Code.Sub ? amount : -amount;
                subject = Preceding(shift, decision);
            }

            var name = IlReading.Describe(subject, decision.First, graph.Method)
                       ?? Argument(subject, graph.Method);

            if (name == null)
            {
                return Condition.Unreadable("switch");
            }

            var where = IlReading.Where(
                subject, decision.First, graph.HasThis, graph.Method, out var stoppedAt);

            // A switch sends one value down a branch rather than comparing two, so it is built here
            // and not where the other conditions say where they lost the subject. Said nowhere, a
            // `context: null` from a switch looked like a condition that had never been asked.
            var lost = where == null ? "switch:" + Shape(stoppedAt) : null;
            var cases = new List<Condition>();

            for (var index = 0; index < targets.Length; index++)
            {
                if (ReferenceEquals(taken.First, targets[index]))
                {
                    cases.Add(Condition.FromTest(new Precondition
                    {
                        Left = name,
                        Operator = "==",
                        Right = (index + offset).ToString(),
                        Context = where,
                        SubjectLost = lost,
                        Offset = decision.Last.Offset
                    }));
                }
            }

            if (cases.Count > 0)
            {
                return Condition.Either(cases);
            }

            // Not one of the cases, so this is the default. Both ends of the range are needed.
            return Condition.Every(new[]
            {
                Condition.FromTest(new Precondition
                {
                    Left = name, Operator = ">=", Right = offset.ToString(),
                    Context = where, SubjectLost = lost, Offset = decision.Last.Offset
                }),
                Condition.FromTest(new Precondition
                {
                    Left = name, Operator = ">=", Right = (targets.Length + offset).ToString(),
                    Context = where, SubjectLost = lost, Offset = decision.Last.Offset
                })
            });
        }

        /// <summary>
        /// The name of a parameter a value was loaded from.
        /// </summary>
        /// <remarks>
        /// <see cref="IlReading.Describe"/> does not name arguments, and for the most part it should
        /// not — an argument is a name from inside one method and means nothing beside a caller's
        /// terms. A switch is the case where it is worth having anyway, because the whole condition
        /// is the argument and without it there is no sentence at all. The <c>context</c> the atom
        /// carries says <c>arg:N</c>, so nobody can mistake it for the receiver's own state.
        /// </remarks>
        private static string Argument(Instruction instruction, MethodDefinition method)
        {
            if (instruction == null || method == null)
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
                case Code.Ldarg:
                case Code.Ldarg_S:
                    return (instruction.Operand as ParameterDefinition)?.Name;
                default: return null;
            }

            if (method.HasThis)
            {
                index--;
            }

            return index >= 0 && index < method.Parameters.Count ? method.Parameters[index].Name : null;
        }

        /// <summary>
        /// The two values a comparison compared.
        /// </summary>
        /// <remarks>
        /// The right one is whatever sits immediately before, which needs no analysis. The left one
        /// is underneath it, and getting there means skipping over everything the right one
        /// consumed — one instruction back is only one slot back for a value that consumed nothing.
        /// Reading it as one instruction back named the wrong operand whenever the right-hand side
        /// was a field or a call on something: <c>a == b.Count</c> came out as <c>b == b.Count</c>.
        /// Where the skip cannot be made the left side is left unnamed and the condition is dropped
        /// as unread, which is what it always was.
        /// </remarks>
        private static void Operands(
            Instruction consumer,
            BasicBlock decision,
            bool hasThis,
            MethodDefinition method,
            out string left,
            out string right,
            out string context,
            out Instruction unreadAt,
            out string lost,
            out WatchTarget watch)
        {
            var boundary = decision.First;
            var rightAt = IlReading.Preceding(consumer, boundary);
            var leftAt = IlReading.Under(rightAt, boundary);

            right = IlReading.Describe(rightAt, boundary, method);
            left = IlReading.Describe(leftAt, boundary, method);

            // The same instruction the name came from, asked whether it is somewhere to look. Taken
            // here rather than by the callers because this is where the left side is worked out, and
            // a second walk to find it again could disagree with the first.
            //
            // A field first, and failing that the field a value was read off. `spellCards.Count` is
            // produced by a call and has nowhere to look until the list behind it is asked for.
            watch = WatchTarget.From(IlReading.Holding(leftAt, method))
                    ?? WatchTarget.ReadOff(leftAt, boundary, method);

            // The side that could not be named, so a count of unread conditions can say what shape
            // defeated it. Left first: it is the one a walk gives up on, the right being whatever
            // sits immediately before the branch.
            unreadAt = left == null ? leftAt : (right == null ? rightAt : null);

            // Both sides must agree, or the sentence is about two objects at once and there is no
            // one thing to rewrite it against. A side rooted in nothing but constants agrees with
            // anything, which is the ordinary shape: a field of `this` compared with a number.
            var leftWhere = IlReading.Where(leftAt, boundary, hasThis, method, out var leftStop);
            var rightWhere = IlReading.Where(rightAt, boundary, hasThis, method, out var rightStop);

            context = IlReading.Agreeing(leftWhere, rightWhere);
            lost = context != null
                ? null
                : leftWhere == null && rightWhere == null
                    ? "both:" + Shape(leftStop) + "/" + Shape(rightStop)
                    : leftWhere == null
                        ? "left:" + Shape(leftStop)
                        : rightWhere == null
                            ? "right:" + Shape(rightStop)
                            : "disagree:" + leftWhere + "/" + rightWhere;
        }

        /// <summary>
        /// The instruction that actually produced the value an instruction consumes.
        /// </summary>
        /// <remarks>
        /// A release build leaves the value on the stack, so the producer is simply the instruction
        /// before. A debug build does not: it computes into a local and reads it straight back, and
        /// it pads with <c>nop</c>. Both shapes come out of the same compiler on the same source —
        /// the editor compiles optimised and a development player build compiles for debugging — so
        /// a reader that only knows one of them reports whichever build it was not looking at as
        /// having no readable conditions at all.
        ///
        /// The store is only followed when it sits immediately before the load. A local assigned
        /// somewhere further back may have been assigned somewhere else too, and following it then
        /// would name a value that is not necessarily the one being tested. That is the kind of
        /// wrong answer this analysis would rather not give, so it stops and reports the condition
        /// as unread instead.
        /// </remarks>
        private static Instruction Producer(Instruction consumer, BasicBlock within)
        {
            var value = Preceding(consumer, within);

            if (!IsLoadLocal(value, out var slot))
            {
                return value;
            }

            var store = Preceding(value, within);

            return IsStoreLocal(store, out var stored) && stored == slot
                ? Preceding(store, within)
                : value;
        }

        /// <summary>The instruction before, bounded by the block being read.</summary>
        private static Instruction Preceding(Instruction instruction, BasicBlock within)
        {
            return IlReading.Preceding(instruction, within?.First);
        }

        private static bool IsLoadLocal(Instruction instruction, out int slot)
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
                    slot = (instruction.Operand as VariableReference)?.Index ?? -1;
                    return slot >= 0;
                default: return false;
            }
        }

        private static bool IsStoreLocal(Instruction instruction, out int slot)
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
                    slot = (instruction.Operand as VariableReference)?.Index ?? -1;
                    return slot >= 0;
                default: return false;
            }
        }

        /// <summary>
        /// The comparison a debug build leaves as a value rather than as a branch.
        /// </summary>
        /// <remarks>
        /// <c>if (a == b)</c> becomes <c>beq</c> when optimised and <c>ceq</c> followed by a
        /// branch on the result when not. <c>&gt;=</c> and <c>&lt;=</c> have no instruction of
        /// their own and arrive as the negation of their opposite — <c>clt</c> then
        /// <c>ldc.i4.0 ceq</c> — so the negation is unwrapped here rather than reported as two
        /// separate comparisons of something against zero.
        /// </remarks>
        private static string ComparisonOperator(
            Instruction instruction, bool holds, BasicBlock within, out Instruction operands)
        {
            operands = instruction;

            if (instruction == null)
            {
                return null;
            }

            switch (instruction.OpCode.Code)
            {
                case Code.Ceq:
                    // A comparison against a literal zero is how the compiler writes "not". When
                    // what it negates is itself a comparison, the two collapse into one operator.
                    var zero = Preceding(instruction, within);

                    if (IlReading.TryConstant(zero, out var value) && value == 0)
                    {
                        var negated = Preceding(zero, within);
                        var inner = ComparisonOperator(negated, !holds, within, out operands);

                        if (inner != null)
                        {
                            return inner;
                        }

                        operands = instruction;
                    }

                    return holds ? "==" : "!=";

                case Code.Clt:
                case Code.Clt_Un:
                    return holds ? "<" : ">=";

                case Code.Cgt:
                case Code.Cgt_Un:
                    return holds ? ">" : "<=";

                case Code.Call:
                case Code.Callvirt:
                    return OperatorMethod(instruction.Operand as MethodReference, holds);

                default:
                    return null;
            }
        }

        /// <summary>
        /// A comparison written as a method, which is the only kind some types have.
        /// </summary>
        /// <remarks>
        /// A string comparison, a Unity object tested against null, a struct with an <c>==</c> of
        /// its own — none of these produce a <c>ceq</c>. They compile to a call, and reading the
        /// call as an opaque value turned <c>name == "GameClearScene"</c> into
        /// <c>String.op_Equality() != 0</c>, which names neither side of the thing being decided.
        ///
        /// Only the six comparisons are taken. An operator that is not a comparison leaves a value,
        /// not a decision, and belongs on one side of one rather than in place of it.
        /// </remarks>
        private static string OperatorMethod(MethodReference method, bool holds)
        {
            if (method == null || method.Parameters.Count != 2 || method.HasThis)
            {
                return null;
            }

            switch (method.Name)
            {
                case "op_Equality": return holds ? "==" : "!=";
                case "op_Inequality": return holds ? "!=" : "==";
                case "op_LessThan": return holds ? "<" : ">=";
                case "op_GreaterThan": return holds ? ">" : "<=";
                case "op_LessThanOrEqual": return holds ? "<=" : ">";
                case "op_GreaterThanOrEqual": return holds ? ">=" : "<";
                default: return null;
            }
        }

        private static string Operator(Code code, bool branched)
        {
            switch (code)
            {
                case Code.Beq:
                case Code.Beq_S:
                    return branched ? "==" : "!=";

                case Code.Bne_Un:
                case Code.Bne_Un_S:
                case Code.Brtrue:
                case Code.Brtrue_S:
                    return branched ? "!=" : "==";

                case Code.Brfalse:
                case Code.Brfalse_S:
                    return branched ? "==" : "!=";

                case Code.Bgt:
                case Code.Bgt_S:
                case Code.Bgt_Un:
                case Code.Bgt_Un_S:
                    return branched ? ">" : "<=";

                case Code.Bge:
                case Code.Bge_S:
                case Code.Bge_Un:
                case Code.Bge_Un_S:
                    return branched ? ">=" : "<";

                case Code.Blt:
                case Code.Blt_S:
                case Code.Blt_Un:
                case Code.Blt_Un_S:
                    return branched ? "<" : ">=";

                case Code.Ble:
                case Code.Ble_S:
                case Code.Ble_Un:
                case Code.Ble_Un_S:
                    return branched ? "<=" : ">";

                default:
                    return null;
            }
        }

    }
}
