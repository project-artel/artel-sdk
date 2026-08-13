using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// A property whose accessor does nothing but touch one field.
    /// </summary>
    /// <remarks>
    /// State changed through a property was invisible. Only field stores and a list of engine
    /// setters counted as effects, so a game that writes <c>controller.currentLife -= 1</c> had the
    /// condition for losing a life read perfectly and no effect but a sound. Measured on Trash Dash
    /// that was life, coins and premium — the three things that game is about.
    ///
    /// Only the trivial accessors, and that restriction is the whole design. A setter with a body of
    /// its own is a method the analysis already reads: it is a call in the same module, so its own
    /// record carries its own effects. Counting the call site as a write *as well* would report one
    /// change twice, in two records that nothing merges, and a reader would have no way to tell that
    /// they are the same event.
    ///
    /// Named after the field rather than the property so that both ways of writing it agree. Inside
    /// the class the compiler emits a field store; outside it emits this call. They are one fact and
    /// have to arrive under one name.
    /// </remarks>
    internal static class SimpleSetter
    {
        /// <summary>How many instructions an accessor may hold and still count as trivial.</summary>
        /// <remarks>
        /// A store is three plus the return; a load is two plus the return. The allowance is loose
        /// enough for the <c>nop</c> a debug build pads with and tight enough that nothing with a
        /// branch in it can pass.
        /// </remarks>
        private const int MaxInstructions = 8;

        private static readonly Dictionary<string, FieldReference> Known =
            new Dictionary<string, FieldReference>(StringComparer.Ordinal);

        /// <summary>
        /// The field a trivial accessor reaches, or null when it is anything else.
        /// </summary>
        /// <remarks>
        /// Cached by the reference's own name because a property is called from many places and the
        /// answer cannot change between them.
        /// </remarks>
        internal static FieldReference FieldBehind(MethodReference method)
        {
            if (method == null || !IsAccessor(method.Name))
            {
                return null;
            }

            var key = method.FullName;

            if (Known.TryGetValue(key, out var already))
            {
                return already;
            }

            var found = Read(method);
            Known[key] = found;
            return found;
        }

        private static bool IsAccessor(string name)
        {
            return name.Length > 4 &&
                   (name.StartsWith("set_", StringComparison.Ordinal) ||
                    name.StartsWith("get_", StringComparison.Ordinal));
        }

        private static FieldReference Read(MethodReference method)
        {
            MethodDefinition definition;

            try
            {
                definition = method.Resolve();
            }
            catch (Exception)
            {
                return null;
            }

            if (definition == null || !definition.HasBody)
            {
                return null;
            }

            // The engine's own properties are not the game's state, and several of them are already
            // recognised by name as the specific things they are.
            var space = definition.DeclaringType?.Namespace;

            if (space != null &&
                (space == "UnityEngine" || space.StartsWith("UnityEngine.", StringComparison.Ordinal)))
            {
                return null;
            }

            var body = definition.Body;

            if (body.Instructions.Count > MaxInstructions)
            {
                return null;
            }

            FieldReference touched = null;

            foreach (var instruction in body.Instructions)
            {
                switch (instruction.OpCode.Code)
                {
                    case Code.Nop:
                    case Code.Ret:
                    case Code.Ldarg_0:
                    case Code.Ldarg_1:
                    case Code.Ldarg:
                    case Code.Ldarg_S:
                    case Code.Stloc_0:
                    case Code.Ldloc_0:
                        continue;

                    case Code.Stfld:
                    case Code.Stsfld:
                    case Code.Ldfld:
                    case Code.Ldsfld:
                        if (touched != null)
                        {
                            // Two fields, so this is not one field wearing a property's name.
                            return null;
                        }

                        touched = instruction.Operand as FieldReference;
                        continue;

                    default:
                        // Anything else — a branch, a call, arithmetic — means the accessor does
                        // something of its own, and that something is read where it is written.
                        return null;
                }
            }

            return touched;
        }

        /// <summary>Cleared between assemblies: a name means a different thing in each.</summary>
        internal static void Forget()
        {
            Known.Clear();
        }
    }
}
