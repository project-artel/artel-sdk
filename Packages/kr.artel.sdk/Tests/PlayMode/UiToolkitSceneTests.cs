using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Artel.Domain;
using Artel.Protocol.Dto;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace Artel.Tests
{
    /// <summary>
    /// The UI Toolkit sample scene, played by a live <see cref="ArtelManager"/> running the same
    /// action batches a QA run sends. Everything here is the real thing except the network: a scene
    /// asset built by <c>UiToolkitSceneBuilder</c>, a <see cref="UIDocument"/> with a UXML layout
    /// and a theme, the SDK's own overlay and cursor, and <c>move_mouse</c> / <c>mouse_down</c> /
    /// <c>mouse_up</c> going through <c>ActionExecutor</c>.
    /// </summary>
    /// <remarks>
    /// The scene carries one uGUI button beside the UI Toolkit ones as a control, so the two kinds
    /// of UI are measured in the same frame under the same conditions.
    /// </remarks>
    public sealed class UiToolkitSceneTests
    {
        private const string SceneName = "UiToolkitSample";

        private static int teardowns;

        private Scene sample;
        private GameObject host;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // A manager survives scene loads by design, so one left alive by an earlier test makes
            // the manager below a duplicate, and Awake destroys duplicates.
            foreach (var stale in Object.FindObjectsOfType<ArtelManager>(true))
            {
                Object.DestroyImmediate(stale.gameObject);
            }

            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);
            sample = SceneManager.GetSceneByName(SceneName);
            SceneManager.SetActiveScene(sample);

            // One for the scene's Start calls, which is where the sample wires its buttons, and one
            // for the EventSystem's Start, which is where UI Toolkit's PanelRaycaster is created.
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            ArtelInput.ReleaseAllVirtualInput();

            if (host != null)
            {
                Object.DestroyImmediate(host);
            }

            // The tests that run after this one scan the active scene, so the sample must not be it.
            // The name has to be new every time: CreateScene throws on a name already in use, and a
            // throw here leaves the sample loaded, so the next test loads a second copy of it and
            // reads its readouts off whichever copy it happens to find.
            SceneManager.SetActiveScene(
                SceneManager.CreateScene("after " + SceneName + " " + ++teardowns));
            yield return SceneManager.UnloadSceneAsync(sample);
        }

        [UnityTest]
        public IEnumerator MoveAndPress_ReachesTheUiToolkitButtonInTheScene()
        {
            var manager = CreateManager();
            yield return null;
            HideTheOverlay();

            // Coordinates are the only way to aim at this button: the scan reports no block for it,
            // which the scan test below states outright.
            var target = ToolkitButton("play-button").worldBound.center;

            yield return ClickAt(manager, target);

            Assert.That(StatusText(), Is.EqualTo("ui toolkit play 1"));
            Assert.That(UguiStatusText(), Is.EqualTo("ui toolkit play 1"));
        }

        [UnityTest]
        public IEnumerator MoveAndPress_ReachesTheSecondUiToolkitButtonToo()
        {
            var manager = CreateManager();
            yield return null;
            HideTheOverlay();

            yield return ClickAt(manager, ToolkitButton("quit-button").worldBound.center);

            // Not the first button, which is what a raycast landing on the panel rather than on the
            // element would produce.
            Assert.That(StatusText(), Is.EqualTo("ui toolkit quit"));
        }

        /// <summary>
        /// The control. The uGUI button in the same scene is aimed at the way an agent aims — by the
        /// id the scan reported for it — because for that button the scan reports one.
        /// </summary>
        [UnityTest]
        public IEnumerator MoveAndPress_ReachesTheUguiButtonByTheIdTheScanReported()
        {
            var manager = CreateManager();
            yield return null;
            HideTheOverlay();

            var block = FindBlock(new SceneScanner().Scan().Scene.Children, UguiButtonName);
            Assert.That(block, Is.Not.Null, "the scan did not report the uGUI button");

            yield return RunBatch(
                manager,
                NewAction(1, "move_mouse", Params((long)block.Id)),
                NewAction(2, "mouse_down", new List<object>()),
                NewAction(3, "mouse_up", new List<object>()));
            yield return null;

            Assert.That(UguiStatusText(), Is.EqualTo("ugui play 1"));
        }

        /// <summary>
        /// What the content map has to say about this scene: the uGUI button, with a name and a
        /// clickable component, and nothing at all for the two UI Toolkit buttons beside it. That is
        /// the gap — the pointer reaches them, but an agent reading the map cannot know they exist.
        /// </summary>
        [UnityTest]
        public IEnumerator Scan_ReportsTheUguiButtonAndNeitherUiToolkitButton()
        {
            yield return null;

            var roots = new SceneScanner().Scan().Scene.Children;

            var ugui = FindBlock(roots, UguiButtonName);
            Assert.That(ugui, Is.Not.Null);
            Assert.That(ugui.Components, Has.Some.InstanceOf<ButtonComponent>());

            Assert.That(FindBlock(roots, "play-button"), Is.Null);
            Assert.That(FindBlock(roots, "quit-button"), Is.Null);

            // The document's own GameObject is there, so the absence above is not the document
            // having been missed — it is the buttons inside it not being GameObjects.
            var document = FindBlock(roots, "UI Document");
            Assert.That(document, Is.Not.Null);
            Assert.That(document.Components, Is.Empty);
            Assert.That(document.Children, Is.Empty);
        }

        private static string UguiButtonName
        {
            // The sample script owns both names; spelling them again here would let the two drift.
            get { return "ugui play button"; }
        }

        private IEnumerator ClickAt(ArtelManager manager, Vector2 topLeftPoint)
        {
            yield return RunBatch(
                manager,
                NewAction(1, "move_mouse", Params((double)topLeftPoint.x, (double)topLeftPoint.y)),
                NewAction(2, "mouse_down", new List<object>()),
                NewAction(3, "mouse_up", new List<object>()));
            yield return null;
        }

        private static Button ToolkitButton(string name)
        {
            var document = Object.FindObjectOfType<UIDocument>();
            Assert.That(document, Is.Not.Null, "the scene has no UIDocument");
            var button = document.rootVisualElement.Q<Button>(name);
            Assert.That(button, Is.Not.Null, "the layout has no button named " + name);
            return button;
        }

        private static string StatusText()
        {
            return Object.FindObjectOfType<UIDocument>().rootVisualElement.Q<Label>("status-label").text;
        }

        private static string UguiStatusText()
        {
            var status = GameObject.Find("ugui status");
            Assert.That(status, Is.Not.Null, "the scene has no uGUI readout");
            return status.GetComponent<Text>().text;
        }

        /// <summary>
        /// The SDK's overlay gate is a full-screen Image with raycastTarget on at sortingOrder
        /// short.MaxValue - 1, so while it is up it answers every ray before the game sees one. A
        /// real QA run only begins once registration has taken it down.
        /// </summary>
        private static void HideTheOverlay()
        {
            foreach (var raycaster in Object.FindObjectsOfType<GraphicRaycaster>(true))
            {
                if (raycaster.gameObject.name == "Artel Overlay Canvas")
                {
                    raycaster.gameObject.SetActive(false);
                }
            }
        }

        private static SceneBlock FindBlock(IReadOnlyList<SceneBlock> blocks, string name)
        {
            foreach (var block in blocks)
            {
                if (block.Name == name)
                {
                    return block;
                }

                var found = FindBlock(block.Children, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static List<object> Params(params object[] values)
        {
            return new List<object>(values);
        }

        private static ActionRequestDto NewAction(int id, string method, List<object> parameters)
        {
            return new ActionRequestDto { Id = id, Method = method, Parameters = parameters };
        }

        private static IEnumerator RunBatch(ArtelManager manager, params ActionRequestDto[] actions)
        {
            var request = new ArtelRequestDto
            {
                Type = "ACTION",
                Actions = new List<ActionRequestDto>(actions)
            };
            var routine = (IEnumerator)typeof(ArtelManager)
                .GetMethod("ExecuteActionRequest", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(manager, new object[] { request });

            yield return manager.StartCoroutine(routine);
        }

        private ArtelManager CreateManager()
        {
            host = new GameObject("Artel ui toolkit scene test");
            var manager = host.AddComponent<ArtelManager>();
            manager.SetWebSocketTransport(new SilentTransport(), false);
            return manager;
        }

        private sealed class SilentTransport : IArtelWebSocketTransport
        {
            public bool IsConnected { get { return true; } }

            public ArtelTransportPhase Phase { get { return ArtelTransportPhase.Connected; } }

            public void Start()
            {
            }

            public void Stop()
            {
            }

            public bool TryDequeueMessage(out ArtelWebSocketMessage message)
            {
                message = null;
                return false;
            }

            public void Send(string text)
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
