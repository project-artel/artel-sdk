using UnityEngine;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// The area an object covers on screen, in the numbers an agent aims with.
    /// </summary>
    /// <remarks>
    /// Pixels from the top left, which is not the space Unity works in. The engine measures from the
    /// bottom left and the action protocol from the top, and the flip has to happen on one side of
    /// the wire or the other. It happens here, so that a caller can take the numbers a scan reported
    /// and send them straight back as somewhere to point.
    ///
    /// The SDK has its own reader of the same rule. Sharing it would mean this assembly referencing
    /// the SDK runtime, and the SDK runtime is about to reference this one — the scan has to be
    /// startable when a connection opens. One of the two directions has to be a copy, and the copy
    /// belongs on the side that must keep working with no SDK at all.
    /// </remarks>
    internal static class ScreenArea
    {
        private static readonly Vector3[] Corners = new Vector3[4];

        private static Camera _camera;

        /// <summary>Resolves the camera once for a whole scan rather than once per object.</summary>
        /// <remarks>
        /// <c>Camera.main</c> searches by tag, which is a scene-wide lookup. Paid per object it
        /// would dominate the walk on a scene of any size.
        /// </remarks>
        internal static void Begin()
        {
            _camera = Camera.main;
        }

        internal static void Forget()
        {
            _camera = null;
        }

        /// <summary>Where this is on screen, or a zero-sized area when it is nowhere.</summary>
        internal static Rect Of(Transform subject)
        {
            if (subject == null)
            {
                return new Rect(0f, 0f, 0f, 0f);
            }

            if (subject is RectTransform rect)
            {
                return FromCorners(rect);
            }

            // A sprite is not a RectTransform, and a plain transform is a point with no extent.
            // Reading the renderer's own bounds is what makes such a thing aimable at all — without
            // it every world object would report a zero-width area in the middle of itself.
            var renderer = subject.GetComponent<Renderer>();

            return renderer == null ? AtPoint(subject.position) : FromBounds(renderer.bounds);
        }

        private static Rect FromCorners(RectTransform subject)
        {
            subject.GetWorldCorners(Corners);

            var canvas = subject.GetComponentInParent<Canvas>();

            // A canvas drawn in screen space has no camera between it and the screen; asking one to
            // project its corners moves them somewhere the player never sees.
            var through = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : _camera;

            var first = Project(Corners[0], through);
            var min = first;
            var max = first;

            for (var index = 1; index < 4; index++)
            {
                var point = Project(Corners[index], through);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            return Between(min, max);
        }

        private static Rect FromBounds(Bounds bounds)
        {
            var min = Vector2.zero;
            var max = Vector2.zero;

            for (var index = 0; index < 8; index++)
            {
                var corner = new Vector3(
                    (index & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (index & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (index & 4) == 0 ? bounds.min.z : bounds.max.z);

                var point = Project(corner, _camera);

                if (index == 0)
                {
                    min = point;
                    max = point;
                    continue;
                }

                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            return Between(min, max);
        }

        private static Rect AtPoint(Vector3 world)
        {
            var point = Project(world, _camera);

            return new Rect(point.x, point.y, 0f, 0f);
        }

        /// <summary>World to screen, already flipped to measure down from the top.</summary>
        private static Vector2 Project(Vector3 world, Camera through)
        {
            var point = through == null
                ? new Vector3(world.x, world.y, 0f)
                : through.WorldToScreenPoint(world);

            return new Vector2(point.x, Screen.height - point.y);
        }

        private static Rect Between(Vector2 min, Vector2 max)
        {
            return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
        }
    }
}
