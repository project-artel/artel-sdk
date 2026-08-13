using System.Collections.Generic;

namespace Artel.Affordances.CodeGen
{
    /// <summary>A decision, and which of its ways was taken to arrive.</summary>
    /// <remarks>
    /// Which way matters as much as which decision. The same comparison reads as
    /// <c>StagePosition &gt;= 1</c> down one edge and <c>&lt; 1</c> down the other, and a model that
    /// remembers only the decision has lost the half that says what had to be true.
    /// </remarks>
    internal readonly struct Governor
    {
        internal readonly int Decision;
        internal readonly int Taken;

        internal Governor(int decision, int taken)
        {
            Decision = decision;
            Taken = taken;
        }
    }

    /// <summary>
    /// Which decisions each block is subject to.
    /// </summary>
    /// <remarks>
    /// Two questions matter later and both are about paths: the code a key guards is the blocks
    /// control dependent on that key's branch, and the preconditions for arriving somewhere are the
    /// decisions that block is control dependent on. Asked this way, <c>A || B</c> needs no special
    /// handling — the short-circuit is just another edge.
    ///
    /// Post-dominance comes first because control dependence is defined in terms of it: B is
    /// control dependent on A when A can choose whether B runs, which is to say B does not
    /// post-dominate A but lies on a path out of it.
    /// </remarks>
    internal sealed class ControlDependence
    {
        /// <summary>
        /// Passes the fixed-point loop may take before the graph is declared unfit.
        /// </summary>
        /// <remarks>
        /// The loop converges in a handful of passes on anything well formed. A bound this loose
        /// only ever catches input that was never going to settle.
        /// </remarks>
        private const int MaxPasses = 200;

        private readonly ControlFlowGraph _graph;
        private readonly BasicBlock[] _immediatePostDominator;
        private readonly int[] _reverseOrder;
        private readonly bool[] _reachesExit;
        private readonly List<Governor>[] _dependsOn;

        /// <summary>Blocks left out because no path from them arrives at the exit.</summary>
        internal int StrandedBlocks { get; private set; }

        /// <summary>True when a bound was reached and the answer is incomplete.</summary>
        internal bool HitLimit { get; private set; }

        internal int DecisionCount { get; private set; }
        internal int DependenceCount { get; private set; }

        private ControlDependence(ControlFlowGraph graph)
        {
            _graph = graph;
            var count = graph.Blocks.Count;
            _immediatePostDominator = new BasicBlock[count];
            _reverseOrder = new int[count];
            _reachesExit = new bool[count];
            _dependsOn = new List<Governor>[count];
        }

        internal static ControlDependence Compute(ControlFlowGraph graph)
        {
            var dependence = new ControlDependence(graph);
            dependence.Run();
            return dependence;
        }

        /// <summary>The decisions this block is subject to.</summary>
        internal IReadOnlyList<Governor> Governing(int blockIndex)
        {
            return _dependsOn[blockIndex] ?? (IReadOnlyList<Governor>)System.Array.Empty<Governor>();
        }

        private void Run()
        {
            var order = OrderFromExit();
            ComputePostDominators(order);
            ComputeDependence();
        }

        /// <summary>
        /// Blocks that can reach the exit, nearest the exit first.
        /// </summary>
        /// <remarks>
        /// This is the step that keeps the whole thing finite, and leaving it out is what froze an
        /// editor that then could not be opened to find out why. Post-dominance is only defined for
        /// blocks with a path to the exit; a block without one — an endless loop, code the compiler
        /// left behind — has no immediate post-dominator, and comparing two such blocks walks a
        /// chain of parents that never meet because neither has any.
        ///
        /// Bounding the comparison would stop the spin. Refusing to start it is better: the answer
        /// for those blocks does not exist, so they are counted and set aside rather than guessed
        /// at.
        /// </remarks>
        private List<BasicBlock> OrderFromExit()
        {
            var postOrder = new List<BasicBlock>();
            var visited = new bool[_graph.Blocks.Count];
            var nodes = new Stack<BasicBlock>();
            var nextEdge = new Stack<int>();

            nodes.Push(_graph.Exit);
            nextEdge.Push(0);
            visited[_graph.Exit.Index] = true;

            // Walked with an explicit stack. A method deep enough to matter is deep enough to
            // overflow a recursive walk, and that failure arrives as a dead editor.
            while (nodes.Count > 0)
            {
                var node = nodes.Peek();
                var edge = nextEdge.Pop();

                if (edge < node.Predecessors.Count)
                {
                    nextEdge.Push(edge + 1);
                    var previous = node.Predecessors[edge];

                    if (!visited[previous.Index])
                    {
                        visited[previous.Index] = true;
                        nodes.Push(previous);
                        nextEdge.Push(0);
                    }

                    continue;
                }

                postOrder.Add(node);
                nodes.Pop();
            }

            foreach (var block in _graph.Blocks)
            {
                if (!visited[block.Index])
                {
                    StrandedBlocks++;
                }
            }

            foreach (var block in postOrder)
            {
                _reachesExit[block.Index] = true;
            }

            postOrder.Reverse();

            for (var position = 0; position < postOrder.Count; position++)
            {
                _reverseOrder[postOrder[position].Index] = position;
            }

            return postOrder;
        }

        private void ComputePostDominators(List<BasicBlock> order)
        {
            _immediatePostDominator[_graph.Exit.Index] = _graph.Exit;

            var changed = true;
            var passes = 0;

            while (changed)
            {
                if (passes++ >= MaxPasses)
                {
                    HitLimit = true;
                    return;
                }

                changed = false;

                foreach (var block in order)
                {
                    if (block.IsExit)
                    {
                        continue;
                    }

                    BasicBlock candidate = null;

                    foreach (var successor in block.Successors)
                    {
                        if (!_reachesExit[successor.Index] || _immediatePostDominator[successor.Index] == null)
                        {
                            continue;
                        }

                        candidate = candidate == null ? successor : Intersect(successor, candidate);

                        if (candidate == null)
                        {
                            break;
                        }
                    }

                    if (candidate != null && _immediatePostDominator[block.Index] != candidate)
                    {
                        _immediatePostDominator[block.Index] = candidate;
                        changed = true;
                    }
                }
            }
        }

        /// <summary>Climbs both nodes up the tree until they stand on the same one.</summary>
        private BasicBlock Intersect(BasicBlock left, BasicBlock right)
        {
            var steps = 0;
            var bound = _graph.Blocks.Count * 2;

            while (left != right)
            {
                while (_reverseOrder[left.Index] > _reverseOrder[right.Index])
                {
                    left = _immediatePostDominator[left.Index];

                    if (left == null || steps++ > bound)
                    {
                        HitLimit = true;
                        return null;
                    }
                }

                while (_reverseOrder[right.Index] > _reverseOrder[left.Index])
                {
                    right = _immediatePostDominator[right.Index];

                    if (right == null || steps++ > bound)
                    {
                        HitLimit = true;
                        return null;
                    }
                }

                if (steps++ > bound)
                {
                    HitLimit = true;
                    return null;
                }
            }

            return left;
        }

        /// <summary>
        /// Walks each decision's outgoing edges up to where the paths rejoin.
        /// </summary>
        /// <remarks>
        /// Everything between a branch and the point both of its ways meet again is code that
        /// branch decided to run. That meeting point is its immediate post-dominator, which is why
        /// it had to be computed first.
        /// </remarks>
        private void ComputeDependence()
        {
            var bound = _graph.Blocks.Count + 1;

            foreach (var decision in _graph.Blocks)
            {
                if (!decision.IsDecision || !_reachesExit[decision.Index])
                {
                    continue;
                }

                DecisionCount++;
                var rejoin = _immediatePostDominator[decision.Index];

                foreach (var successor in decision.Successors)
                {
                    if (!_reachesExit[successor.Index])
                    {
                        continue;
                    }

                    var runner = successor;
                    var steps = 0;

                    while (runner != null && runner != rejoin)
                    {
                        if (steps++ > bound)
                        {
                            HitLimit = true;
                            break;
                        }

                        Record(runner.Index, new Governor(decision.Index, successor.Index));
                        runner = _immediatePostDominator[runner.Index];
                    }
                }
            }
        }

        private void Record(int governed, Governor governor)
        {
            var governors = _dependsOn[governed];

            if (governors == null)
            {
                governors = new List<Governor>();
                _dependsOn[governed] = governors;
            }

            foreach (var existing in governors)
            {
                if (existing.Decision == governor.Decision && existing.Taken == governor.Taken)
                {
                    return;
                }
            }

            governors.Add(governor);
            DependenceCount++;
        }
    }
}
