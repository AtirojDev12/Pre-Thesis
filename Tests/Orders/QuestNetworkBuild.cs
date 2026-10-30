using System.IO;
using Mirror;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class QuestNetworkBuild
{
    public static void Build()
    {
        PlayerSettings.companyName = "PreThesisTests";
        PlayerSettings.productName = "QuestNetworkChecks";
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var probe = new GameObject("Quest Network Probe").AddComponent<QuestNetworkProbe>();
        var template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/NetworkManager.prefab").GetComponent<NetworkManager>();
        probe.playerPrefab = template.playerPrefab;
        probe.spawnPrefabs.AddRange(template.spawnPrefabs);
        const string boot = "Assets/QuestNetworkBoot.unity";
        EditorSceneManager.SaveScene(scene, boot);
        Directory.CreateDirectory("QuestNetworkBuild");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { boot, "Assets/Scenes/Map/Cinema_GamePlay.unity" },
            locationPathName = "QuestNetworkBuild/QuestNetworkChecks.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        });
        File.WriteAllText("quest-network-build-result.txt", report.summary.result + " errors=" + report.summary.totalErrors);
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
