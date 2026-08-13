using System.Collections.Generic;
using Mono.Cecil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// Recognises the singleton that every Unity project writes.
    /// </summary>
    /// <remarks>
    /// <code>
    /// void Awake() {
    ///     if (instance == null) { instance = this; DontDestroyOnLoad(gameObject); }
    ///     else Destroy(gameObject);
    /// }
    /// </code>
    ///
    /// Read as evidence this is a game that destroys objects and changes its own state depending on
    /// a condition, which is what a candidate is. Twelve of them in the sample game, and every one
    /// would have become a specification saying that entering a scene twice deletes something.
    ///
    /// It is not dropped. The record stays, with what it is written on it, because "this was
    /// recognised as plumbing" and "this was never found" are different things and a reader that
    /// cannot tell them apart will go looking for the second.
    /// </remarks>
    internal static class SingletonPlumbing
    {
        /// <summary>Whether everything this case does is keep one instance of itself alive.</summary>
        internal static bool Explains(MethodDefinition entry, List<Outcome> outcomes)
        {
            if (outcomes.Count == 0 || !IsStartup(entry?.Name))
            {
                return false;
            }

            var owner = entry.DeclaringType;

            foreach (var outcome in outcomes)
            {
                if (!IsSelfDestruction(outcome) && !IsInstanceField(outcome, owner))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Only where the pattern lives.
        /// </summary>
        /// <remarks>
        /// The same two effects written anywhere else are a real thing the game does — a pickup
        /// destroying itself on collect writes exactly this way. What makes it plumbing is that it
        /// runs when the object comes up, before anybody has done anything.
        /// </remarks>
        private static bool IsStartup(string name)
        {
            return name == "Awake" || name == "OnEnable";
        }

        private static bool IsSelfDestruction(Outcome outcome)
        {
            if (outcome.Kind != "destroy")
            {
                return false;
            }

            // Its own object, named as the property that fetched it. Destroying something else at
            // startup is not this pattern.
            //
            // `this.gameObject` and the bare `this` are what the reader says now that it names the
            // object a call was made on; the older spellings are kept because an assembly that
            // cannot be read that far still falls back to them.
            return outcome.Target == "this.gameObject" ||
                   outcome.Target == "this" ||
                   outcome.Target == "Component.gameObject" ||
                   outcome.Target == "Object.gameObject" ||
                   outcome.Target == "Component.this";
        }

        /// <summary>
        /// A write to the type's own static field of its own type — the instance holder.
        /// </summary>
        /// <remarks>
        /// Checked against the field rather than against its name. <c>instance</c>, <c>Instance</c>,
        /// <c>_instance</c> and <c>current</c> are all the same idea, and a list of names is a list
        /// that is wrong for the next project.
        /// </remarks>
        private static bool IsInstanceField(Outcome outcome, TypeDefinition owner)
        {
            if (outcome.Kind != "write" || owner == null || outcome.Target == null)
            {
                return false;
            }

            var dot = outcome.Target.LastIndexOf('.');

            if (dot < 0)
            {
                return false;
            }

            var name = outcome.Target.Substring(dot + 1);

            foreach (var field in owner.Fields)
            {
                if (field.Name != name)
                {
                    continue;
                }

                return field.IsStatic && field.FieldType?.FullName == owner.FullName;
            }

            return false;
        }
    }
}
