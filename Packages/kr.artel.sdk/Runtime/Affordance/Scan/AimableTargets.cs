using System.Collections.Generic;
using UnityEngine;

namespace Artel.Affordances.Scan
{
    /// <summary>One thing on screen an agent could send a pointer at.</summary>
    public sealed class AimableTarget
    {
        /// <summary>The instance id, which is what an action names when it aims.</summary>
        public int Id;

        /// <summary>Where it is in the hierarchy, and which one it is when several share that.</summary>
        public string Path;

        public string Selector;
        public string Scene;

        /// <summary><c>button</c> · <c>editText</c> · <c>text</c> · <c>image</c> · <c>sprite</c> · <c>block</c>.</summary>
        public string Kind;

        public bool Active;
        public bool Interactable;
        public Rect Area;

        /// <summary>
        /// The object itself, so that acting on it does not have to find it again.
        /// </summary>
        /// <remarks>
        /// An id crosses the wire; a click does not. Whoever is asked to press this needs the live
        /// component, and the only cheap moment to have hold of it is while the walk is standing on
        /// it. Looking one up afterwards from an id means searching the scene for it.
        /// </remarks>
        public GameObject Subject;
    }

    /// <summary>
    /// What the last walk found that could be acted on, kept so that acting does not walk again.
    /// </summary>
    /// <remarks>
    /// The report and this are two answers from one walk. Every transform is already enumerated —
    /// the report's rule only decides what gets written — so asking a second question of each object
    /// costs the question and not another traversal.
    ///
    /// Nobody reads this yet. It is filled here so that the issue which moves the action channel
    /// over carries only the change that moves it, and can be reverted on its own.
    /// </remarks>
    public static class AimableTargets
    {
        private static readonly Dictionary<int, AimableTarget> ById = new Dictionary<int, AimableTarget>();
        private static readonly List<AimableTarget> Found = new List<AimableTarget>();

        public static IReadOnlyList<AimableTarget> All => Found;

        public static bool TryGet(int id, out AimableTarget target)
        {
            return ById.TryGetValue(id, out target);
        }

        /// <summary>
        /// Drops what a previous walk of this scene found, so a rescan replaces rather than doubles.
        /// </summary>
        /// <remarks>
        /// Per scene rather than wholesale: a game with two scenes open should not lose the first
        /// one's targets because the second was rescanned. Objects that outlive their scene are
        /// gathered under a name of their own and cleared with it.
        /// </remarks>
        public static void Forget(string scene)
        {
            for (var index = Found.Count - 1; index >= 0; index--)
            {
                if (Found[index].Scene != scene)
                {
                    continue;
                }

                ById.Remove(Found[index].Id);
                Found.RemoveAt(index);
            }
        }

        public static void ForgetEverything()
        {
            ById.Clear();
            Found.Clear();
        }

        internal static void Keep(AimableTarget target)
        {
            if (target == null)
            {
                return;
            }

            // A rescan reaching the same object replaces what was said about it. Its area and
            // whether it can be pressed are both things that change while a game runs.
            if (ById.ContainsKey(target.Id))
            {
                Found.RemoveAll(existing => existing.Id == target.Id);
            }

            ById[target.Id] = target;
            Found.Add(target);
        }
    }
}
