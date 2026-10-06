using System;
using System.IO;
using System.Reflection;
using Mirror;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Run only in the isolated batch project described in README.md.</summary>
public static class GhostWarningRegression
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static int failures;
    static readonly System.Collections.Generic.List<string> results = new System.Collections.Generic.List<string>();
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
    static void Call(object target, string name) => target.GetType().GetMethod(name, Fields).Invoke(target, null);
    static void Check(bool pass, string label)
    {
        results.Add((pass ? "PASS " : "FAIL ") + label);
        if (!pass) failures++;
    }
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
        try { Checks(); }
        catch (Exception exception) { results.Add("FAIL " + exception); failures++; }
        File.WriteAllLines("warning-results.txt", results);
        Debug.Log(string.Join("\n", results));
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }
    static void Checks()
    {
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Player.prefab");
        Check(playerPrefab.GetComponentInChildren<PlayerGhostWarningController>(true) != null, "Player prefab has warning controller");
        Check(playerPrefab.GetComponentInChildren<GhostWarningEffect>(true) != null, "Player prefab has editable effect");
        var movingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Floderไอนน/Prefab/Enemy/Test_Ghost.prefab");
        var ticketPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Floderไอนน/Prefab/Enemy/TicketPunish.prefab");
        Check(movingPrefab.GetComponent<GhostWarningSource>().Category == GhostWarningCategory.Proximity, "Moving ghost uses proximity");
        Check(ticketPrefab.GetComponent<GhostWarningSource>().Category == GhostWarningCategory.Event, "Stationary ticket ghost uses events");
        Check(typeof(GhostWarningSource).GetMethod("InvokeUserCode_TargetWarn__NetworkConnectionToClient",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) != null, "Mirror generated targeted RPC receiver");
        Check(typeof(GhostWarningSource).GetProperty("NetworkwarningActive") != null, "Mirror generated synced danger gate");

        var body = new GameObject("Warning player", typeof(NetworkIdentity));
        var pivot = new GameObject("CameraPivot");
        pivot.transform.SetParent(body.transform);
        var cameraObject = new GameObject("Camera", typeof(Camera));
        cameraObject.transform.SetParent(pivot.transform);
        var rig = pivot.AddComponent<FirstPersonCamera>();
        rig.playerBody = body.transform;
        var controller = pivot.GetComponent<PlayerGhostWarningController>();
        var effect = pivot.GetComponent<GhostWarningEffect>();
        Call(rig, "Awake");
        Call(controller, "Awake");
        Call(rig, "Start");
        Set(rig, "fovLerpSpeed", 0f);

        var ghostObject = new GameObject("Moving danger");
        ghostObject.transform.position = Vector3.right * 10f;
        var source = ghostObject.AddComponent<GhostWarningSource>();
        Call(source, "OnEnable");
        Call(ghostObject.GetComponent<NetworkIdentity>(), "InitializeNetworkBehaviours");
        Call(controller, "Update");
        Check(!effect.IsPlaying, "Far player receives no warning");
        ghostObject.transform.position = Vector3.right * 4f;
        Call(controller, "Update");
        Check(effect.IsPlaying, "Entering range starts a pulse");
        Set(effect, "startedAt", Time.time - 0.15f);
        Check(Mathf.Abs(controller.FovReduction - 6f) < 0.01f, "Peak reduces FOV by six degrees");
        Set(rig, "locomotionFov", 60f);
        Call(rig, "Update");
        Check(Mathf.Abs(cameraObject.GetComponent<Camera>().fieldOfView - 54f) < 0.01f, "Walking FOV includes warning once");
        Set(rig, "locomotionFov", 75f);
        Call(rig, "Update");
        Check(Mathf.Abs(cameraObject.GetComponent<Camera>().fieldOfView - 69f) < 0.01f, "Sprint base and warning compose without accumulation");
        Set(effect, "startedAt", Time.time - 2f);
        Call(effect, "LateUpdate");
        Set(controller, "nextWarningTime", -1f);
        Call(controller, "Update");
        Check(!effect.IsPlaying, "Stationary nearby ghost does not repeat after cooldown");
        ghostObject.transform.position = Vector3.right * 5.5f;
        Call(controller, "Update");
        ghostObject.transform.position = Vector3.right * 4f;
        Call(controller, "Update");
        Check(!effect.IsPlaying, "Boundary hysteresis prevents re-trigger");
        ghostObject.transform.position = Vector3.right * 7f;
        Call(controller, "Update");
        ghostObject.transform.position = Vector3.right * 4f;
        Call(controller, "Update");
        Check(effect.IsPlaying, "Leaving outer radius rearms entry");
        var secondObject = new GameObject("Second danger");
        var second = secondObject.AddComponent<GhostWarningSource>();
        Call(second, "OnEnable");
        float originalStart = (float)typeof(GhostWarningEffect).GetField("startedAt", Fields).GetValue(effect);
        Call(controller, "Update");
        Check((float)typeof(GhostWarningEffect).GetField("startedAt", Fields).GetValue(effect) == originalStart,
            "Another ghost does not stack or restart active pulse");
        PlayerGhostWarningController.SuppressLocal(3f);
        Check(!effect.IsPlaying && controller.FovReduction == 0f, "Jumpscare immediately cancels warning");
        Call(controller, "Update");
        Set(controller, "suppressedUntil", -1f);
        Set(controller, "nextWarningTime", -1f);
        Call(controller, "Update");
        Check(!effect.IsPlaying, "Still-near ghost does not replay after jumpscare");
        Call(second, "OnDisable");
        Object.DestroyImmediate(secondObject);
        source.SetWarningActive(false);
        Call(controller, "Update");
        source.SetWarningActive(true);
        Call(controller, "Update");
        Check(effect.IsPlaying, "New dangerous state inside range can warn again");
        Call(source, "OnDisable");
        Object.DestroyImmediate(ghostObject);
        Call(controller, "Update");
        Check(!effect.IsPlaying, "Source destruction cancels pulse");

        var zoneObject = new GameObject("Zone", typeof(BoxCollider));
        var zone = zoneObject.GetComponent<BoxCollider>();
        zone.size = Vector3.one * 4f;
        zone.isTrigger = true;
        var eventObject = new GameObject("Event danger");
        var eventSource = eventObject.AddComponent<GhostWarningSource>();
        Call(eventSource, "OnEnable");
        Call(eventObject.GetComponent<NetworkIdentity>(), "InitializeNetworkBehaviours");
        Set(eventSource, "category", GhostWarningCategory.DangerZone);
        Set(eventSource, "dangerZone", zone);
        Check(eventSource.Contains(Vector3.zero, false), "Zone includes player inside");
        Check(!eventSource.Contains(Vector3.right * 2.5f, false)
            && eventSource.Contains(Vector3.right * 2.5f, true), "Zone exit margin prevents boundary flicker");
        Set(eventSource, "category", GhostWarningCategory.Event);
        Check(!eventSource.Contains(Vector3.zero, false), "Event ghost never warns from proximity");
        Set(controller, "nextWarningTime", -1f);
        controller.ReceiveEvent(eventSource);
        Call(controller, "Update");
        Check(effect.IsPlaying, "Explicit event uses the same non-stacking effect");
        effect.Cancel();

        // Emulate ownership flags directly, without opening ports or contacting a server.
        typeof(NetworkClient).GetField("connectState", BindingFlags.Static | BindingFlags.NonPublic)
            .SetValue(null, ConnectState.Connected);
        typeof(NetworkIdentity).GetProperty("netId").SetValue(eventSource.netIdentity, (uint)1);
        typeof(NetworkIdentity).GetProperty("isLocalPlayer").SetValue(body.GetComponent<NetworkIdentity>(), false);
        controller.ReceiveEvent(eventSource);
        Call(controller, "Update");
        Check(!effect.IsPlaying, "Remote player copy cannot display warning");
        typeof(NetworkIdentity).GetProperty("isLocalPlayer").SetValue(body.GetComponent<NetworkIdentity>(), true);
        Set(controller, "nextWarningTime", -1f);
        controller.ReceiveEvent(eventSource);
        Call(controller, "Update");
        Check(effect.IsPlaying, "Owning network player can display targeted event");
        SpectatorSession.Requested = true;
        Call(controller, "Update");
        Check(!effect.IsPlaying, "Spectator does not display player warning");
        SpectatorSession.Requested = false;
        typeof(NetworkClient).GetField("connectState", BindingFlags.Static | BindingFlags.NonPublic)
            .SetValue(null, ConnectState.None);
        NetworkMode.SessionEnding = true;
        Call(controller, "Update");
        Check(!effect.IsPlaying && controller.FovReduction == 0f, "Session ending clears local effect");
        NetworkMode.SessionEnding = false;
        Object.DestroyImmediate((Object)typeof(GhostWarningEffect).GetField("overlay", Fields).GetValue(effect));
        Object.DestroyImmediate((Object)typeof(GhostWarningEffect).GetField("texture", Fields).GetValue(effect));
        Call(controller, "OnDisable");
        Object.DestroyImmediate(body);
        Object.DestroyImmediate(zoneObject);
        Call(eventSource, "OnDisable");
        Object.DestroyImmediate(eventObject);
    }
}
