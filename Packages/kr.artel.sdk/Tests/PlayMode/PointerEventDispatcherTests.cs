using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Artel.Tests
{
    public sealed class PointerEventDispatcherTests
    {
        private static readonly Vector2 GrabPoint = new Vector2(100f, 100f);
        private static readonly Vector2 DropPoint = new Vector2(300f, 200f);

        private GameObject eventSystemObject;
        private GameObject canvasObject;
        private PointerFixtureBehaviour source;
        private PointerFixtureBehaviour destination;

        [TearDown]
        public void TearDown()
        {
            if (canvasObject != null)
            {
                Object.DestroyImmediate(canvasObject);
            }

            if (eventSystemObject != null)
            {
                Object.DestroyImmediate(eventSystemObject);
            }
        }

        [UnityTest]
        public IEnumerator PressMoveRelease_DrivesTheWholeDragSequence()
        {
            BuildScene();
            yield return null;

            var dispatcher = new PointerEventDispatcher();
            dispatcher.MoveTo(GrabPoint);
            dispatcher.Press(0);
            dispatcher.MoveTo(new Vector2(200f, 150f));
            dispatcher.MoveTo(DropPoint);
            dispatcher.Release(0);

            Assert.That(
                source.Events,
                // The up comes before the end of the drag, the order Unity's own input module uses.
                Is.EqualTo(new[] { "down", "beginDrag", "drag", "drag", "up", "endDrag" }));
            Assert.That(destination.Events, Is.EqualTo(new[] { "drop" }));
            Assert.That(source.DragPositions[1], Is.EqualTo(DropPoint));
        }

        [UnityTest]
        public IEnumerator Release_ClicksWhenThePointerNeverTravelled()
        {
            BuildScene();
            yield return null;

            var dispatcher = new PointerEventDispatcher();
            dispatcher.MoveTo(GrabPoint);
            dispatcher.Press(0);
            dispatcher.Release(0);

            Assert.That(source.Events, Is.EqualTo(new[] { "down", "up", "click" }));
        }

        /// <summary>
        /// uGUI 가 받은 누름의 대상을 남긴다. 이것이 없으면 Canvas 버튼을 눌러도 결과의
        /// <c>reached</c> 가 비어, 받는 쪽이 눌린 버튼을 빈 곳으로 읽는다(ARTEL-1175).
        /// </summary>
        [UnityTest]
        public IEnumerator Press_RemembersWhatTheUiPressReached()
        {
            BuildScene();
            yield return null;

            var dispatcher = new PointerEventDispatcher();
            dispatcher.MoveTo(GrabPoint);
            dispatcher.Press(0);

            Assert.That(dispatcher.LastPressReceiver, Is.EqualTo("canvas/drag source"));

            dispatcher.Release(0);
            dispatcher.ForgetLastPress();
            Assert.That(dispatcher.LastPressReceiver, Is.Empty);
        }

        [UnityTest]
        public IEnumerator Press_OnNothingLeavesTheReceiverEmpty()
        {
            BuildScene();
            yield return null;

            var dispatcher = new PointerEventDispatcher();
            dispatcher.MoveTo(new Vector2(2f, 2f));
            dispatcher.Press(0);

            Assert.That(dispatcher.LastPressReceiver, Is.Empty);
            dispatcher.Release(0);
        }

        [UnityTest]
        public IEnumerator Move_BelowTheDragThresholdIsNotADrag()
        {
            BuildScene();
            yield return null;

            var dispatcher = new PointerEventDispatcher();
            dispatcher.MoveTo(GrabPoint);
            dispatcher.Press(0);
            // Under EventSystem.pixelDragThreshold, which defaults to 10: a twitch is still a click.
            dispatcher.MoveTo(GrabPoint + new Vector2(3f, 0f));
            dispatcher.Release(0);

            Assert.That(source.Events, Does.Not.Contain("beginDrag"));
            Assert.That(source.Events, Does.Contain("click"));
        }

        [UnityTest]
        public IEnumerator ReleaseAll_EndsADragThatWasStillInProgress()
        {
            BuildScene();
            yield return null;

            var dispatcher = new PointerEventDispatcher();
            dispatcher.MoveTo(GrabPoint);
            dispatcher.Press(0);
            dispatcher.MoveTo(DropPoint);
            dispatcher.ReleaseAll();

            Assert.That(source.Events, Does.Contain("endDrag"));
            Assert.That(destination.Events, Is.EqualTo(new[] { "drop" }));
        }

        private void BuildScene()
        {
            eventSystemObject = new GameObject("event system", typeof(EventSystem));

            canvasObject = new GameObject("canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            source = CreateTarget("drag source", GrabPoint);
            destination = CreateTarget("drop target", DropPoint);
        }

        private PointerFixtureBehaviour CreateTarget(string name, Vector2 screenPosition)
        {
            var targetObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(PointerFixtureBehaviour));
            targetObject.transform.SetParent(canvasObject.transform, false);

            var rectTransform = targetObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.zero;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = new Vector2(80f, 80f);
            rectTransform.anchoredPosition = screenPosition;

            return targetObject.GetComponent<PointerFixtureBehaviour>();
        }
    }
}
