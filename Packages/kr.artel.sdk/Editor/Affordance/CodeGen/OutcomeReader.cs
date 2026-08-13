using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>Recognises the changes a single instruction makes.</summary>
    internal static class OutcomeReader
    {
        private const string SceneManagerType = "UnityEngine.SceneManagement.SceneManager";
        private const string ApplicationType = "UnityEngine.Application";
        private const string PlayerPrefsType = "UnityEngine.PlayerPrefs";
        private const string GameObjectType = "UnityEngine.GameObject";
        private const string TransformType = "UnityEngine.Transform";
        private const string ObjectType = "UnityEngine.Object";

        /// <summary>
        /// Reads one instruction, bounded by the block it sits in.
        /// </summary>
        /// <remarks>
        /// The bound arrived late and it was needed all along. Naming the object a call was made on
        /// means walking back over its arguments, and an argument is a *stack slot*, not an
        /// instruction — <c>transform.localScale = Scale * 1.2f</c> stepping back one instruction
        /// lands on the literal and reported the receiver as <c>1.2</c>. Forty-nine of a hundred and
        /// nineteen observable effects in the sample game named a number or gave up.
        /// </remarks>
        internal static Outcome ReadDirect(
            Instruction instruction, Instruction boundary, MethodDefinition within)
        {
            Outcome outcome;

            switch (instruction.OpCode.Code)
            {
                case Code.Stfld:
                case Code.Stsfld:
                    outcome = Write(instruction, boundary, within);
                    break;

                case Code.Call:
                case Code.Callvirt:
                    outcome = Called(instruction, boundary, within);
                    break;

                default:
                    return null;
            }

            if (outcome != null)
            {
                outcome.Offset = instruction.Offset;
            }

            return outcome;
        }

        /// <summary>
        /// State the game keeps after it is closed.
        /// </summary>
        /// <remarks>
        /// A saved value outlives the run, which makes it both the thing a test most wants to assert
        /// on and the thing that makes the next test start somewhere unexpected. Reading these is
        /// what lets a specification say a button was what created the save.
        /// </remarks>
        private static Outcome Stored(
            Instruction instruction, MethodReference called, Instruction boundary,
            MethodDefinition within)
        {
            switch (called.Name)
            {
                case "SetInt":
                case "SetFloat":
                case "SetString":
                    // Key first, then value: the key is two pushes back.
                    return new Outcome
                    {
                        Kind = "saved",
                        Category = "state",
                        Target = Key(IlReading.Under(IlReading.Preceding(instruction, boundary), boundary)),
                        Detail = IlReading.Describe(IlReading.Preceding(instruction, boundary), boundary, within)
                    };

                case "DeleteKey":
                    return new Outcome
                    {
                        Kind = "saved",
                        Category = "state",
                        Target = Key(IlReading.Preceding(instruction, boundary)),
                        Detail = "deleted"
                    };

                case "DeleteAll":
                    return new Outcome { Kind = "saved", Category = "state", Target = "*", Detail = "deleted" };

                default:
                    return null;
            }
        }

        private static string Key(Instruction instruction)
        {
            if (instruction != null && instruction.OpCode.Code == Code.Ldstr)
            {
                return instruction.Operand as string;
            }

            // A key held in a variable. Which slot was written cannot be answered from here, and
            // saying so beats naming the wrong one.
            return "(not a literal)";
        }

        private static Outcome Called(
            Instruction instruction, Instruction boundary, MethodDefinition within)
        {
            if (!(instruction.Operand is MethodReference called))
            {
                return null;
            }

            var declaring = called.DeclaringType?.FullName;

            if (declaring == GameObjectType && called.Name == "SetActive")
            {
                return new Outcome
                {
                    Kind = "active-state",
                    Category = "availability",
                    Target = Receiver(instruction, boundary),
                    Detail = Boolean(IlReading.Preceding(instruction, boundary), boundary),
                    Watch = WatchTarget.From(IlReading.Rooted(called, instruction, boundary, within))
                };
            }

            if (called.Name == "set_enabled" && IsUnityType(declaring))
            {
                return new Outcome
                {
                    Kind = "component-enabled",
                    Category = "availability",
                    Target = Receiver(instruction, boundary),
                    Detail = Boolean(IlReading.Preceding(instruction, boundary), boundary),
                    Watch = WatchTarget.From(IlReading.Rooted(called, instruction, boundary, within))
                };
            }

            if (called.Name == "set_interactable" &&
                declaring != null && declaring.StartsWith("UnityEngine.UI.", System.StringComparison.Ordinal))
            {
                return new Outcome
                {
                    Kind = "interactable",
                    Category = "availability",
                    Target = Receiver(instruction, boundary),
                    Detail = Boolean(IlReading.Preceding(instruction, boundary), boundary),
                    Watch = WatchTarget.From(IlReading.Rooted(called, instruction, boundary, within))
                };
            }

            if (declaring == TransformType && IsTransformSetter(called.Name))
            {
                return new Outcome
                {
                    Kind = "transform",
                    Category = "observable",
                    Target = Receiver(instruction, boundary) + "." + called.Name.Substring(4),
                    Detail = IlReading.Describe(IlReading.Preceding(instruction, boundary), boundary, within),
                    Watch = WatchTarget.From(IlReading.Rooted(called, instruction, boundary, within)),
                    WatchSource = Source(IlReading.Preceding(instruction, boundary), boundary, within)
                };
            }

            // TextMeshPro's own way of setting a label. Every game measured uses the method rather
            // than the property, so recognising only `set_text` left the two chat windows of the
            // sample game with no observable effect at all and the enemies with no visible health.
            if (declaring != null && called.Name == "SetText" &&
                declaring.StartsWith("TMPro.", System.StringComparison.Ordinal))
            {
                return new Outcome
                {
                    Kind = "ui-value",
                    Category = "observable",
                    Watch = WatchTarget.From(IlReading.Rooted(called, instruction, boundary, within)),
                    Target = Receiver(instruction, boundary) + ".text",
                    // The whole argument list rather than one slot: SetText has overloads that take
                    // a format string and numbers, and which of them ran is part of what changed.
                    Detail = IlReading.Arguments(called, instruction, boundary)
                };
            }

            if (IsUiSetter(declaring, called.Name))
            {
                return new Outcome
                {
                    Kind = "ui-value",
                    Category = "observable",
                    Target = Receiver(instruction, boundary) + "." + called.Name.Substring(4),
                    Detail = IlReading.Describe(IlReading.Preceding(instruction, boundary), boundary, within),
                    Watch = WatchTarget.From(IlReading.Rooted(called, instruction, boundary, within))
                };
            }

            if (declaring == ObjectType &&
                (called.Name == "Instantiate" || called.Name == "Destroy" || called.Name == "DestroyImmediate"))
            {
                return new Outcome
                {
                    Kind = called.Name == "Instantiate" ? "instantiate" : "destroy",
                    Category = "observable",
                    // Argument zero, never the instruction before. What is made or destroyed is
                    // always the first argument and the overloads differ in what follows it, so
                    // stepping back one landed on whatever came last — the rotation of an
                    // `Instantiate(prefab, position, rotation)`, the delay of a `Destroy(o, t)`.
                    // Thirteen of one and four of the other named the wrong thing, and four of
                    // those were not vague but false: `destroy Iceball.lifetime`.
                    Target = IlReading.ArgumentAt(called, instruction, boundary, 0, within)
                             ?? "(not a simple target)",

                    // A prefab chosen in one of several branches and made after they join. The
                    // name is the local's; these are what the branches put in it.
                    TargetCandidates = IlReading.Candidates(
                        IlReading.ArgumentFrom(called, instruction, boundary, 0),
                        boundary, within, MostCandidates)
                };
            }

            if (declaring != null &&
                declaring.StartsWith("UnityEngine.Events.UnityEvent", System.StringComparison.Ordinal) &&
                called.Name == "Invoke")
            {
                return new Outcome
                {
                    Kind = "event",
                    Category = "observable",
                    Target = Receiver(instruction, boundary)
                };
            }

            // Which animation, not just that there was one. `SetTrigger` names the parameter it
            // pulls and that name is a literal sitting in the instruction — the same reading that
            // turns `CompareTag()` into `CompareTag("Spell")`. Without it every one of these came
            // out as "the animation changes", which is true of all of them at once and so says
            // nothing: fourteen records in the development build and ten in the editor scan.
            //
            // The method's own name is kept in front of the argument because setting a trigger and
            // setting a number are different things to look for on the screen.
            if (declaring == "UnityEngine.Animator" && called.Name.StartsWith("Set", System.StringComparison.Ordinal))
            {
                var arguments = IlReading.Arguments(called, instruction, boundary);

                return new Outcome
                {
                    Kind = "animation",
                    Category = "observable",
                    Watch = WatchTarget.From(IlReading.Rooted(called, instruction, boundary, within)),
                    AnimatorName = Literal(IlReading.ArgumentFrom(called, instruction, boundary, 0)),
                    Target = Receiver(instruction, boundary),
                    Detail = arguments == null
                        ? called.Name
                        : called.Name + "(" + arguments + ")"
                };
            }

            if (declaring == "UnityEngine.AudioSource" &&
                (called.Name == "Play" || called.Name == "PlayOneShot" || called.Name == "Stop"))
            {
                return new Outcome
                {
                    Kind = "audio",
                    Category = "observable",
                    Target = Receiver(instruction, boundary),
                    Detail = called.Name
                };
            }

            if (declaring != null &&
                (declaring == "UnityEngine.Rigidbody" || declaring == "UnityEngine.Rigidbody2D") &&
                (called.Name == "MovePosition" || called.Name == "MoveRotation"))
            {
                return new Outcome
                {
                    Kind = "physics-move",
                    Category = "observable",
                    Target = Receiver(instruction, boundary),
                    Detail = called.Name
                };
            }

            var tweened = TweenedTransform(called);

            if (tweened != null)
            {
                return new Outcome
                {
                    Kind = "transform",
                    Category = "observable",
                    // Argument zero, not a receiver: these are extension methods, so the transform
                    // being moved is passed rather than called on and `Receiver` rightly says there
                    // is none.
                    Target = (IlReading.ArgumentAt(called, instruction, boundary, 0, within)
                              ?? "(not a simple target)") + "." + tweened,
                    Detail = IlReading.ArgumentAt(called, instruction, boundary, 1),

                    // Rooted from the arguments for the same reason the target is read from them.
                    // The map cursor's walk to `village` is a tween and its walk to `battle1` is a
                    // plain assignment, and a watcher that saw one and not the other would report
                    // four of the five map moves and call it the set.
                    Watch = WatchTarget.From(IlReading.RootedAt(
                        IlReading.ArgumentFrom(called, instruction, boundary, 0), boundary, within)),
                    WatchSource = Source(
                        IlReading.ArgumentFrom(called, instruction, boundary, 1), boundary, within)
                };
            }

            if (declaring == PlayerPrefsType)
            {
                return Stored(instruction, called, boundary, within);
            }

            if (declaring == SceneManagerType &&
                (called.Name == "LoadScene" || called.Name == "LoadSceneAsync"))
            {
                var argument = IlReading.Preceding(instruction, boundary);

                if (argument != null && argument.OpCode.Code == Code.Ldstr)
                {
                    return new Outcome { Kind = "scene", Category = "observable", Target = argument.Operand as string };
                }

                if (IlReading.TryConstant(argument, out var index))
                {
                    return new Outcome { Kind = "scene", Category = "observable", Target = "#" + index };
                }

                return new Outcome { Kind = "scene", Category = "observable", Target = "(not a literal)" };
            }

            // Only the setter. The getter is recognised by the same helper because reading the
            // direction of a change needs it — `currentLife -= 1` fetches through the getter before
            // it stores through the setter — but a fetch is not a change and must not be written as
            // one.
            var written = called.Name.StartsWith("set_", System.StringComparison.Ordinal)
                ? SimpleSetter.FieldBehind(called)
                : null;

            if (written != null)
            {
                // A property that only assigns a field is the same change as writing the field, and
                // the game's own code does it both ways — from inside the class the compiler writes
                // the field, from outside it calls the setter. Named after the field so the two
                // halves say the same thing and can be put together.
                return new Outcome
                {
                    Kind = "write",
                    Category = "state",
                    Target = IlReading.FieldName(written),
                    Detail = Direction(instruction, written) ?? IlReading.Describe(instruction.Previous),
                    Watch = WatchTarget.Of(written, !called.HasThis)
                };
            }

            if (declaring == ApplicationType && called.Name == "Quit")
            {
                return new Outcome { Kind = "quit", Category = "observable", Target = string.Empty };
            }

            return null;
        }

        /// <summary>
        /// Where a written value was read from, when it was read off another object.
        /// </summary>
        /// <remarks>
        /// The map cursor is moved by assigning it another marker's position, so the value has a
        /// place of its own that can be watched. Only that shape: the instruction has to be a
        /// property read on something, and the something has to root to a field. A value computed
        /// from three others is not somewhere, and saying it was would put a member in the watch
        /// list that answers a different question than the one asked.
        /// </remarks>
        /// <summary>The string an instruction pushes, when it pushes one written in the code.</summary>
        private static string Literal(Instruction from)
        {
            return from != null && from.OpCode.Code == Code.Ldstr ? from.Operand as string : null;
        }

        private static WatchTarget Source(
            Instruction from, Instruction boundary, MethodDefinition within)
        {
            return WatchTarget.ReadOff(from, boundary, within);
        }

        /// <summary>
        /// A field being written, and which way when it can be told.
        /// </summary>
        /// <remarks>
        /// The direction is the point. A pair of direction keys writes the same field from the same
        /// method, and what separates them is that one adds and the other subtracts.
        /// </remarks>
        private static Outcome Write(
            Instruction instruction, Instruction boundary, MethodDefinition within)
        {
            var field = instruction.Operand as FieldReference;
            var name = IlReading.FieldName(field);

            if (name == null)
            {
                return null;
            }

            var detail = Direction(instruction, field)
                         ?? IlReading.Describe(IlReading.Preceding(instruction, boundary), boundary, within);
            return new Outcome
            {
                Kind = "write",
                Category = "state",
                Target = name,
                Detail = detail,
                Watch = WatchTarget.Of(field, instruction.OpCode.Code == Code.Stsfld)
            };
        }

        /// <summary>
        /// What the call was made on, walked by stack slot rather than by instruction.
        /// </summary>
        /// <remarks>
        /// The count of arguments comes from the signature, and skipping each of them is
        /// <see cref="IlReading.Under"/>'s job — an argument may be one instruction or twenty.
        /// Stepping back a fixed number of instructions named a literal as the receiver, which is
        /// worse than admitting it is unknown: <c>1.2.localScale</c> and <c>0.sprite</c> read like
        /// values somebody could act on.
        /// </remarks>
        private static string Receiver(Instruction call, Instruction boundary)
        {
            return IlReading.Receiver(call.Operand as MethodReference, call, boundary)
                   ?? "(not a simple receiver)";
        }

        private static string Boolean(Instruction instruction, Instruction boundary)
        {
            return IlReading.TryConstant(instruction, out var value)
                ? (value == 0 ? "false" : "true")
                : IlReading.Describe(instruction, boundary) ?? "(not a literal)";
        }

        /// <summary>How many branches choosing one value are still a choice worth listing.</summary>
        private const int MostCandidates = 8;

        private static bool IsUnityType(string fullName)
        {
            return fullName != null && fullName.StartsWith("UnityEngine.", System.StringComparison.Ordinal);
        }

        private static bool IsTransformSetter(string name)
        {
            return name == "set_position" || name == "set_localPosition" ||
                   name == "set_rotation" || name == "set_localRotation" ||
                   name == "set_localScale";
        }

        /// <summary>
        /// Which part of a transform a tweening library was told to change, or null.
        /// </summary>
        /// <remarks>
        /// A tween moves what is on the screen as surely as assigning the property does, and leaving
        /// it out cost the sample game its whole map: eight arrow-key records changed a lane index
        /// and nothing else, so the one thing a person sees — the character walking to the next
        /// stage — could not be written down.
        ///
        /// Matched on the shape of the name rather than a list of signatures, because the list is
        /// the part that goes stale. Every shortcut that changes something is named for what it
        /// changes, and the ones that only steer a running tween (<c>DOKill</c>, <c>DOComplete</c>,
        /// <c>DOPause</c>) are named for that instead and fall through — this is an allowlist by
        /// shape, not "anything starting with DO".
        ///
        /// Nothing here is resolved. The namespace and the parameter type are read off the reference
        /// as the assembly stored it, so a project without the library present reads exactly as it
        /// did before rather than failing to find it. That is the whole reason a third party can be
        /// named here at all: <see cref="CallGraph"/> may not follow a call it cannot read the body
        /// of, but naming the change at the call site never needed the body.
        /// </remarks>
        private static string TweenedTransform(MethodReference called)
        {
            var declaring = called.DeclaringType?.FullName;

            if (declaring == null ||
                !declaring.StartsWith("DG.Tweening.", System.StringComparison.Ordinal) ||
                !called.Name.StartsWith("DO", System.StringComparison.Ordinal) ||
                called.Parameters.Count < 2 ||
                called.Parameters[0].ParameterType?.FullName != TransformType)
            {
                return null;
            }

            var name = called.Name;

            if (name.Contains("Move") || name.Contains("Jump") || name.Contains("Path"))
            {
                return "position";
            }

            if (name.Contains("Rotat") || name.Contains("LookAt"))
            {
                return "rotation";
            }

            return name.Contains("Scale") ? "localScale" : null;
        }

        /// <summary>
        /// A property whose new value is on the screen.
        /// </summary>
        /// <remarks>
        /// Renderers are here as well as uGUI and TMP. A sprite swapped on a
        /// <c>SpriteRenderer</c> is as visible as one swapped on an <c>Image</c>, and leaving it out
        /// had a cost that was easy to miss: a block whose only effect is unrecognised has no
        /// effects at all, so it is dropped before anything else about it is read. The sample game's
        /// map background is drawn that way, and the <c>switch</c> deciding it was read correctly
        /// while the record it governed no longer existed.
        /// </remarks>
        private static bool IsUiSetter(string declaring, string name)
        {
            if (declaring == null)
            {
                return false;
            }

            if (declaring == "UnityEngine.SpriteRenderer")
            {
                return name == "set_sprite" || name == "set_color" ||
                       name == "set_flipX" || name == "set_flipY";
            }

            if (!declaring.StartsWith("UnityEngine.UI.", System.StringComparison.Ordinal) &&
                !declaring.StartsWith("TMPro.", System.StringComparison.Ordinal))
            {
                return false;
            }

            return name == "set_text" || name == "set_sprite" || name == "set_color" ||
                   name == "set_value" || name == "set_isOn";
        }

        private static string Direction(Instruction store, FieldReference field)
        {
            var operation = store.Previous;

            if (operation == null)
            {
                return null;
            }

            string sign;

            if (operation.OpCode.Code == Code.Add) sign = "+";
            else if (operation.OpCode.Code == Code.Sub) sign = "-";
            else return null;

            if (!IlReading.TryConstant(operation.Previous, out var step))
            {
                return null;
            }

            return ReadsSame(operation.Previous.Previous, field) ? sign + step : null;
        }

        /// <summary>
        /// Whether this instruction fetched the same field the write is about.
        /// </summary>
        /// <remarks>
        /// Either by reading the field or by calling the property that reads it. <c>currentLife -= 1</c>
        /// written from outside the class compiles to getter, subtract, setter — and the direction is
        /// the whole point of the sentence, so the getter has to count.
        /// </remarks>
        private static bool ReadsSame(Instruction load, FieldReference field)
        {
            if (load == null)
            {
                return false;
            }

            if (load.OpCode.Code == Code.Ldfld || load.OpCode.Code == Code.Ldsfld)
            {
                return load.Operand is FieldReference loaded && loaded.FullName == field.FullName;
            }

            if (load.OpCode.Code != Code.Call && load.OpCode.Code != Code.Callvirt)
            {
                return false;
            }

            var read = SimpleSetter.FieldBehind(load.Operand as MethodReference);
            return read != null && read.FullName == field.FullName;
        }
    }
}
