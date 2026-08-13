using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// Finds the assemblies a game assembly was compiled against.
    /// </summary>
    /// <remarks>
    /// Deciding whether a type is a behaviour means walking its base types, and every step of that
    /// walk lands in another assembly — <c>UnityEngine.CoreModule</c> at the least. Cecil cannot
    /// follow those on its own here because the assembly under analysis is a stream in memory with
    /// no directory to search next to.
    ///
    /// The reference paths handed over by the compiler are the answer, and they are the only
    /// answer: a module's own <c>AssemblyReferences</c> lists what its code actually used, which is
    /// a different and smaller set.
    /// </remarks>
    internal sealed class CompiledAssemblyResolver : IAssemblyResolver
    {
        private readonly Dictionary<string, string> _pathsByName = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, AssemblyDefinition> _opened = new Dictionary<string, AssemblyDefinition>(StringComparer.Ordinal);

        internal CompiledAssemblyResolver(ICompiledAssembly compiledAssembly)
        {
            var references = compiledAssembly.References;
            if (references == null)
            {
                return;
            }

            foreach (var reference in references)
            {
                if (string.IsNullOrEmpty(reference))
                {
                    continue;
                }

                // Keyed by file name because that is what an assembly reference carries. Later
                // entries win; the compiler does not hand over two paths for one name.
                _pathsByName[Path.GetFileNameWithoutExtension(reference)] = reference;
            }
        }

        public AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            return Resolve(name, new ReaderParameters(ReadingMode.Deferred));
        }

        public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            if (name == null)
            {
                return null;
            }

            if (_opened.TryGetValue(name.Name, out var alreadyOpen))
            {
                return alreadyOpen;
            }

            var opened = Open(name.Name, parameters);

            // Cached even when it came back null. A reference that cannot be found will be asked
            // for once per type that inherits through it, and failing takes as long as succeeding.
            _opened[name.Name] = opened;
            return opened;
        }

        private AssemblyDefinition Open(string name, ReaderParameters parameters)
        {
            if (!_pathsByName.TryGetValue(name, out var path) || !File.Exists(path))
            {
                return null;
            }

            parameters.AssemblyResolver = this;

            try
            {
                return AssemblyDefinition.ReadAssembly(path, parameters);
            }
            catch (Exception)
            {
                // Unreadable references are ordinary input when walking someone else's build.
                // Returning null costs one unresolved base type; throwing costs the whole assembly.
                return null;
            }
        }

        public void Dispose()
        {
            foreach (var assembly in _opened.Values)
            {
                assembly?.Dispose();
            }

            _opened.Clear();
        }
    }
}
