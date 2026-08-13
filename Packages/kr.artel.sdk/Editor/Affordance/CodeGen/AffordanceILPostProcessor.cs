using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// Reads the game's compiled code and records what its behaviours listen for and change.
    /// </summary>
    /// <remarks>
    /// Runs inside Unity's compilation pipeline, which means it needs nothing from the game team
    /// beyond having the package installed — no attributes, no edits, no build step of their own.
    /// It also runs before IL2CPP converts anything, so the result is present whatever the final
    /// build format is.
    ///
    /// Nothing here runs unless <see cref="EnableDefine"/> is set. Three separate times this
    /// analysis wedged an editor that then could not be opened to investigate why, and the way out
    /// was killing Unity and editing the manifest by hand. A switch that turns the whole thing off
    /// without uninstalling it is worth more than anything it can find.
    /// </remarks>
    public sealed class AffordanceILPostProcessor : ILPostProcessor
    {
        /// <summary>Scripting define symbol that opts a project into analysis.</summary>
        /// <remarks>
        /// Guarded twice on purpose. The assembly definition constrains itself to this symbol, so
        /// without it this code is never compiled and costs a project nothing — that is what keeps
        /// the package safe to leave installed through a shipping build. The check below is what
        /// guarantees no game assembly is touched even if the assembly does get built some other
        /// way, and it is the only one that can report itself.
        /// </remarks>
        internal const string EnableDefine = "ARTEL_AFFORDANCE";

        /// <summary>
        /// How long one assembly may be analysed before the rest is left undone.
        /// </summary>
        /// <remarks>
        /// Every loop in here is bounded, so this is not what makes the analysis finish. It is what
        /// makes it finish *soon*: a shape of assembly nobody anticipated can be slow while being
        /// perfectly finite, and a compile that sits for minutes is a broken build to the person
        /// waiting on it. Whatever was reached by then is reported, along with the fact that it
        /// stopped early.
        /// </remarks>
        private const long BudgetMilliseconds = 10000;

        /// <summary>
        /// Assemblies belonging to the engine, the toolchain, or this vendor.
        /// </summary>
        /// <remarks>
        /// Named by what to skip rather than what to take. Requiring a reference to this package
        /// would have been tidier, but auto-referencing only reaches Unity's predefined
        /// assemblies — a game that splits its code into assembly definitions would be passed over
        /// entirely, and those are the projects most likely to want this.
        ///
        /// <c>Artel</c> rather than <c>Artel.Affordances</c>: everything under it is this vendor's,
        /// including the sibling SDK a project may have installed alongside. Measured on the sample
        /// project, two of that SDK's own components accounted for two megabytes of a two-and-a-half
        /// megabyte report — evidence about tooling, which is never what anybody wants a
        /// specification of.
        ///
        /// The match is on a name boundary, so <c>Artel</c> covers <c>Artel.Tracking</c> and leaves
        /// something merely beginning with those letters alone. The cost of the wider prefix is that
        /// a game whose own assembly is named this way would be passed over **silently** — a refusal
        /// decided in <see cref="WillProcess"/> has nowhere to report itself, which is the same
        /// reason the build-kind question is asked later than this one.
        /// </remarks>
        private static readonly string[] SkippedPrefixes =
        {
            "Artel", "Unity.Artel",
            "UnityEngine", "UnityEditor", "Unity", "System", "mscorlib", "netstandard",
            "nunit", "Newtonsoft", "Mono"
        };

        public override ILPostProcessor GetInstance() => this;

        /// <remarks>
        /// What kind of build this is gets asked in <see cref="Process"/> rather than here, even
        /// though it is a reason not to touch the assembly and this is where those live. Answering
        /// no here means <see cref="Process"/> is never called, and <see cref="Process"/> is what
        /// holds the diagnostics — so a refusal decided here is a refusal nobody is told about.
        /// The two answered here are ones a person already knows the answer to: they set the
        /// define, and they know what they named their assemblies.
        /// </remarks>
        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            return Unlocked && IsEnabledFor(compiledAssembly) && !IsSkipped(compiledAssembly.Name);
        }

        /// <summary>Held shut while this analyser is carried into the SDK. ARTEL-393 deletes it.</summary>
        /// <remarks>
        /// Nine and a half thousand lines arrived here in one move, and whether they arrived intact
        /// is a different question from whether they should run — different evidence, different way
        /// of being wrong. So the move lands with the analyser unable to touch anything, and the
        /// issue that lets it run carries only the change that lets it run.
        ///
        /// A field rather than a constant so it reads as a switch someone flips, not a literal the
        /// compiler folds away. Left in front of the real conditions rather than replacing them:
        /// the reader can still see what would be asked, and unlocking is one term deleted.
        /// </remarks>
        private static readonly bool Unlocked = false;

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            var diagnostics = new List<DiagnosticMessage>();

            InMemoryAssembly baked = null;

            try
            {
                baked = IsDiscoveryBuild(compiledAssembly)
                    ? Survey(compiledAssembly, diagnostics)
                    : Declined(compiledAssembly, diagnostics);
            }
            catch (Exception exception)
            {
                // Never take the build down. A missing result costs accuracy in the generated
                // spec; a corrupted assembly costs the game team their build, and the whole
                // premise here is being safe to drop into someone else's project.
                Report(diagnostics, compiledAssembly.Name, "skipped, " + exception.Message);
                baked = null;
            }

            // Null means the compiler's own output is used exactly as it was. Every path that
            // fails, refuses, or finds nothing arrives here with null, so the original stands
            // unless the whole of it worked.
            return new ILPostProcessResult(baked, diagnostics);
        }

        /// <summary>
        /// Counts what is in scope and says so.
        /// </summary>
        /// <remarks>
        /// The counts are the deliverable of this stage. An earlier build of this package had a
        /// writer that failed to construct and returned null, and the scan that followed reported
        /// no coverage gaps — not because it had covered the game, but because it had nothing to
        /// compare against. Silence read as success. Whatever this stage does or does not manage to
        /// do, it says which.
        /// </remarks>
        private static InMemoryAssembly Survey(
            ICompiledAssembly compiledAssembly,
            List<DiagnosticMessage> diagnostics)
        {
            // A property's name means a different field in each assembly, so nothing learned about
            // one may be carried into the next.
            SimpleSetter.Forget();

            var carriedSymbols = HasSymbols(compiledAssembly);
            var readSymbols = carriedSymbols;

            using (var resolver = new CompiledAssemblyResolver(compiledAssembly))
            using (var assembly = ReadAssembly(compiledAssembly, resolver, ref readSymbols))
            {
                var flow = new FlowTally();
                var roots = new List<MethodDefinition>();
                var wirable = new HashSet<MethodDefinition>();
                var behaviours = 0;
                var unresolved = 0;
                var inspectorCallable = 0;
                var engineMessages = 0;
                var oversized = 0;
                var ignored = 0;

                // GetTypes reaches nested types, which is where the compiler puts the body of a
                // coroutine or a lambda — the parts of a behaviour that are easiest to miss.
                foreach (var type in assembly.MainModule.GetTypes())
                {
                    var verdict = AnalysisScope.Inspect(type);

                    if (verdict == TypeVerdict.Unresolved)
                    {
                        unresolved++;
                        continue;
                    }

                    if (verdict != TypeVerdict.Behaviour)
                    {
                        continue;
                    }

                    behaviours++;

                    foreach (var method in type.Methods)
                    {
                        var scope = AnalysisScope.Classify(method);

                        switch (scope)
                        {
                            case MethodScope.InspectorCallable:
                                inspectorCallable++;
                                break;
                            case MethodScope.EngineMessage:
                                engineMessages++;
                                break;
                            default:
                                ignored++;
                                continue;
                        }

                        if (AnalysisScope.IsTooLarge(method))
                        {
                            oversized++;
                            continue;
                        }

                        roots.Add(method);

                        if (scope == MethodScope.InspectorCallable)
                        {
                            wirable.Add(method);
                        }
                    }
                }

                // The entry points are where to start, not what to read. Update tends to hold three
                // calls and no decision, while the keys and the conditions guarding them sit in the
                // private helper it calls.
                var variants = new List<Variant>();
                var sites = new CallSiteConditions(assembly.MainModule);
                var reached = CallGraph.Close(roots, assembly.MainModule, out var truncated);
                var clock = Stopwatch.StartNew();

                foreach (var trace in reached)
                {
                    if (clock.ElapsedMilliseconds > BudgetMilliseconds)
                    {
                        flow.OutOfTime = true;
                        break;
                    }

                    flow.Reached++;

                    // Straight-line methods still carry direct effects and call edges. They are
                    // the ordinary shape of button handlers and helpers, so every reached method
                    // gets a bounded graph rather than only methods with a branch.
                    if (AnalysisScope.IsTooLarge(trace.Method))
                    {
                        continue;
                    }

                    var triggerKind = wirable.Contains(trace.Entry)
                        ? "unity-event"
                        : "lifecycle";

                    Graph(trace, ref flow, variants, triggerKind, sites);
                }

                flow.Milliseconds = clock.ElapsedMilliseconds;

                // Before the gaps below are hung on them, so that two cases which will end up
                // carrying the same gaps are still recognised as the same case.
                flow.Folded = DuplicateVariants.Fold(variants);

                flow.Variants = variants.Count;
                flow.Roots = roots.Count;
                flow.Truncated = truncated;

                foreach (var variant in variants)
                {
                    if (truncated)
                    {
                        variant.AddGap("call-graph-limit");
                    }

                    if (flow.OutOfTime)
                    {
                        variant.AddGap("assembly-time-limit");
                    }

                    if (unresolved > 0)
                    {
                        variant.AddGap("unresolved-types-in-assembly");
                    }

                    if (oversized > 0)
                    {
                        variant.AddGap("oversized-entry-methods-skipped");
                    }
                }

                // Said whether or not anything was found, and said first when nothing was. An
                // assembly reported as having no behaviours while its base types would not resolve
                // is not a small result, it is an unreliable one.
                var doubt = unresolved > 0
                    ? " " + unresolved + " types could not be traced to a base type — these are unaccounted for."
                    : string.Empty;

                if (behaviours == 0)
                {
                    Report(diagnostics, compiledAssembly.Name, "no MonoBehaviour, nothing to analyse." + doubt);
                    return null;
                }

                var message =
                    behaviours + " behaviours, " +
                    inspectorCallable + " inspector-callable and " + engineMessages +
                    " engine messages in scope, " + ignored + " methods ignored.";

                if (oversized > 0)
                {
                    message += " " + oversized + " over " + AnalysisScope.MaxInstructions +
                               " instructions, left alone.";
                }

                message += flow.Describe() + doubt;

                var written = AffordanceWriter.Write(
                    assembly.MainModule, compiledAssembly, resolver, variants);

                if (written.Refusal != null)
                {
                    Report(diagnostics, compiledAssembly.Name,
                        message + " Nothing was baked: " + written.Refusal + ".");
                    return null;
                }

                if (written.Written == 0)
                {
                    Report(diagnostics, compiledAssembly.Name,
                        message + " Nothing to bake, so the assembly is left as it was.");
                    return null;
                }

                message += " Baked " + written.Written + " onto types";
                message += written.Unattached > 0
                    ? "; " + written.Unattached + " belong to no component and were dropped."
                    : ".";
                message += " " + written.ResourceBytes + " bytes as a resource.";

                // Both numbers or neither. The list alone reads as the whole of what can be checked
                // while the game runs, and it is only that if the refusals are small.
                message += " " + written.Watched + " members to watch, " +
                           written.Unwatchable + " values with nowhere to read them.";

                if (written.Oversized > 0)
                {
                    message += " " + written.Oversized +
                               " evidence documents exceeded their serialization bound and were dropped.";
                }

                if (carriedSymbols && !readSymbols)
                {
                    // Handing back a rewritten assembly without its symbols would cost the game
                    // team stack traces and breakpoints in their own code. Not worth a result.
                    Report(diagnostics, compiledAssembly.Name,
                        message + " Left as it was: the debug symbols could not be read back.");
                    return null;
                }

                var result = Rewrite(assembly, readSymbols);

                if (result == null)
                {
                    Report(diagnostics, compiledAssembly.Name,
                        message + " Left as it was: writing the assembly produced nothing.");
                    return null;
                }

                Report(diagnostics, compiledAssembly.Name, message);
                return result;
            }
        }

        private static bool HasSymbols(ICompiledAssembly compiledAssembly)
        {
            var pdb = compiledAssembly.InMemoryAssembly.PdbData;
            return pdb != null && pdb.Length > 0;
        }

        /// <summary>
        /// Writes the changed assembly out.
        /// </summary>
        /// <remarks>
        /// Into fresh streams, and returned only once the write has finished. Cecil failing part
        /// way through leaves a half-written buffer that never leaves this method, and the caller
        /// hands back what the compiler produced instead.
        /// </remarks>
        private static InMemoryAssembly Rewrite(AssemblyDefinition assembly, bool symbols)
        {
            using (var pe = new MemoryStream())
            using (var pdb = new MemoryStream())
            {
                var parameters = new WriterParameters();

                if (symbols)
                {
                    parameters.WriteSymbols = true;
                    parameters.SymbolWriterProvider = new PortablePdbWriterProvider();
                    parameters.SymbolStream = pdb;
                }

                assembly.Write(pe, parameters);

                var image = pe.ToArray();

                if (image.Length == 0)
                {
                    return null;
                }

                return new InMemoryAssembly(image, symbols ? pdb.ToArray() : new byte[0]);
            }
        }

        private static AssemblyDefinition ReadAssembly(
            ICompiledAssembly compiledAssembly,
            IAssemblyResolver resolver,
            ref bool symbols)
        {
            if (symbols)
            {
                try
                {
                    return AssemblyDefinition.ReadAssembly(
                        new MemoryStream(compiledAssembly.InMemoryAssembly.PeData),
                        new ReaderParameters
                        {
                            AssemblyResolver = resolver,
                            ReadingMode = ReadingMode.Immediate,
                            InMemory = true,
                            ReadSymbols = true,
                            SymbolReaderProvider = new PortablePdbReaderProvider(),
                            SymbolStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData)
                        });
                }
                catch (Exception)
                {
                    // Read again without them. The analysis needs no symbols; what changes is that
                    // the result can no longer be written back, and the caller checks for that.
                    symbols = false;
                }
            }

            return AssemblyDefinition.ReadAssembly(
                new MemoryStream(compiledAssembly.InMemoryAssembly.PeData),
                new ReaderParameters
                {
                    AssemblyResolver = resolver,
                    ReadingMode = ReadingMode.Immediate,
                    InMemory = true
                });
        }

        /// <summary>What the control flow pass managed across one assembly.</summary>
        private struct FlowTally
        {
            internal int Roots;
            internal int Reached;
            internal int Methods;
            internal int Blocks;
            internal int Decisions;
            internal int Dependencies;
            internal int Variants;

            /// <summary>Variants with something on the way there that could not be read.</summary>
            internal int Incomplete;

            internal long Milliseconds;

            /// <summary>True when the time budget ran out with methods left.</summary>
            internal bool OutOfTime;

            /// <summary>True when following calls stopped before running out of calls.</summary>
            internal bool Truncated;

            /// <summary>Methods holding blocks with no path to the exit.</summary>
            internal int Stranded;

            /// <summary>Methods where a bound was reached, leaving the answer partial.</summary>
            internal int Limited;

            /// <summary>Methods with more blocks than the graph will hold.</summary>
            internal int Abandoned;

            /// <summary>Cases that turned out to be another way to one already found.</summary>
            internal int Folded;

            /// <summary>Cases whose method was handed over rather than called.</summary>
            internal int Handed;

            internal string Describe()
            {
                if (Methods == 0 && Abandoned == 0)
                {
                    return string.Empty;
                }

                var text = " Following calls from " + Roots + " entry points reached " + Reached +
                           " methods; graphed " + Methods + ": " + Blocks + " blocks, " +
                           Decisions + " decisions, " + Dependencies + " control dependencies.";

                if (Truncated)
                {
                    text += " The call walk hit its bound of " + CallGraph.MaxMethods +
                            " routes or " + CallGraph.MaxInstructionsScanned +
                            " instructions and did not finish.";
                }

                if (Folded > 0)
                {
                    text += " " + Folded + " were another way to a case already found.";
                }

                // Said apart from the unread count, which it would otherwise inflate. A case reached
                // through a delegate counts as incomplete because no condition crosses that edge —
                // that is a decision, not a reading that failed, and the two must not add up into
                // one number that looks like the analysis got worse.
                if (Handed > 0)
                {
                    text += " " + Handed + " were handed over rather than called.";
                }

                // Said separately rather than folded into the totals. A count that quietly leaves
                // out what it could not do reads as a complete answer.
                if (Stranded > 0)
                {
                    text += " " + Stranded + " hold blocks with no path to the exit.";
                }

                if (Limited > 0)
                {
                    text += " " + Limited + " hit a bound and are incomplete.";
                }

                if (Abandoned > 0)
                {
                    text += " " + Abandoned + " exceeded " + ControlFlowGraph.MaxBlocks + " blocks.";
                }

                text += " Built " + Variants + " evidence cases in " + Milliseconds + "ms";

                text += Incomplete > 0
                    ? "; " + Incomplete + " have conditions or paths that could not be read."
                    : ".";

                if (OutOfTime)
                {
                    text += " Stopped after " + BudgetMilliseconds +
                            "ms with methods left unread.";
                }

                return text;
            }
        }

        /// <summary>
        /// The callee's words said where the entry stands, or nothing when they cannot be.
        /// </summary>
        /// <remarks>
        /// Arguments only for a call the entry made itself. What was passed two hops down is
        /// written in the first callee's words, and carrying those back is a second translation on
        /// top of the one the receiver already needs.
        /// </remarks>
        private static Binding Bound(CallSiteConditions sites, CallGraph.Trace trace)
        {
            var receiver = sites.ReceivedAlong(trace.Path, out var standing);

            string[] args = null;
            string[] whose = null;

            if (trace.Path != null && trace.Path.Count == 2)
            {
                sites.PassedOn(trace.Path[0], trace.Path[1], out args, out whose);
            }

            var binding = Binding.Of(trace.Method, receiver, standing, args, whose);

            return binding.Anything ? binding : null;
        }

        private static void Graph(
            CallGraph.Trace trace,
            ref FlowTally tally,
            List<Variant> variants,
            string triggerKind,
            CallSiteConditions sites)
        {
            var method = trace.Method;
            var graph = ControlFlowGraph.Build(method.Body);

            if (graph == null)
            {
                return;
            }

            if (graph.Abandoned)
            {
                tally.Abandoned++;
                return;
            }

            tally.Methods++;
            tally.Blocks += graph.Blocks.Count;

            var dependence = ControlDependence.Compute(graph);
            tally.Decisions += dependence.DecisionCount;
            tally.Dependencies += dependence.DependenceCount;

            if (dependence.StrandedBlocks > 0)
            {
                tally.Stranded++;
            }

            if (dependence.HitLimit)
            {
                tally.Limited++;
            }

            var before = variants.Count;
            VariantBuilder.Collect(
                method,
                trace.Entry,
                trace.Path,
                trace.PathTruncated,
                graph,
                dependence,
                variants,
                triggerKind,
                // Not overridden for a delegate edge. Composing along the path already gives the
                // right answer: the edge that handed the method over has no call site, so nothing
                // is carried across it, while the conditions *inside* the handed-over body are real
                // and belong to it. Replacing the whole path with Always threw those away and left
                // a third of the assembly reading as unread.
                sites.Along(trace.Path),
                sites.StaysOnThis(trace.Path),

                // What the far end is running on, said where the entry stands. Null when every
                // step stayed on the same object, and null the moment one step cannot be carried.
                Bound(sites, trace));

            for (var index = before; index < variants.Count; index++)
            {
                if (trace.ThroughDelegate)
                {
                    // Nothing was carried down this path, and that is a decision rather than a
                    // failure to read: the site that made the delegate is not the site that runs it.
                    variants[index].AddGap("reached-through-delegate");
                    variants[index].HandedAt = trace.HandedAt;
                    variants[index].HandedIn = trace.HandedIn;
                    variants[index].HandedTo = trace.HandedTo;
                    tally.Handed++;
                }

                if (dependence.StrandedBlocks > 0)
                {
                    variants[index].AddGap("control-flow-does-not-reach-exit");
                }

                if (dependence.HitLimit)
                {
                    variants[index].AddGap("control-dependence-limit");
                }

                if (variants[index].Incomplete)
                {
                    tally.Incomplete++;
                }
            }
        }

        /// <summary>Says the same thing twice, because neither channel alone is enough.</summary>
        /// <remarks>
        /// The file is what reaches the console, by way of an editor script that reads it after the
        /// reload. The diagnostic reaches only the editor log, which is where to look when the file
        /// could not be written — and the file is the part that can fail, so the log keeps the
        /// record either way.
        /// </remarks>
        private static void Report(List<DiagnosticMessage> diagnostics, string assemblyName, string detail)
        {
            var message = assemblyName + ": " + detail;

            if (!ScopeReport.TryWrite(assemblyName, message))
            {
                message += " (could not write " + ScopeReport.ReportDirectory + ", editor log only)";
            }

            diagnostics.Add(new DiagnosticMessage
            {
                DiagnosticType = DiagnosticType.Warning,
                MessageData = "[Artel] " + message
            });
        }

        /// <summary>
        /// True when this compilation is one a person is developing against.
        /// </summary>
        /// <remarks>
        /// Discovery is a thing done while making the game, and a shipped game should carry no
        /// trace of it. Asking the build what kind of build it is answers that without anyone
        /// having to remember to turn something off before release — and remembering is exactly
        /// what fails, quietly, once, on the build that goes out.
        ///
        /// The two symbols are the editor's own and the player's development flag. The same pair
        /// is spelled again as an <c>#if</c> in <c>AffordanceBootstrap</c>, which decides the
        /// matching question on the runtime side. They cannot share a constant: that one is a
        /// preprocessor test, evaluated where its own assembly is compiled, and a preprocessor
        /// cannot read a value from anywhere. Change one and change the other.
        /// </remarks>
        private static bool IsDiscoveryBuild(ICompiledAssembly compiledAssembly)
        {
            var defines = compiledAssembly.Defines;

            if (defines == null)
            {
                return false;
            }

            foreach (var define in defines)
            {
                if (string.Equals(define, "UNITY_EDITOR", StringComparison.Ordinal) ||
                    string.Equals(define, "DEVELOPMENT_BUILD", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Leaves the assembly alone and says why, listing what it went on.
        /// </summary>
        /// <remarks>
        /// Deciding by the kind of build costs nothing to use and cannot be forgotten, but it is
        /// decided about the person rather than by them, so the one way it fails is silence: an
        /// analysis that does not run and does not say so reads exactly like an analysis that
        /// found nothing. Hence a message on the way out.
        ///
        /// The defines that could plausibly have carried the answer are listed with it. If the
        /// pair above ever turns out to be the wrong pair to look for, this line is what says so,
        /// and it says it on the first build rather than after a hunt.
        /// </remarks>
        private static InMemoryAssembly Declined(
            ICompiledAssembly compiledAssembly,
            List<DiagnosticMessage> diagnostics)
        {
            var defines = compiledAssembly.Defines ?? Array.Empty<string>();
            var related = new List<string>();

            foreach (var define in defines)
            {
                if (define != null &&
                    (define.IndexOf("BUILD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     define.IndexOf("DEBUG", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     define.IndexOf("DEVELOP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     define.IndexOf("EDITOR", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    related.Add(define);
                }
            }

            Report(diagnostics, compiledAssembly.Name,
                "not an editor or development build, so nothing was baked and the assembly is " +
                "the compiler's own. " + defines.Length + " defines, of which these could have " +
                "said otherwise: " +
                (related.Count > 0 ? string.Join(", ", related) : "none") + ".");

            return null;
        }

        /// <summary>True when the project asked for analysis by defining <see cref="EnableDefine"/>.</summary>
        private static bool IsEnabledFor(ICompiledAssembly compiledAssembly)
        {
            var defines = compiledAssembly.Defines;
            if (defines == null)
            {
                return false;
            }

            foreach (var define in defines)
            {
                if (string.Equals(define, EnableDefine, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when the name belongs to the engine, the toolchain, or this package.
        /// </summary>
        /// <remarks>
        /// Matched on whole dotted segments, not on characters. A plain prefix test reads
        /// <c>Systems.Gameplay</c> as <c>System</c> and drops a game's own code without a word
        /// about it, and splitting gameplay code into a <c>Systems</c> assembly is an ordinary
        /// thing to do.
        /// </remarks>
        private static bool IsSkipped(string assemblyName)
        {
            foreach (var prefix in SkippedPrefixes)
            {
                if (!assemblyName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (assemblyName.Length == prefix.Length || assemblyName[prefix.Length] == '.')
                {
                    return true;
                }
            }

            return false;
        }
    }
}
