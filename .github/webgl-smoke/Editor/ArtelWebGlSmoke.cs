using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ArtelWebGlSmoke
{
    public static void Build()
    {
        var development = Environment.GetCommandLineArgs().Contains("-artelDevelopment");
        if (CompilationPipeline.GetAssemblies(AssembliesType.Player)
            .Any(assembly => assembly.name == "Artel.Runtime" ||
                 assembly.assemblyReferences.Any(reference => reference.name == "Artel.Runtime")))
            throw new Exception("WebGL player compilation unexpectedly references Artel.Runtime");
        if (!CompilationPipeline.GetAssemblies(AssembliesType.Editor)
            .Any(assembly => assembly.name == "Artel.Runtime"))
            throw new Exception("WebGL Editor compilation must retain Artel.Runtime");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Input smoke").AddComponent<ArtelWebGlInputSmoke>();
        EditorSceneManager.SaveScene(scene, "Assets/WebGlSmoke.unity");
        var output = development ? "Build/WebGL-development" : "Build/WebGL-release";
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/WebGlSmoke.unity" },
            locationPathName = output,
            target = BuildTarget.WebGL,
            options = development ? BuildOptions.Development : BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("WebGL smoke build failed: " + report.summary.result);

        Debug.Log("ARTEL_WEBGL_SMOKE_PASS " + (development ? "development" : "release"));
    }
}
