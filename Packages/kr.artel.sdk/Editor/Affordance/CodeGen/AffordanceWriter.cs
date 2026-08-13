using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// Bakes what was found onto the game's own types.
    /// </summary>
    /// <remarks>
    /// The only change made to a game assembly, anywhere in this package: a custom attribute added
    /// to a type. No method body is touched, nothing is renamed, no code is inserted. An attribute
    /// cannot change what the game does, which is the property that makes this safe to put in
    /// somebody else's build — and the reason the analysis bakes metadata rather than
    /// instrumentation.
    ///
    /// Attaching to the type rather than keeping a table on the side is what survives the trip. The
    /// scan that reads this runs against a built player where names may be obfuscated and IL2CPP
    /// has rewritten everything; an attribute is still on whatever the type became.
    /// </remarks>
    internal static class AffordanceWriter
    {
        private const string RuntimeAssembly = "Artel.Affordances.Runtime";
        private const string AttributeType = "Artel.Affordances.AffordanceAttribute";

        private const int MaxPayloadCharacters = 32768;

        internal sealed class Result
        {
            internal int Written;
            internal int Unattached;
            internal int Oversized;
            internal string Refusal;
            internal int ResourceBytes;
            internal int Anchored;

            /// <summary>How many distinct members the evidence asks somebody to watch.</summary>
            internal int Watched;

            /// <summary>
            /// How many conditions and effects named a value with nowhere to read it.
            /// </summary>
            /// <remarks>
            /// Said beside the list because the two numbers are only meaningful together. A short
            /// list next to a large refusal count is a game whose logic runs through calls, and a
            /// watcher that saw only the list would think it had covered everything.
            /// </remarks>
            internal int Unwatchable;
        }

        /// <summary>
        /// Adds one attribute per variant to the type it belongs to.
        /// </summary>
        /// <remarks>
        /// Refuses rather than guesses when the assembly holding the attribute cannot be found on
        /// disk. Inventing an assembly identity would produce a game assembly referring to
        /// something that may not exist under that name, and the failure would arrive as a type
        /// load error in a built player rather than here. Finding the file and reading its identity
        /// is not guessing, which is why <see cref="FindRuntimeAssembly"/> is allowed to look past
        /// the compiler's reference list.
        /// </remarks>
        internal static Result Write(
            ModuleDefinition module,
            ICompiledAssembly compiledAssembly,
            IAssemblyResolver resolver,
            List<Variant> variants)
        {
            var result = new Result();

            var attributePath = FindRuntimeAssembly(compiledAssembly);

            if (attributePath == null)
            {
                result.Refusal = "the runtime assembly is not among this assembly's references";
                return result;
            }

            MethodReference constructor;

            using (var runtime = AssemblyDefinition.ReadAssembly(
                attributePath,
                new ReaderParameters(ReadingMode.Deferred) { AssemblyResolver = resolver }))
            {
                var attribute = runtime.MainModule.GetType(AttributeType);

                if (attribute == null)
                {
                    result.Refusal = "the runtime assembly does not define " + AttributeType;
                    return result;
                }

                MethodDefinition declared = null;

                foreach (var method in attribute.Methods)
                {
                    if (method.IsConstructor && method.Parameters.Count == 2 &&
                        method.Parameters[0].ParameterType.MetadataType == MetadataType.Int32 &&
                        method.Parameters[1].ParameterType.MetadataType == MetadataType.Int32)
                    {
                        declared = method;
                        break;
                    }
                }

                if (declared == null)
                {
                    result.Refusal = "the attribute has no constructor of the expected shape";
                    return result;
                }

                // Imported from a file read with Cecil, never loaded. The assembly reference this
                // adds to the module is the real identity of the assembly that will ship.
                constructor = module.ImportReference(declared);
            }

            var integers = module.TypeSystem.Int32;

            // Anything already baked on is cleared first. The pipeline hands over a freshly compiled
            // assembly, so this should never find one — but an assembly that has been through here
            // twice would carry two generations of evidence at once, and the older half would be
            // indistinguishable from the newer while quietly contradicting it.
            Clear(module, variants);
            EvidenceResource.Detach(module);

            // Grouped by type because that is how it is asked for. A scene holds many instances of
            // few types, and the scan looks the answer up once per type.
            var byOwner = new List<TypeDefinition>();
            var payloadsByOwner = new Dictionary<TypeDefinition, List<string>>();

            var callers = Callers(module, variants);

            foreach (var variant in variants)
            {
                if (variant.Owner == null || variant.Owner.Module != module)
                {
                    // Found in code the game reaches but does not hang on a GameObject. The scan
                    // looks types up from the components it finds, so there is nothing to look this
                    // up from.
                    result.Unattached++;
                    continue;
                }

                bool truncated;
                var payload = EvidenceJson.Write(variant, callers, out truncated);

                if (truncated)
                {
                    variant.AddGap("evidence-serialization-limit");
                    payload = EvidenceJson.Write(variant, out truncated);
                }

                if (payload.Length > MaxPayloadCharacters)
                {
                    result.Oversized++;
                    continue;
                }

                if (!payloadsByOwner.TryGetValue(variant.Owner, out var list))
                {
                    list = new List<string>();
                    payloadsByOwner[variant.Owner] = list;
                    byOwner.Add(variant.Owner);
                }

                list.Add(payload);
                result.Written++;
            }

            var blob = new StringBuilder(1024);

            for (var anchor = 0; anchor < byOwner.Count; anchor++)
            {
                var owner = byOwner[anchor];

                // The anchor survives renaming, the name survives stripping. Both are written so
                // that whichever of the two is left can still find this.
                var attribute = new CustomAttribute(constructor);
                attribute.ConstructorArguments.Add(
                    new CustomAttributeArgument(integers, EvidenceJson.SchemaVersion));
                attribute.ConstructorArguments.Add(new CustomAttributeArgument(integers, anchor));
                owner.CustomAttributes.Add(attribute);

                blob.Append(anchor).Append('\t').Append(owner.FullName).Append('\t')
                    .Append('[').Append(string.Join(",", payloadsByOwner[owner].ToArray())).Append(']')
                    .Append('\n');
            }

            result.Anchored = byOwner.Count;
            result.ResourceBytes = EvidenceResource.Attach(module, blob.ToString());

            // Written from every variant, including the ones that could not be anchored to a type.
            // What to watch is a question about the assembly, and a static field on a class no
            // GameObject carries is exactly the kind that decides a screen.
            var watch = WatchListJson.Write(variants);
            result.Watched = watch.Watched;
            result.Unwatchable = watch.Unwatchable;
            result.ResourceBytes += EvidenceResource.AttachWatch(module, watch.Document);

            return result;
        }

        /// <summary>Takes off any evidence from an earlier pass over this assembly.</summary>
        /// <summary>
        /// Who calls each method a record starts at, read from the whole assembly.
        /// </summary>
        /// <remarks>
        /// A record says what it calls. Nothing said what calls it, and the two are not the same
        /// list read backwards: a record's calls are the ones inside the blocks that survived, and
        /// a caller with no record of its own leaves no trace at all. Six of the sample game's
        /// features sit behind that — a card is dealt from a turn state that is not a behaviour and
        /// a drop zone is cleared from inside a coroutine, and neither caller is anywhere in the
        /// document. Read from the assembly instead of from the records, both are there.
        ///
        /// Only for methods a record starts at, because that is the question being answered: given
        /// a record nobody can see how to reach, what reaches it. Every other edge is already
        /// written where it is used.
        ///
        /// Names the caller and stops. Whether that caller is something a tester can do is the
        /// reader's question, and a method that is only ever called from <c>MoveNext</c> is an
        /// honest answer to it — better than the silence that had the same shape as "nothing calls
        /// this".
        /// </remarks>
        private static Dictionary<string, List<string>> Callers(
            ModuleDefinition module, List<Variant> variants)
        {
            var wanted = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var variant in variants)
            {
                if (variant.EntryId != null)
                {
                    wanted.Add(variant.EntryId);
                }
            }

            var found = new Dictionary<string, List<string>>(System.StringComparer.Ordinal);

            if (wanted.Count == 0)
            {
                return found;
            }

            foreach (var type in module.GetTypes())
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                    {
                        continue;
                    }

                    var from = MethodIdentity.Of(method);

                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode.Code != Code.Call &&
                            instruction.OpCode.Code != Code.Callvirt &&
                            instruction.OpCode.Code != Code.Newobj &&
                            instruction.OpCode.Code != Code.Ldftn)
                        {
                            continue;
                        }

                        var called = MethodIdentity.Of(instruction.Operand as MethodReference);

                        if (called == null || called == from || !wanted.Contains(called))
                        {
                            continue;
                        }

                        if (!found.TryGetValue(called, out var list))
                        {
                            list = new List<string>();
                            found[called] = list;
                        }

                        if (!list.Contains(from))
                        {
                            list.Add(from);
                        }
                    }
                }
            }

            return found;
        }

        private static void Clear(ModuleDefinition module, List<Variant> variants)
        {
            var seen = new HashSet<TypeDefinition>();

            foreach (var variant in variants)
            {
                var owner = variant.Owner;

                if (owner == null || owner.Module != module || !seen.Add(owner))
                {
                    continue;
                }

                for (var index = owner.CustomAttributes.Count - 1; index >= 0; index--)
                {
                    if (owner.CustomAttributes[index].AttributeType.FullName == AttributeType)
                    {
                        owner.CustomAttributes.RemoveAt(index);
                    }
                }
            }
        }

        /// <summary>
        /// Where the assembly holding the attribute is on disk.
        /// </summary>
        /// <remarks>
        /// A game's own code compiles into <c>Assembly-CSharp</c>, which references every
        /// auto-referenced package and so lists this one. Code split into assembly definitions
        /// references only what it declares, and a game team has no reason to declare this — the
        /// whole promise is that they change nothing. Those assemblies used to be refused, which
        /// meant the projects most likely to want this got the least: in the sample project one
        /// assembly built 324 evidence cases and baked none of them.
        ///
        /// So the reference list is asked first and the directories it names are searched second.
        /// The identity written into the game assembly is read from the file that is found, never
        /// composed from a name, which is what the earlier refusal was protecting against — a
        /// reference to something that does not exist under that name fails as a type load error in
        /// a built player rather than here.
        /// </remarks>
        private static string FindRuntimeAssembly(ICompiledAssembly compiledAssembly)
        {
            var references = compiledAssembly.References;

            if (references == null)
            {
                return null;
            }

            var folders = new List<string>();

            foreach (var reference in references)
            {
                if (string.IsNullOrEmpty(reference))
                {
                    continue;
                }

                if (string.Equals(Path.GetFileNameWithoutExtension(reference), RuntimeAssembly,
                        StringComparison.Ordinal) && File.Exists(reference))
                {
                    return reference;
                }

                var folder = Path.GetDirectoryName(reference);

                if (!string.IsNullOrEmpty(folder) && !folders.Contains(folder))
                {
                    folders.Add(folder);
                }
            }

            // Compiled output sits together, so the assembly this package builds is beside the ones
            // the game was compiled against even when nothing pointed at it.
            foreach (var folder in folders)
            {
                var candidate = Path.Combine(folder, RuntimeAssembly + ".dll");

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
