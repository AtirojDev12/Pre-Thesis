// Runs only in the isolated project created by Run-PopcornChecks.ps1.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Mirror;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class PopcornRegressionRunner
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> results = new List<string>();
    static int failures;
    static IEnumerator checks;
    static double deadline;
    static PlayerHealth player;
    static Camera camera;
    static PlayerInteractor interactor;
    static PopcornPreparation preparation;
    static ItemHoldingSystem holder;
    static PopcornGameManager manager;
    static CounterSlot slot;
    static Keyboard keyboard;
    static PopcornStation[] stations;
    static PopcornStation movedStation;
    static Vector3 originalPosition;

    static PopcornRegressionRunner()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("PopcornRegression", false)) return;
            deadline = EditorApplication.timeSinceStartup + 120;
            checks = RunChecks();
            var loop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            var systems = loop.subSystemList.ToList();
            systems.Add(new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(PopcornRegressionRunner), updateDelegate = Tick });
            loop.subSystemList = systems.ToArray();
            UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(loop);
            EditorApplication.update += EditorApplication.Step;
        };
    }
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
        EditorSceneManager.OpenScene("Assets/Scenes/Map/Cinema_GamePlay.unity");
        SessionState.SetBool("PopcornRegression", true);
        EditorApplication.EnterPlaymode();
    }
    static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    static void Check(bool pass, string message)
    {
        results.Add((pass ? "PASS " : "FAIL ") + message);
        if (!pass) failures++;
        Debug.Log(results[results.Count - 1]);
    }
    static PopcornStation Station(PopcornStationKind kind, PopcornFlavor flavor = PopcornFlavor.None) =>
        stations.First(s => s.Kind == kind && (flavor == PopcornFlavor.None || s.Flavor == flavor));
    static void Input(bool held)
    {
        var inputManager = typeof(InputSystem).GetField("s_Manager", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        inputManager.GetType().GetMethod("OnBeforeUpdate", Private).Invoke(inputManager, new object[] { InputUpdateType.Dynamic });
        InputState.Change(keyboard, held ? new KeyboardState(Key.E) : new KeyboardState(), InputUpdateType.Dynamic);
    }
    static void Aim(PopcornStation station)
    {
        // Move the actual authored station into a clear fixture area, preserving its mesh/collider.
        if (movedStation != null) movedStation.transform.position = originalPosition;
        movedStation = station;
        originalPosition = station.transform.position;
        station.transform.position = new Vector3(1000, 5, 1000);
        if (station.Kind == PopcornStationKind.Ghost && GhostFavorRecovery.Instance != null)
            Set(GhostFavorRecovery.Instance, "homePosition", station.transform.position);
        Physics.SyncTransforms();
        var collider = station.GetComponentInChildren<Collider>();
        Vector3 target = collider.bounds.center;
        camera.transform.position = target + Vector3.back * 1.5f;
        camera.transform.LookAt(target);
        Cursor.lockState = CursorLockMode.Locked;
        Physics.SyncTransforms();
    }
    static void Press(PopcornStation station)
    {
        Aim(station);
        Input(true);
        Call(interactor, "Update");
        Input(false);
    }
    static IEnumerator Fill(PopcornStation station)
    {
        Aim(station);
        Input(true);
        preparation.Interact(station);
        // Batch EditorApplication.Step releases cursor/input between frames. Drive
        // the real component with fixed-delta ticks inside one input update instead.
        float simulatedSeconds = 0f;
        bool completedEarly = false;
        int ticks = 0;
        while (preparation.IsPreparing && ++ticks < 10000)
        {
            Input(true);
            Cursor.lockState = CursorLockMode.Locked;
            simulatedSeconds += Time.deltaTime;
            Call(preparation, "Update");
            if (holder.IsReady && simulatedSeconds < 2.999f) completedEarly = true;
        }
        Input(false);
        Check(holder.IsReady && !completedEarly && simulatedSeconds >= 2.999f && simulatedSeconds < 3f + Time.deltaTime * 2f,
            station.Kind + " completes after three seconds of simulated held input");
        yield break;
    }
    static IEnumerator RunChecks()
    {
        for (int i = 0; i < 20; i++) yield return null;
        Application.runInBackground = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        player = PlayerHealth.LocalInstance;
        if (player == null) throw new Exception("Scene did not create local player");
        camera = player.GetComponentInChildren<Camera>();
        interactor = player.GetComponent<PlayerInteractor>();
        preparation = Object.FindAnyObjectByType<PopcornPreparation>();
        holder = Object.FindAnyObjectByType<ItemHoldingSystem>();
        manager = Object.FindAnyObjectByType<PopcornGameManager>();
        slot = Object.FindAnyObjectByType<CounterSlot>();
        stations = Object.FindObjectsByType<PopcornStation>(FindObjectsInactive.Exclude);
        keyboard = InputSystem.AddDevice<Keyboard>();
        keyboard.MakeCurrent();
        player.GetComponent<PlayerMovement>().enabled = false;
        player.GetComponentInChildren<FirstPersonCamera>().enabled = false;
        var body = player.GetComponent<Rigidbody>();
        body.isKinematic = true;
        player.transform.position = new Vector3(1000, 3, 1000);
        Check(stations.Length == 8, "All eight authored stations bound, including both dispenser cubes");
        foreach (var station in stations)
            Check(station.GetComponentsInChildren<Collider>().Any(c => c.enabled && !c.isTrigger), station.name + " has an interaction collider");
        Check(Resources.Load<Shader>("InteractionOutline") != null, "Outline shader included in build resources");
        foreach (var station in stations.Where(s => s.Kind == PopcornStationKind.Scoop))
            Check(station.GetInteractionPrompt().Contains(station.Flavor.ToString()), "Empty hands hover identifies " + station.Flavor);
        Check(!holder.Hold(PopcornFlavor.Cheese) && !holder.MixGhost(), "Cannot fill or mix without a container");
        Press(Station(PopcornStationKind.Bucket));
        Check(holder.HasItem && !holder.IsReady && !holder.IsCup, "E at bucket spawner picks up empty bucket");
        Check(((GameObject)Get(holder, "heldVisual")).transform.parent == camera.transform, "Container attaches to local hand view");
        Check(!holder.PickUp(true) && !holder.Hold(PopcornFlavor.Drink), "Held bucket cannot be replaced or filled with water");
        var cheese = Station(PopcornStationKind.Scoop, PopcornFlavor.Cheese);
        Aim(cheese);
        Input(true);
        preparation.Interact(cheese);
        Call(preparation, "Update");
        Check(preparation.IsPreparing && !holder.IsReady, "Preparation remains incomplete after one tick");
        Input(false);
        Call(preparation, "Update");
        Check(!preparation.IsPreparing && !holder.IsReady, "Releasing E cancels scooping without filling");
        var filling = Fill(cheese);
        while (filling.MoveNext()) yield return null;
        Check(holder.HeldFlavor == PopcornFlavor.Cheese && !holder.GhostMixed, "Scoop preserves selected base flavor");
        Press(Station(PopcornStationKind.Ghost));
        Check(holder.GhostMixed && holder.HeldFlavor == PopcornFlavor.Cheese && !holder.MixGhost(), "Ghost mix adds once without replacing flavor");
        var recovery = GhostFavorRecovery.Instance;
        var home = (Vector3)Get(recovery, "homePosition");
        var ghostManager = GhostManager.Instance;
        ghostManager.PlayerToggleLights(false);
        Set(recovery, "darkSeconds", 0f);
        Call(recovery, "Update");
        Check(recovery.IsHome, "Brief warning flicker does not relocate GhostFavor");
        Set(recovery, "darkSeconds", 0.6f);
        Call(recovery, "Update");
        var destinations = ((Transform[])Get(recovery, "destinations")).Where(t => t != null).ToArray();
        Check(!recovery.IsHome && (destinations.Length > 0
            ? destinations.Any(t => t.position == recovery.VisibleState.position)
            : recovery.VisibleState.position == Object.FindAnyObjectByType<TicketMinigame>().GhostFavorDestination.position),
            "Sustained blackout relocates GhostFavor to configured destination or ticket booth fallback");
        holder.Consume();
        holder.PickUp(false);
        holder.Hold(PopcornFlavor.Cheese);
        Check(!holder.MixGhost(), "Displaced GhostFavor cannot season popcorn");
        Check(!recovery.ServerInteract(player, false) && !recovery.IsLocalCarrier, "Out of range pickup rejected");
        player.transform.position = recovery.VisibleState.position + Vector3.back;
        recovery.ServerInteract(player, false);
        Check(recovery.IsLocalCarrier && !holder.MixGhost(), "Player picks up shared seasoning but cannot use it while carried");
        var otherObject = new GameObject("Second recovery player", typeof(Mirror.NetworkIdentity));
        var other = otherObject.AddComponent<PlayerHealth>();
        other.transform.position = player.transform.position;
        recovery.ServerInteract(other, false);
        Check(recovery.IsLocalCarrier, "Simultaneous second pickup cannot steal carried object");
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "GhostFavor drop test floor";
        floor.transform.position = new Vector3(1100, 0, 1000);
        floor.transform.localScale = new Vector3(20, 0.2f, 20);
        player.transform.position = new Vector3(1100, 5, 1000);
        Call(recovery, "Update");
        Call(recovery, "LateUpdate");
        float releaseHeight = recovery.VisibleState.position.y;
        recovery.ServerInteract(player, true);
        var favorBody = Station(PopcornStationKind.Ghost).GetComponent<Rigidbody>();
        Check(!favorBody.isKinematic && favorBody.useGravity, "Dropping enables authoritative gravity");
        var simulationMode = Physics.simulationMode;
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            Physics.SyncTransforms();
            for (int i = 0; i < 200; i++)
            {
                Physics.Simulate(0.02f);
                Call(recovery, "Update");
                Call(recovery, "LateUpdate");
            }
        }
        finally { Physics.simulationMode = simulationMode; }
        var favorCollider = Station(PopcornStationKind.Ghost).GetComponent<Collider>();
        Check(recovery.VisibleState.position.y < releaseHeight - 1f &&
            Mathf.Abs(favorCollider.bounds.min.y - floor.GetComponent<Collider>().bounds.max.y) < 0.05f,
            "Dropped GhostFavor falls and rests on floor without hovering or sinking");
        other.transform.position = recovery.VisibleState.position + Vector3.back;
        recovery.ServerInteract(other, false);
        Check(favorBody.isKinematic && !favorBody.useGravity, "Picking up stops drop physics");
        Object.Destroy(floor);
        Check(ReferenceEquals(Get(recovery, "carrier"), other), "Another player can pick up dropped GhostFavor");
        other.transform.position = home;
        Call(recovery, "Update");
        Call(recovery, "LateUpdate");
        Check(recovery.IsHome && Station(PopcornStationKind.Ghost).transform.position == home && holder.MixGhost(),
            "Other player returns seasoning to exact home position and unlocks mixing");
        Object.Destroy(otherObject);
        ghostManager.PlayerToggleLights(true);
        Call(recovery, "Update");
        player.transform.position = new Vector3(1000, 3, 1000);
        holder.Consume();
        Press(Station(PopcornStationKind.Cup));
        Check(holder.IsCup && !holder.IsReady && !holder.MixGhost(), "Cup starts empty and rejects early ghost mix");
        Check(!holder.Hold(PopcornFlavor.Paprika), "Cup rejects popcorn");
        var water = Station(PopcornStationKind.Water);
        Aim(water);
        Input(true);
        preparation.Interact(water);
        Call(preparation, "Update");
        Check(preparation.IsPreparing && !holder.IsReady, "Preparation remains incomplete after one tick");
        Input(false);
        Call(preparation, "Update");
        Check(!preparation.IsPreparing && !holder.IsReady, "Releasing E cancels water fill");
        Aim(water);
        Input(true);
        preparation.Interact(water);
        Call(preparation, "Update");
        Check(preparation.IsPreparing, "Water starts while E is held and aimed");
        camera.transform.rotation = Quaternion.LookRotation(Vector3.up);
        Call(preparation, "Update");
        Check(!preparation.IsPreparing && !holder.IsReady, "Looking away cancels water fill");
        Aim(water);
        preparation.Interact(water);
        Check((float)Get(preparation, "elapsed") == 0f, "Cancelled progress restarts from zero");
        var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.transform.position = camera.transform.position + camera.transform.forward * .5f;
        blocker.transform.localScale = Vector3.one * .3f;
        Physics.SyncTransforms();
        Call(preparation, "Update");
        Check(!preparation.IsPreparing, "Occlusion cancels filling");
        blocker.SetActive(false);
        Object.Destroy(blocker);
        Input(false);
        filling = Fill(water);
        while (filling.MoveNext()) yield return null;
        Check(holder.HeldFlavor == PopcornFlavor.Drink && holder.MixGhost(), "Filled water accepts ghost flavor");
        Check(((GameObject)Get(holder, "heldVisual")).name.Contains("PapperBotteWaterGhost"), "Ghost water uses authored prefab");
        holder.Consume();
        foreach (var flavor in new[] { PopcornFlavor.Cheese, PopcornFlavor.BBQ, PopcornFlavor.Paprika, PopcornFlavor.Drink })
        {
            Check(PopcornRecipe.Matches(flavor, false, flavor, PopcornCustomerType.Human), flavor + " human recipe");
            Check(PopcornRecipe.Matches(flavor, true, flavor, PopcornCustomerType.Ghost), flavor + " ghost recipe");
            Check(!PopcornRecipe.Matches(flavor, false, flavor, PopcornCustomerType.Ghost), flavor + " ghost requires mix");
            Check(!PopcornRecipe.Matches(flavor, true, flavor, PopcornCustomerType.Human), flavor + " human rejects ghost mix");
        }
        // Exercise the same authoritative resolver used by network requests (offline authority).
        var sync = PopcornNetSync.Instance;
        Check(sync != null, "Scene initializes authoritative score system offline");
        int score = sync.Score;
        float health = player.CurrentHealth;
        Set(sync, "currentCustomerType", PopcornCustomerType.Ghost);
        Set(sync, "currentOrder", PopcornFlavor.Drink);
        Set(sync, "customerWaiting", true);
        sync.RequestServe(PopcornFlavor.Drink, false);
        Check(sync.Score == score && player.CurrentHealth == health - 10f, "Missing ghost mix keeps original penalty and score");
        Set(sync, "customerWaiting", true);
        Set(sync, "currentOrder", PopcornFlavor.Drink);
        sync.RequestServe(PopcornFlavor.Drink, true);
        Check(sync.Score == score + 1 && !sync.CustomerWaiting, "Correct ghost drink scores once and closes order");
        sync.RequestServe(PopcornFlavor.Drink, true);
        Check(sync.Score == score + 1, "Repeated serve cannot score twice");
        Aim(water);
        Input(false);
        Call(interactor, "Update");
        Check(water.GetComponent<InteractionOutline>() != null && water.GetComponentsInChildren<Renderer>().Any(r => r.name == "White Interaction Outline"), "Crosshair adds visible white outline");
        camera.transform.rotation = Quaternion.LookRotation(Vector3.up);
        Call(interactor, "Update");
        Check(!water.GetComponentsInChildren<Renderer>().Any(r => r.name == "White Interaction Outline"), "Looking away removes white outline");
        holder.PickUp(false);
        player.OnStopLocalPlayer();
        Check(!holder.HasItem, "Losing local player clears inventory");
        player.OnStartLocalPlayer();

        // Render the real UI at a fixed display size for readability review.
        Screen.SetResolution(1280, 720, false);
        if (movedStation != null) movedStation.transform.position = originalPosition;
        movedStation = null;
        var bootstrap = Object.FindAnyObjectByType<PopcornMinigameBootstrap>();
        Transform anchor = (Transform)Get(bootstrap, "cashierUiAnchor");
        camera.transform.position = anchor.position - anchor.forward * 1.2f;
        camera.transform.LookAt(anchor.position, anchor.up);
        var orderText = (TMPro.TMP_Text)Get(manager, "orderText");
        orderText.text = PopcornRecipe.OrderLabel(PopcornCustomerType.Ghost, PopcornFlavor.Paprika);
        for (int i = 0; i < 4; i++) yield return null;
        orderText.ForceMeshUpdate();
        Check(!orderText.isTextOverflowing, "Cashier order and ghost requirement fit the display");
        Capture("popcorn-cashier.png");
        for (int i = 0; i < 4; i++) yield return null;
        holder.PickUp(true);
        Aim(water);
        Input(true);
        preparation.Interact(water);
        Set(preparation, "elapsed", 1.5f);
        Call(preparation, "UpdateProgress");
        var panel = (GameObject)Get(preparation, "progressPanel");
        // Keep a snapshot of the 50% HUD while the headless editor resets input.
        Object.Instantiate(panel, panel.transform.parent, false);
        preparation.enabled = false;
        Input(false);
        for (int i = 0; i < 4; i++) yield return null;
        Capture("popcorn-progress.png");
        for (int i = 0; i < 4; i++) yield return null;

        // Exercise real Mirror commands and generated snapshot serialization.
        Object.Destroy(player.gameObject);
        yield return null;
        var networkObject = new GameObject("Recovery Host Test");
        var transport = networkObject.AddComponent<kcp2k.KcpTransport>();
        transport.Port = 17994;
        var network = networkObject.AddComponent<NetworkManager>();
        var authoredNetwork = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/NetworkManager.prefab").GetComponent<NetworkManager>();
        network.transport = transport;
        network.playerPrefab = authoredNetwork.playerPrefab;
        network.spawnPrefabs = new List<GameObject>(authoredNetwork.spawnPrefabs);
        network.StartHost();
        for (int i = 0; i < 120 && NetworkClient.localPlayer == null; i++) yield return null;
        Check(NetworkClient.localPlayer != null, "Mirror host player connected");
        player = PlayerHealth.LocalInstance;
        player.GetComponent<PlayerMovement>().enabled = false;
        player.GetComponent<Rigidbody>().isKinematic = true;
        ghostManager.PlayerToggleLights(false);
        Set(recovery, "darkSeconds", 0.6f);
        Call(recovery, "Update");
        Check(PopcornNetSync.Instance.GhostFavor.displaced, "Host publishes blackout displacement");
        player.transform.position = recovery.VisibleState.position + Vector3.back;
        recovery.RequestInteraction();
        for (int i = 0; i < 10; i++) yield return null;
        Check(recovery.VisibleState.carrierId == player.netId && recovery.IsLocalCarrier,
            "Mirror pickup command assigns requesting player");
        var writer = new NetworkWriter();
        writer.Write(recovery.VisibleState);
        var snapshot = new NetworkReader(writer.ToArraySegment()).Read<GhostFavorState>();
        Check(snapshot.displaced && snapshot.carrierId == player.netId && snapshot.position == recovery.VisibleState.position,
            "Mirror serializes complete carried state for remote and late-joining clients");
        recovery.RequestInteraction(true);
        for (int i = 0; i < 10; i++) yield return null;
        Check(recovery.VisibleState.carrierId == 0 && !recovery.IsHome, "Mirror drop command releases shared object");
        Check(!favorBody.isKinematic && favorBody.useGravity &&
            Vector3.Distance(recovery.VisibleState.position, favorBody.position) < 0.1f,
            "Host publishes the falling rigidbody pose");
        var secondPlayer = Object.Instantiate(network.playerPrefab);
        NetworkServer.Spawn(secondPlayer);
        var secondHealth = secondPlayer.GetComponent<PlayerHealth>();
        secondPlayer.transform.position = recovery.VisibleState.position;
        recovery.ServerInteract(secondHealth, false);
        Call(recovery, "Update");
        Check(recovery.VisibleState.carrierId == secondHealth.netId, "Server accepts a different network player as carrier");
        NetworkServer.Destroy(secondPlayer);
        yield return null;
        Call(recovery, "Update");
        Check(recovery.VisibleState.carrierId == 0 && !recovery.IsHome, "Despawned carrier releases item for recovery");
        player.transform.position = recovery.VisibleState.position;
        recovery.RequestInteraction();
        for (int i = 0; i < 10; i++) yield return null;
        player.transform.position = home;
        Call(recovery, "Update");
        Check(recovery.IsHome && recovery.VisibleState.carrierId == 0, "Host publishes completed return");
        network.StopHost();
    }
    static void Capture(string path)
    {
        // ScreenCapture does not write frames in a headless batch editor.
        var overlays = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude)
            .Where(c => c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
        foreach (var canvas in overlays)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = .3f;
        }
        var render = new RenderTexture(1280, 720, 24);
        var previous = RenderTexture.active;
        camera.targetTexture = render;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture.active = render;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = previous;
        foreach (var canvas in overlays) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        Object.Destroy(image);
        Object.Destroy(render);
    }

    static void Tick()
    {
        if (checks == null) return;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timed out");
            if (checks.MoveNext()) return;
        }
        catch (Exception exception) { Check(false, exception.ToString()); }
        checks = null;
        SessionState.SetBool("PopcornRegression", false);
        File.WriteAllLines("popcorn-results.txt", results);
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }
}
