using global::UnityEngine;

namespace Artel
{
    /// <summary>
    /// Calls the <c>OnMouse*</c> handlers the engine would call, for the agent's pointer instead of
    /// the real one.
    /// </summary>
    /// <remarks>
    /// These are not EventSystem events and no amount of input mocking reaches them: the engine
    /// picks a collider from the OS cursor every frame and invokes the handler itself, and the
    /// legacy input backend takes no injected values. A game built on <c>OnMouseDown</c> — which
    /// most 2D Unity games are — is otherwise entirely unreachable.
    /// <para>
    /// The handlers are private by convention, so they are reached the way the engine reaches them:
    /// by name, on every component of the object.
    /// </para>
    /// </remarks>
    internal sealed class VirtualMouseMessenger
    {
        /// <summary>The engine sends these for the left button only, so this follows.</summary>
        private const int DrivingButton = 0;

        private const string MouseEnter = "OnMouseEnter";
        private const string MouseOver = "OnMouseOver";
        private const string MouseExit = "OnMouseExit";
        private const string MouseDown = "OnMouseDown";
        private const string MouseDrag = "OnMouseDrag";
        private const string MouseUp = "OnMouseUp";
        private const string MouseUpAsButton = "OnMouseUpAsButton";

        private readonly RaycastHit[] spatialHits = new RaycastHit[8];

        private GameObject hovered;
        private GameObject pressed;

        /// <summary>
        /// 마지막 누름이 무엇에 닿았나. 닿은 것이 없으면 빈 문자열이다.
        /// </summary>
        /// <remarks>
        /// 액션 결과가 "상태를 밀었다"가 아니라 "무엇이 받았다"를 말할 수 있게 하려고 남긴다.
        /// 그 둘이 구분되지 않아, 겨냥이 빗나간 것과 게임이 입력을 막고 있는 것과 사람이
        /// 포인터를 도로 가져간 것이 모두 같은 `ok` 로 보였다(ARTEL-769).
        /// <para>
        /// 새로 계산하는 값이 아니다. <see cref="Pick"/> 이 이미 고른 것을 안 버리는 것뿐이다.
        /// </para>
        /// </remarks>
        internal string LastPressReceiver { get; private set; } = string.Empty;

        /// <summary>
        /// 누름이 메신저를 지나갔나. <see cref="LastPressReceiver"/> 가 비어 있을 때, 닿은 것이
        /// 없었던 것인지 메신저가 아예 안 돌았던 것인지를 가른다 — 사람이 포인터를 도로 가져가면
        /// <c>AdvanceFrame</c> 이 <see cref="Tick"/> 대신 <see cref="Clear"/> 를 부른다.
        /// </summary>
        internal bool SawPress { get; private set; }

        /// <summary>
        /// One tick of what the engine does every frame: work out what the pointer is over, tell it
        /// so, and keep telling whatever is being dragged.
        /// </summary>
        public void Tick(Vector2 screenPosition, bool buttonHeld)
        {
            var target = Pick(screenPosition);
            UpdateHover(target);

            if (pressed != null)
            {
                if (buttonHeld)
                {
                    // The engine keeps sending this to the object the press started on, even after
                    // the pointer has left it. That is what makes dragging past the edge work.
                    Send(pressed, MouseDrag);
                }
                else
                {
                    Release(target);
                }

                return;
            }

            if (buttonHeld)
            {
                SawPress = true;
                LastPressReceiver = target == null ? string.Empty : Path(target);

                if (target != null)
                {
                    pressed = target;
                    Send(pressed, MouseDown);
                }
            }
        }

        /// <summary>
        /// Ends a press without a release of its own. The connection dropping mid-drag has to look
        /// to the game like the button coming up, or its handler waits forever.
        /// </summary>
        public void Clear()
        {
            if (pressed != null)
            {
                Release(null);
            }

            UpdateHover(null);
        }

        private void Release(GameObject target)
        {
            var wasPressed = pressed;
            pressed = null;

            Send(wasPressed, MouseUp);
            if (wasPressed != null && wasPressed == target)
            {
                Send(wasPressed, MouseUpAsButton);
            }
        }

        private void UpdateHover(GameObject target)
        {
            if (hovered != target)
            {
                Send(hovered, MouseExit);
                hovered = target;
                Send(hovered, MouseEnter);
            }

            // Every frame it stays there, not once on arrival.
            Send(hovered, MouseOver);
        }

        /// <summary>
        /// The one object the engine would deliver to: the nearest hit along a ray from the camera,
        /// 2D and 3D compared on the same distance, filtered by <see cref="Camera.eventMask"/>.
        /// </summary>
        /// <remarks>
        /// A ray rather than a 2D overlap test, even though an overlap would find sprites a ray can
        /// miss. Matching the engine matters more than reaching more: something the engine cannot
        /// pick is something a person cannot click, and an agent that clicks it anyway reports a
        /// game working when it does not.
        /// <para>
        /// One target, not everything under the pointer — the engine picks a single hit and sends
        /// to it, which is why a game with overlapping sprites at the same depth resolves the
        /// ambiguity itself. Only <c>Camera.main</c> is consulted; the engine walks every camera,
        /// so a scene that renders interactive objects through a second one is not covered.
        /// </para>
        /// </remarks>
        private GameObject Pick(Vector2 screenPosition)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return null;
            }

            var ray = camera.ScreenPointToRay(screenPosition);
            var flat = Physics2D.GetRayIntersection(ray, camera.farClipPlane, camera.eventMask);

            var hitCount = Physics.RaycastNonAlloc(
                ray, spatialHits, camera.farClipPlane, camera.eventMask);

            var closest = flat.collider == null ? float.MaxValue : flat.distance;
            var nearest = flat.collider == null ? null : flat.collider.gameObject;
            for (var index = 0; index < hitCount; index++)
            {
                if (spatialHits[index].distance < closest)
                {
                    closest = spatialHits[index].distance;
                    nearest = spatialHits[index].collider.gameObject;
                }
            }

            return nearest;
        }

        /// <summary>
        /// The null check is Unity's, so an object destroyed while the pointer was on it is simply
        /// not told anything.
        /// </summary>
        /// <summary>읽는 사람이 pulse 에서 본 이름과 맞출 수 있도록 계층 경로로 적는다.</summary>
        private static string Path(GameObject target)
        {
            var name = target.name;
            for (var parent = target.transform.parent; parent != null; parent = parent.parent)
            {
                name = parent.name + "/" + name;
            }

            return name;
        }

        /// <summary>다음 누름을 재기 전에 지난 것을 지운다.</summary>
        internal void ForgetLastPress()
        {
            LastPressReceiver = string.Empty;
            SawPress = false;
        }

        private static void Send(GameObject target, string message)
        {
            if (target != null)
            {
                target.SendMessage(message, SendMessageOptions.DontRequireReceiver);
            }
        }
    }
}
