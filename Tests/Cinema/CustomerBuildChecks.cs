using System;
using System.IO;
using Mirror;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CustomerBuildChecks
{
    public static void Build()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use the isolated test project.");
        PlayerSettings.companyName = "PreThesisTests";
        PlayerSettings.productName = "CustomerBuildChecks";
        // Deterministic reproduction of the original unsafe read before Identity.Awake.
        var inactive = new GameObject("Uninitialized ticket network object");
        inactive.SetActive(false);
        var sync = inactive.AddComponent<TicketNetSync>();
        if (sync.netIdentity != null || sync.IsClientReady || sync.IsServerReady)
            throw new Exception("Readiness guard failed before NetworkIdentity initialization.");
        sync.Publish(default);
        sync.RequestMovie(0, 0);
        sync.RequestSale(false, 0);
        UnityEngine.Object.DestroyImmediate(inactive);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var probe = new GameObject("Customer Build Probe").AddComponent<CustomerBuildProbe>();
        probe.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/NetworkManager.prefab")
            .GetComponent<NetworkManager>().playerPrefab;
        const string boot = "Assets/CustomerBuildBoot.unity";
        EditorSceneManager.SaveScene(scene, boot);
        Directory.CreateDirectory("CustomerBuild");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { boot, "Assets/Scenes/Map/Cinema_GamePlay.unity" },
            locationPathName = "CustomerBuild/CustomerChecks.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        File.WriteAllText("customer-build-result.txt", report.summary.result + " errors=" + report.summary.totalErrors);
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
