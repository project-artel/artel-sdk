using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Artel.Tracking
{
    /// <summary>
    /// <c>onScreen</c> 인 블록이 실제로 보이는지 판정한다(ARTEL-1225).
    /// </summary>
    /// <remarks>
    /// <c>onScreen</c> 은 "카메라에 투영되어 frame 안에 떨어진다" 까지만 뜻한다. 여기서는 그 위에 다섯 가지를 더 본다:
    /// 자기 renderer 와 graphic, 위쪽 <c>CanvasGroup</c> 과 <c>Canvas</c>, 위쪽 mask, 그리고 조작할 수 있는 블록에
    /// 한해 다른 것에 가려졌는지.
    ///
    /// 가려짐은 <c>EventSystem.RaycastAll</c> 로 중심점 하나만 쏴서 본다. 블록마다 쏘면 scan 이 블록 수만큼 raycast 를
    /// 하므로 <c>Selectable</c> 이 있는 블록에만 쏜다. raycast 는 <c>raycastTarget</c> 인 graphic 과 collider 만 맞히므로,
    /// 아무것도 맞지 않으면 가려졌다고 하지 않는다. SDK 자신의 overlay(<c>Instrument</c> 가 붙은 것)에 맞은 것은 건너뛴다.
    ///
    /// mask 는 영역의 교차만 본다. <c>Mask</c> 의 stencil 이 그리는 실제 모양(둥근 모서리 같은 것)은 보지 않는다.
    /// </remarks>
    internal sealed class BlockVisibility
    {
        private readonly List<RaycastResult> raycastResults = new List<RaycastResult>();
        private PointerEventData pointer;
        private EventSystem eventSystem;

        public void BeginScan()
        {
            eventSystem = EventSystem.current;
            pointer = eventSystem != null ? new PointerEventData(eventSystem) : null;
        }

        public bool IsVisible(Transform transform, Rect screenRect, Func<RectTransform, Rect?> screenRectOf)
        {
            if (!transform.gameObject.activeInHierarchy)
            {
                return false;
            }

            if (!OwnRendererShows(transform) || !OwnGraphicShows(transform))
            {
                return false;
            }

            if (!AncestorsShow(transform, screenRect, screenRectOf))
            {
                return false;
            }

            return !IsCovered(transform, screenRect);
        }

        private static bool OwnRendererShows(Transform transform)
        {
            var renderer = transform.GetComponent<Renderer>();
            if (renderer == null)
            {
                return true;
            }

            if (!renderer.enabled)
            {
                return false;
            }

            var sprite = renderer as SpriteRenderer;
            return sprite == null || sprite.color.a > 0f;
        }

        private static bool OwnGraphicShows(Transform transform)
        {
            var graphic = transform.GetComponent<Graphic>();
            return graphic == null || (graphic.enabled && graphic.color.a > 0f);
        }

        /// <summary>
        /// 부모를 따라 올라가며 <c>CanvasGroup.alpha</c> 를 곱하고, 꺼진 <c>Canvas</c> 와 겹치지 않는 mask 를 찾는다.
        /// </summary>
        /// <remarks>
        /// <c>CanvasGroup.ignoreParentGroups</c> 를 만나면 그 위의 alpha 는 곱하지 않는다. Unity 가 그 위 group 을 무시하는
        /// 것과 같다. mask 는 자기 자신의 것은 보지 않는다 — mask 는 자식을 자르지 자기를 자르지 않는다.
        /// </remarks>
        private static bool AncestorsShow(Transform transform, Rect screenRect, Func<RectTransform, Rect?> screenRectOf)
        {
            var alpha = 1f;
            var groupsApply = true;

            for (var current = transform; current != null; current = current.parent)
            {
                if (groupsApply)
                {
                    var group = current.GetComponent<CanvasGroup>();
                    if (group != null && group.enabled)
                    {
                        alpha *= group.alpha;
                        if (group.ignoreParentGroups)
                        {
                            groupsApply = false;
                        }
                    }
                }

                var canvas = current.GetComponent<Canvas>();
                if (canvas != null && !canvas.enabled)
                {
                    return false;
                }

                if (current != transform && ClipsAway(current, screenRect, screenRectOf))
                {
                    return false;
                }
            }

            return alpha > 0f;
        }

        private static bool ClipsAway(Transform ancestor, Rect screenRect, Func<RectTransform, Rect?> screenRectOf)
        {
            var rectTransform = ancestor as RectTransform;
            if (rectTransform == null)
            {
                return false;
            }

            var rectMask = ancestor.GetComponent<RectMask2D>();
            var mask = ancestor.GetComponent<Mask>();
            var clips = (rectMask != null && rectMask.enabled) || (mask != null && mask.enabled);
            if (!clips)
            {
                return false;
            }

            var maskRect = screenRectOf(rectTransform);
            return maskRect.HasValue && !Overlaps(maskRect.Value, screenRect);
        }

        private static bool Overlaps(Rect mask, Rect block)
        {
            return OverlapsOnAxis(block.xMin, block.xMax, mask.xMin, mask.xMax) &&
                   OverlapsOnAxis(block.yMin, block.yMax, mask.yMin, mask.yMax);
        }

        /// <summary>
        /// 폭이 0 인 블록(점이나 선)은 경계 위에 있어도 안에 있다고 본다. 폭이 있는 블록은 경계만 닿으면 겹치지 않는다.
        /// </summary>
        private static bool OverlapsOnAxis(float blockMin, float blockMax, float maskMin, float maskMax)
        {
            return blockMin == blockMax
                ? blockMin >= maskMin && blockMin <= maskMax
                : blockMin < maskMax && blockMax > maskMin;
        }

        private bool IsCovered(Transform transform, Rect screenRect)
        {
            if (pointer == null || eventSystem == null)
            {
                return false;
            }

            var selectable = transform.GetComponent<Selectable>();
            if (selectable == null)
            {
                return false;
            }

            // screenRect 는 좌상단 원점이고 EventSystem 은 좌하단 원점이다.
            pointer.position = new Vector2(screenRect.center.x, Screen.height - screenRect.center.y);
            raycastResults.Clear();
            eventSystem.RaycastAll(pointer, raycastResults);
            foreach (var result in raycastResults)
            {
                var hit = result.gameObject;

                // SDK 가 띄운 overlay 와 커서는 게임 화면의 일부가 아니다. 그것에 덮였다고 하면 overlay 가 떠 있는 동안
                // 게임의 모든 버튼이 안 보인다고 보고된다.
                if (hit == null || hit.GetComponentInParent<Artel.Affordances.Scan.Instrument>(true) != null)
                {
                    continue;
                }

                return hit.transform != transform && !hit.transform.IsChildOf(transform);
            }

            return false;
        }
    }
}
