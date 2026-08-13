using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// Where a method was hung on something that will call it later.
    /// </summary>
    /// <remarks>
    /// A call graph follows calls, and a game built on event channels barely has any. A button
    /// publishes to a ScriptableObject asset and whatever does the work has subscribed to the same
    /// asset from somewhere else entirely; the two halves never name each other. Measured on Chop
    /// Chop this was not a shortfall but the whole result — 1,468 evidence records and nothing a
    /// specification could be written from, because every button ended at a channel.
    ///
    /// The subscription itself is in the IL and is unmissable: taking a method's address is
    /// <c>ldftn</c>, and there is exactly one reason to do it. What it is attached to comes from
    /// following the value forward to whatever consumes it — an <c>add_</c> accessor, an
    /// <c>AddListener</c>, or a store into a delegate field.
    ///
    /// This does not join the two halves. It cannot: which subscriber hears which publisher is
    /// decided by the asset an inspector field points at, and that is in the scene, not in the code.
    /// What is written here is the type of the channel on both sides, which narrows it to the
    /// channels of that type, and the serialized field read gives the rest.
    /// </remarks>
    internal static class Subscriptions
    {
        /// <summary>How many instructions forward a delegate is followed before giving up.</summary>
        private const int Reach = 8;

        internal static void ReadInto(BasicBlock block, ModuleDefinition module, List<Subscription> found)
        {
            for (var instruction = block.First; instruction != null; instruction = instruction.Next)
            {
                var subscription = ReadAt(instruction, block, module);

                if (subscription != null)
                {
                    found.Add(subscription);
                }

                if (instruction == block.Last)
                {
                    break;
                }
            }
        }

        private static Subscription ReadAt(Instruction instruction, BasicBlock block, ModuleDefinition module)
        {
            if (instruction.OpCode.Code != Code.Ldftn && instruction.OpCode.Code != Code.Ldvirtftn)
            {
                return null;
            }

            var handler = Resolve(instruction.Operand as MethodReference);

            if (handler == null || handler.Module != module)
            {
                // A delegate over engine code. The game did not write it and cannot be told to run it.
                return null;
            }

            var attach = Attachment(instruction, block);

            if (attach == null)
            {
                return null;
            }

            var subscription = new Subscription
            {
                Handler = handler.FullName,
                HandlerId = MethodIdentity.Of(handler),
                Offset = instruction.Offset
            };

            if (attach.OpCode.Code == Code.Stfld || attach.OpCode.Code == Code.Stsfld)
            {
                var field = attach.Operand as FieldReference;

                if (field == null)
                {
                    return null;
                }

                // The type that declares the field, not the field's own type. A field-like event
                // compiles to a delegate field, so its type is UnityAction — true and useless. What
                // makes a subscriber meet a publisher is the type the event belongs to, which is
                // also the type declaring the Raise the publisher called.
                subscription.Channel = IlReading.FieldName(field);
                subscription.ChannelType = field.DeclaringType?.FullName;
                subscription.Member = field.Name;
                return subscription;
            }

            var accessor = attach.Operand as MethodReference;

            if (accessor == null || !IsAttaching(accessor.Name))
            {
                return null;
            }

            subscription.Channel = IlReading.Receiver(accessor, attach, block.First);
            subscription.ChannelType = accessor.DeclaringType?.FullName;
            subscription.Member = accessor.Name.StartsWith("add_", System.StringComparison.Ordinal)
                ? accessor.Name.Substring(4)
                : accessor.Name;

            return subscription;
        }

        /// <summary>
        /// The instruction that takes delivery of the delegate.
        /// </summary>
        /// <remarks>
        /// Between taking a method's address and handing it over there is only ever wrapping: the
        /// delegate is constructed, sometimes combined with what was already there, sometimes cast
        /// back to its own type. Anything else means this value went somewhere this cannot follow,
        /// and following it wrongly would name a channel that is not the one it was attached to.
        /// </remarks>
        private static Instruction Attachment(Instruction from, BasicBlock block)
        {
            var at = from.Next;

            for (var step = 0; step < Reach && at != null; step++)
            {
                switch (at.OpCode.Code)
                {
                    case Code.Nop:
                    case Code.Newobj:
                    case Code.Castclass:
                        break;

                    case Code.Stfld:
                    case Code.Stsfld:
                        return at;

                    case Code.Call:
                    case Code.Callvirt:
                        // Delegate.Combine sits in the middle of a += on a field; anything else is
                        // the thing being subscribed to.
                        if (!IsCombining(at.Operand as MethodReference))
                        {
                            return at;
                        }

                        break;

                    default:
                        return null;
                }

                if (at == block.Last)
                {
                    return null;
                }

                at = at.Next;
            }

            return null;
        }

        private static MethodDefinition Resolve(MethodReference reference)
        {
            try
            {
                return reference?.Resolve();
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static bool IsCombining(MethodReference method)
        {
            return method != null &&
                   method.DeclaringType?.FullName == "System.Delegate" &&
                   (method.Name == "Combine" || method.Name == "Remove");
        }

        private static bool IsAttaching(string name)
        {
            return name != null &&
                   (name.StartsWith("add_", System.StringComparison.Ordinal) || name == "AddListener");
        }
    }
}
