using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

/// <summary>
/// A game whose screen is UI Toolkit, with one uGUI button beside it as a control. Pressing either
/// one writes what happened where a test — or a person looking at the window — can read it.
/// </summary>
/// <remarks>
/// Both paths write to both readouts, so a click that arrived is visible whichever readout the
/// reader happens to look at, and a click that arrived by the wrong path is visible as the wrong
/// text rather than as no text.
/// </remarks>
public sealed class UiToolkitSampleGame : MonoBehaviour
{
    public const string UguiButtonName = "ugui play button";
    public const string UguiStatusName = "ugui status";

    private Label toolkitStatus;
    private Text uguiStatus;
    private int plays;

    /// <summary>
    /// Wiring happens in Start, not OnEnable. <see cref="UIDocument.rootVisualElement"/> is null
    /// until the document's own OnEnable has built the panel, and the order of two OnEnable calls
    /// in one scene is not something this script gets to decide.
    /// </summary>
    private void Start()
    {
        var document = FindObjectOfType<UIDocument>();
        if (document != null)
        {
            var root = document.rootVisualElement;
            toolkitStatus = root.Q<Label>("status-label");
            root.Q<Button>("play-button").clicked += () => Report("ui toolkit play " + ++plays);
            root.Q<Button>("quit-button").clicked += () => Report("ui toolkit quit");
        }

        var uguiStatusObject = GameObject.Find(UguiStatusName);
        uguiStatus = uguiStatusObject == null ? null : uguiStatusObject.GetComponent<Text>();

        var uguiButtonObject = GameObject.Find(UguiButtonName);
        var uguiButton = uguiButtonObject == null
            ? null
            : uguiButtonObject.GetComponent<UnityEngine.UI.Button>();
        if (uguiButton != null)
        {
            uguiButton.onClick.AddListener(() => Report("ugui play " + ++plays));
        }

        Report("ready");
    }

    private void Report(string what)
    {
        if (toolkitStatus != null)
        {
            toolkitStatus.text = what;
        }

        if (uguiStatus != null)
        {
            uguiStatus.text = what;
        }

        Debug.Log("[sample] " + what);
    }
}
