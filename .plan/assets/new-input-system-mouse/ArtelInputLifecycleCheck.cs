using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

public static class ArtelInputLifecycleCheck
{
    private static int phase, entries;
    private static bool entered, exited;
    private static double deadline, sampleAt;
    private static bool oldEnabled;
    private static EnterPlayModeOptions oldOptions;
    private static Mouse previous;
    private static Vector2 delivered;
    private static bool held;
    private static int mouseCount;
    private static InputSettings originalSettings, cloneSettings;
    private static Type input, adapter;

    public static void Run()
    {
        oldEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        oldOptions = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        input = typeof(Artel.ArtelManager).Assembly.GetType("Artel.ArtelInput", true);
        adapter = input.Assembly.GetType("Artel.NewInputMouse", true);
        deadline = EditorApplication.timeSinceStartup + 90;
        EditorApplication.update += Tick;
        InputSystem.onAfterUpdate += Capture;
        EditorApplication.playModeStateChanged += OnMode;
    }

    private static void OnMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode) entered = true;
        if (state == PlayModeStateChange.EnteredEditMode) exited = true;
    }
    private static void Capture()
    {
        if (UnityEngine.InputSystem.LowLevel.InputState.currentUpdateType != UnityEngine.InputSystem.LowLevel.InputUpdateType.Dynamic || Mouse.current == null) return;
        delivered = Mouse.current.position.ReadValue();
        held = Mouse.current.leftButton.isPressed;
    }
    private static object Get(Type type, string name) => type.GetProperty(name,
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
    private static void Call(string name, params object[] args) => input.GetMethod(name,
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void Tick()
    {
        try
        {
            Check(EditorApplication.timeSinceStartup < deadline, "Lifecycle check timed out.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (phase == 0)
            {
                entered = exited = false;
                EditorApplication.isPlaying = true;
                phase = 1;
            }
            else if (phase == 1 && entered)
            {
                sampleAt = EditorApplication.timeSinceStartup + 1;
                phase = 4;
            }
            else if (phase == 4 && EditorApplication.timeSinceStartup >= sampleAt && Time.frameCount > 3)
            {
                Check(previous == null || !previous.added, "Previous mouse survived exit.");
                Check(!(bool)Get(adapter, "IsActive"), "Previous backend remained active.");
                Check(Get(adapter, "ValidationError") == null, "Backend did not register.");
                mouseCount = InputSystem.devices.OfType<Mouse>().Count();
                originalSettings = InputSystem.settings;
                input.GetProperty("PointerHeldForRun", BindingFlags.Static | BindingFlags.NonPublic)
                    .SetValue(null, true);
                Call("MoveMouse", new Vector2(120, 80));
                Call("PressMouseButton", 0);
                cloneSettings = InputSystem.settings;
                sampleAt = EditorApplication.timeSinceStartup + 0.5;
                phase = 2;
            }
            else if (phase == 2 && EditorApplication.timeSinceStartup >= sampleAt)
            {
                Check((bool)Get(adapter, "IsActive"), "QA mouse was not activated.");
                Check(InputSystem.devices.OfType<Mouse>().Count() == mouseCount + 1, "Duplicate QA mouse.");
                previous = Mouse.current;
                Check(delivered == new Vector2(120, 80), "Position was not delivered: " + delivered + " frame=" + Time.frameCount);
                Check(held, "Press was not delivered.");
                EditorApplication.isPlaying = false;
                phase = 3;
            }
            else if (phase == 3 && exited)
            {
                Check(!previous.added, "QA mouse remained after exiting Play Mode.");
                Check(!Resources.FindObjectsOfTypeAll<GameObject>().Any(obj => obj.name == "Artel QA Mouse lifetime"), "Lifetime object remained.");
                Check(cloneSettings == null, "Cloned InputSettings remained.");
                Check(InputSystem.settings == originalSettings, "Original settings were not restored.");
                if (++entries == 2) Finish(null);
                else phase = 0;
            }
        }
        catch (Exception error) { Finish(error); }
    }
    private static void Finish(Exception error)
    {
        EditorApplication.update -= Tick;
        InputSystem.onAfterUpdate -= Capture;
        EditorApplication.playModeStateChanged -= OnMode;
        EditorSettings.enterPlayModeOptionsEnabled = oldEnabled;
        EditorSettings.enterPlayModeOptions = oldOptions;
        File.WriteAllText("lifecycle-check-result.txt", error == null ? "PASS: two entries without domain reload" : error.ToString());
        if (error != null) Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
