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
    static PopcornItemHoldingSystem holder;
    static PopcornGameManager manager;
    static CounterSlot slot;
    static Keyboard keyboard;
    static PopcornStation[] stations;
    static PopcornStation movedStation;
    static Vector3 originalPosition;
    static Transform movedAnchor;
    static Vector3 originalAnchorPosition;

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
        SmokeSortingChecks.Run();
        EditorSceneManager.OpenScene("Assets/Scenes/Map/Cinema_GamePlay.unity");
        var sceneBootstrap = Object.FindAnyObjectByType<PopcornMinigameBootstrap>();
        var sceneMaker = (Transform)Get(sceneBootstrap, "refillMaker");
        if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sceneMaker.gameObject) != "Assets/Prefab/PrefabModel/Popcorn_Maker.prefab")
            throw new Exception("Refill machine must reference the authored Popcorn_Maker prefab instance");
        SessionState.SetBool("PopcornRegression", true);
        EditorApplication.EnterPlaymode();
    }
    static object Get(object target, string field) => target.GetType().GetField(field, Private)?.GetValue(target) ??
        target.GetType().GetProperty(field, Private)?.GetValue(target);
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
        if (movedAnchor != null) movedAnchor.position = originalAnchorPosition;
        movedAnchor = null;
        movedStation = station;
        originalPosition = station.transform.position;
        Vector3 fixture = station.Kind == PopcornStationKind.Water ? new Vector3(4000, 5, 4000) : new Vector3(1000, 5, 1000);
        if (station.PlacementAnchor != null && !station.PlacementAnchor.IsChildOf(station.transform))
        {
            movedAnchor = station.PlacementAnchor;
            originalAnchorPosition = movedAnchor.position;
            movedAnchor.position += fixture - originalPosition;
        }
        station.transform.position = fixture;
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
        if (station.Kind == PopcornStationKind.Water && !preparation.TryGetBottle(station, out _))
            preparation.Interact(station);
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
    static void CheckReusableHolding()
    {
        var fixture = new GameObject("Independent future minigame fixture");
        var hand = new GameObject("Generic hand anchor").transform;
        var placement = new GameObject("Generic workstation anchor").transform;
        hand.position = Vector3.one * 2000;
        placement.position = hand.position + Vector3.right * 3;
        placement.localScale = new Vector3(2, 3, 4);
        var raw = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var prepared = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        raw.transform.position = Vector3.one * -2000;
        prepared.transform.position = Vector3.one * -2003;
        var core = fixture.AddComponent<ItemHoldingSystem>();
        var definition = new HeldItemDefinition("future-minigame.product", raw, .4f);
        int changed = 0, cleared = 0;
        core.Changed += () => changed++;
        core.ItemCleared += item => { if (item == definition) cleared++; };
        Check(!core.TryPickUp(definition) && !core.HasItem && changed == 0,
            "Generic holding rejects missing hand anchors without changing state");
        core.ConfigureHandAnchor(hand, new Vector3(.1f, -.2f, .5f), new Vector3(0, 15, 0));
        Check(core.TryPickUp(definition) && core.IsInHand && core.Visual.transform.parent == hand && changed == 1,
            "Generic holding supports a future minigame item without popcorn recipes, player or UI dependencies");
        Check(!core.TryPickUp(definition) && !core.TryReplaceVisual(null) && core.CurrentPrefab == raw,
            "Generic single slot rejects replacement pickups and missing presentation prefabs");
        Check(core.TryPlace(placement, 27) && core.IsPlaced && core.PlacementId == 27 && !core.TryDiscardInHand(),
            "Generic placed items disable hand discard and retain the caller's workstation ID");
        Check(core.TryReplaceVisual(prepared) && core.IsPlaced && core.Definition.id == definition.id && core.CurrentPrefab == prepared,
            "Generic processing changes presentation while retaining product identity and placement");
        placement.position += Vector3.forward;
        Call(core, "LateUpdate");
        var bounds = core.Visual.GetComponentInChildren<Renderer>().bounds;
        Check(Vector3.Distance(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), placement.position) < .001f &&
            Mathf.Abs(Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) - .4f) < .001f,
            "Generic placement follows moved anchors and applies configurable size despite nonuniform workstation scale");
        core.SetTransferPending(true);
        Check(!core.TryRetrieve() && !core.TryReplaceVisual(raw), "Generic pending transfers lock conflicting actions");
        core.SetTransferPending(false);
        Check(core.TryRetrieve() && core.IsInHand && core.PlacementAnchor == null && core.CurrentPrefab == prepared &&
            !core.Visual.GetComponentsInChildren<Collider>().Any(c => c.enabled),
            "Generic retrieval keeps the prepared visual and restores safe hand placement");
        Check(core.TryBeginSubmission(out uint token) && !core.TryDiscardInHand() && !core.TryPlace(placement) &&
            !core.FinishSubmission(token + 1, true) && core.SubmissionPending,
            "Generic submissions lock hand actions and reject stale acknowledgements");
        Check(core.FinishSubmission(token, false) && core.HasItem && !core.SubmissionPending,
            "Rejected generic submissions unlock without consuming the item");
        core.TryBeginSubmission(out token);
        Check(core.FinishSubmission(token, true) && !core.HasItem && cleared == 1,
            "Accepted generic submissions clear the item and notify its minigame exactly once");
        core.Clear();
        Check(cleared == 1 && core.TryPickUp(definition), "Generic cleanup is idempotent and permits the next item");
        var lastVisual = core.Visual;
        core.enabled = false;
        Check(!core.HasItem && !lastVisual.activeSelf && cleared == 2, "Generic component teardown cleans up its slot and detached visuals");
        Object.Destroy(fixture); Object.Destroy(hand.gameObject); Object.Destroy(placement.gameObject);
        Object.Destroy(raw); Object.Destroy(prepared);
    }

    static IEnumerator CheckMachineBottles(bool online, PopcornStation first)
    {
        holder.Consume();
        var second = stations.First(s => s.Kind == PopcornStationKind.Water && s.PlacementAnchor != first.PlacementAnchor && s.Flavor == PopcornFlavor.Drink);
        foreach (var machine in new[] { first, second })
        {
            Aim(machine);
            player.transform.position = machine.PlacementAnchor.position;
            holder.PickUp(true);
            for (int i = 0; i < 8; i++) yield return null;
            preparation.Interact(machine);
            for (int i = 0; i < 8; i++) yield return null;
            Check(!holder.HasItem && preparation.TryGetBottle(machine, out var empty) && !empty.IsReady,
                "Placement frees the hand and creates a machine-owned empty bottle " + online);
        }
        Check(preparation.TryGetBottle(first, out _) && preparation.TryGetBottle(second, out _),
            "One player can leave bottles at both dispenser spots " + online);
        if (online)
        {
            var sync = PopcornNetSync.Instance;
            var peerObject = Object.Instantiate(Object.FindAnyObjectByType<NetworkManager>().playerPrefab);
            NetworkServer.Spawn(peerObject);
            var peerHeld = peerObject.GetComponent<PlayerHeldItems>();
            var peer = new NetworkConnectionToClient(777);
            typeof(NetworkConnection).GetProperty("identity").SetValue(peer, peerObject.GetComponent<NetworkIdentity>());
            preparation.TryGetBottle(first, out var original);
            peerObject.transform.position = Vector3.one * -2000;
            Check(!sync.ServerTryPickUpBottle(peer, preparation.SpotId(first), 0, original.revision, out _), "Server rejects out-of-range pickup");
            peerObject.transform.position = first.PlacementAnchor.position;
            Check(!sync.ServerTryPickUpBottle(peer, preparation.SpotId(first), 0, original.revision + 1, out _), "Server rejects stale bottle revisions");
            Check(sync.ServerTryPickUpBottle(peer, preparation.SpotId(first), 0, original.revision, out _) && peerHeld.TaskItem.isCup,
                "Teammate can collect a shared empty bottle with empty hands");
            Check(!sync.ServerTryPickUpBottle(player.connectionToClient, preparation.SpotId(first), holder.StateRevision, original.revision, out _),
                "Simultaneous pickup cannot duplicate the same bottle");
            Check(sync.ServerTryPlaceBottle(peer, first.Id, peerHeld.TaskItem.revision), "Teammate can replace the bottle at the freed spot");
            preparation.TryGetBottle(first, out var abandoned);
            CheckBottleWorkCleanup(sync, first, peer, abandoned, "disconnect");
            NetworkServer.Destroy(peerObject);
            yield return null;
            Check(preparation.TryGetBottle(first, out _) && preparation.TryGetBottle(second, out _), "Placed bottles persist independently of a player's despawn");
            player.transform.position = first.PlacementAnchor.position;
            preparation.TryGetBottle(first, out var reserved);
            var pendingRefills = (HashSet<NetworkConnectionToClient>)Get(sync, "heldRefills");
            pendingRefills.Add(player.connectionToClient);
            Check(!sync.ServerTryPickUpBottle(player.connectionToClient, preparation.SpotId(first), holder.StateRevision, reserved.revision, out _),
                "Pending server refill blocks a conflicting bottle pickup before its hand publication arrives");
            pendingRefills.Remove(player.connectionToClient);
            CheckBottleWorkCleanup(sync, first, player.connectionToClient, reserved, "timeout");
            CheckBottleWorkCleanup(sync, first, player.connectionToClient, reserved, "downed");
            CheckBottleObserverSnapshot(sync);
        }
        holder.PickUp(true);
        Aim(first);
        player.transform.position = first.PlacementAnchor.position;
        for (int i = 0; i < 8; i++) yield return null;
        preparation.Interact(first);
        for (int i = 0; i < 8; i++) yield return null;
        Check(holder.HasItem && preparation.TryGetBottle(first, out _), "Occupied spot rejects placement without consuming the held bottle " + online);
        holder.TryDiscardInHand();
        for (int i = 0; i < 8; i++) yield return null;
        Check(preparation.TryGetBottle(first, out _) && preparation.TryGetBottle(second, out _), "R only discards the hand item and preserves all machine bottles " + online);
        Call(preparation, "RenderPlacedBottles");
        var visual = preparation.BottleVisualAt(first);
        var bounds = visual.GetComponentInChildren<Renderer>().bounds;
        camera.transform.position = bounds.center + Vector3.back * .8f;
        camera.transform.LookAt(bounds.center);
        Physics.SyncTransforms();
        Check(interactor.GetInteractableAlongRay(new Ray(camera.transform.position, camera.transform.forward)) is PlacedBottleInteractable,
            "Empty placed bottle is directly ray-interactable " + online + " hits=" + string.Join(";", Physics.RaycastAll(camera.transform.position, camera.transform.forward, 3f).OrderBy(h => h.distance).Select(h => h.collider.name)));
        Input(true); Call(interactor, "Update"); Input(false);
        for (int i = 0; i < 8; i++) yield return null;
        Check(holder.IsInHand && holder.IsCup && !holder.IsReady && !preparation.TryGetBottle(first, out _), "E retrieves an empty bottle and frees its spot " + online);
        preparation.Interact(first);
        for (int i = 0; i < 8; i++) yield return null;
        foreach (var flavor in new[] { PopcornFlavor.Drink, PopcornFlavor.Fanta, PopcornFlavor.OrangeJuice, PopcornFlavor.Pepsi })
        {
            var station = stations.First(s => s.Kind == PopcornStationKind.Water && s.PlacementAnchor == first.PlacementAnchor && s.Flavor == flavor);
            Aim(station);
            player.transform.position = station.PlacementAnchor.position;
            if (!preparation.TryGetBottle(station, out _))
            {
                holder.PickUp(true);
                for (int i = 0; i < 8; i++) yield return null;
                preparation.Interact(station);
                for (int i = 0; i < 8; i++) yield return null;
            }
            if (online)
            {
                var sync = PopcornNetSync.Instance;
                int id = (int)Get(preparation, "requestId") + 100;
                Set(preparation, "activeStation", station); Set(preparation, "preparingPlayer", player);
                Set(preparation, "requestId", id); Set(preparation, "awaitingResult", true);
                sync.RequestSupply(station.Id, id, PopcornSupplyAction.Begin);
                for (int i = 0; i < 8; i++) yield return null;
                Check(preparation.TryGetBottle(station, out var busy) && busy.fillingActor == player.netId, "Server reserves the bottle during dispensing");
                Check(!sync.ServerTryPickUpBottle(player.connectionToClient, preparation.SpotId(station), holder.StateRevision, busy.revision, out _),
                    "Server blocks pickup while pouring");
                Vector3 oldStation = second.transform.position, oldAnchor = second.PlacementAnchor.position;
                second.transform.position = station.transform.position + Vector3.right;
                second.PlacementAnchor.position = station.PlacementAnchor.position + Vector3.right;
                Physics.SyncTransforms();
                preparation.TryGetBottle(second, out var otherBottle);
                Check(!sync.ServerTryPickUpBottle(player.connectionToClient, preparation.SpotId(second), holder.StateRevision, otherBottle.revision, out _),
                    "Server blocks pickup from another idle machine while this player is pouring");
                second.transform.position = oldStation;
                second.PlacementAnchor.position = oldAnchor;
                Physics.SyncTransforms();
                CheckBottleObserverSnapshot(sync);
                preparation.RequestBottlePickup(station);
                Check(!holder.HasItem, "Bottle cannot be removed during dispensing");
                for (double until = NetworkTime.time + 3.2; NetworkTime.time < until;) yield return null;
                sync.RequestSupply(station.Id, id, PopcornSupplyAction.Complete);
                for (int i = 0; i < 8; i++) yield return null;
            }
            else
            {
                Input(true); preparation.Interact(station);
                Input(false); Call(preparation, "Update");
                Check(preparation.TryGetBottle(station, out var cancelled) && !cancelled.IsReady, "Cancelled fill leaves its empty bottle on the spot");
                Call(preparation, "FinishOfflineBottle", station, flavor);
            }
            Check(!holder.HasItem && preparation.TryGetBottle(station, out var ready) && ready.flavor == flavor,
                "Completed " + flavor + " stays colored on its machine " + online);
            if (online)
            {
                var sync = PopcornNetSync.Instance;
                CheckBottleObserverSnapshot(sync);
                Check(preparation.TryGetBottle(station, out var snapshot) && snapshot.flavor == flavor && preparation.TryGetBottle(second, out _),
                    "Late join snapshot contains every occupied spot and drink contents");
            }
            preparation.RequestBottlePickup(station);
            for (int i = 0; i < 8; i++) yield return null;
            Check(holder.CanServe && holder.HeldFlavor == flavor && !preparation.TryGetBottle(station, out _), "Explicit pickup preserves prepared drink and restores serving " + online);
            holder.Consume();
            for (int i = 0; i < 8; i++) yield return null;
        }
        Aim(second); player.transform.position = second.PlacementAnchor.position;
        preparation.RequestBottlePickup(second);
        for (int i = 0; i < 8; i++) yield return null;
        Check(holder.HasItem && !preparation.TryGetBottle(second, out _), "Other machine's bottle remains collectible after multiple placements " + online);
        holder.Consume();
    }

    static void CheckBottleObserverSnapshot(PopcornNetSync sync)
    {
        var writer = new NetworkWriter();
        sync.PlacedDrinks.OnSerializeAll(writer);
        var observer = new SyncDictionary<int, PlacedDrinkState>();
        observer.OnDeserializeAll(new NetworkReader(writer.ToArraySegment()));
        bool same = observer.Count == sync.PlacedDrinks.Count && sync.PlacedDrinks.All(pair =>
            observer.TryGetValue(pair.Key, out var value) && value.revision == pair.Value.revision &&
            value.flavor == pair.Value.flavor && value.fillingActor == pair.Value.fillingActor);
        Check(same, "Independent late observer receives all bottle identities, colors and pouring locks");
        var pending = new NetworkWriter();
        sync.PlacedDrinks.OnSerializeDelta(pending);
        observer.OnDeserializeDelta(new NetworkReader(pending.ToArraySegment()));
        Check(observer.Count == sync.PlacedDrinks.Count && sync.PlacedDrinks.All(pair => observer[pair.Key].Equals(pair.Value)),
            "Observer ignores overlapping deltas already included in its joining snapshot");
    }

    static void CheckBottleWorkCleanup(PopcornNetSync sync, PopcornStation station, NetworkConnectionToClient connection, PlacedDrinkState bottle, string reason)
    {
        int spot = preparation.SpotId(station);
        var workType = typeof(PopcornNetSync).GetNestedType("SupplyWork", BindingFlags.NonPublic);
        object work = Activator.CreateInstance(workType);
        uint actor = connection.identity.netId;
        workType.GetField("station").SetValue(work, station.Id);
        workType.GetField("request").SetValue(work, 700000);
        workType.GetField("actor").SetValue(work, actor);
        workType.GetField("heldRevision").SetValue(work, bottle.revision);
        workType.GetField("started").SetValue(work, NetworkTime.time - (reason == "timeout" ? 10 : 0));
        var workMap = (IDictionary)Get(sync, "supplyWork");
        workMap.Add(connection, work);
        bottle.fillingActor = actor;
        sync.PlacedDrinks[spot] = bottle;
        sync.SoundActions[actor] = new PopcornSoundAction { station = station.Id, request = 700000, started = NetworkTime.time };
        if (reason == "downed") Set(player, "isDowned", true);
        Call(sync, "Update");
        if (reason == "downed") Set(player, "isDowned", false);
        Check(!workMap.Contains(connection) && !sync.SoundActions.ContainsKey(actor) &&
            sync.PlacedDrinks[spot].fillingActor == 0 && sync.PlacedDrinks[spot].revision == bottle.revision && !sync.PlacedDrinks[spot].IsReady,
            "Server " + reason + " cleanup releases pouring and audio while preserving the machine bottle");
        var newer = bottle;
        newer.revision++;
        sync.PlacedDrinks[spot] = newer;
        Call(sync, "ReleaseDrinkWork", work);
        Check(sync.PlacedDrinks[spot].fillingActor == actor, "Stale work cleanup cannot unlock a different bottle generation");
        bottle.fillingActor = 0;
        sync.PlacedDrinks[spot] = bottle;
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
        holder = Object.FindAnyObjectByType<PopcornItemHoldingSystem>();
        CheckReusableHolding();
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
        Check(stations.Length == 18, "All eighteen stations bound, including two machine bodies and eight drink controls");
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
        Check(holder.Holding.CurrentPrefab == (GameObject)Get(bootstrapSetup, "heldPopcornPrefab"),
            "Scoop uses the scene's configured unflavored popcorn prefab");
        Press(cheese);
        Check(holder.HeldFlavor == PopcornFlavor.Cheese && !holder.GhostMixed, "Scoop preserves selected base flavor");
        Press(Station(PopcornStationKind.Ghost));
        Check(holder.GhostMixed && holder.HeldFlavor == PopcornFlavor.Cheese && !holder.MixGhost(), "Ghost mix adds once without replacing flavor");
        var recovery = GhostFavorRecovery.Instance;
        var home = (Vector3)Get(recovery, "homePosition");
        var buildingLights = BuildingLightManager.Instance;
        buildingLights.SetLights(false);
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
        buildingLights.SetLights(true);
        Call(recovery, "Update");
        player.transform.position = new Vector3(1000, 3, 1000);
        holder.Consume();
        Press(Station(PopcornStationKind.Cup));
        Check(holder.IsCup && !holder.IsReady && !holder.MixGhost(), "Cup starts empty and rejects early ghost mix");
        Check(!holder.Hold(PopcornFlavor.Paprika), "Cup rejects popcorn");
        var maker = Station(PopcornStationKind.Maker);
        var makerFx = maker.GetComponentInChildren<PreThesis.Vfx.PopcornMachineVfx>();
        Check(makerFx != null && makerFx.makerStation == maker.transform && makerFx.kernels == null && makerFx.seasoning == null &&
            makerFx.GetComponentsInChildren<ParticleSystem>().Length == 1, "Refill maker contains only the preview steam effect");
        holder.Consume();
        Aim(maker);
        var steamMain = makerFx.steam.main;
        steamMain.loop = false;
        Input(true);
        preparation.Interact(maker);
        Call(makerFx, "Update");
        makerFx.steam.Simulate(.3f, false, false, true);
        Check(preparation.IsMakingRefillAt(maker.transform) && makerFx.steam.main.loop && makerFx.steam.particleCount > 0,
            "Making a refill starts looping smoke even if the emitter was configured as one-shot");
        Input(false);
        Call(preparation, "Update");
        Call(makerFx, "Update");
        Check(!makerFx.steam.isPlaying && makerFx.steam.particleCount == 0 && !holder.HasItem,
            "Releasing E clears maker smoke without creating a refill");
        var makingRefill = Fill(maker);
        while (makingRefill.MoveNext()) yield return null;
        Call(makerFx, "Update");
        Check(holder.IsRefill && ((GameObject)Get(holder, "heldVisual")).name.Contains("NotastePopcorn") &&
            !makerFx.steam.isPlaying && makerFx.steam.particleCount == 0,
            "Completed maker action stops smoke and gives NotastePopcorn as a refill batch");
        int beforeRefill = preparation.TankRemaining;
        Press(tank);
        Check(!holder.HasItem && preparation.TankRemaining == Mathf.Min(configuredCapacity, beforeRefill + preparation.RefillServings),
            "NotastePopcorn batch is consumed when topping up the tank");
        var water = Station(PopcornStationKind.Water, PopcornFlavor.Drink);
        var bottles = CheckMachineBottles(false, water);
        while (bottles.MoveNext()) yield return bottles.Current;
        Set(preparation, "offlineRemaining", configuredCapacity);
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
        if (movedAnchor != null) movedAnchor.position = originalAnchorPosition;
        movedAnchor = null;
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
        // The normal arrival restores the lights before spawning. It must still
        // relocate seasoning, and the shared snapshot must change immediately.
        buildingLights.SetLights(true);
        var ghostManager = GhostManager.Instance;
        ghostManager.CancelInvoke();
        ghostManager.StopAllCoroutines();
        Call(ghostManager, "DespawnGhost");
        ghostManager.ServerDevSummonNow(player.gameObject);
        Check(buildingLights != null && BuildingLightManager.LightsOn && !recovery.IsHome &&
            PopcornNetSync.Instance.GhostFavor.displaced,
            "Real ghost spawn with lights on immediately publishes seasoning relocation");
        Vector3 arrivalPosition = recovery.VisibleState.position;
        recovery.ServerOnGhostAppeared();
        Check(recovery.VisibleState.position == arrivalPosition,
            "Repeated ghost arrival leaves an unfinished recovery in place");
        player.transform.position = arrivalPosition;
        recovery.ServerInteract(player, false);
        Check(PopcornNetSync.Instance.GhostFavor.carrierId == player.netId,
            "Server pickup publishes carrier before the next Update");
        recovery.ServerInteract(player, true);
        Check(PopcornNetSync.Instance.GhostFavor.carrierId == 0,
            "Server drop publishes release before the next Update");
        recovery.ServerInteract(player, false);
        player.transform.position = home;
        Call(recovery, "Update");
        Check(recovery.IsHome, "Spawn-triggered recovery returns home before blackout checks");
        ghostManager.CancelInvoke();
        Call(ghostManager, "DespawnGhost");
        buildingLights.SetLights(false);
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
            // Visual coverage uses a prepared server fixture; clients cannot publish a filled drink.
            if (visible.isCup && visible.IsReady) Set(secondHeld, "taskItem", visible);
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
        for (int i = 0; i < 8; i++) yield return null;
        Check(supply.SoundActions.TryGetValue(player.netId, out var makerSound) && makerSound.station == maker.Id && makerSound.request == 905,
            "Mirror maker begin publishes the shared action driving looping sound and smoke");
        var makerWriter = new NetworkWriter();
        supply.SoundActions.OnSerializeAll(makerWriter);
        var joiningMakerObserver = new SyncDictionary<uint, PopcornSoundAction>();
        joiningMakerObserver.OnDeserializeAll(new NetworkReader(makerWriter.ToArraySegment()));
        Check(joiningMakerObserver.TryGetValue(player.netId, out var joiningMakerSound) && joiningMakerSound.started == makerSound.started && joiningMakerSound.station == maker.Id,
            "Late joining maker observer receives the station, request and shared playback start time");
        for (double until = NetworkTime.time + 3.2; NetworkTime.time < until;) yield return null;
        supply.RequestSupply(maker.Id, 905, PopcornSupplyAction.Complete);
        for (int i = 0; i < 8; i++) yield return null;
        Check(holder.IsRefill && ((GameObject)Get(holder, "heldVisual")).name.Contains("NotastePopcorn"),
            "Mirror maker response gives requesting player authored NotastePopcorn refill batch");
        Check(!supply.SoundActions.ContainsKey(player.netId), "Maker completion removes the replicated sound and smoke action");
        var completedRefill = holder.Holding.Visual;
        supply.RequestSupply(maker.Id, 905, PopcornSupplyAction.Complete);
        for (int i = 0; i < 8; i++) yield return null;
        Check(holder.IsRefill && holder.Holding.Visual == completedRefill,
            "Duplicate maker completion cannot create or replace a second refill batch");
        var remoteMaking = new PopcornSoundAction { station = maker.Id, request = 1, started = NetworkTime.time };
        supply.SoundActions.Add(uint.MaxValue, remoteMaking);
        supply.SoundActions.Add(uint.MaxValue - 1, remoteMaking);
        Check(preparation.IsMakingRefillAt(maker.transform), "Observer maker action snapshot starts steam");
        supply.SoundActions.Remove(uint.MaxValue);
        Check(preparation.IsMakingRefillAt(maker.transform), "One player cancelling leaves steam active for another maker user");
        supply.SoundActions.Remove(uint.MaxValue - 1);
        Check(!preparation.IsMakingRefillAt(maker.transform), "Removing the last maker action stops observer steam");
        remoteMaking.started -= PopcornPreparation.PreparationSeconds + 1;
        supply.SoundActions.Add(uint.MaxValue, remoteMaking);
        Check(!preparation.IsMakingRefillAt(maker.transform), "Expired maker snapshot cannot leave steam stuck on");
        supply.SoundActions.Remove(uint.MaxValue);
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
        bottles = CheckMachineBottles(true, water);
        while (bottles.MoveNext()) yield return bottles.Current;
        holder.Consume();
        player.transform.position = maker.GetComponentInChildren<Collider>().bounds.center;
        for (int i = 0; i < 8; i++) yield return null;
        var lastRequests = (Dictionary<NetworkConnectionToClient, int>)Get(supply, "lastSoundRequest");
        int makerRequest = lastRequests[player.connectionToClient] + 1;
        for (double until = NetworkTime.time + .25; NetworkTime.time < until;) yield return null;
        supply.RequestSupply(maker.Id, makerRequest, PopcornSupplyAction.Begin);
        for (int i = 0; i < 8; i++) yield return null;
        Check(supply.SoundActions.ContainsKey(player.netId), "A later maker action restarts replicated sound and smoke");
        supply.RequestSupply(maker.Id, makerRequest, PopcornSupplyAction.Cancel);
        for (int i = 0; i < 8; i++) yield return null;
        Check(!supply.SoundActions.ContainsKey(player.netId) && !holder.HasItem &&
            !((HashSet<NetworkConnectionToClient>)Get(supply, "heldRefills")).Contains(player.connectionToClient),
            "Cancelling maker work removes its network effect and grants no refill token");
        for (double until = NetworkTime.time + .25; NetworkTime.time < until;) yield return null;
        supply.RequestSupply(maker.Id, makerRequest + 1, PopcornSupplyAction.Begin);
        for (int i = 0; i < 8; i++) yield return null;
        supply.RequestSupply(maker.Id, makerRequest, PopcornSupplyAction.Cancel);
        for (int i = 0; i < 8; i++) yield return null;
        Check(supply.SoundActions.TryGetValue(player.netId, out var restartedMaker) && restartedMaker.request == makerRequest + 1,
            "An old cancellation cannot stop the next maker action's looping sound or smoke");
        supply.RequestSupply(maker.Id, makerRequest + 1, PopcornSupplyAction.Cancel);
        for (int i = 0; i < 8; i++) yield return null;
        Check(!supply.SoundActions.ContainsKey(player.netId), "Matching cancellation clears the restarted maker effect");
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
