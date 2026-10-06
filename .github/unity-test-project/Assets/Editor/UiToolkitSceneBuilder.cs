using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UI.Button;
using Image = UnityEngine.UI.Image;

/// <summary>
/// Builds the UI Toolkit sample scene and the assets it needs, so the scene in the repository is
/// something a script wrote and can write again rather than a file nobody can regenerate.
/// </summary>
/// <remarks>
/// Run it with
/// <c>-batchmode -quit -executeMethod UiToolkitSceneBuilder.Build</c>.
/// </remarks>
public static class UiToolkitSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/UiToolkitSample.unity";
    private const string ThemePath = "Assets/UiToolkit/UnityDefaultRuntimeTheme.tss";
    private const string LayoutPath = "Assets/UiToolkit/UiToolkitSample.uxml";
    private const string SettingsPath = "Assets/UiToolkit/UiToolkitSamplePanelSettings.asset";

    public static void Build()
    {
        AssetDatabase.Refresh();

        var settings = CreatePanelSettings();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        var documentObject = new GameObject("UI Document");
        documentObject.SetActive(false);
        var document = documentObject.AddComponent<UIDocument>();
        document.panelSettings = settings;
        document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LayoutPath);
        documentObject.SetActive(true);

        CreateUguiControl();

        new GameObject("Sample Game", typeof(UiToolkitSampleGame));

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();

        Debug.Log("[builder] wrote " + ScenePath);
    }

    private static PanelSettings CreatePanelSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(SettingsPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }

        // Without a theme the panel still lays out and picks, but it draws unstyled and logs an
        // error every frame, and a scene a person is meant to look at should look like the game.
        settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        EditorUtility.SetDirty(settings);
        return settings;
    }

    /// <summary>
    /// One uGUI button in the same scene, in a part of the screen the UI Toolkit buttons do not
    /// cover. It is the control: whatever the pointer does to it is what the pointer does to the
    /// UI the SDK already supports, measured under the same conditions in the same frame.
    /// </summary>
    private static void CreateUguiControl()
    {
        var canvasObject = new GameObject(
            "uGUI Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        var buttonObject = new GameObject(
            UiToolkitSampleGame.UguiButtonName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(canvasObject.transform, false);
        PlaceFromTopLeft(buttonObject.GetComponent<RectTransform>(), new Vector2(400f, 60f), new Vector2(200f, 80f));

        var labelObject = new GameObject(
            "ugui play label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelObject.transform.SetParent(buttonObject.transform, false);
        var label = labelObject.GetComponent<Text>();
        label.text = "uGUI Play";
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.black;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        StretchToParent(labelObject.GetComponent<RectTransform>());

        var statusObject = new GameObject(
            UiToolkitSampleGame.UguiStatusName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        statusObject.transform.SetParent(canvasObject.transform, false);
        var status = statusObject.GetComponent<Text>();
        status.text = "ready";
        status.alignment = TextAnchor.MiddleLeft;
        status.color = Color.white;
        status.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        PlaceFromTopLeft(statusObject.GetComponent<RectTransform>(), new Vector2(400f, 16f), new Vector2(200f, 32f));
    }

    /// <summary>
    /// UXML measures from the top left; a RectTransform anchored to the bottom left counts up. The
    /// two readouts sit at matching places on screen so the scene reads as one screen, not two.
    /// </summary>
    private static void PlaceFromTopLeft(RectTransform rect, Vector2 topLeft, Vector2 size)
    {
        // Anchored to the top left corner, which is the corner UXML counts from, so both readouts
        // take the same numbers.
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
    }

    private static void StretchToParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
