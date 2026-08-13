using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>A run of instructions entered only at the top and left only at the bottom.</summary>
    internal sealed class BasicBlock
    {
        internal int Index;
        internal Instruction First;
        internal Instruction Last;
        internal bool IsExit;

        internal readonly List<BasicBlock> Successors = new List<BasicBlock>();
        internal readonly List<BasicBlock> Predecessors = new List<BasicBlock>();

        /// <summary>True when leaving this block is a decision rather than a step.</summary>
        internal bool IsDecision => Successors.Count > 1;
    }

    /// <summary>
    /// The shape of a method, as blocks and the ways between them.
    /// </summary>
    /// <remarks>
    /// Everything later asks path questions — which code a key guards, what had to be true to
    /// arrive somewhere — and a path question cannot be answered by reading instructions in the
    /// order they were written. An <c>if/else</c> chain holding an <c>||</c> puts the body guarded
    /// by one key physically between the tests for another.
    /// </remarks>
    internal sealed class ControlFlowGraph
    {
        /// <summary>
        /// Blocks past which a method is left alone.
        /// </summary>
        /// <remarks>
        /// Generated code arrives at sizes no one writes by hand. The size filter upstream already
        /// turns away the worst of it; this is the backstop for what gets through.
        /// </remarks>
        internal const int MaxBlocks = 2000;

        internal List<BasicBlock> Blocks { get; private set; }
        internal BasicBlock Entry { get; private set; }

        /// <summary>Whether this method has a receiver, so that ldarg.0 can be told apart.</summary>
        internal bool HasThis { get; private set; }

        /// <summary>The method this is the shape of, for naming its own arguments.</summary>
        internal MethodDefinition Method { get; private set; }

        /// <summary>
        /// The local a coroutine copies its resume point into, or -1.
        /// </summary>
        /// <remarks>
        /// The dispatch at the top of <c>MoveNext</c> reads the state field once and then branches
        /// on the copy, sometimes several times over. Only the first of those blocks mentions the
        /// field; the rest test a local that looks like any other. Knowing which local it is turns
        /// all of them back into what they are.
        /// </remarks>
        internal int StateSlot { get; private set; } = -1;

        /// <summary>
        /// Where every return and throw arrives.
        /// </summary>
        /// <remarks>
        /// Synthetic, and holding no instructions. Post-dominance is asked from a single end point,
        /// and a method with three <c>return</c>s has three without this.
        /// </remarks>
        internal BasicBlock Exit { get; private set; }

        /// <summary>True when the method was too large to graph and nothing here is usable.</summary>
        internal bool Abandoned { get; private set; }

        /// <summary>Which local the coroutine's state field was copied into, if any.</summary>
        private static int ResumeSlot(Mono.Collections.Generic.Collection<Instruction> instructions)
        {
            for (var index = 0; index + 1 < instructions.Count; index++)
            {
                var load = instructions[index];

                if (load.OpCode.Code != Code.Ldfld ||
                    !(load.Operand is FieldReference field) ||
                    field.Name != "<>1__state")
                {
                    continue;
                }

                switch (instructions[index + 1].OpCode.Code)
                {
                    case Code.Stloc_0: return 0;
                    case Code.Stloc_1: return 1;
                    case Code.Stloc_2: return 2;
                    case Code.Stloc_3: return 3;
                    case Code.Stloc:
                    case Code.Stloc_S:
                        return (instructions[index + 1].Operand as VariableReference)?.Index ?? -1;
                }
            }

            return -1;
        }

        internal static ControlFlowGraph Build(MethodBody body)
        {
            var instructions = body.Instructions;
            if (instructions.Count == 0)
            {
                return null;
            }

            var leaders = FindLeaders(body);
            var graph = new ControlFlowGraph
            {
                Blocks = new List<BasicBlock>(),
                HasThis = body.Method != null && body.Method.HasThis,
                StateSlot = ResumeSlot(instructions),
                Method = body.Method
            };

            var blockByFirst = new Dictionary<Instruction, BasicBlock>();
            BasicBlock current = null;

            foreach (var instruction in instructions)
            {
                if (current == null || leaders.Contains(instruction))
                {
                    if (graph.Blocks.Count >= MaxBlocks)
                    {
                        graph.Abandoned = true;
                        return graph;
                    }

                    current = new BasicBlock { Index = graph.Blocks.Count, First = instruction };
                    graph.Blocks.Add(current);
                    blockByFirst[instruction] = current;
                }

                current.Last = instruction;
            }

            graph.Entry = graph.Blocks[0];
            graph.Exit = new BasicBlock { Index = graph.Blocks.Count, IsExit = true };
            graph.Blocks.Add(graph.Exit);

            graph.Connect(blockByFirst);
            return graph;
        }

        /// <summary>Instructions that can only be entered at, and so must begin a block.</summary>
        private static HashSet<Instruction> FindLeaders(MethodBody body)
        {
            var leaders = new HashSet<Instruction>();
            var instructions = body.Instructions;
            leaders.Add(instructions[0]);

            foreach (var instruction in instructions)
            {
                switch (instruction.OpCode.FlowControl)
                {
                    case FlowControl.Branch:
                    case FlowControl.Cond_Branch:
                        AddTargets(leaders, instruction);
                        AddNext(leaders, instruction);
                        break;

                    case FlowControl.Return:
                    case FlowControl.Throw:
                        AddNext(leaders, instruction);
                        break;
                }
            }

            // Region boundaries begin blocks too. The ways into and out of a handler are not
            // modelled below, but letting a block straddle the edge of one would put instructions
            // that run together into a run that does not.
            foreach (var handler in body.ExceptionHandlers)
            {
                AddIfPresent(leaders, handler.TryStart);
                AddIfPresent(leaders, handler.TryEnd);
                AddIfPresent(leaders, handler.HandlerStart);
                AddIfPresent(leaders, handler.HandlerEnd);
                AddIfPresent(leaders, handler.FilterStart);
            }

            return leaders;
        }

        private static void AddTargets(HashSet<Instruction> leaders, Instruction instruction)
        {
            if (instruction.Operand is Instruction target)
            {
                leaders.Add(target);
                return;
            }

            if (instruction.Operand is Instruction[] targets)
            {
                foreach (var each in targets)
                {
                    AddIfPresent(leaders, each);
                }
            }
        }

        private static void AddNext(HashSet<Instruction> leaders, Instruction instruction)
        {
            AddIfPresent(leaders, instruction.Next);
        }

        private static void AddIfPresent(HashSet<Instruction> leaders, Instruction instruction)
        {
            if (instruction != null)
            {
                leaders.Add(instruction);
            }
        }

        private void Connect(Dictionary<Instruction, BasicBlock> blockByFirst)
        {
            foreach (var block in Blocks)
            {
                if (block.IsExit)
                {
                    continue;
                }

                var last = block.Last;

                switch (last.OpCode.FlowControl)
                {
                    case FlowControl.Branch:
                        LinkToTargets(block, last, blockByFirst);
                        break;

                    case FlowControl.Cond_Branch:
                        LinkToTargets(block, last, blockByFirst);
                        LinkToNext(block, last, blockByFirst);
                        break;

                    case FlowControl.Return:
                    case FlowControl.Throw:
                        Link(block, Exit);
                        break;

                    default:
                        LinkToNext(block, last, blockByFirst);
                        break;
                }
            }
        }

        private void LinkToTargets(BasicBlock from, Instruction last, Dictionary<Instruction, BasicBlock> blockByFirst)
        {
            if (last.Operand is Instruction target)
            {
                LinkTo(from, target, blockByFirst);
                return;
            }

            if (last.Operand is Instruction[] targets)
            {
                foreach (var each in targets)
                {
                    LinkTo(from, each, blockByFirst);
                }
            }
        }

        private void LinkToNext(BasicBlock from, Instruction last, Dictionary<Instruction, BasicBlock> blockByFirst)
        {
            if (last.Next == null)
            {
                // Running off the end of the body. Malformed, but it is someone else's assembly.
                Link(from, Exit);
                return;
            }

            LinkTo(from, last.Next, blockByFirst);
        }

        private void LinkTo(BasicBlock from, Instruction target, Dictionary<Instruction, BasicBlock> blockByFirst)
        {
            if (target != null && blockByFirst.TryGetValue(target, out var to))
            {
                Link(from, to);
            }
        }

        private static void Link(BasicBlock from, BasicBlock to)
        {
            // A switch can name the same target twice. Counting that as two ways between the same
            // pair would have every later count off by however many times it happens.
            if (from.Successors.Contains(to))
            {
                return;
            }

            from.Successors.Add(to);
            to.Predecessors.Add(from);
        }
    }
}
