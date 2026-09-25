using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class PlayerAnimationBuild
{
    public static void Build()
    {
        if (!Application.isBatchMode) throw new System.InvalidOperationException("Use the isolated test project.");
        PlayerSettings.companyName = "PreThesisTests";
        PlayerSettings.productName = "PlayerAnimationChecks";
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var probe = new GameObject("Animation Probe").AddComponent<PlayerAnimationProbe>();
        probe.prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Player.prefab");
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/AnimationProbe.unity");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/AnimationProbe.unity" }, locationPathName = "AnimationBuild/AnimationChecks.exe",
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
