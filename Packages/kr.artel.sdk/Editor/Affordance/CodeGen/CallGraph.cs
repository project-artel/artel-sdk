using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// Everything the entry points can reach inside this assembly.
    /// </summary>
    /// <remarks>
    /// An entry point is a root, not the subject. <c>Update</c> on a real behaviour tends to be
    /// three calls and no decision at all, while the keys it reads and the conditions guarding them
    /// sit in a private helper it calls — out of scope by every rule that picks entry points, and
    /// so never looked at. Control dependence computed only over the roots would be correct and
    /// would answer nothing.
    ///
    /// Only calls landing in the same module are followed. The game's own code is the subject; what
    /// happens inside the engine is not, and resolving those references is most of what the walk
    /// would spend its time on.
    /// </remarks>
    internal static class CallGraph
    {
        internal sealed class Trace
        {
            internal MethodDefinition Entry;
            internal MethodDefinition Method;
            internal List<MethodDefinition> Path;
            internal bool PathTruncated;

            /// <summary>
            /// True when somewhere on the way here the game handed a method over instead of calling it.
            /// </summary>
            /// <remarks>
            /// It matters because the condition under which a delegate was *made* is not the
            /// condition under which it *runs*. A handler attached in <c>OnEnable</c> was made
            /// unconditionally and runs whenever the event fires; a predicate handed to
            /// <c>WaitUntil</c> runs every frame until it answers yes. Neither is the site that
            /// created it, so no condition is carried across that edge — and the record says the
            /// edge was crossed so that nobody reads the silence as "nothing had to be true".
            /// </remarks>
            internal bool ThroughDelegate;

            /// <summary>
            /// Where in the method just before this one on the path it was handed over, or -1.
            /// </summary>
            /// <remarks>
            /// Set only on the hop itself, never carried past an ordinary call after it — past that
            /// point the number would name an offset in a method the path no longer ends beside,
            /// which is worse than not saying.
            ///
            /// A handed-over method has no call edge, so nothing said where among its siblings it
            /// belongs. Its effects sit in the method that handed it over, ordered by offset, and
            /// the predicate that waits between two of them could not be placed among them: a
            /// reader either guessed an order the report never established, or left the wait out.
            /// The offset is the whole of what is needed, and it was being read and dropped.
            /// </remarks>
            internal int HandedAt = -1;

            /// <summary>Which step of <see cref="Path"/> that offset is an offset into.</summary>
            internal int HandedIn = -1;

            /// <summary>What took the handed-over method, when that can be read.</summary>
            internal string HandedTo;
        }

        /// <summary>A method the game handed over, and where it did so.</summary>
        internal struct Handover
        {
            internal MethodDefinition Method;
            internal int Offset;

            /// <summary>What took it, when that can be read.</summary>
            internal string To;
        }

        /// <summary>
        /// Methods gathered before the walk gives up.
        /// </summary>
        /// <remarks>
        /// Reached in the shape of assembly where following calls was never going to end well —
        /// generated dispatch, deep mutual recursion. The count is reported when it trips so a
        /// truncated answer is not mistaken for a small one.
        /// </remarks>
        internal const int MaxMethods = 4000;
        internal const int MaxPathLength = 64;
        internal const int MaxInstructionsScanned = 200000;

        /// <summary>How many times over the walk goes back for what was only handed over.</summary>
        /// <remarks>A lambda inside a lambda is ordinary; a chain of them four deep is not.</remarks>
        internal const int MaxDelegateRounds = 4;

        internal static List<Trace> Close(
            List<MethodDefinition> roots,
            ModuleDefinition module,
            out bool truncated)
        {
            truncated = false;

            var reached = new List<Trace>();
            var seen = new HashSet<string>();
            var pending = new Stack<Trace>();
            var deferred = new List<Trace>();
            var instructionsScanned = 0;

            foreach (var root in roots)
            {
                pending.Push(new Trace
                {
                    Entry = root,
                    Method = root,
                    Path = new List<MethodDefinition> { root }
                });
            }

            // Two rounds over one worklist. The first follows calls only; the second picks up what
            // was merely handed over and follows calls from there in turn. Ordering them this way is
            // what makes a method that is reachable both ways get the better of the two accounts.
            var rounds = 0;

            // A worklist rather than recursion. Call chains in generated code go deeper than a
            // stack survives, and that failure arrives as a dead editor rather than an exception.
            while (pending.Count > 0 || (rounds++ < MaxDelegateRounds && Drain(deferred, pending)))
            {
                var trace = pending.Pop();
                var key = trace.Entry.MetadataToken.ToInt32() + ":" +
                          trace.Method.MetadataToken.ToInt32();

                if (!seen.Add(key))
                {
                    continue;
                }

                reached.Add(trace);

                if (reached.Count >= MaxMethods)
                {
                    truncated = true;
                    return reached;
                }

                var instructionCount = trace.Method.HasBody
                    ? trace.Method.Body.Instructions.Count
                    : 0;

                if (instructionsScanned > MaxInstructionsScanned - instructionCount)
                {
                    truncated = true;
                    return reached;
                }

                instructionsScanned += instructionCount;

                foreach (var handover in HandedOverBy(trace.Method, module))
                {
                    var handed = handover.Method;
                    var path = new List<MethodDefinition>(trace.Path);

                    if (path.Count < MaxPathLength)
                    {
                        path.Add(handed);
                    }

                    // Kept for later rather than pushed. A method may be both called and handed
                    // over, and the called path is the better account of it — it carries the
                    // conditions of the call sites, where the handed-over one carries nothing.
                    // Racing them on one stack let the worse answer win about a third of the time.
                    deferred.Add(new Trace
                    {
                        Entry = trace.Entry,
                        Method = handed,
                        Path = path,
                        PathTruncated = trace.PathTruncated || path.Count >= MaxPathLength,
                        ThroughDelegate = true,
                        HandedAt = handover.Offset,
                        HandedIn = path.Count - 2,
                        HandedTo = handover.To
                    });
                }

                foreach (var callee in CalleesOf(trace.Method, module))
                {
                    var path = new List<MethodDefinition>(trace.Path);
                    var pathTruncated = trace.PathTruncated;

                    if (path.Count < MaxPathLength)
                    {
                        path.Add(callee);
                    }
                    else
                    {
                        pathTruncated = true;
                    }

                    pending.Push(new Trace
                    {
                        Entry = trace.Entry,
                        Method = callee,
                        Path = path,
                        PathTruncated = pathTruncated,
                        ThroughDelegate = trace.ThroughDelegate,

                        // Carried now that it says which body it belongs to. The offset stops
                        // meaning the last step and starts meaning a step, so an ordinary call
                        // after the hand-over no longer makes it a lie.
                        HandedAt = trace.HandedAt,
                        HandedIn = trace.HandedIn,
                        HandedTo = trace.HandedTo
                    });
                }
            }

            return reached;
        }

        /// <summary>Moves everything set aside onto the worklist, and says whether there was any.</summary>
        private static bool Drain(List<Trace> deferred, Stack<Trace> pending)
        {
            foreach (var trace in deferred)
            {
                pending.Push(trace);
            }

            var any = deferred.Count > 0;
            deferred.Clear();
            return any;
        }

        internal static IEnumerable<MethodDefinition> CalleesOf(MethodDefinition method, ModuleDefinition module)
        {
            if (!method.HasBody)
            {
                yield break;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                var callee = CalleeAt(instruction, module);

                if (callee != null)
                {
                    yield return callee;
                }

                var resumed = MachineAt(instruction, module);

                if (resumed != null)
                {
                    yield return resumed;
                }
            }
        }

        /// <summary>
        /// Methods the game hands over for something else to call.
        /// </summary>
        /// <remarks>
        /// Taking a method's address is <c>ldftn</c>, and there is one reason to do it: somebody else
        /// is going to run it. A lambda passed to <c>WaitUntil</c>, a handler added to an event, a
        /// comparison given to <c>Sort</c> — all of them are code the game wrote and none of them is
        /// reachable by following calls, because the call is made from the engine or from a library.
        ///
        /// In the sample game this is where "press Space for the next line" lives: the input is
        /// inside <c>() =&gt; Input.GetKeyDown(Space)</c>, and following calls from the coroutine
        /// that created it never arrived.
        ///
        /// Deliberately not part of <see cref="CalleeAt"/>. That answers "what did this instruction
        /// call", and the answer here is nothing — the edge is reachability, not a call, and writing
        /// it as a call edge would claim a call that never happens at that offset.
        /// </remarks>
        internal static IEnumerable<Handover> HandedOverBy(MethodDefinition method, ModuleDefinition module)
        {
            if (!method.HasBody)
            {
                yield break;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code != Code.Ldftn && instruction.OpCode.Code != Code.Ldvirtftn)
                {
                    continue;
                }

                var handed = SafeResolve(instruction.Operand as MethodReference);

                if (handed != null && handed.HasBody && handed.Module == module)
                {
                    yield return new Handover
                    {
                        Method = handed,
                        Offset = instruction.Offset,
                        To = TakenBy(instruction)
                    };
                }
            }
        }

        /// <summary>
        /// What a handed-over method was given to.
        /// </summary>
        /// <remarks>
        /// Where it went decides what it means. A predicate handed to <c>WaitUntil</c> is a coroutine
        /// standing still until it comes true, so everything the coroutine does afterwards waits on
        /// it; the same predicate handed to a list of callbacks means nothing of the sort. The
        /// report said where the handover happened and not what took it, so a reader with an input
        /// nobody branched on had no way to tell those apart — and the sample game's whole story
        /// screen advances on a <c>WaitUntil(() =&gt; GetKeyDown(Space))</c>.
        ///
        /// Read forward from the <c>ldftn</c> over the delegate's own construction, which is the
        /// only thing between it and whatever wanted it. Named and not interpreted: this says the
        /// predicate went to <c>UnityEngine.WaitUntil</c>, and what waiting means is the reader's
        /// to know.
        /// </remarks>
        private static string TakenBy(Instruction handover)
        {
            var at = handover.Next;

            for (var step = 0; step < MaxHandoverLookahead && at != null; step++)
            {
                if (!(at.Operand is MethodReference taker))
                {
                    at = at.Next;
                    continue;
                }

                // The delegate's own constructor is the wrapping, not the destination.
                if (at.OpCode.Code == Code.Newobj && taker.DeclaringType != null &&
                    IsDelegate(taker.DeclaringType))
                {
                    at = at.Next;
                    continue;
                }

                if (at.OpCode.Code == Code.Newobj || at.OpCode.Code == Code.Call ||
                    at.OpCode.Code == Code.Callvirt)
                {
                    var owner = taker.DeclaringType?.FullName;

                    return owner == null
                        ? null
                        : owner + "::" + taker.Name;
                }

                at = at.Next;
            }

            return null;
        }

        /// <summary>How far past a handover the thing that took it is looked for.</summary>
        private const int MaxHandoverLookahead = 8;

        private static bool IsDelegate(TypeReference type)
        {
            var name = type.FullName;

            return name != null &&
                   (name.StartsWith("System.Func`", System.StringComparison.Ordinal) ||
                    name.StartsWith("System.Action", System.StringComparison.Ordinal) ||
                    name.StartsWith("System.Predicate`", System.StringComparison.Ordinal) ||
                    name.StartsWith("UnityEngine.Events.UnityAction", System.StringComparison.Ordinal));
        }

        /// <summary>
        /// The body of a coroutine, which nothing in the game ever calls.
        /// </summary>
        /// <remarks>
        /// A method with a <c>yield</c> in it is compiled into two things: a generator that builds a
        /// state machine and returns it, and the machine's <c>MoveNext</c> holding everything the
        /// method actually did. The game calls the generator. Unity calls <c>MoveNext</c>, from the
        /// engine, so following calls from the game's own code never arrives there and the whole
        /// body of every coroutine is missed — in the sample game the cast, the wave ending, the
        /// dialogue and the turn handover are all inside one.
        ///
        /// The edge is the construction. <c>newobj</c> of the compiler's own type is the moment the
        /// machine comes into existence, and it happens in the generator, in the game's own code,
        /// where it can be seen. Only types that have a <c>MoveNext</c> qualify, which is what
        /// separates an iterator from the display class of a lambda.
        /// </remarks>
        internal static MethodDefinition MachineAt(Instruction instruction, ModuleDefinition module)
        {
            if (instruction.OpCode.Code != Code.Newobj ||
                !(instruction.Operand is MethodReference constructor))
            {
                return null;
            }

            var type = constructor.DeclaringType;

            if (type == null || !type.Name.StartsWith("<", StringComparison.Ordinal))
            {
                return null;
            }

            var definition = SafeResolveType(type);

            if (definition == null || definition.Module != module)
            {
                return null;
            }

            foreach (var method in definition.Methods)
            {
                if (method.Name == "MoveNext" && method.HasBody && method.Parameters.Count == 0)
                {
                    return method;
                }
            }

            return null;
        }

        private static TypeDefinition SafeResolveType(TypeReference reference)
        {
            try
            {
                return reference.Resolve();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The method this instruction calls, when it is one of the game's own.</summary>
        internal static MethodDefinition CalleeAt(Instruction instruction, ModuleDefinition module)
        {
            if (instruction.OpCode.FlowControl != FlowControl.Call ||
                !(instruction.Operand is MethodReference reference))
            {
                return null;
            }

            // Checked before resolving. Nearly every call in a behaviour is into the engine, and
            // resolving each one to find that out is the expensive way to learn it.
            if (!IsSameModule(reference, module))
            {
                return null;
            }

            var definition = SafeResolve(reference);

            return definition != null && definition.HasBody && definition.Module == module
                ? definition
                : null;
        }

        private static bool IsSameModule(MethodReference reference, ModuleDefinition module)
        {
            var scope = reference.DeclaringType?.Scope;

            // A null scope means the reference names something in the module being read.
            return scope == null || ReferenceEquals(scope, module);
        }

        private static MethodDefinition SafeResolve(MethodReference reference)
        {
            try
            {
                return reference.Resolve();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
