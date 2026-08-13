using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Artel.Affordances.Scan
{
    /// <summary>What a component's inspector fields point at.</summary>
    internal struct Reference
    {
        internal string Field;
        internal string Type;
        internal string Name;

        /// <summary>
        /// What makes two fields on two components the same object.
        /// </summary>
        /// <remarks>
        /// The number itself means nothing outside the run that produced it, which is exactly what
        /// is wanted: it is a join key, not an identity to keep. Two behaviours holding the same
        /// event channel asset carry the same one here, and that is the only place in the whole
        /// report where that fact exists — the code says a channel of some type, the scene says
        /// which asset, and neither says it alone.
        /// </remarks>
        internal int Id;

        /// <summary>Where in the scene, when it is something in one.</summary>
        internal string Path;

        /// <summary>
        /// True when this is not in any scene — a prefab, or an asset.
        /// </summary>
        /// <remarks>
        /// It has to be said because the two used to look identical. A prefab's root transform has
        /// no parent, so the path built for it was its own name, which is exactly what a scene root
        /// object's path looks like: <c>CardManager.cardPrefab -&gt; "Card"</c> and
        /// <c>MapMove.character -&gt; "wordHead"</c> were the same shape and only one of them was
        /// somewhere a test could go.
        /// </remarks>
        internal bool Asset;

        /// <summary>
        /// The component types a referenced prefab carries.
        /// </summary>
        /// <remarks>
        /// This is the answer to "who makes this". A type that only ever exists on a prefab is
        /// missing from the report until something instantiates it, and the report could not say
        /// whether that was because nobody ever does — dead code — or because the run had not got
        /// there yet. A prefab held in an inspector field by a component that *is* in a scene is
        /// the second case, and this is where that shows.
        /// </remarks>
        internal List<string> Carries;

        /// <summary>
        /// The object itself, for following it further. Never written to the report.
        /// </summary>
        /// <remarks>
        /// The report gets names and a join key; this is the live reference, and it exists only so
        /// that a prefab held two steps away can be found. Kept off the written form deliberately —
        /// nothing outside this run could use it.
        /// </remarks>
        internal UnityEngine.Object Held;
    }

    /// <summary>
    /// Reads the object references Unity serialized on a component.
    /// </summary>
    /// <remarks>
    /// The analysis reads code and the scan reads hierarchy, and an inspector reference is the one
    /// fact that belongs to neither. <c>_teleportChannel.RaiseEvent()</c> is in the code without
    /// saying which channel, and the asset is in the scene without saying what happens when it is
    /// raised. Measured on Chop Chop, 23 channel types had both a publisher and a subscriber in the
    /// evidence and not one of them could be paired to an actual asset.
    ///
    /// Only references are read, not values. A field's number or string is the game's own data and
    /// carries no wiring; reading it would make the report a dump of the game's content, cost the
    /// size of one, and say nothing about what a player can do.
    /// </remarks>
    internal static class SerializedReferences
    {
        private const int MaxReferencesPerComponent = 32;
        private const int MaxElementsPerCollection = 16;

        /// <summary>How many distinct component types are read off one prefab.</summary>
        private const int MaxCarriedTypes = 16;

        /// <summary>What each prefab carries, worked out once however many fields point at it.</summary>
        private static readonly Dictionary<int, List<string>> CarriedByPrefab =
            new Dictionary<int, List<string>>();

        private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public |
                                              BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly Dictionary<Type, FieldInfo[]> FieldsByType =
            new Dictionary<Type, FieldInfo[]>();

        internal static void Read(Component component, List<Reference> found)
        {
            ReadInto(component, found);
        }

        private static void ReadInto(UnityEngine.Object holder, List<Reference> found)
        {
            if (holder == null)
            {
                return;
            }

            foreach (var field in FieldsOf(holder.GetType()))
            {
                if (found.Count >= MaxReferencesPerComponent)
                {
                    return;
                }

                object value;

                try
                {
                    value = field.GetValue(holder);
                }
                catch (Exception)
                {
                    // A field whose type failed to load. One unreadable field is not a reason to
                    // stop reading the component.
                    continue;
                }

                if (value is UnityEngine.Object single)
                {
                    Add(found, field.Name, single);
                    continue;
                }

                if (value is IEnumerable many && !(value is string))
                {
                    var taken = 0;

                    foreach (var element in many)
                    {
                        if (taken >= MaxElementsPerCollection || found.Count >= MaxReferencesPerComponent)
                        {
                            break;
                        }

                        if (element is UnityEngine.Object member)
                        {
                            Add(found, field.Name, member);
                            taken++;
                        }
                    }
                }
            }
        }

        private static void Add(List<Reference> found, string field, UnityEngine.Object value)
        {
            // Unity's destroyed objects compare equal to null while still being a live reference.
            // An empty slot in the inspector arrives here the same way, and neither points at
            // anything a test could act on.
            if (value == null)
            {
                return;
            }

            var reference = new Reference
            {
                Field = field,
                Type = value.GetType().FullName,
                Name = value.name,
                Id = value.GetInstanceID(),
                Held = value
            };

            var subject = value as GameObject ?? (value as Component)?.gameObject;

            if (subject == null)
            {
                // A sprite, a clip, a ScriptableObject. Never in a scene and carries nothing that
                // can be walked to.
                reference.Asset = true;
                found.Add(reference);
                return;
            }

            if (subject.scene.IsValid())
            {
                reference.Path = ScenePath.Of(subject.transform);
            }
            else
            {
                // No scene means a prefab. Its path is not written at all: the string that could be
                // built for it is indistinguishable from a scene root's, which is worse than none.
                reference.Asset = true;
                reference.Carries = CarriedBy(subject);
            }

            found.Add(reference);
        }

        /// <summary>
        /// Follows an asset one or two steps to find what it would ultimately put in a scene.
        /// </summary>
        /// <remarks>
        /// A prefab is often not held directly. The sample game's enemies live in a
        /// <c>ScriptableObject</c> that a pool component points at, so the chain is
        /// <c>EnemyPoolController.enemyDataContainer → EnemyData.prefab → Enemy</c> and reading only
        /// the component's own fields finds none of it — which left the report unable to tell those
        /// enemies from dead code.
        ///
        /// Attributed to the field in the scene, not to the link in the middle. That field is the
        /// one a person or an agent can actually follow, and naming the intermediate asset would be
        /// telling them about a step they cannot take.
        ///
        /// Two steps and sixty-four objects, whichever comes first. A ScriptableObject can hold a
        /// graph, and the point here is finding prefabs rather than walking the game's content.
        /// </remarks>
        internal static void Trace(UnityEngine.Object from, string ownerType, string field)
        {
            var seen = new HashSet<int>();
            Follow(from, ownerType, field, 0, seen);
        }

        private const int MaxTraceDepth = 2;
        private const int MaxTraced = 64;

        private static void Follow(
            UnityEngine.Object value, string ownerType, string field, int depth, HashSet<int> seen)
        {
            if (value == null || depth > MaxTraceDepth || seen.Count >= MaxTraced ||
                !seen.Add(value.GetInstanceID()))
            {
                return;
            }

            var subject = value as GameObject ?? (value as Component)?.gameObject;

            if (subject != null)
            {
                if (subject.scene.IsValid())
                {
                    // Already in a scene, so it is not something that has to be made.
                    return;
                }

                foreach (var carried in CarriedBy(subject))
                {
                    AffordanceReport.Creates(carried, ownerType, field);
                }

                // A prefab's own components can hold further prefabs — a pool holding what it spawns.
                foreach (var component in Components(subject))
                {
                    Onward(component, ownerType, field, depth, seen);
                }

                return;
            }

            // A ScriptableObject or any other asset. Its fields are read the same way a component's
            // are, because that is where a prefab held indirectly is kept.
            Onward(value, ownerType, field, depth, seen);
        }

        private static void Onward(
            UnityEngine.Object holder, string ownerType, string field, int depth, HashSet<int> seen)
        {
            var further = new List<UnityEngine.Object>();

            try
            {
                foreach (var slot in FieldsOf(holder.GetType()))
                {
                    Gather(slot.GetValue(holder), further, 0);
                }
            }
            catch (Exception)
            {
                return;
            }

            foreach (var reference in further)
            {
                Follow(reference, ownerType, field, depth + 1, seen);
            }
        }

        /// <summary>
        /// Every object reference inside a value, however deeply the game nested it.
        /// </summary>
        /// <remarks>
        /// Written because the emitted references are not enough to answer &quot;who makes this&quot;.
        /// The sample game keeps its enemy prefabs in a <c>List&lt;EnemyData&gt;</c> where
        /// <c>EnemyData</c> is a plain serializable struct — the list holds structs, not objects, so
        /// reading only the fields that are objects found nothing and five live enemy types read as
        /// dead code.
        ///
        /// This walk does not reach the report. It exists to register who would make a type, and
        /// stopping at a serializable struct would be stopping one step short of the answer.
        /// </remarks>
        private static void Gather(object value, List<UnityEngine.Object> into, int depth)
        {
            if (value == null || depth > MaxNesting || into.Count >= MaxTraced)
            {
                return;
            }

            if (value is UnityEngine.Object held)
            {
                if (held != null)
                {
                    into.Add(held);
                }

                return;
            }

            if (value is string)
            {
                return;
            }

            if (value is IEnumerable many)
            {
                foreach (var element in many)
                {
                    Gather(element, into, depth + 1);
                }

                return;
            }

            var type = value.GetType();

            if (type.IsPrimitive || type.IsEnum)
            {
                return;
            }

            var space = type.Namespace;

            if (space != null &&
                (space == "UnityEngine" || space.StartsWith("UnityEngine.", StringComparison.Ordinal) ||
                 space == "System" || space.StartsWith("System.", StringComparison.Ordinal)))
            {
                return;
            }

            try
            {
                foreach (var slot in FieldsOf(type))
                {
                    Gather(slot.GetValue(value), into, depth + 1);
                }
            }
            catch (Exception)
            {
                // A field that will not read. The rest of the value is still worth walking.
            }
        }

        /// <summary>How deep a serializable value is walked looking for object references.</summary>
        private const int MaxNesting = 4;

        private static Component[] Components(GameObject subject)
        {
            try
            {
                return subject.GetComponentsInChildren<Component>(true);
            }
            catch (Exception)
            {
                return new Component[0];
            }
        }

        /// <summary>
        /// The game's own component types on a prefab, including its children.
        /// </summary>
        /// <remarks>
        /// Children included because a prefab is a tree and the behaviour is as likely to be one
        /// level down — a spell prefab whose animator and collider hang off a child. Engine
        /// components are left out for the same reason their fields are: nobody wrote them.
        /// </remarks>
        private static List<string> CarriedBy(GameObject prefab)
        {
            var id = prefab.GetInstanceID();

            if (CarriedByPrefab.TryGetValue(id, out var already))
            {
                return already;
            }

            var carried = new List<string>();

            try
            {
                foreach (var component in prefab.GetComponentsInChildren<Component>(true))
                {
                    if (component == null || carried.Count >= MaxCarriedTypes)
                    {
                        continue;
                    }

                    var type = component.GetType();
                    var space = type.Namespace;

                    if (space != null &&
                        (space == "UnityEngine" || space.StartsWith("UnityEngine.", StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    // Base classes too. A prefab carrying BossEnemy will, when instantiated, be an
                    // Enemy as well — and Enemy is the type the shared rules are baked onto, so
                    // asking only about the exact component left the base with no known creator and
                    // reading like dead code.
                    for (var current = type; Walkable(current); current = current.BaseType)
                    {
                        var name = current.FullName;

                        if (name != null && !carried.Contains(name) && carried.Count < MaxCarriedTypes)
                        {
                            carried.Add(name);
                        }
                    }
                }
            }
            catch (Exception)
            {
                carried.Clear();
            }

            CarriedByPrefab[id] = carried;
            return carried;
        }

        /// <summary>
        /// The fields Unity would serialize, from the type down to where the engine's own begin.
        /// </summary>
        /// <remarks>
        /// Sorted by name so that two runs over the same game produce the same bytes, and cached
        /// because a scene holds many instances of few types.
        /// </remarks>
        private static FieldInfo[] FieldsOf(Type type)
        {
            if (FieldsByType.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var fields = new List<FieldInfo>();
            var named = new HashSet<string>(StringComparer.Ordinal);

            for (var current = type; Walkable(current); current = current.BaseType)
            {
                foreach (var field in current.GetFields(Declared))
                {
                    // A derived class can shadow a base field of the same name. What the object
                    // presents is the most derived one, which is the one already taken.
                    if (Serialized(field) && named.Add(field.Name))
                    {
                        fields.Add(field);
                    }
                }
            }

            fields.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));

            var answer = fields.ToArray();
            FieldsByType[type] = answer;
            return answer;
        }

        /// <summary>
        /// Stops where the game's own code stops.
        /// </summary>
        /// <remarks>
        /// By namespace rather than by naming the base classes, because <c>Button</c> is as much the
        /// engine's as <c>MonoBehaviour</c> is and its <c>m_TargetGraphic</c> is engine plumbing —
        /// true, and not a wiring anyone wrote. Measured on Chop Chop those fields were a third of
        /// the report on their own.
        ///
        /// A game type that derives from an engine one still has all of its own fields read; the
        /// walk stops when it reaches the engine's part of the chain, which is exactly where the
        /// game stopped writing.
        /// </remarks>
        private static bool Walkable(Type type)
        {
            if (type == null || type == typeof(object))
            {
                return false;
            }

            var space = type.Namespace;

            return space == null ||
                   (space != "UnityEngine" &&
                    !space.StartsWith("UnityEngine.", StringComparison.Ordinal));
        }

        private static bool Serialized(FieldInfo field)
        {
            if (field.IsStatic || field.IsInitOnly || field.IsLiteral || field.IsNotSerialized)
            {
                return false;
            }

            return field.IsPublic || field.GetCustomAttribute<SerializeField>(true) != null;
        }

        internal static void Forget()
        {
            FieldsByType.Clear();
            CarriedByPrefab.Clear();
        }
    }
}
