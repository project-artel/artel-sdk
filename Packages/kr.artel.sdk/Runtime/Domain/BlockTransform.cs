using UnityEngine;

namespace Artel.Domain
{
    /// <summary>
    /// Where a block sits, in the two frames a reader needs.
    /// </summary>
    /// <remarks>
    /// <see cref="World"/> is the object's own position and does not move when the camera does.
    /// <see cref="ScreenRect"/> is that position projected onto what the player is looking at.
    /// Neither covers the other: a UI element under a ScreenSpaceOverlay canvas has a world
    /// position measured in screen pixels, and a world-space object off the side of the frame
    /// still has a real position.
    /// </remarks>
    public readonly struct BlockTransform
    {
        public Vector3 World { get; }

        /// <summary>
        /// The area the block covers on screen, in pixels, with the origin at the top left and y
        /// growing downwards.
        /// </summary>
        /// <remarks>
        /// Measured against <see cref="SceneSnapshot.Screen"/>, which is not necessarily the size
        /// of the video frame a reader is looking at — the stream downscales. Mapping one to the
        /// other is a matter of scaling by the ratio between them.
        /// </remarks>
        public Rect ScreenRect { get; }

        /// <summary>
        /// Whether <see cref="ScreenRect"/> can be believed: the block projects in front of the
        /// camera and lands somewhere inside the frame.
        /// </summary>
        /// <remarks>
        /// This is not the same as being visible. A block clipped away by a RectMask2D, hidden
        /// behind another object, or drawn by a fully transparent CanvasGroup still reports true.
        /// <see cref="Visible"/> answers that question.
        /// </remarks>
        public bool OnScreen { get; }

        /// <summary>
        /// Whether a player looking at the frame could see the block (ARTEL-1225).
        /// </summary>
        /// <remarks>
        /// <see cref="OnScreen"/> 가 true 이고, 아래가 모두 아닐 때 true 다. 판정 규칙은 <c>BlockVisibility</c> 에 있다.
        /// <list type="bullet">
        /// <item>GameObject 가 꺼져 있음</item>
        /// <item>자기 <c>Renderer</c> 가 꺼져 있거나 <c>SpriteRenderer.color.a</c> 가 0</item>
        /// <item>자기 <c>Graphic</c> 이 꺼져 있거나 <c>Graphic.color.a</c> 가 0</item>
        /// <item>위쪽 <c>CanvasGroup.alpha</c> 의 곱이 0, 또는 위쪽 <c>Canvas</c> 가 꺼져 있음</item>
        /// <item>위쪽 <c>RectMask2D</c>·<c>Mask</c> 의 영역과 겹치지 않음</item>
        /// <item>조작할 수 있는 블록인데 중심점의 <c>EventSystem.RaycastAll</c> 맨 위가 자기나 자기 자식이 아님</item>
        /// </list>
        /// </remarks>
        public bool Visible { get; }

        public BlockTransform(Vector3 world, Rect screenRect, bool onScreen)
            : this(world, screenRect, onScreen, onScreen)
        {
        }

        public BlockTransform(Vector3 world, Rect screenRect, bool onScreen, bool visible)
        {
            World = world;
            ScreenRect = screenRect;
            OnScreen = onScreen;
            Visible = onScreen && visible;
        }

        internal BlockTransform WithVisible(bool visible)
        {
            return new BlockTransform(World, ScreenRect, OnScreen, visible);
        }
    }
}
