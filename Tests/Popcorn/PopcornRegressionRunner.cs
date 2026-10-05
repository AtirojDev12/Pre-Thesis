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
        HeldItemsSetup.Install();
        EditorSceneManager.OpenScene("Assets/Scenes/Map/Cinema_GamePlay.unity");
        var sceneBootstrap = Object.FindAnyObjectByType<PopcornMinigameBootstrap>();
        var sceneMaker = (Transform)Get(sceneBootstrap, "refillMaker");
        if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sceneMaker.gameObject) != "Assets/Prefab/PrefabModel/Popcorn_Maker.prefab")
            throw new Exception("Refill machine must reference the authored Popcorn_Maker prefab instance");
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
    static void SeatOrder(PopcornNetSync sync, PopcornCustomerType type, PopcornFlavor first, PopcornFlavor second = PopcornFlavor.None)
    {
        if (slot.ActiveCustomer != null)
        {
            var previous = slot.ActiveCustomer;
            slot.Vacate(previous);
            previous.gameObject.SetActive(false);
            Object.Destroy(previous.gameObject);
        }
        Set(sync, "currentOrder", PopcornOrderState.Create(sync.Order.id + 1, type, first, second));
        Call(manager, "SeatCustomerFromSync");
        var customer = slot.ActiveCustomer;
        var stateField = typeof(PopcornCustomer).GetField("state", Private);
        stateField.SetValue(customer, Enum.ToObject(stateField.FieldType, 1));
        player.transform.position = customer.transform.position + Vector3.back;
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
            if ((holder.IsReady || holder.HasPopcorn || holder.IsRefill) && simulatedSeconds < 2.999f) completedEarly = true;
        }
        Input(false);
        Check((holder.IsReady || holder.HasPopcorn || holder.IsRefill) && !completedEarly && simulatedSeconds >= 2.999f && simulatedSeconds < 3f + Time.deltaTime * 2f,
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
        Check(stations.Length == 16, "All sixteen authored stations bound, including tank, maker and eight drink controls");
        var bootstrapSetup = Object.FindAnyObjectByType<PopcornMinigameBootstrap>();
        var makerReference = (Transform)Get(bootstrapSetup, "refillMaker");
        Check(makerReference.name == "Popcorn_Maker", "Refill station binds the authored maker instance checked before Play mode");
        int configuredCapacity = preparation.Capacity;
        Check(preparation.TankRemaining == configuredCapacity && preparation.TankCapacity == configuredCapacity, "Tank starts full at configured capacity");
        var tankCanvas = (Canvas)Get(preparation, "tankCanvas");
        Check(tankCanvas != null && tankCanvas.renderMode == RenderMode.WorldSpace &&
            tankCanvas.transform.parent == Station(PopcornStationKind.Tank).transform,
            "Supply display is a world-space canvas attached directly to PopCornTank");
        Check(!preparation.GetComponentsInChildren<Canvas>().Any(c => c.transform.Find("Tank Supply") != null),
            "Player preparation HUD has no remaining-popcorn display");
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
        Press(cheese);
        Check(!holder.IsReady && !holder.HasPopcorn, "Cannot choose flavor before scooping from tank");
        var tank = Station(PopcornStationKind.Tank);
        Aim(tank);
        Input(true);
        preparation.Interact(tank);
        Call(preparation, "Update");
        Check(preparation.IsPreparing && !holder.IsReady, "Preparation remains incomplete after one tick");
        Input(false);
        Call(preparation, "Update");
        Check(!preparation.IsPreparing && !holder.IsReady, "Releasing E cancels scooping without filling");
        var filling = Fill(tank);
        while (filling.MoveNext()) yield return null;
        Check(holder.HasPopcorn && !holder.IsReady && holder.HeldFlavor == PopcornFlavor.None && preparation.TankRemaining == configuredCapacity - 1,
            "Tank gives unflavored HeldPopcorn and consumes exactly one serving");
        Check(((GameObject)Get(holder, "heldVisual")).name.Contains("HeldPopcorn"), "Scoop uses authored HeldPopcorn prefab");
        Press(cheese);
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
        holder.Scoop();
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
        var water = Station(PopcornStationKind.Water, PopcornFlavor.Drink);
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
        var maker = Station(PopcornStationKind.Maker);
        filling = Fill(maker);
        while (filling.MoveNext()) yield return null;
        Check(holder.IsRefill && !holder.IsReady && ((GameObject)Get(holder, "heldVisual")).name.Contains("NewPopcorn"),
            "Maker gives authored NewPopcorn batch, which cannot be served");
        Press(tank);
        Check(preparation.TankRemaining == configuredCapacity && !holder.HasItem, "Top-up caps stock at maximum and consumes batch");
        filling = Fill(maker);
        while (filling.MoveNext()) yield return null;
        Press(tank);
        Check(preparation.TankRemaining == configuredCapacity && holder.IsRefill, "Full tank rejects refill and retains held batch");
        Set(preparation, "offlineRemaining", 0);
        Press(tank);
        Check(preparation.TankRemaining == 10 && !holder.HasItem, "Empty tank accepts ten-serving refill");
        holder.PickUp(false);
        Set(preparation, "offlineRemaining", 0);
        Press(tank);
        Check(!preparation.IsPreparing && !holder.HasPopcorn, "Empty tank rejects scooping without granting popcorn");
        holder.Consume();
        Set(preparation, "offlineRemaining", configuredCapacity);
        foreach (var drink in new[] { PopcornFlavor.Fanta, PopcornFlavor.OrangeJuice, PopcornFlavor.Pepsi })
        {
            holder.PickUp(true);
            var dispenser = Station(PopcornStationKind.Water, drink);
            filling = Fill(dispenser);
            while (filling.MoveNext()) yield return null;
            string expected = drink == PopcornFlavor.Fanta ? "PapperBotteFanta" : drink == PopcornFlavor.Pepsi ? "PapperBottePepsi" : "PapperBotteOrangeJuice";
            Check(holder.HeldFlavor == drink && ((GameObject)Get(holder, "heldVisual")).name.Contains(expected), drink + " uses correct prepared prefab");
            Check(holder.MixGhost() && holder.GhostMixed && holder.HeldFlavor == drink &&
                ((GameObject)Get(holder, "heldVisual")).name.Contains("PapperBotteWaterGhost"),
                drink + " ghost seasoning swaps to authored ghost bottle while preserving ordered drink");
            holder.Consume();
        }
        Check(stations.Count(st => st.Kind == PopcornStationKind.Water && st.Flavor == PopcornFlavor.Drink) == 2 &&
            stations.Count(st => st.Kind == PopcornStationKind.Water && st.Flavor == PopcornFlavor.Fanta) == 2 &&
            stations.Count(st => st.Kind == PopcornStationKind.Water && st.Flavor == PopcornFlavor.OrangeJuice) == 2 &&
            stations.Count(st => st.Kind == PopcornStationKind.Water && st.Flavor == PopcornFlavor.Pepsi) == 2,
            "Every drink binds exactly two authored dispenser controls");
        var generatedOrders = new HashSet<PopcornFlavor>();
        for (int i = 0; i < 1000; i++) generatedOrders.Add(PopcornRecipe.RandomOrder());
        Check(generatedOrders.Count == 7 && generatedOrders.All(PopcornRecipe.IsOrder), "NPC menu includes all seven food and drink orders");
        var waterTask = new ZoneTask { kind = ZoneTaskKind.Water, flavor = PopcornFlavor.Drink };
        Check(waterTask.Matches(ZoneTaskKind.Water, PopcornFlavor.Drink, -1, false) &&
            !waterTask.Matches(ZoneTaskKind.Water, PopcornFlavor.Pepsi, -1, false), "Existing Water task counts only water");
        foreach (var flavor in new[] { PopcornFlavor.Cheese, PopcornFlavor.BBQ, PopcornFlavor.Paprika, PopcornFlavor.Drink,
            PopcornFlavor.Fanta, PopcornFlavor.Pepsi, PopcornFlavor.OrangeJuice })
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
        SeatOrder(sync, PopcornCustomerType.Ghost, PopcornFlavor.Drink);
        var heldNetwork = player.GetComponent<PlayerHeldItems>();
        uint revision = heldNetwork.Publish(new TaskHeldState { hasItem = true, isCup = true, flavor = PopcornFlavor.Drink });
        sync.RequestServe(sync.Order.id, revision);
        Check(sync.Score == score && player.CurrentHealth == health - 10f, "Missing ghost mix keeps original penalty and score");
        SeatOrder(sync, PopcornCustomerType.Ghost, PopcornFlavor.Drink);
        revision = heldNetwork.Publish(new TaskHeldState { hasItem = true, isCup = true, flavor = PopcornFlavor.Drink, ghostMixed = true });
        sync.RequestServe(sync.Order.id, revision);
        Check(sync.Score == score + 1 && !sync.CustomerWaiting, "Correct ghost drink scores once and closes order");
        sync.RequestServe(sync.Order.id, revision);
        Check(sync.Score == score + 1, "Repeated serve cannot score twice");
        UnityEngine.Random.InitState(314159);
        int doubles = 0;
        var pairs = new HashSet<string>();
        for (uint i = 1; i <= 20000; i++)
        {
            var generated = PopcornRecipe.RandomCustomerOrder(i, PopcornCustomerType.Human);
            if (generated.Count == 2) { doubles++; pairs.Add(generated.first + "/" + generated.second); }
        }
        Check(doubles > 5700 && doubles < 6300, "Order generation follows the 70/30 split over 20000 customers");
        Check(pairs.Count == 49, "Every ordered pair is possible, including duplicate products and same-category pairs");
        int beforeMulti = sync.Score;
        SeatOrder(sync, PopcornCustomerType.Human, PopcornFlavor.BBQ, PopcornFlavor.Pepsi);
        var originalCustomer = slot.ActiveCustomer;
        var bbqBoard = Object.FindObjectsByType<ZoneTaskList>(FindObjectsSortMode.None).First(b => b.TaskCount > 0 && b.TaskAt(0).kind == ZoneTaskKind.Popcorn);
        int bbqLine = Enumerable.Range(0, bbqBoard.TaskCount).First(i => bbqBoard.TaskAt(i).flavor == PopcornFlavor.BBQ);
        int bbqBefore = bbqBoard.ProgressOf(bbqLine);
        revision = heldNetwork.Publish(new TaskHeldState { hasItem = true, hasPopcorn = true, flavor = PopcornFlavor.BBQ });
        uint multiId = sync.Order.id;
        sync.RequestServe(multiId, revision);
        Check(sync.CustomerWaiting && sync.Order.DeliveredCount == 1 && slot.ActiveCustomer == originalCustomer,
            "First correct delivery preserves the original two-item NPC and partial progress");
        Check(sync.Score == beforeMulti + 1 && bbqBoard.ProgressOf(bbqLine) == Mathf.Min(bbqBefore + 1, bbqBoard.TaskAt(bbqLine).target),
            "Each accepted item retains existing score and flavor-task credit");
        sync.RequestServe(multiId, revision);
        Check(sync.CustomerWaiting && sync.Score == beforeMulti + 1, "Duplicate delivery request cannot consume or score a second item");
        revision = heldNetwork.Publish(new TaskHeldState { hasItem = true, isCup = true, flavor = PopcornFlavor.Pepsi });
        sync.RequestServe(multiId, revision);
        Check(!sync.CustomerWaiting && sync.Order.Complete && sync.Score == beforeMulti + 2,
            "Second correct delivery completes the order and scores independently");
        SeatOrder(sync, PopcornCustomerType.Human, PopcornFlavor.Pepsi, PopcornFlavor.Pepsi);
        Check(sync.Order.Label().Contains("×2") && sync.Order.Label().Contains("0/2"), "Repeated product order groups as ×2 with quantity progress");
        revision = heldNetwork.Publish(new TaskHeldState { hasItem = true, isCup = true, flavor = PopcornFlavor.Pepsi });
        sync.RequestServe(sync.Order.id, revision);
        Check(sync.CustomerWaiting && sync.Order.Label().Contains("1/2"), "First of two identical drinks leaves one outstanding");
        revision = heldNetwork.Publish(new TaskHeldState { hasItem = true, isCup = true, flavor = PopcornFlavor.Pepsi });
        sync.RequestServe(sync.Order.id, revision);
        Check(!sync.CustomerWaiting && sync.Order.Complete, "Second identical drink fulfills the second entry exactly once");
        SeatOrder(sync, PopcornCustomerType.Human, PopcornFlavor.BBQ, PopcornFlavor.Paprika);
        revision = heldNetwork.Publish(new TaskHeldState { hasItem = true, hasPopcorn = true, flavor = PopcornFlavor.BBQ });
        sync.RequestServe(sync.Order.id, revision);
        int earnedBeforeMistake = sync.Score;
        revision = heldNetwork.Publish(new TaskHeldState { hasItem = true, isCup = true, flavor = PopcornFlavor.Drink });
        sync.RequestServe(sync.Order.id, revision);
        Check(!sync.CustomerWaiting && !sync.Order.Complete && sync.Score == earnedBeforeMistake && !heldNetwork.TaskItem.hasItem,
            "Wrong second delivery consumes the item, sends NPC away and preserves earlier earned credit");
        SeatOrder(sync, PopcornCustomerType.Human, PopcornFlavor.Drink);
        revision = heldNetwork.Publish(new TaskHeldState { hasItem = true, isCup = true, flavor = PopcornFlavor.Drink });
        int beforeStale = sync.Score;
        sync.RequestServe(multiId, revision);
        Check(sync.CustomerWaiting && heldNetwork.TaskItem.IsReady && sync.Score == beforeStale,
            "A stale order ID cannot consume a held item or serve the next customer");
        var orderWriter = new NetworkWriter();
        orderWriter.Write(sync.Order);
        var orderSnapshot = new NetworkReader(orderWriter.ToArraySegment()).Read<PopcornOrderState>();
        Check(orderSnapshot.id == sync.Order.id && orderSnapshot.first == sync.Order.first && orderSnapshot.waiting,
            "Mirror serializes complete atomic order snapshots for remote and late-joining clients");
        Check(!manager.GetComponentsInChildren<TMPro.TMP_Text>().Any(t => t.name == "Score Text" || t.text.Contains("+1")),
            "Cashier has no score-total or +1 popup");
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

        // Render the tank-attached stock display before the other UI review images.
        if (movedStation != null) movedStation.transform.position = originalPosition;
        movedStation = null;
        camera.transform.position = tankCanvas.transform.position + Vector3.back * 1.2f;
        camera.transform.LookAt(tankCanvas.transform.position);
        for (int i = 0; i < 4; i++) yield return null;
        var remainingText = (TMPro.TMP_Text)Get(preparation, "tankText");
        remainingText.ForceMeshUpdate();
        Check(!remainingText.isTextOverflowing && remainingText.text.Contains(configuredCapacity + " / " + configuredCapacity),
            "World-space tank stock label fits and shows current supply");
        Capture("popcorn-tank.png");

        // Render the real UI at a fixed display size for readability review.
        Screen.SetResolution(1280, 720, false);
        if (movedStation != null) movedStation.transform.position = originalPosition;
        movedStation = null;
        var bootstrap = Object.FindAnyObjectByType<PopcornMinigameBootstrap>();
        Transform anchor = (Transform)Get(bootstrap, "cashierUiAnchor");
        camera.transform.position = anchor.position - anchor.forward * 1.2f;
        camera.transform.LookAt(anchor.position, anchor.up);
        var orderText = (TMPro.TMP_Text)Get(manager, "orderText");
        SeatOrder(sync, PopcornCustomerType.Ghost, PopcornFlavor.Paprika, PopcornFlavor.OrangeJuice);
        ((TMPro.TMP_Text)Get(manager, "feedbackText")).text = "";
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
        camera = player.GetComponentInChildren<Camera>();
        interactor = player.GetComponent<PlayerInteractor>();
        player.GetComponentInChildren<FirstPersonCamera>().enabled = false;
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
        var secondHeld = secondPlayer.GetComponent<PlayerHeldItems>();
        secondPlayer.GetComponent<PlayerInventory>().ServerAddItem(ItemCatalog.WalkieTalkie);
        Call(secondHeld, "SetTask", new TaskHeldState { revision = 1, hasItem = true, hasPopcorn = true, flavor = PopcornFlavor.BBQ, ghostMixed = true });
        Call(secondHeld, "LateUpdate");
        var remoteTask = secondPlayer.transform.Find("Remote task item");
        var remoteRadio = secondPlayer.transform.Find("Remote walkie-talkie");
        Check(remoteTask != null && remoteRadio != null && remoteTask.gameObject.activeSelf && remoteRadio.gameObject.activeSelf,
            "A remote player displays task item and radio together");
        Transform chest = secondPlayer.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Chest);
        Vector3 chestCentre = chest != null ? chest.position : secondPlayer.transform.position + Vector3.up * 1.3f;
        Check(Vector3.Distance(remoteTask.position, chestCentre) < .5f && Vector3.Distance(remoteRadio.position, chestCentre) < .5f,
            "Both remote visuals sit around the model chest rather than its camera or feet");
        Check(!remoteTask.GetComponentsInChildren<Collider>().Any(c => c.enabled) && !remoteRadio.GetComponentsInChildren<Collider>().Any(c => c.enabled),
            "Remote held visuals have no active pickup/physics colliders");
        var heldWriter = new NetworkWriter();
        secondHeld.OnSerialize(heldWriter, true);
        Call(secondHeld, "SetTask", new TaskHeldState { revision = 2 });
        secondHeld.OnDeserialize(new NetworkReader(heldWriter.ToArraySegment()), true);
        Check(secondHeld.TaskItem.hasItem && secondHeld.TaskItem.flavor == PopcornFlavor.BBQ && secondHeld.TaskItem.ghostMixed,
            "Mirror initial held-item snapshot preserves product and Ghost Flavor");
        uint remoteRevision = 10;
        foreach (var state in new[] {
            new TaskHeldState { hasItem = true }, new TaskHeldState { hasItem = true, isCup = true },
            new TaskHeldState { hasItem = true, isRefill = true },
            new TaskHeldState { hasItem = true, hasPopcorn = true },
            new TaskHeldState { hasItem = true, hasPopcorn = true, flavor = PopcornFlavor.Cheese },
            new TaskHeldState { hasItem = true, hasPopcorn = true, flavor = PopcornFlavor.Paprika },
            new TaskHeldState { hasItem = true, isCup = true, flavor = PopcornFlavor.Drink },
            new TaskHeldState { hasItem = true, isCup = true, flavor = PopcornFlavor.Pepsi },
            new TaskHeldState { hasItem = true, isCup = true, flavor = PopcornFlavor.Fanta },
            new TaskHeldState { hasItem = true, isCup = true, flavor = PopcornFlavor.OrangeJuice } })
        {
            var visible = state; visible.revision = ++remoteRevision;
            Call(secondHeld, "SetTask", visible);
            Call(secondHeld, "LateUpdate");
            Check(secondPlayer.transform.Find("Remote task item") != null && secondPlayer.transform.Find("Remote walkie-talkie") != null,
                "Remote chest models support " + (state.isRefill ? "refill supply" : state.flavor.ToString()) + " alongside radio");
        }
        Call(secondHeld, "SetTask", new TaskHeldState { revision = ++remoteRevision });
        Call(secondHeld, "LateUpdate");
        Check(!secondPlayer.GetComponentsInChildren<Transform>().Any(t => t.name == "Remote task item" && t.gameObject.activeSelf),
            "Clearing held state immediately hides the remote task model");
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
        preparation.enabled = true;
        holder.Consume();
        var supply = PopcornNetSync.Instance;
        Check(supply.TankRemaining == configuredCapacity && supply.TankCapacity == configuredCapacity, "Host initializes authoritative tank full");
        player.transform.position = new Vector3(1000, 3, 1000);
        supply.RequestSupply(tank.Id, 900, PopcornSupplyAction.Begin);
        supply.RequestSupply(tank.Id, 900, PopcornSupplyAction.Complete);
        for (int i = 0; i < 8; i++) yield return null;
        Check(supply.TankRemaining == configuredCapacity, "Out-of-range supply requests cannot change stock");
        player.transform.position = tank.GetComponent<Collider>().bounds.center;
        supply.RequestSupply(tank.Id, 901, PopcornSupplyAction.Complete);
        for (int i = 0; i < 8; i++) yield return null;
        Check(supply.TankRemaining == configuredCapacity, "Completion without a server-started action is rejected");
        supply.RequestSupply(tank.Id, 902, PopcornSupplyAction.Begin);
        supply.RequestSupply(tank.Id, 902, PopcornSupplyAction.Complete);
        for (int i = 0; i < 8; i++) yield return null;
        Check(supply.TankRemaining == configuredCapacity, "Server rejects skipping preparation duration");
        supply.RequestSupply(tank.Id, 903, PopcornSupplyAction.Begin);
        supply.RequestSupply(tank.Id, 903, PopcornSupplyAction.Cancel);
        for (double until = NetworkTime.time + 3.2; NetworkTime.time < until;) yield return null;
        supply.RequestSupply(tank.Id, 903, PopcornSupplyAction.Complete);
        for (int i = 0; i < 8; i++) yield return null;
        Check(supply.TankRemaining == configuredCapacity, "Cancelled server action consumes no popcorn");
        holder.PickUp(false);
        Set(preparation, "activeStation", tank);
        Set(preparation, "preparingPlayer", player);
        Set(preparation, "requestId", 904);
        Set(preparation, "awaitingStart", true);
        Set(preparation, "awaitingResult", true); // Keep the input fixture stable while real network time elapses.
        supply.RequestSupply(tank.Id, 904, PopcornSupplyAction.Begin);
        for (int i = 0; i < 8; i++) yield return null;
        Check(!(bool)Get(preparation, "awaitingStart"), "Server acknowledgement starts local preparation timer");
        Check(supply.SoundActions.TryGetValue(player.netId, out var scoopSound) && scoopSound.station == tank.Id && scoopSound.request == 904,
            "Server-approved scoop publishes one sound action keyed by the requesting player");
        var soundWriter = new NetworkWriter();
        supply.SoundActions.OnSerializeAll(soundWriter);
        var observerSounds = new SyncDictionary<uint, PopcornSoundAction>();
        observerSounds.OnDeserializeAll(new NetworkReader(soundWriter.ToArraySegment()));
        Check(observerSounds.TryGetValue(player.netId, out var observerSound) && observerSound.station == tank.Id && observerSound.started == scoopSound.started,
            "Joining observer snapshot restores the active station and shared sound start time");
        for (double until = NetworkTime.time + 3.2; NetworkTime.time < until;) yield return null;
        supply.RequestSupply(tank.Id, 904, PopcornSupplyAction.Complete);
        for (int i = 0; i < 8; i++) yield return null;
        Check(supply.TankRemaining == configuredCapacity - 1 && holder.HasPopcorn && !holder.IsReady,
            "Real Mirror completion decrements stock and gives plain popcorn only to requesting player");
        Check(!supply.SoundActions.ContainsKey(player.netId), "Scoop completion removes the shared loop exactly once");
        supply.RequestSupply(tank.Id, 904, PopcornSupplyAction.Complete);
        for (int i = 0; i < 8; i++) yield return null;
        Check(supply.TankRemaining == configuredCapacity - 1, "Repeated completion cannot decrement twice");
        player.transform.position = maker.GetComponentInChildren<Collider>().bounds.center;
        holder.Consume();
        Set(preparation, "activeStation", maker);
        Set(preparation, "preparingPlayer", player);
        Set(preparation, "requestId", 905);
        Set(preparation, "awaitingResult", true);
        supply.RequestSupply(maker.Id, 905, PopcornSupplyAction.Begin);
        for (double until = NetworkTime.time + 3.2; NetworkTime.time < until;) yield return null;
        supply.RequestSupply(maker.Id, 905, PopcornSupplyAction.Complete);
        for (int i = 0; i < 8; i++) yield return null;
        Check(holder.IsRefill && ((GameObject)Get(holder, "heldVisual")).name.Contains("NewPopcorn"),
            "Mirror maker response gives requesting player authored NewPopcorn");
        player.transform.position = tank.GetComponent<Collider>().bounds.center;
        Set(supply, "tankRemaining", configuredCapacity);
        supply.RequestSupply(tank.Id, 906, PopcornSupplyAction.Refill);
        for (int i = 0; i < 8; i++) yield return null;
        Check(supply.TankRemaining == configuredCapacity, "Server refuses topping up full tank");
        Set(supply, "tankRemaining", configuredCapacity - 1);
        preparation.Interact(tank);
        for (int i = 0; i < 8; i++) yield return null;
        Check(supply.TankRemaining == configuredCapacity && !holder.HasItem,
            "Full-tank rejection preserves batch; accepted Mirror top-up caps stock and consumes local NewPopcorn");
        Set(supply, "tankRemaining", 0);
        supply.RequestSupply(tank.Id, 908, PopcornSupplyAction.Refill);
        for (int i = 0; i < 8; i++) yield return null;
        Check(supply.TankRemaining == 0, "Consumed batch cannot refill twice");
        Set(supply, "tankRemaining", 7);
        var stockWriter = new NetworkWriter();
        supply.OnSerialize(stockWriter, true);
        Set(supply, "tankRemaining", 0);
        supply.OnDeserialize(new NetworkReader(stockWriter.ToArraySegment()), true);
        Check(supply.TankRemaining == 7 && supply.TankCapacity == configuredCapacity, "Mirror initial snapshot carries remaining stock and capacity for joining clients");
        holder.Consume();
        holder.PickUp(true);
        Aim(water);
        player.transform.position = water.GetComponentInChildren<Collider>().bounds.center;
        for (int i = 0; i < 8; i++) yield return null;
        Set(preparation, "activeStation", water);
        Set(preparation, "preparingPlayer", player);
        Set(preparation, "requestId", 910);
        Set(preparation, "awaitingStart", true);
        Set(preparation, "awaitingResult", true);
        supply.RequestSupply(water.Id, 910, PopcornSupplyAction.Begin);
        for (int i = 0; i < 8; i++) yield return null;
        Check(supply.SoundActions.TryGetValue(player.netId, out var waterSound) && waterSound.station == water.Id,
            "Water hold waits for server approval and publishes its dispenser sound sequence");
        supply.RequestSupply(water.Id, 910, PopcornSupplyAction.Cancel);
        for (int i = 0; i < 8; i++) yield return null;
        Check(!supply.SoundActions.ContainsKey(player.netId) && !holder.IsReady, "Water release removes pouring state without filling the cup");
        Call(preparation, "Cancel", false, false);
        for (double until = NetworkTime.time + 0.25; NetworkTime.time < until;) yield return null;
        Set(preparation, "activeStation", water);
        Set(preparation, "preparingPlayer", player);
        Set(preparation, "requestId", 911);
        Set(preparation, "awaitingResult", true);
        supply.RequestSupply(water.Id, 911, PopcornSupplyAction.Begin);
        for (double until = NetworkTime.time + 3.2; NetworkTime.time < until;) yield return null;
        supply.RequestSupply(water.Id, 911, PopcornSupplyAction.Complete);
        for (int i = 0; i < 8; i++) yield return null;
        Check(holder.IsReady && holder.IsCup && !supply.SoundActions.ContainsKey(player.netId) && supply.TankRemaining == 7,
            "Server-approved water completion fills the cup, stops pouring and consumes no popcorn stock");
        holder.Consume();
        for (int i = 0; i < 8; i++) yield return null;
        supply.RequestSupply(water.Id, 912, PopcornSupplyAction.Begin);
        for (int i = 0; i < 8; i++) yield return null;
        Check(!supply.SoundActions.ContainsKey(player.netId), "Water without an empty cup cannot broadcast pouring audio");

        var soundLibrary = Resources.Load<SoundLibrary>("Audio/SoundLibrary");
        foreach (string id in new[] { "Popcorn_Pickup", "Popcorn_Flavor", "Popcorn_WaterStart", "Popcorn_WaterPour", "Popcorn_WaterEnd", "Popcorn_Scoop", "Popcorn_Making" })
        {
            var entry = soundLibrary != null ? soundLibrary.Find(id) : null;
            Check(entry != null && entry.spatial && entry.category == SoundCategory.Sfx && entry.clips.Length == 1 && entry.clips[0] != null,
                "Popcorn sound library resolves a spatial SFX clip for " + id);
        }
        holder.Consume();
        SeatOrder(supply, PopcornCustomerType.Human, PopcornFlavor.Pepsi, PopcornFlavor.Pepsi);
        var networkCustomer = slot.ActiveCustomer;
        holder.PickUp(true);
        holder.Hold(PopcornFlavor.Pepsi);
        manager.TryServe(networkCustomer, player.gameObject);
        Check(holder.SubmissionPending, "Network submission locks the prepared item until server acknowledgement");
        for (int i = 0; i < 8; i++) yield return null;
        Check(!holder.SubmissionPending && !holder.HasItem && supply.CustomerWaiting && supply.Order.DeliveredCount == 1 && slot.ActiveCustomer == networkCustomer,
            "Real Mirror serve consumes once, unlocks hands and updates the same partial-order NPC");
        holder.PickUp(true);
        holder.Hold(PopcornFlavor.Pepsi);
        manager.TryServe(networkCustomer, player.gameObject);
        for (int i = 0; i < 8; i++) yield return null;
        Check(!holder.SubmissionPending && !holder.HasItem && !supply.CustomerWaiting && supply.Order.Complete,
            "Real Mirror second delivery completes duplicate order and clears the owner's item");
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
        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
            new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = render });
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
