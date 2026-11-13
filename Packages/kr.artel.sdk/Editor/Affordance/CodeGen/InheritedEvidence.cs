using System.Collections.Generic;
using Mono.Cecil;

namespace Artel.Affordances.CodeGen
{
    /// <summary>
    /// 부모 class 에 선언된 진입점의 기록을, 그것을 물려받고 다시 선언하지 않은 자식 type 에도 싣는다(ARTEL-1228).
    /// </summary>
    /// <remarks>
    /// 기록은 <c>owner</c> 로 scene 의 컴포넌트에 이어진다. 그런데 scene 에 놓이는 것은 언제나 runtime type 이고, 부모의
    /// <c>Start</c> 를 그대로 쓰는 자식 컴포넌트는 부모 type 이름으로는 어디에도 놓이지 않는다. 그래서 부모 type 의
    /// 기록은 부모 type 이 직접 놓인 scene 에만 가고, 자식이 놓인 scene 에는 오지 않았다.
    ///
    /// 자식 type 이 같은 이름과 같은 parameter 로 메서드를 다시 선언했으면 싣지 않는다. Unity 의 메시지도 override 도
    /// 자식 쪽 선언이 불리기 때문이다. 부모와 자식 사이의 class 가 다시 선언해도 마찬가지다.
    ///
    /// 같은 assembly 안의 class 만 본다. 다른 assembly 의 부모는 그쪽 분석이 그쪽 type 에 굽고, 여기서는 그 기록을 다시
    /// 쓸 수 없다.
    /// </remarks>
    internal static class InheritedEvidence
    {
        private const int MaxDepth = 16;

        /// <summary>기록마다, 그 기록을 함께 실을 자식 type 들. 없으면 목록에 없다.</summary>
        internal static Dictionary<Variant, List<TypeDefinition>> Heirs(ModuleDefinition module, List<Variant> variants)
        {
            var heirs = new Dictionary<Variant, List<TypeDefinition>>();
            var descendants = Descendants(module);

            if (descendants.Count == 0)
            {
                return heirs;
            }

            var entries = new Dictionary<string, MethodDefinition>(System.StringComparer.Ordinal);

            foreach (var variant in variants)
            {
                if (variant.Owner == null || variant.Owner.Module != module ||
                    !descendants.TryGetValue(variant.Owner, out var below))
                {
                    continue;
                }

                var entry = EntryOf(module, variant.EntryId, entries);

                // 진입점이 그 owner 자신의 메서드일 때만이다. 다른 type 에서 출발해 부모의 메서드에 닿은 기록은 출발한
                // 객체에 속하고, 자식이 물려받는 동작이 아니다.
                if (entry == null || entry.DeclaringType != variant.Owner)
                {
                    continue;
                }

                foreach (var heir in below)
                {
                    if (Redeclares(heir, variant.Owner, entry))
                    {
                        continue;
                    }

                    if (!heirs.TryGetValue(variant, out var list))
                    {
                        list = new List<TypeDefinition>();
                        heirs[variant] = list;
                    }

                    list.Add(heir);
                }
            }

            return heirs;
        }

        /// <summary>같은 module 안의 behaviour class 마다, 그것을 상속하는 abstract 가 아닌 behaviour 들.</summary>
        private static Dictionary<TypeDefinition, List<TypeDefinition>> Descendants(ModuleDefinition module)
        {
            var found = new Dictionary<TypeDefinition, List<TypeDefinition>>();

            foreach (var type in module.GetTypes())
            {
                if (type.IsAbstract || AnalysisScope.Inspect(type) != TypeVerdict.Behaviour)
                {
                    continue;
                }

                var current = BaseOf(type, module);

                for (var depth = 0; depth < MaxDepth && current != null; depth++)
                {
                    if (!found.TryGetValue(current, out var list))
                    {
                        list = new List<TypeDefinition>();
                        found[current] = list;
                    }

                    list.Add(type);
                    current = BaseOf(current, module);
                }
            }

            return found;
        }

        /// <summary>같은 module 안의 부모 class, 또는 없음.</summary>
        internal static TypeDefinition BaseOf(TypeDefinition type, ModuleDefinition module)
        {
            var reference = type?.BaseType;

            if (reference == null)
            {
                return null;
            }

            TypeDefinition resolved;

            try
            {
                resolved = reference.Resolve();
            }
            catch (System.Exception)
            {
                return null;
            }

            return resolved != null && resolved.Module == module ? resolved : null;
        }

        /// <summary><paramref name="heir"/> 가 <paramref name="ancestor"/> 를 상속하면 참.</summary>
        internal static bool Derives(TypeDefinition heir, TypeDefinition ancestor)
        {
            if (heir == null || ancestor == null || heir == ancestor)
            {
                return false;
            }

            var current = BaseOf(heir, heir.Module);

            for (var depth = 0; depth < MaxDepth && current != null; depth++)
            {
                if (current == ancestor)
                {
                    return true;
                }

                current = BaseOf(current, heir.Module);
            }

            return false;
        }

        private static MethodDefinition EntryOf(
            ModuleDefinition module, string entryId, Dictionary<string, MethodDefinition> known)
        {
            if (string.IsNullOrEmpty(entryId))
            {
                return null;
            }

            if (known.TryGetValue(entryId, out var cached))
            {
                return cached;
            }

            MethodDefinition found = null;
            var parts = entryId.Split('|');
            var type = parts.Length > 2 ? module.GetType(parts[1]) : null;

            if (type != null)
            {
                foreach (var method in type.Methods)
                {
                    if (MethodIdentity.Of(method) == entryId)
                    {
                        found = method;
                        break;
                    }
                }
            }

            known[entryId] = found;
            return found;
        }

        /// <summary><paramref name="heir"/> 부터 <paramref name="ancestor"/> 바로 아래까지 중 누가 같은 메서드를 다시 선언했나.</summary>
        private static bool Redeclares(TypeDefinition heir, TypeDefinition ancestor, MethodDefinition entry)
        {
            var current = heir;

            for (var depth = 0; depth < MaxDepth && current != null && current != ancestor; depth++)
            {
                foreach (var method in current.Methods)
                {
                    if (method.Name == entry.Name && SameParameters(method, entry))
                    {
                        return true;
                    }
                }

                current = BaseOf(current, heir.Module);
            }

            return false;
        }

        private static bool SameParameters(MethodDefinition left, MethodDefinition right)
        {
            if (left.Parameters.Count != right.Parameters.Count)
            {
                return false;
            }

            for (var index = 0; index < left.Parameters.Count; index++)
            {
                if (left.Parameters[index].ParameterType.FullName != right.Parameters[index].ParameterType.FullName)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
