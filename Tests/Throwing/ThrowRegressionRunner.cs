// Installed only into the isolated regression project by Run-ThrowChecks.ps1.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ThrowRegressionRunner
{
    static readonly List<string> results = new List<string>();
    static int failures;
    static IEnumerator checks;
    static double deadline;
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static ThrowRegressionRunner()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("ThrowRegression", false)) return;
            deadline = EditorApplication.timeSinceStartup + 90;
            checks = RunChecks();
            var loop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            var systems = loop.subSystemList.ToList();
            systems.Add(new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(ThrowRegressionRunner), updateDelegate = Tick });
            loop.subSystemList = systems.ToArray();
            UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(loop);
            EditorApplication.update += EditorApplication.Step;
        };
    }
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
        WalkieTalkiePrefabSetup.BuildAssets();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool("ThrowRegression", true);
        EditorApplication.EnterPlaymode();
    }
    static void Check(bool valid, string description)
    {
        results.Add((valid ? "PASS " : "FAIL ") + description);
        if (!valid) failures++;
    }
    static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    static void QInput(Keyboard keyboard, bool pressed)
    {
        // Editor batch stepping uses an Editor input update. Drive the same Dynamic
        // input phase as the running game, as the project's popcorn tests do.
        var inputManager = typeof(InputSystem).GetField("s_Manager", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        inputManager.GetType().GetMethod("OnBeforeUpdate", Private).Invoke(inputManager, new object[] { InputUpdateType.Dynamic });
        keyboard.MakeCurrent();
        InputState.Change(keyboard, pressed ? new KeyboardState(Key.Q) : new KeyboardState(), InputUpdateType.Dynamic);
        Cursor.lockState = CursorLockMode.Locked;
        GameplayInput.Blocked = false;
    }
    static PlayerInventory Player(string name, Vector3 position)
    {
        GameObject root = new GameObject(name);
        root.SetActive(false);
        root.AddComponent<NetworkIdentity>();
        root.AddComponent<PlayerInventory>();
        root.AddComponent<PlayerItemThrow>();
        root.transform.position = position;
        root.SetActive(true);
        return root.GetComponent<PlayerInventory>();
    }
    static WorldInventoryItem World(Vector3 position, bool power = false)
    {
        GameObject root = Object.Instantiate(Resources.Load<GameObject>(PlayerItemThrow.WorldPrefabPath), position, Quaternion.identity);
        WorldInventoryItem item = root.GetComponent<WorldInventoryItem>();
        item.Initialize(InventorySlot.Of(ItemCatalog.WalkieTalkie, power));
        return item;
    }
    static IEnumerator RunChecks()
    {
        PlayerInventory player = Player("Offline player", Vector3.zero);
        PlayerInventory other = Player("Other player", new Vector3(.2f, 0, 0));
        yield return null;
        player.OnStartServer();
        other.OnStartServer();
        Check(Resources.Load<GameObject>("Items/WalkieTalkie").GetComponent<WalkieTalkieVisual>() != null, "Real held visual prefab exists");
        GameObject prefab = Resources.Load<GameObject>(PlayerItemThrow.WorldPrefabPath);
        Check(prefab.GetComponent<NetworkTransformReliable>().syncDirection == SyncDirection.ServerToClient, "World physics transform uses server authority");
        Check(new SerializedObject(prefab.GetComponent<NetworkIdentity>()).FindProperty("_assetId").uintValue != 0,
            "World prefab persists a network asset ID for standalone builds");
        WorldInventoryItem world = World(new Vector3(0, 1, 1));
        Physics.SyncTransforms();
        world.Interact(player.gameObject);
        Check(player.GetSlot(0).itemId == ItemCatalog.WalkieTalkie && !player.GetSlot(0).poweredOn,
            "Pickup uses first empty slot and preserves powered-off state");
        Check(!world.CanInteract() && !world.GetComponent<Collider>().enabled &&
            world.GetComponentsInChildren<Renderer>().All(r => !r.enabled), "Successful pickup immediately disables world visuals, collider and interaction");
        world.Interact(other.gameObject);
        Check(other.GetSlot(0).IsEmpty, "Second pickup of the same world object grants nothing");
        yield return null;
        world = World(new Vector3(0, 1, 1));
        Physics.SyncTransforms();
        world.Interact(player.gameObject);
        Check(world.CanInteract() && world.GetComponent<Collider>().enabled, "Duplicate radio rejection leaves the world object available");
        world.Interact(other.gameObject);
        Check(other.GetSlot(0).itemId == ItemCatalog.WalkieTalkie && !world.CanInteract(), "Another eligible player can pick up a previously rejected object");
        yield return null;
        PlayerItemThrow throwing = player.GetComponent<PlayerItemThrow>();
        Call(throwing, "BeginCharge", 0, ItemCatalog.WalkieTalkie);
        Call(throwing, "ReleaseCharge", Vector3.forward, true);
        world = Object.FindAnyObjectByType<WorldInventoryItem>();
        Check(player.GetSlot(0).IsEmpty && world != null && world.GetComponent<Rigidbody>().linearVelocity.magnitude < 1,
            "Tap Q drop removes inventory item and gives only minimal launch speed");
        world.Interact(player.gameObject);
        yield return null;
        Call(throwing, "BeginCharge", 0, ItemCatalog.WalkieTalkie);
        Set(throwing, "serverStarted", Time.timeAsDouble - 5);
        Call(throwing, "ReleaseCharge", Vector3.forward, false);
        world = Object.FindAnyObjectByType<WorldInventoryItem>();
        Check(world != null && Mathf.Abs(world.GetComponent<Rigidbody>().linearVelocity.magnitude - 9f) < .01f,
            "Holding beyond full charge clamps launch speed to maximum");
        Call(throwing, "ReleaseCharge", Vector3.forward, false);
        Check(Object.FindObjectsByType<WorldInventoryItem>(FindObjectsSortMode.None).Length == 1, "Repeated release cannot create another world item");
        world.Interact(player.gameObject);
        yield return null;
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = new Vector3(0, 1, .4f);
        wall.transform.localScale = new Vector3(2, 2, .1f);
        Physics.SyncTransforms();
        Call(throwing, "BeginCharge", 0, ItemCatalog.WalkieTalkie);
        Call(throwing, "ReleaseCharge", Vector3.forward, true);
        Check(!player.HeldSlot.IsEmpty && Object.FindObjectsByType<WorldInventoryItem>(FindObjectsSortMode.None).Length == 0,
            "Wall-blocked drop preserves inventory and spawns nothing");
        world = World(new Vector3(0, 1, 1));
        Physics.SyncTransforms();
        player.ServerRemoveAt(0);
        world.Interact(player.gameObject);
        Check(player.HeldSlot.IsEmpty && world.CanInteract(), "Server-side obstruction rejects pickup through a wall");
        Object.Destroy(wall);
        Object.Destroy(world.gameObject);
        yield return null;
        var slots = (SyncList<InventorySlot>)typeof(PlayerInventory).GetField("slots", Private).GetValue(player);
        for (int i = 0; i < 4; i++) slots[i] = InventorySlot.Of("test_filler_" + i);
        world = World(new Vector3(0, 1, 1));
        Physics.SyncTransforms();
        world.Interact(player.gameObject);
        Check(world.CanInteract() && world.GetComponent<Collider>().enabled, "Full inventory rejection does not hide or consume the pickup");
        for (int i = 0; i < 4; i++) slots[i] = InventorySlot.Empty;
        slots[2] = InventorySlot.Of("test_filler");
        slots[0] = InventorySlot.Of("test_filler_0");
        world.Interact(player.gameObject);
        Check(player.GetSlot(1).itemId == ItemCatalog.WalkieTalkie, "Pickup fills the first available slot, not necessarily the selected slot");
        yield return null;
        for (int i = 0; i < 4; i++) slots[i] = InventorySlot.Empty;
        Call(player, "ApplyLoadout", (object)new string[] { ItemCatalog.WalkieTalkie });
        player.ServerRemoveAt(0);
        Call(player, "ApplyLoadout", (object)new string[] { ItemCatalog.WalkieTalkie });
        Check(player.HeldSlot.IsEmpty, "Repeated loadout request cannot recreate a dropped item");
        player.ServerAddItem(InventorySlot.Of(ItemCatalog.WalkieTalkie, false));
        Call(throwing, "BeginCharge", 0, ItemCatalog.WalkieTalkie);
        Set(throwing, "localCharging", true);
        Check(throwing.MovementMultiplier == .5f, "Charging applies the configured walking-speed reduction");
        Call(throwing, "CancelLocal");
        Call(throwing, "ReleaseCharge", Vector3.forward, false);
        Check(throwing.MovementMultiplier == 1f && !player.HeldSlot.IsEmpty &&
            Object.FindObjectsByType<WorldInventoryItem>(FindObjectsSortMode.None).Length == 0,
            "Cancellation restores speed and cannot release the held item afterward");
        Call(throwing, "BeginCharge", 0, ItemCatalog.WalkieTalkie);
        Set(throwing, "lastHeartbeat", Time.timeAsDouble - 3);
        Call(throwing, "ReleaseCharge", Vector3.forward, false);
        Check(!player.HeldSlot.IsEmpty, "Expired charge heartbeat cannot throw an item");
        Call(throwing, "BeginCharge", 0, ItemCatalog.WalkieTalkie);
        Call(throwing, "ReleaseCharge", new Vector3(float.NaN, 0, 0), false);
        Check(!player.HeldSlot.IsEmpty, "Invalid aim values preserve inventory");
        GameObject playerAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Player.prefab");
        Check(playerAsset.GetComponent<PlayerItemThrow>() != null, "Player prefab contains the throw controller");
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Cursor.lockState = CursorLockMode.Locked;
        Set(throwing, "focused", true);
        QInput(keyboard, true);
        // A batch editor cannot lock the native cursor. Supply the gameplay-enabled
        // gate while exercising the real input, charging, UI and release logic.
        Call(throwing, "UpdateLocalInput", true);
        Check(throwing.IsCharging, "Actual Q press starts charging the selected radio");
        Set(throwing, "localStarted", Time.unscaledTime - 3);
        Set(throwing, "serverStarted", Time.timeAsDouble - 3);
        Call(throwing, "UpdateLocalInput", true);
        Check(throwing.IsCharging && Object.FindObjectsByType<WorldInventoryItem>(FindObjectsSortMode.None).Length == 0,
            "Holding Q at maximum does not automatically throw");
        QInput(keyboard, false);
        Call(throwing, "UpdateLocalInput", true);
        world = Object.FindAnyObjectByType<WorldInventoryItem>();
        Check(!throwing.IsCharging && player.HeldSlot.IsEmpty && world != null,
            "Actual Q release throws once and clears the selected slot");
        if (world != null) Object.Destroy(world.gameObject);
        player.ServerAddItem(ItemCatalog.WalkieTalkie);
        QInput(keyboard, true);
        Call(throwing, "UpdateLocalInput", true);
        Call(throwing, "UpdateLocalInput", false);
        QInput(keyboard, false);
        Call(throwing, "UpdateLocalInput", true);
        Check(!throwing.IsCharging && !player.HeldSlot.IsEmpty && throwing.MovementMultiplier == 1,
            "Blocked gameplay cancels actual Q charge and release does not drop the item");
        InputSystem.RemoveDevice(keyboard);
        yield return null;
        ThrowChargeHUD hud = ThrowChargeHUD.Create();
        hud.Show("Walkie-Talkie", 1f);
        Check(hud.gameObject.activeSelf && hud.GetComponentsInChildren<TMPro.TMP_Text>().Any(t => t.text.StartsWith("MAX")), "Full charge UI remains visible with release hint");
        Capture(hud);
        hud.Hide();
        Check(!hud.gameObject.activeSelf, "Charge UI hides after release/cancellation");
        Object.Destroy(hud.gameObject);
        Object.Destroy(player.gameObject);
        Object.Destroy(other.gameObject);
        yield return null;

        // Real Mirror host lifecycle: spawn, server inventory mutation, RPC and despawn.
        GameObject managerRoot = new GameObject("Test network", typeof(TelepathyTransport), typeof(NetworkManager));
        NetworkManager manager = managerRoot.GetComponent<NetworkManager>();
        manager.transport = managerRoot.GetComponent<TelepathyTransport>();
        manager.autoCreatePlayer = false;
        manager.spawnPrefabs.Add(prefab);
        manager.StartHost();
        for (int i = 0; i < 6; i++) yield return null;
        player = Player("Network player", Vector3.zero);
        NetworkServer.Spawn(player.gameObject);
        other = Player("Network contender", new Vector3(.2f, 0, 0));
        NetworkServer.Spawn(other.gameObject);
        world = World(new Vector3(0, 1, 1));
        NetworkServer.Spawn(world.gameObject);
        uint worldId = world.netId;
        Physics.SyncTransforms();
        world.Interact(player.gameObject);
        world.Interact(other.gameObject);
        Check(player.GetSlot(0).itemId == ItemCatalog.WalkieTalkie && other.GetSlot(0).IsEmpty,
            "Mirror host: competing pickup grants exactly one inventory item");
        Check(!NetworkServer.spawned.ContainsKey(worldId) && !world.GetComponent<Collider>().enabled,
            "Mirror host: claimed object is immediately unspawned and disabled");
        for (int i = 0; i < 6; i++) yield return null;
        Check(!NetworkClient.spawned.ContainsKey(worldId), "Mirror client observes pickup despawn");
        manager.StopHost();
    }
    static void Capture(ThrowChargeHUD hud)
    {
        RenderPipelineAsset pipeline = GraphicsSettings.defaultRenderPipeline;
        RenderPipelineAsset qualityPipeline = QualitySettings.renderPipeline;
        GraphicsSettings.defaultRenderPipeline = null;
        QualitySettings.renderPipeline = null;
        Camera camera = new GameObject("Preview camera", typeof(Camera)).GetComponent<Camera>();
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.nearClipPlane = .1f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.13f, .15f, .18f);
        Canvas canvas = hud.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        var render = new RenderTexture(1280, 720, 24);
        var previous = RenderTexture.active;
        camera.targetTexture = render;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture.active = render;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        image.Apply();
        File.WriteAllBytes("throw-ui.png", image.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = previous;
        Object.Destroy(camera.gameObject);
        Object.Destroy(image);
        Object.Destroy(render);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = qualityPipeline;
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
        SessionState.SetBool("ThrowRegression", false);
        File.WriteAllLines("throw-results.txt", results);
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }
}
