using System.Text;
using UnityEngine;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// Names an object by where it sits.
    /// </summary>
    /// <remarks>
    /// The identity a specification can act on. An instance id means nothing across a restart and a
    /// bare name is rarely unique, but a path down the hierarchy is what a person reads in the
    /// editor and what a test executor can look up again.
    /// </remarks>
    internal static class ScenePath
    {
        /// <summary>How deep a hierarchy is followed before the path is left partial.</summary>
        private const int MaxDepth = 64;

        internal static string Of(Transform transform)
        {
            return Of(transform, -1);
        }

        /// <summary>
        /// The same walk, with each step saying which of its parent's children it is.
        /// </summary>
        /// <remarks>
        /// A name is not an identity when a game spawns things. Five enemies of one kind are five
        /// objects at one path — <c>TurnBattleScene/RangedCat(Clone)</c> five times over in the
        /// sample game — and a test told to click that has been told nothing. The place among its
        /// siblings is what tells them apart, and it is a thing the executor can count for itself.
        ///
        /// Written beside the plain path rather than instead of it. The plain one is what a person
        /// reads and what the rest of the report already joins on; this one is for whoever has to
        /// pick one of five.
        ///
        /// It says where a thing was, not which thing it is. The order children sit in is fixed for
        /// objects the scene was authored with; for ones the game made, it is the order they were
        /// made in, which holds for as long as that run does. Nothing here claims more.
        /// </remarks>
        internal static string SelectorOf(Transform transform, int rootIndex)
        {
            return Of(transform, rootIndex);
        }

        private static string Of(Transform transform, int rootIndex = -1)
        {
            if (transform == null)
            {
                return null;
            }

            var numbered = rootIndex >= 0;
            var parts = new string[MaxDepth];
            var count = 0;
            var current = transform;

            while (current != null && count < MaxDepth)
            {
                parts[count++] = numbered
                    ? current.name + "[" + current.GetSiblingIndex() + "]"
                    : current.name;

                current = current.parent;
            }

            // A root's place among the scene's roots is not its sibling index — Unity answers that
            // one with zero however many roots there are, which is why five spawned enemies were
            // all `[0]`. The walk is what knows the order, so it says.
            if (numbered && current == null && count > 0)
            {
                parts[count - 1] = transform.root.name + "[" + rootIndex + "]";
            }

            var path = new StringBuilder();

            // A hierarchy deeper than the bound, or one a broken prefab has made circular, is said
            // to be cut rather than reported as a root-level object it is not.
            if (current != null)
            {
                path.Append(".../");
            }

            for (var index = count - 1; index >= 0; index--)
            {
                path.Append(parts[index]);

                if (index > 0)
                {
                    path.Append('/');
                }
            }

            return path.ToString();
        }
    }
}
