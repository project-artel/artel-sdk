using System;
using System.Collections.Generic;
using System.Reflection;

namespace Artel.Affordances.Live
{
    /// <summary>
    /// Everything on a component that can be read back, beyond what the evidence asked for.
    /// </summary>
    /// <remarks>
    /// The watch list is what the conditions and effects named, and for checking the specifications
    /// we already have that is exactly the right list — it is as long as the evidence requires
    /// rather than as long as the game is.
    ///
    /// It is the wrong list for the specifications we do not have yet. The analysis misses things:
    /// the agent's own account of its output counts a hundred and eighteen unresolved targets and
    /// fifty-two runtime instances it never observed, ninety-five of its hundred and seventy
    /// scenarios sit at <c>review</c>, and twenty-six triggers were never reached at all. Those are
    /// the rows a person will later write by hand, and the field such a row turns on is precisely a
    /// field no condition mentioned — otherwise the analysis would have found the row itself.
    ///
    /// So the question asked here is not "does the evidence want this" but "can this be read".
    /// The costs of the two mistakes are not the same. Watch too narrowly and the value is simply
    /// unavailable, and the only way to get it is to compile the game again — which is the thing
    /// this package exists not to require. Watch too widely and it costs reading time and traffic,
    /// both of which are ours to tune. Between a mistake that cannot be undone from here and one
    /// that can, take the one that can.
    ///
    /// What still cannot be read is unchanged by any of this: a local or a parameter exists only
    /// while its method runs, and no amount of widening reaches into a frame that has ended.
    /// </remarks>
    internal static class Readable
    {
        /// <summary>
        /// Assemblies whose fields are not the game's own.
        /// </summary>
        /// <remarks>
        /// Named by what to skip rather than what to take, the same way the analysis chooses which
        /// assemblies to read. Reading every private field of <c>Image</c>, <c>TMP_Text</c> and
        /// <c>EventTrigger</c> would bury the game's own state under hundreds of layout values
        /// nobody asked for, and those values change for reasons no specification mentions — which
        /// is a gate held open for nothing.
        ///
        /// Matched on a name boundary so <c>Unity</c> covers <c>Unity.TextMeshPro</c> and leaves an
        /// assembly merely beginning with those letters alone.
        /// </remarks>
        private static readonly string[] NotTheGames =
        {
            "UnityEngine", "UnityEditor", "Unity", "Artel", "System", "mscorlib", "netstandard",
            "nunit", "Newtonsoft", "Mono", "TMPro"
        };

        /// <summary>
        /// How many types are remembered before the lot is dropped and worked out again.
        /// </summary>
        /// <remarks>
        /// The same trade <see cref="Worth"/> makes. A game that loads assemblies for an hour would
        /// otherwise grow a row for every type it ever saw; dropping all of them costs one pass of
        /// reflection and cannot give a wrong answer.
        /// </remarks>
        private const int MaxRemembered = 2048;

        private const string BackingPrefix = "<";
        private const string BackingSuffix = ">k__BackingField";

        private static readonly Dictionary<Type, List<Watched>> Answered =
            new Dictionary<Type, List<Watched>>();

        /// <summary>
        /// The members to read on this component: what the evidence named, and what else is there.
        /// </summary>
        /// <param name="type">The concrete component type on the object being read.</param>
        /// <param name="named">What the watch list already holds for it, or null.</param>
        internal static List<Watched> On(Type type, List<Watched> named)
        {
            if (type == null)
            {
                return named;
            }

            if (Answered.TryGetValue(type, out var already))
            {
                return already;
            }

            if (Answered.Count >= MaxRemembered)
            {
                Answered.Clear();
            }

            var answer = Ask(type, named);
            Answered[type] = answer;
            return answer;
        }

        private static List<Watched> Ask(Type type, List<Watched> named)
        {
            var members = new List<Watched>();
            var taken = new HashSet<string>(StringComparer.Ordinal);

            if (named != null)
            {
                foreach (var member in named)
                {
                    members.Add(member);

                    if (member.Field != null)
                    {
                        taken.Add(member.Field.Name);
                    }
                }
            }

            if (!TheGames(type))
            {
                return members;
            }

            const BindingFlags Flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            FieldInfo[] fields;

            try
            {
                // Declared here and inherited both. A behaviour that keeps its state on a base class
                // of its own is the ordinary shape of a game's code, and reading only the leaf would
                // report the subclass as having nothing in it.
                fields = type.GetFields(Flags);
            }
            catch (Exception)
            {
                // A type reflection will not open is one component, not a reason to lose the object.
                return members;
            }

            foreach (var field in fields)
            {
                if (taken.Contains(field.Name) || Skip(field))
                {
                    continue;
                }

                members.Add(new Watched
                {
                    Declaring = field.DeclaringType == null ? type.FullName : field.DeclaringType.FullName,
                    Member = field.Name,
                    Property = Spoken(field.Name),
                    Type = field.FieldType.FullName,
                    Static = false,
                    Field = field,
                    Owner = type,
                    Asked = false
                });
            }

            return members;
        }

        /// <summary>
        /// Fields that answer nothing and would only add churn.
        /// </summary>
        /// <remarks>
        /// A delegate field is a list of subscribers. That it is non-null says a handler is attached
        /// and nothing about what the game is doing, and its identity changes whenever anything
        /// subscribes — a value that moves for reasons no specification mentions is exactly what the
        /// gate exists to keep out.
        /// </remarks>
        private static bool Skip(FieldInfo field)
        {
            if (field.IsStatic || field.IsLiteral)
            {
                return true;
            }

            var type = field.FieldType;

            return typeof(Delegate).IsAssignableFrom(type);
        }

        /// <summary>
        /// What everything other than reflection calls this field.
        /// </summary>
        /// <remarks>
        /// The same need the watch list already has for the members it was given: an automatic
        /// property is a field called <c>&lt;Instance&gt;k__BackingField</c>, and a reading naming it
        /// that way joins to nothing anybody else wrote. Null when the two are the same, so a
        /// reading only carries the second name when there is one.
        /// </remarks>
        private static string Spoken(string name)
        {
            if (!name.StartsWith(BackingPrefix, StringComparison.Ordinal) ||
                !name.EndsWith(BackingSuffix, StringComparison.Ordinal))
            {
                return null;
            }

            var length = name.Length - BackingPrefix.Length - BackingSuffix.Length;

            return length <= 0 ? null : name.Substring(BackingPrefix.Length, length);
        }

        private static bool TheGames(Type type)
        {
            string assembly;

            try
            {
                assembly = type.Assembly.GetName().Name;
            }
            catch (Exception)
            {
                return false;
            }

            if (string.IsNullOrEmpty(assembly))
            {
                return false;
            }

            foreach (var prefix in NotTheGames)
            {
                if (!assembly.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (assembly.Length == prefix.Length || assembly[prefix.Length] == '.')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
