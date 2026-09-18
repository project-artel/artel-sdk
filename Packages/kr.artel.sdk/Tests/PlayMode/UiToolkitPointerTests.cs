using System.Collections;
using System.Collections.Generic;
using System.Text;
using Artel.Domain;
using Artel.Tracking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Artel.Tests
{
    /// <summary>
    /// A game whose UI is UI Toolkit rather than uGUI, put to the two questions that decide whether
    /// the agent can play it: does the pointer reach a <see cref="Button"/> drawn by a
    /// <see cref="UIDocument"/>, and does the scan report that button.
    /// </summary>
    /// <remarks>
    /// The two have different answers. The pointer reaches it, because a runtime panel registers a
    /// <c>PanelRaycaster</c> with the EventSystem and <c>PanelEventHandler</c> forwards uGUI pointer
    /// events into the visual tree — the dispatcher needs no UI Toolkit code of its own. The scan
    /// does not report it, because a UI Toolkit button is a <see cref="VisualElement"/> and the scan
    /// walks <see cref="Transform"/>s.
    /// </remarks>
    public sealed class UiToolkitPointerTests
    {
        private const float ButtonLeft = 40f;
        private const float ButtonTop = 60f;
        private const float ButtonWidth = 200f;
        private const float ButtonHeight = 80f;

        private GameObject eventSystemObject;
        private GameObject documentObject;
        private PanelSettings panelSettings;
        private Button button;
        private int clicks;

        [SetUp]
        public void SetUp()
        {
            // A manager left alive by the project's own scene or by an earlier test brings the SDK
            // overlay with it, and the overlay's gate is a full-screen Image with raycastTarget on
            // at sortingOrder short.MaxValue - 1. It would answer every raycast in this file before
            // the panel could, which says nothing about UI Toolkit.
            foreach (var stale in Object.FindObjectsOfType<ArtelManager>(true))
            {
                Object.DestroyImmediate(stale.gameObject);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (documentObject != null)
            {
                Object.DestroyImmediate(documentObject);
            }

            if (eventSystemObject != null)
            {
                Object.DestroyImmediate(eventSystemObject);
            }

            if (panelSettings != null)
            {
                Object.DestroyImmediate(panelSettings);
            }

            clicks = 0;
            button = null;
        }

        [UnityTest]
        public IEnumerator Click_ReachesAUiToolkitButton()
        {
            yield return BuildPanel(withInputModule: true);

            var dispatcher = new PointerEventDispatcher();
            dispatcher.MoveTo(ScreenPointOf(button));
            dispatcher.Press(0);
            dispatcher.Release(0);

            yield return null;

            Assert.That(clicks, Is.EqualTo(1));
        }

        /// <summary>
        /// The condition the test above depends on, written down so a change that breaks it fails
        /// here instead of in a QA run. <c>PanelRaycaster</c> answers nothing at all while the
        /// EventSystem carries no input module — not at the button, not anywhere on the screen —
        /// so the dispatcher's raycast comes back empty and no press is delivered.
        /// </summary>
        /// <remarks>
        /// The SDK's own EventSystem is built with a <see cref="StandaloneInputModule"/>
        /// (<c>ArtelOverlayController</c>), so a scene that has no EventSystem of its own still
        /// meets the condition. A game that builds an EventSystem without a module does not.
        /// </remarks>
        [UnityTest]
        public IEnumerator Click_MissesAUiToolkitButtonWhenTheEventSystemHasNoInputModule()
        {
            yield return BuildPanel(withInputModule: false);

            var screenPoint = ScreenPointOf(button);

            // The panel itself would pick the button at that point, so the miss is the raycaster's.
            Assert.That(button.panel.Pick(PanelPointOf(screenPoint)), Is.SameAs(button));

            var dispatcher = new PointerEventDispatcher();
            dispatcher.MoveTo(screenPoint);
            dispatcher.Press(0);
            dispatcher.Release(0);

            yield return null;

            Assert.That(clicks, Is.EqualTo(0));
        }

        /// <summary>
        /// What the content map has to say about a UI Toolkit button: nothing. The document's
        /// GameObject is reported with no components, so the button carries no name, no rect and no
        /// id an agent could aim at. Stated as the behaviour that is there, not the one anyone wants.
        /// </summary>
        [UnityTest]
        public IEnumerator Scan_ReportsTheDocumentWithNoComponents()
        {
            yield return BuildPanel(withInputModule: true);

            var found = new List<string>();
            foreach (var child in new SceneScanner().Scan().Scene.Children)
            {
                Collect(child, found);
            }

            Assert.That(found, Is.EqualTo(new[] { "ui document: (no components)" }));
        }

        private IEnumerator BuildPanel(bool withInputModule)
        {
            // A panel with no theme style sheet logs an error and still lays out and picks, which is
            // all these tests read. Assigning a theme would mean shipping a ThemeStyleSheet asset.
            LogAssert.ignoreFailingMessages = true;

            eventSystemObject = withInputModule
                ? new GameObject("event system", typeof(EventSystem), typeof(StandaloneInputModule))
                : new GameObject("event system", typeof(EventSystem));

            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            panelSettings.scale = 1f;

            // The document has to know its settings before OnEnable, which is where it builds the
            // runtime panel, so it is assigned while the object is still inactive.
            documentObject = new GameObject("ui document");
            documentObject.SetActive(false);
            var document = documentObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            documentObject.SetActive(true);

            button = new Button(() => clicks++) { text = "press me" };
            button.style.position = Position.Absolute;
            button.style.left = ButtonLeft;
            button.style.top = ButtonTop;
            button.style.width = ButtonWidth;
            button.style.height = ButtonHeight;
            document.rootVisualElement.Add(button);

            // Layout runs on the panel's own update, so the button has no worldBound until a frame
            // has gone by. Two, because the EventSystem creates the raycaster in its own Start.
            yield return null;
            yield return null;
        }

        /// <summary>
        /// UI Toolkit measures from the top left with y going down; the screen coordinates the
        /// dispatcher takes start at the bottom left with y going up.
        /// </summary>
        private static Vector2 ScreenPointOf(VisualElement element)
        {
            var bounds = element.worldBound;
            return new Vector2(bounds.center.x, Screen.height - bounds.center.y);
        }

        private static Vector2 PanelPointOf(Vector2 screenPoint)
        {
            return new Vector2(screenPoint.x, Screen.height - screenPoint.y);
        }

        private static void Collect(SceneBlock block, List<string> found)
        {
            if (block.Name == "ui document")
            {
                found.Add(block.Name + ": " + DescribeComponents(block));
            }

            foreach (var child in block.Children)
            {
                Collect(child, found);
            }
        }

        private static string DescribeComponents(SceneBlock block)
        {
            if (block.Components.Count == 0)
            {
                return "(no components)";
            }

            var description = new StringBuilder();
            foreach (var component in block.Components)
            {
                if (description.Length > 0)
                {
                    description.Append(", ");
                }

                description.Append(component.GetType().Name);
            }

            return description.ToString();
        }
    }
}
