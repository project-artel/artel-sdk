using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>What a type turned out to be.</summary>
    internal enum TypeVerdict
    {
        /// <summary>Cannot carry game logic on a GameObject.</summary>
        NotBehaviour,

        /// <summary>Derives from MonoBehaviour.</summary>
        Behaviour,

        /// <summary>
        /// The base type chain ran out before reaching an answer.
        /// </summary>
        /// <remarks>
        /// Kept apart from <see cref="NotBehaviour"/> deliberately. A type whose base class lives
        /// in an assembly that would not open looks exactly like a type that was never a behaviour,
        /// and silently treating one as the other drops the game's own code with nothing said. It
        /// costs a counter to tell them apart and it is the difference between a small number and
        /// a wrong answer.
        /// </remarks>
        Unresolved
    }

    /// <summary>Why a method is worth analysing.</summary>
    internal enum MethodScope
    {
        /// <summary>Nothing reaches this method through a player's hands.</summary>
        OutOfScope,

        /// <summary>Wired to a UnityEvent in the inspector — a button's onClick, for instance.</summary>
        InspectorCallable,

        /// <summary>Called by the engine. Where key and pointer handling lives.</summary>
        EngineMessage
    }

    /// <summary>
    /// Decides what is worth looking at before any looking happens.
    /// </summary>
    /// <remarks>
    /// The first version of this analysis was slow on a project of a few hundred scripts, and the
    /// reason was not that the work was expensive — it was that almost none of the work could
    /// affect the result. Narrowing first is cheaper than caching afterwards, and it is what keeps
    /// the later stages small enough to reason about.
    /// </remarks>
    internal static class AnalysisScope
    {
        private const string BehaviourTypeName = "UnityEngine.MonoBehaviour";
        private const string ObjectTypeName = "UnityEngine.Object";

        /// <summary>
        /// How far a base-type walk may go before it is treated as garbage.
        /// </summary>
        /// <remarks>
        /// Nothing legitimate is this deep. The cap is not for depth, it is for cycles: a
        /// hand-written or obfuscated assembly can describe a type as its own ancestor, and this
        /// loop is the kind that has already frozen an editor that then could not be opened to find
        /// out why.
        /// </remarks>
        private const int MaxInheritanceDepth = 32;

        /// <summary>
        /// Instruction count past which a method is left alone.
        /// </summary>
        /// <remarks>
        /// Generated code — state machines, large switch dispatchers — arrives at sizes no human
        /// writes. Counted and reported rather than dropped quietly, because a method this big is
        /// usually a compiler artifact and knowing that changes what to do about it.
        /// </remarks>
        internal const int MaxInstructions = 4000;

        /// <summary>
        /// Methods the engine calls on a behaviour.
        /// </summary>
        /// <remarks>
        /// Collected regardless of visibility, which is the whole point of listing them: these are
        /// private by convention, so a filter that asked for public members dropped every one of
        /// them — and with them the pointer handling and the <c>Update</c> bodies where key input
        /// is read. Restricting the earlier filter to what the inspector can call is what left
        /// mouse and drag unaccounted for.
        /// </remarks>
        private static readonly HashSet<string> EngineMessages = new HashSet<string>(StringComparer.Ordinal)
        {
            "Awake", "Start", "OnEnable", "OnDisable", "OnDestroy",
            "Update", "FixedUpdate", "LateUpdate", "OnGUI",

            // The end of the run. A game that saves on the way out does it here, and that is a
            // change a test has to know about: the state the next run starts from was decided by
            // whether the player quit, not by anything they pressed. Left out, the sample game's
            // "progress is saved when you quit" had no evidence at all.
            "OnApplicationQuit", "OnApplicationPause", "OnApplicationFocus",
            "OnMouseDown", "OnMouseUp", "OnMouseUpAsButton", "OnMouseDrag",
            "OnMouseEnter", "OnMouseExit", "OnMouseOver",
            "OnTriggerEnter", "OnTriggerStay", "OnTriggerExit",
            "OnTriggerEnter2D", "OnTriggerStay2D", "OnTriggerExit2D",
            "OnCollisionEnter", "OnCollisionStay", "OnCollisionExit",
            "OnCollisionEnter2D", "OnCollisionStay2D", "OnCollisionExit2D",

            // Handlers the event system calls through an interface. Their argument is an
            // EventData, which is not a UnityEngine.Object and not a primitive, so the
            // inspector-callable rule turns every one of them away. On a project built with uGUI
            // this is where clicking and dragging lives — leaving it to that rule would repeat the
            // omission that lost the magic methods, on the same category of input.
            "OnPointerClick", "OnPointerDown", "OnPointerUp", "OnPointerEnter", "OnPointerExit",
            "OnBeginDrag", "OnDrag", "OnEndDrag", "OnDrop", "OnScroll",
            "OnInitializePotentialDrag", "OnSubmit", "OnCancel", "OnMove",
            "OnSelect", "OnDeselect"
        };

        /// <summary>Works out whether the type can sit on a GameObject and carry game logic.</summary>
        internal static TypeVerdict Inspect(TypeDefinition type)
        {
            if (type == null || type.IsInterface || !type.IsClass)
            {
                return TypeVerdict.NotBehaviour;
            }

            var reached = Walk(type, BehaviourTypeName, out var unresolved);

            if (reached)
            {
                return TypeVerdict.Behaviour;
            }

            return unresolved ? TypeVerdict.Unresolved : TypeVerdict.NotBehaviour;
        }

        internal static MethodScope Classify(MethodDefinition method)
        {
            if (method == null || method.IsStatic || method.IsAbstract || !method.HasBody)
            {
                return MethodScope.OutOfScope;
            }

            if (EngineMessages.Contains(method.Name))
            {
                return MethodScope.EngineMessage;
            }

            return IsInspectorCallable(method) ? MethodScope.InspectorCallable : MethodScope.OutOfScope;
        }

        /// <summary>
        /// True when a UnityEvent could hold a persistent call to this method.
        /// </summary>
        /// <remarks>
        /// Mirrors what Unity's inspector will offer in the dropdown: an instance method returning
        /// nothing, taking either no argument or one the inspector can supply a literal for.
        /// </remarks>
        private static bool IsInspectorCallable(MethodDefinition method)
        {
            if (!method.IsPublic || method.IsSpecialName || method.HasGenericParameters)
            {
                return false;
            }

            if (method.ReturnType.MetadataType != MetadataType.Void)
            {
                return false;
            }

            if (method.Parameters.Count > 1)
            {
                return false;
            }

            return method.Parameters.Count == 0 || IsInspectorArgument(method.Parameters[0].ParameterType);
        }

        private static bool IsInspectorArgument(TypeReference type)
        {
            switch (type.MetadataType)
            {
                case MetadataType.Boolean:
                case MetadataType.Int32:
                case MetadataType.Single:
                case MetadataType.String:
                    return true;
            }

            var definition = SafeResolve(type);
            if (definition == null)
            {
                return false;
            }

            return definition.IsEnum || DerivesFrom(definition, ObjectTypeName);
        }

        /// <summary>
        /// True when reading this method in order would give the wrong answer.
        /// </summary>
        /// <remarks>
        /// A method with no decision in it has one path, so its control flow graph would be a
        /// single block and building one is wasted. Anything else has to go through the graph:
        /// walking instructions in sequence reads the body guarded by one key as belonging to
        /// another once an <c>if/else</c> chain puts an <c>||</c> between them.
        /// </remarks>
        internal static bool NeedsControlFlow(MethodDefinition method)
        {
            var body = method.Body;
            if (body.HasExceptionHandlers)
            {
                return true;
            }

            foreach (var instruction in body.Instructions)
            {
                if (instruction.OpCode.FlowControl == FlowControl.Cond_Branch)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsTooLarge(MethodDefinition method)
        {
            return method.Body.Instructions.Count > MaxInstructions;
        }

        private static bool DerivesFrom(TypeDefinition type, string baseTypeFullName)
        {
            return Walk(type, baseTypeFullName, out _);
        }

        /// <summary>
        /// Climbs the base type chain looking for a name.
        /// </summary>
        /// <param name="unresolved">
        /// Set when the climb stopped without an answer — a base type that would not open, or a
        /// chain long enough to suspect it loops back on itself.
        /// </param>
        private static bool Walk(TypeDefinition type, string baseTypeFullName, out bool unresolved)
        {
            unresolved = false;
            var current = type;

            for (var depth = 0; depth < MaxInheritanceDepth; depth++)
            {
                if (string.Equals(current.FullName, baseTypeFullName, StringComparison.Ordinal))
                {
                    return true;
                }

                var baseType = current.BaseType;
                if (baseType == null)
                {
                    // Reached the root of the chain. The answer is no, and it is a real answer.
                    return false;
                }

                var resolved = SafeResolve(baseType);
                if (resolved == null)
                {
                    unresolved = true;
                    return false;
                }

                current = resolved;
            }

            unresolved = true;
            return false;
        }

        private static TypeDefinition SafeResolve(TypeReference reference)
        {
            try
            {
                return reference?.Resolve();
            }
            catch (Exception)
            {
                // A reference that will not resolve is ordinary input, not a fault. It costs one
                // unanswered question about one type.
                return null;
            }
        }
    }
}
