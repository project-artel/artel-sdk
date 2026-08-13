using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Mono.Cecil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// Carries the evidence as one compressed blob inside the assembly rather than as an attribute
    /// per record.
    /// </summary>
    /// <remarks>
    /// An attribute per record put the growth on the wrong thing. Measured across three projects the
    /// assembly came out between three and eight times its own size, and which of those it landed on
    /// had nothing to do with how big the game was — a small game with dense branching cost more
    /// than one five times its size. Ninety-eight percent of that growth was the JSON text, most of
    /// it the same method signatures written again and again.
    ///
    /// A resource is not metadata. It does not enlarge the tables a type load walks, it is not
    /// parsed until something asks for it, and it compresses — the same text that took 322KB as
    /// attributes takes 13KB here.
    ///
    /// Deflate rather than gzip because gzip writes a header this has no use for, and one field of
    /// it is a timestamp. Analysing the same assembly twice has to produce the same bytes, and a
    /// clock in the output would quietly break that.
    /// </remarks>
    internal static class EvidenceResource
    {
        /// <summary>
        /// What the resource is called inside the assembly.
        /// </summary>
        /// <remarks>
        /// Named for this package so it cannot collide with a resource the game already carries.
        /// The scan asks for it by this name, which is the one thing here that obfuscation could
        /// take away — an attribute survives renaming because it stays attached to its type, and a
        /// resource has nothing to stay attached to.
        /// </remarks>
        internal const string ResourceName = "kr.artel.affordance.evidence";

        /// <summary>
        /// What the watch list is called inside the assembly.
        /// </summary>
        /// <remarks>
        /// Its own resource rather than another line in the evidence blob. The two are read at
        /// different moments by different code — the evidence when a scan meets a type, the watch
        /// list once before any polling starts — and a reader that wants one should not have to
        /// unpack and skip past the other, which on a real game is two orders of magnitude larger.
        ///
        /// Separate also degrades the right way. An older runtime meeting a newer assembly simply
        /// does not ask for this and reads the evidence exactly as it always did; folding it into
        /// the same document would have made every reader agree about a line it has no use for.
        /// </remarks>
        internal const string WatchResourceName = "kr.artel.affordance.watch";

        /// <summary>Replaces the blob on a module, and says how many bytes it took.</summary>
        internal static int Attach(ModuleDefinition module, string json)
        {
            return Attach(module, ResourceName, json);
        }

        /// <summary>Replaces the watch list on a module, and says how many bytes it took.</summary>
        internal static int AttachWatch(ModuleDefinition module, string json)
        {
            return Attach(module, WatchResourceName, json);
        }

        private static int Attach(ModuleDefinition module, string name, string json)
        {
            Detach(module, name);

            if (string.IsNullOrEmpty(json))
            {
                return 0;
            }

            var packed = Deflate(Encoding.UTF8.GetBytes(json));

            module.Resources.Add(
                new EmbeddedResource(name, ManifestResourceAttributes.Public, packed));

            return packed.Length;
        }

        /// <summary>
        /// Removes a blob left by an earlier pass.
        /// </summary>
        /// <remarks>
        /// The pipeline hands over a freshly compiled assembly, so this should find nothing. An
        /// assembly that has been through here twice would carry two generations at once, and the
        /// older one is indistinguishable from the newer while quietly contradicting it.
        /// </remarks>
        internal static void Detach(ModuleDefinition module)
        {
            Detach(module, ResourceName);
            Detach(module, WatchResourceName);
        }

        private static void Detach(ModuleDefinition module, string name)
        {
            for (var index = module.Resources.Count - 1; index >= 0; index--)
            {
                if (string.Equals(module.Resources[index].Name, name, StringComparison.Ordinal))
                {
                    module.Resources.RemoveAt(index);
                }
            }
        }

        private static byte[] Deflate(byte[] raw)
        {
            using (var output = new MemoryStream())
            {
                // Fixed level on purpose. The default is already deterministic, but naming it is
                // what keeps a framework upgrade from changing the bytes underneath the
                // byte-identical check without anyone touching this file.
                using (var compressor = new DeflateStream(output, CompressionLevel.Optimal, true))
                {
                    compressor.Write(raw, 0, raw.Length);
                }

                return output.ToArray();
            }
        }
    }
}
