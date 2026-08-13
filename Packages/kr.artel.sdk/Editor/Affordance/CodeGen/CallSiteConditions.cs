using System.Collections.Generic;
using Mono.Cecil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// What had to be true for one method to call another.
    /// </summary>
    /// <remarks>
    /// The key a player presses and the change it causes are rarely in the same method: a key test
    /// guards a call, and the scene load sits inside the method called. Read one method at a time,
    /// the input and the outcome are two records that never mention each other.
    ///
    /// This is the half that joins them. Composing along a call path needs the condition at each
    /// step, and that condition belongs to the caller — it is written in the caller's own terms and
    /// stays true when carried forward. Nothing here touches the callee's conditions, which are
    /// written against the callee's receiver and would say something different if moved.
    ///
    /// Worked out once per method and kept. A method high in the call graph is on the way to most of
    /// the others.
    /// </remarks>
    internal sealed class CallSiteConditions
    {
        private readonly ModuleDefinition _module;

        private readonly Dictionary<MethodDefinition, Dictionary<MethodDefinition, Site>> _byCaller =
            new Dictionary<MethodDefinition, Dictionary<MethodDefinition, Site>>();

        private static readonly Dictionary<MethodDefinition, Site> None =
            new Dictionary<MethodDefinition, Site>();

        /// <summary>
        /// One call, and whether it was made on the caller's own object.
        /// </summary>
        /// <remarks>
        /// The condition is the half that composes. The other half is who the call was about: a
        /// helper called on <c>this</c> is talking about the same object as its caller, and its
        /// conditions can be read beside the caller's. Called on anything else, they cannot.
        ///
        /// False as soon as one call site is not on <c>this</c>, because the path stands for all of
        /// them. A method called both ways is a method whose conditions could mean either thing.
        /// </remarks>
        private sealed class Site
        {
            internal Condition When = Condition.Always;
            internal bool OnThis;

            /// <summary>
            /// What the caller called it on, in the caller's own words.
            /// </summary>
            /// <remarks>
            /// Null when the call was made on more than one thing, or on something the caller
            /// could not name. Two different receivers are two different objects and there is no
            /// one expression that is both.
            /// </remarks>
            internal string Receiver;

            /// <summary>Whose the receiver is — `this` or `static`.</summary>
            internal string Where;

            /// <summary>What was passed, in the caller's words, and whose each of them is.</summary>
            internal string[] Args;

            internal string[] ArgWhere;

            internal bool ReceiverKnown;
        }

        internal CallSiteConditions(ModuleDefinition module)
        {
            _module = module;
        }

        /// <summary>Methods whose call sites could not be placed, so their calls read as unguarded.</summary>
        internal int Unplaced { get; private set; }

        /// <summary>
        /// The condition under which <paramref name="caller"/> reaches <paramref name="callee"/>.
        /// </summary>
        /// <remarks>
        /// Always when the call is not guarded by anything, which is both the common case and the
        /// safe answer: a condition that cannot be placed makes the composed path read as reachable
        /// unconditionally, and the caller marks that rather than inventing a guard.
        /// </remarks>
        internal Condition Between(MethodDefinition caller, MethodDefinition callee)
        {
            return SitesIn(caller).TryGetValue(callee, out var site) ? site.When : Condition.Always;
        }

        /// <summary>
        /// Whether every step of a path was a call the caller made on its own object.
        /// </summary>
        /// <remarks>
        /// When it was, <c>this</c> is the same object from one end of the path to the other, and a
        /// condition written against <c>this</c> at the far end says the same thing at the near end.
        /// That is the one case where two conditions can be run into one sentence without the
        /// sentence changing meaning.
        /// </remarks>
        internal bool StaysOnThis(IReadOnlyList<MethodDefinition> path)
        {
            if (path == null || path.Count < 2)
            {
                return true;
            }

            for (var index = 0; index + 1 < path.Count; index++)
            {
                if (!SitesIn(path[index]).TryGetValue(path[index + 1], out var site) || !site.OnThis)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Composes the conditions along a path, in order.</summary>
        internal Condition Along(IReadOnlyList<MethodDefinition> path)
        {
            if (path == null || path.Count < 2)
            {
                return Condition.Always;
            }

            var steps = new List<Condition>(path.Count - 1);

            for (var index = 0; index + 1 < path.Count; index++)
            {
                steps.Add(Between(path[index], path[index + 1]));
            }

            return Condition.Every(steps);
        }

        /// <summary>Records one call site, joining it to any other site for the same callee.</summary>
        private static void Note(
            Dictionary<MethodDefinition, Site> sites, MethodDefinition callee, Condition guard,
            bool onThis, string receiver, string where, string[] args, string[] argWhere)
        {
            if (sites.TryGetValue(callee, out var already))
            {
                already.When = Condition.Either(new[] { already.When, guard });
                already.OnThis &= onThis;

                // Called on two things is called on neither in particular.
                if (already.Receiver != receiver)
                {
                    already.Receiver = null;
                    already.Where = null;
                }

                // Called with two different things is called with neither in particular.
                if (!Same(already.Args, args))
                {
                    already.Args = null;
                    already.ArgWhere = null;
                }

                return;
            }

            sites[callee] = new Site
            {
                When = guard, OnThis = onThis, Receiver = receiver, Where = where,
                Args = args, ArgWhere = argWhere, ReceiverKnown = true
            };
        }

        /// <summary>
        /// What one method called another on, when there is one answer and it is a thing of the
        /// caller's own.
        /// </summary>
        /// <remarks>
        /// The expression is in the caller's words, so it is what the callee's <c>this</c> is called
        /// where the call was written. Only offered when its subject is the caller's <c>this</c> —
        /// a receiver held in a local or handed in as an argument is a thing the caller cannot name
        /// for anyone else either.
        /// </remarks>
        internal string ReceivedOn(MethodDefinition caller, MethodDefinition callee)
        {
            return SitesIn(caller).TryGetValue(callee, out var site) && site.ReceiverKnown
                ? site.Receiver
                : null;
        }

        /// <summary>
        /// What the far end of a path is running on, said where the near end stands.
        /// </summary>
        /// <remarks>
        /// One hop is the receiver the caller wrote. Two is the second receiver written in the
        /// first callee's words, which mean nothing at the entry — so each is carried back a step
        /// at a time, swapping the head of the expression for what the step before called that
        /// object. `A` calls `B` on `A.zone` and `B` calls `C` on `B.slot`, so `C` is running on
        /// `A.zone.slot`.
        ///
        /// Null the moment a step cannot be carried: a call on a local, on an argument, on
        /// something named two different ways. The whole chain has to hold, because an expression
        /// that is right for the last three steps and wrong for the first names the wrong object
        /// with complete confidence.
        ///
        /// Null also when nothing moved — every step on the caller's own object leaves `this`
        /// meaning what it meant, and there is nothing to rewrite.
        /// </remarks>
        internal string ReceivedAlong(IReadOnlyList<MethodDefinition> path, out string where)
        {
            where = null;

            if (path == null || path.Count < 2)
            {
                return null;
            }

            string expression = null;

            for (var index = 0; index + 1 < path.Count; index++)
            {
                var caller = path[index];

                if (!SitesIn(caller).TryGetValue(path[index + 1], out var site))
                {
                    return null;
                }

                if (site.OnThis)
                {
                    // The callee is running on the same object the caller was, so whatever that
                    // was called at the entry is still what it is called.
                    continue;
                }

                if (site.Receiver == null)
                {
                    return null;
                }

                // A static root names its object from anywhere, so it replaces whatever was
                // carried rather than hanging off it.
                if (site.Where == "static" || expression == null)
                {
                    expression = site.Receiver;
                    where = site.Where;
                    continue;
                }

                expression = Condition.Swapped(site.Receiver, caller.DeclaringType?.Name, expression);

                if (expression == null)
                {
                    where = null;
                    return null;
                }
            }

            return expression;
        }

        private static bool Same(string[] left, string[] right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }

            if (left.Length != right.Length)
            {
                return false;
            }

            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>What one method passed another, when there is one answer.</summary>
        internal void PassedOn(
            MethodDefinition caller, MethodDefinition callee, out string[] args, out string[] argWhere)
        {
            args = null;
            argWhere = null;

            if (SitesIn(caller).TryGetValue(callee, out var site))
            {
                args = site.Args;
                argWhere = site.ArgWhere;
            }
        }

        /// <summary>Whether this call was made on the caller's own object.</summary>
        private static bool OnThis(
            Mono.Cecil.Cil.Instruction call, MethodDefinition caller, Mono.Cecil.Cil.Instruction boundary)
        {
            var reference = call.Operand as MethodReference;

            if (reference == null || !reference.HasThis)
            {
                // A static callee has no object of its own, so nothing of the caller's can be
                // mistaken for it.
                return true;
            }

            // The receiver has to *be* `this`, not belong to it. Asking whose the receiver was
            // answers "this" for `this.zone.AddCard()` as readily as for `this.AddCard()` — a field
            // of `this` is about `this` — and on that answer the callee's conditions were run into
            // the caller's sentence as one object's account. They are two objects: the sample game
            // drops a card by calling `AddCard` on `this.combineZone`, and the composed record said
            // `CombineZone.spellCards.Count == 1` with `context: this`, where `this` is the card.
            //
            // Naming a receiver only became possible recently, and saying whose it was is what
            // there was before that. Now that the expression can be read, it is read.
            //
            // Without a block to bound the walk there is no honest answer, and the honest answer
            // when there is no answer is no.
            return boundary != null &&
                   IlReading.Receiver(reference, call, boundary, caller) == "this";
        }

        /// <summary>The receiver's expression, when it is one of the caller's own things.</summary>
        private static string ReceiverAt(
            Mono.Cecil.Cil.Instruction call, MethodDefinition caller, Mono.Cecil.Cil.Instruction boundary,
            out string where)
        {
            where = null;
            var reference = call.Operand as MethodReference;

            if (reference == null || !reference.HasThis || boundary == null)
            {
                return null;
            }

            // The caller's own, or something that stands on its own. A singleton reached through a
            // static — `CardManager.Inst` — names the same object from anywhere, which is more than
            // a thing of the caller's manages; a receiver held in a local or handed in as an
            // argument names nothing outside the method it was written in.
            var standing = IlReading.ReceiverWhere(reference, call, boundary, caller.HasThis);

            if (standing != "this" && standing != "static")
            {
                return null;
            }

            where = standing;
            return IlReading.Receiver(reference, call, boundary, caller);
        }

        /// <summary>What each argument was, in the caller's words, and whose each of them is.</summary>
        private static string[] PassedAt(
            Mono.Cecil.Cil.Instruction call, MethodDefinition caller,
            Mono.Cecil.Cil.Instruction boundary, out string[] whose)
        {
            whose = null;
            var reference = call.Operand as MethodReference;
            var count = reference?.Parameters.Count ?? 0;

            if (count == 0 || boundary == null)
            {
                return null;
            }

            var terms = new string[count];
            whose = new string[count];
            var read = false;

            for (var index = 0; index < count; index++)
            {
                var at = IlReading.ArgumentFrom(reference, call, boundary, index);

                if (at == null)
                {
                    continue;
                }

                terms[index] = IlReading.Describe(at, boundary, caller);
                whose[index] = IlReading.Where(at, boundary, caller.HasThis, caller, out _);
                read |= terms[index] != null;
            }

            return read ? terms : null;
        }

        private Dictionary<MethodDefinition, Site> SitesIn(MethodDefinition caller)
        {
            if (_byCaller.TryGetValue(caller, out var known))
            {
                return known;
            }

            var sites = Build(caller);
            _byCaller[caller] = sites;
            return sites;
        }

        private Dictionary<MethodDefinition, Site> Build(MethodDefinition caller)
        {
            if (!caller.HasBody || AnalysisScope.IsTooLarge(caller))
            {
                return None;
            }

            var sites = new Dictionary<MethodDefinition, Site>();

            // No decision in the body means every call in it runs whenever the method does. Worth
            // its own path because most methods are this shape and building a graph for them is the
            // work the scope filter exists to avoid.
            if (!AnalysisScope.NeedsControlFlow(caller))
            {
                foreach (var instruction in caller.Body.Instructions)
                {
                    var callee = CallGraph.CalleeAt(instruction, _module);

                    if (callee != null)
                    {
                        Note(sites, callee, Condition.Always, OnThis(instruction, caller, null),
                            ReceiverAt(instruction, caller, null, out var standing), standing,
                            null, null);
                    }
                }

                return sites;
            }

            var graph = ControlFlowGraph.Build(caller.Body);

            if (graph == null || graph.Abandoned)
            {
                Unplaced++;
                return None;
            }

            var dependence = ControlDependence.Compute(graph);
            var reached = new Condition[graph.Blocks.Count];
            var state = new byte[graph.Blocks.Count];

            foreach (var block in graph.Blocks)
            {
                if (block.IsExit)
                {
                    continue;
                }

                Condition guard = null;

                for (var instruction = block.First; instruction != null; instruction = instruction.Next)
                {
                    var callee = CallGraph.CalleeAt(instruction, _module);

                    if (callee != null)
                    {
                        // Worked out only once a block turns out to contain a call at all.
                        guard = guard ?? VariantBuilder.ReachOf(graph, dependence, block.Index, reached, state);

                        // Called from two places under different conditions means either of them
                        // will do, which is what an alternative is.
                        Note(sites, callee, guard, OnThis(instruction, caller, block.First),
                            ReceiverAt(instruction, caller, block.First, out var standing), standing,
                            PassedAt(instruction, caller, block.First, out var whose), whose);
                    }

                    if (instruction == block.Last)
                    {
                        break;
                    }
                }
            }

            return sites;
        }
    }
}
