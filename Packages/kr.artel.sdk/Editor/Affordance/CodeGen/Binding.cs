using System.Collections.Generic;
using Mono.Cecil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// What a callee's own words are called where it was called from.
    /// </summary>
    /// <remarks>
    /// A callee's condition is about the callee's object and its parameters, and neither means
    /// anything beside the caller's terms — which is why composing the two was refused rather than
    /// done. Refusing is right only while there is no way to say one in the other's words. At a
    /// call site there is: the caller wrote what it called the method on and what it passed, and
    /// both are expressions in the caller's own terms.
    ///
    /// So this is a translation, not a guess. Everything it cannot translate it declines, and a
    /// condition it declines any part of is not offered at all — a half-translated sentence reads
    /// as one object's account while being two.
    /// </remarks>
    internal sealed class Binding
    {
        /// <summary>The callee's type, whose name heads every term about its own <c>this</c>.</summary>
        internal string Owner;

        /// <summary>What the caller called it on, and whose that is.</summary>
        internal string Receiver;

        internal string ReceiverWhere;

        /// <summary>Parameter name to what was passed for it, and whose that is.</summary>
        internal Dictionary<string, string> Passed;

        internal Dictionary<string, string> PassedWhere;

        internal bool Anything => Receiver != null || (Passed != null && Passed.Count > 0);

        /// <summary>Names the arguments by the parameter they filled.</summary>
        internal static Binding Of(
            MethodDefinition callee, string receiver, string receiverWhere,
            string[] args, string[] argWhere)
        {
            var binding = new Binding
            {
                Owner = callee?.DeclaringType?.Name,
                Receiver = receiver,
                ReceiverWhere = receiverWhere
            };

            if (callee == null || args == null)
            {
                return binding;
            }

            binding.Passed = new Dictionary<string, string>(System.StringComparer.Ordinal);
            binding.PassedWhere = new Dictionary<string, string>(System.StringComparer.Ordinal);

            for (var index = 0; index < callee.Parameters.Count && index < args.Length; index++)
            {
                var name = callee.Parameters[index].Name;

                // An argument nobody could read, or a parameter with no name to match it to. Left
                // out, so a term about it stays untranslatable and the whole condition is declined.
                if (string.IsNullOrEmpty(name) || args[index] == null)
                {
                    continue;
                }

                binding.Passed[name] = args[index];
                binding.PassedWhere[name] = argWhere != null && index < argWhere.Length
                    ? argWhere[index]
                    : null;
            }

            return binding;
        }
    }
}
