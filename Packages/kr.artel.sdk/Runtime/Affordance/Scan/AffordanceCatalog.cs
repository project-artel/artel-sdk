using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// The evidence an assembly carries, read once and kept.
    /// </summary>
    /// <remarks>
    /// It used to be one attribute per record on each type. That made a game assembly three to
    /// eight times its own size across the projects this was measured on, and it did not survive:
    /// managed stripping set to High removes custom attributes, so a hard-stripped build reported a
    /// game with nothing in it.
    ///
    /// Now one compressed resource per assembly holds everything and the attribute is only a
    /// pointer. There are two ways in because the two treatments a build gets each take away a
    /// different one — stripping removes the attribute and leaves the resource, obfuscation renames
    /// the type and leaves the attribute. Asked by anchor first because it is exact, then by name.
    ///
    /// Cached by assembly and by type. A scene holds many instances of few types, and a hundred
    /// buttons of one kind ask the same question a hundred times.
    /// </remarks>
    internal static class AffordanceCatalog
    {
        private const string ResourceName = "kr.artel.affordance.evidence";

        private sealed class Carried
        {
            internal readonly Dictionary<int, string> ByAnchor = new Dictionary<int, string>();

            internal readonly Dictionary<string, string> ByName =
                new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private static readonly Dictionary<Assembly, Carried> Opened =
            new Dictionary<Assembly, Carried>();

        private static readonly Dictionary<Type, string> Known = new Dictionary<Type, string>();

        /// <summary>The evidence array for a type, as the document it already is, or null.</summary>
        internal static string For(Type type)
        {
            if (type == null)
            {
                return null;
            }

            if (Known.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var found = Look(type);

            // Cached even when nothing was found. Most components in a scene are scenery and will
            // be asked about once per instance.
            Known[type] = found;
            return found;
        }

        private static string Look(Type type)
        {
            Carried carried;

            try
            {
                carried = Read(type.Assembly);
            }
            catch (Exception)
            {
                // One assembly whose resource will not open is a gap in the report, not a reason to
                // stop reading the scene.
                return null;
            }

            if (carried == null)
            {
                return null;
            }

            try
            {
                var attributes =
                    (AffordanceAttribute[])type.GetCustomAttributes(typeof(AffordanceAttribute), false);

                if (attributes.Length > 0 &&
                    carried.ByAnchor.TryGetValue(attributes[0].Anchor, out var byAnchor))
                {
                    return byAnchor;
                }
            }
            catch (Exception)
            {
                // Falls through to the name, which is exactly the case the second way in is for.
            }

            return type.FullName != null && carried.ByName.TryGetValue(type.FullName, out var byName)
                ? byName
                : null;
        }

        private static Carried Read(Assembly assembly)
        {
            if (assembly == null)
            {
                return null;
            }

            if (Opened.TryGetValue(assembly, out var already))
            {
                return already;
            }

            var carried = Parse(assembly);
            Opened[assembly] = carried;
            return carried;
        }

        private static Carried Parse(Assembly assembly)
        {
            using (var packed = assembly.GetManifestResourceStream(ResourceName))
            {
                if (packed == null)
                {
                    return null;
                }

                string text;

                using (var expanded = new DeflateStream(packed, CompressionMode.Decompress))
                using (var reader = new StreamReader(expanded, Encoding.UTF8))
                {
                    text = reader.ReadToEnd();
                }

                var carried = new Carried();

                // One type per line: anchor, name, then the array. Split on the first two tabs only.
                // What follows is the document, copied through rather than parsed — its schema
                // belongs to the analyser that wrote it and the agent that reads it, and a third
                // opinion here would have to be kept in step with both.
                foreach (var line in text.Split('\n'))
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    var firstTab = line.IndexOf('\t');
                    var secondTab = firstTab < 0 ? -1 : line.IndexOf('\t', firstTab + 1);

                    if (secondTab < 0)
                    {
                        continue;
                    }

                    var name = line.Substring(firstTab + 1, secondTab - firstTab - 1);
                    var document = line.Substring(secondTab + 1);

                    if (int.TryParse(line.Substring(0, firstTab), out var anchor))
                    {
                        carried.ByAnchor[anchor] = document;
                    }

                    carried.ByName[name] = document;
                }

                return carried;
            }
        }

        /// <summary>
        /// Every type any loaded assembly carries evidence for.
        /// </summary>
        /// <remarks>
        /// The scan can only describe types it meets on a GameObject, and a game keeps most of its
        /// behaviour in prefabs that are only instantiated once something happens. Measured on the
        /// sample game the analysis baked 54 behaviours and a walk of every scene met 21 of them —
        /// the other 33 were in the assembly, correct, and invisible.
        ///
        /// Reading during play recovers most of them, because an instantiated prefab is in a scene
        /// like anything else. What is left is whatever the run never caused to exist, and that is
        /// a real limit of the report rather than a fault in it. Naming the difference is what turns
        /// it from a thing somebody has to discover into a thing the report says.
        ///
        /// Every assembly is opened, once, rather than only those a met type came from. The point is
        /// exactly the assemblies no met type came from.
        ///
        /// Keyed by the name the analyser wrote, which is the name the type had when it was compiled.
        /// An obfuscator runs after that, so these are the original names while what the scan meets
        /// carries the renamed ones — which is why what is placed is decided by comparing the
        /// documents rather than the keys.
        /// </remarks>
        internal static Dictionary<string, string> Everything()
        {
            var named = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Carried carried;

                try
                {
                    carried = Read(assembly);
                }
                catch (Exception)
                {
                    // A dynamic assembly, or one whose resources will not open. Skipping it makes
                    // the answer smaller, never wrong.
                    continue;
                }

                if (carried == null)
                {
                    continue;
                }

                foreach (var pair in carried.ByName)
                {
                    named[pair.Key] = pair.Value;
                }
            }

            return named;
        }

        internal static void Forget()
        {
            Known.Clear();
            Opened.Clear();
        }
    }
}
