// Copy into Assets/Editor in an isolated project. Run RunOffline or RunHost in batch mode.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class CinemaGameplayChecks
{
    const string ScenePath = "Assets/Scenes/Map/Cinema_GamePlay.unity";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> results = new List<string>();
    static int failures, stage, frames;
    static bool host;
    static PlayerHealth player;
    static Camera camera;
    static TicketMinigame ticket;
    static PopcornMinigameBootstrap popcorn;
    static PopcornGameManager popcornManager;
    static CounterSlot slot;
    static GhostManager ghostManager;
    static double deadline;
    static CinemaGameplayChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("CinemaChecks", false)) return;
            host = SessionState.GetBool("CinemaHost", false);
            deadline = EditorApplication.timeSinceStartup + 90;
            var loop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            var systems = loop.subSystemList.ToList();
            systems.Add(new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(CinemaGameplayChecks), updateDelegate = Tick });
            loop.subSystemList = systems.ToArray(); UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(loop);
            EditorApplication.update += EditorApplication.Step;
        };
    }
    public static void RunOffline() => Run(false);
    public static void RunHost() => Run(true);
    static void Run(bool online)
    {
        if (!Application.isBatchMode) throw new Exception("Use isolated batch project");
        EditorSceneManager.OpenScene(ScenePath);
        SessionState.SetBool("CinemaChecks", true); SessionState.SetBool("CinemaHost", online);
        EditorApplication.EnterPlaymode();
    }
    static object Get(object obj, string field) => obj.GetType().GetField(field, Private).GetValue(obj);
    static void Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, Private).Invoke(obj, args);
    static void Check(bool pass, string label) { results.Add((pass ? "PASS " : "FAIL ") + label); if (!pass) failures++; }
    static T Find<T>() where T : Object => Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    static T[] All<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include);
    static Transform Anchor(object obj, string field) => (Transform)Get(obj, field);
    static void Aim(Vector3 eye, Transform target)
    {
        player.transform.position = eye - Vector3.up * 1.6f;
        camera.transform.position = eye; camera.transform.LookAt(target.position);
        Physics.SyncTransforms(); Canvas.ForceUpdateCanvases();
    }
    static void Screenshot(string name, Vector3 eye, Vector3 target)
    {
        camera.transform.position = eye; camera.transform.LookAt(target); camera.fieldOfView = 75;
        Call(ticket, "LateUpdate"); Canvas.ForceUpdateCanvases();
        var rt = new RenderTexture(1280, 720, 24); camera.targetTexture = rt; camera.Render();
        RenderTexture.active = rt;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
        File.WriteAllBytes(name + ".png", image.EncodeToPNG());
        RenderTexture.active = null; camera.targetTexture = null; Object.Destroy(rt); Object.Destroy(image);
    }
    static void Tick()
    {
        if (!SessionState.GetBool("CinemaChecks", false)) return;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timed out at stage " + stage);
            if (++frames < 20) return;
            if (stage == 0)
            {
                ticket = Find<TicketMinigame>(); popcorn = Find<PopcornMinigameBootstrap>();
                ghostManager = Find<GhostManager>(); slot = Find<CounterSlot>(); popcornManager = Find<PopcornGameManager>();
                Check(ticket != null && ticket.enabled && popcorn != null && popcorn.enabled, "Both minigames enabled with complete scene references");
                Check(All<GhostManager>().Length == 1 && All<OfflinePlayerSpawner>().Length == 1 && All<PersistentHUD>().Length == 1, "One ghost manager, offline spawner and HUD");
                Check(All<LightController>().All(c => c.GetComponent<PlayerHealth>() != null), "Light input belongs only to player");
                Check(All<NetworkStartPosition>().Length == 6, "Six multiplayer spawn positions");
                Check(All<NetworkBehaviour>().All(b => b.GetComponentInParent<NetworkIdentity>() != null), "Every network behaviour has an identity");
                var net = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/NetworkManager.prefab").GetComponent<NetworkManager>();
                Check(net.onlineScene == ScenePath && net.transport != null && net.playerPrefab != null, "Online lobby routes to Cinema with configured transport/player");
                Check(net.spawnPrefabs.Contains((GameObject)Get(ghostManager, "ghostPrefab")), "Ghost prefab registered for network spawning");
                Check(EditorBuildSettings.scenes.Any(s => s.enabled && s.path == ScenePath), "Cinema enabled in build settings");
                Check(((Light[])Get(ghostManager, "allLights")).Length == GameObject.Find("AllLight").GetComponentsInChildren<Light>().Length, "Ghost controls all authored cinema lights");
                var ts = Anchor(ticket, "spawnPoint"); var ps = Anchor(popcorn, "customerSpawnPoint");
                Check(Vector3.Distance(ts.position, ps.position) < .01f && ts.parent.name == "Door" && ts.parent.parent.name == "Entrance", "Both customer spawns use Entrance/Door");
                var te = Anchor(ticket, "exitPoint"); var pe = Anchor(popcorn, "customerExitPoint");
                Check(te.parent.name == "Door" && te.parent.parent.name == "Entrance (1)" && pe.parent == te.parent && Vector3.Distance(te.position, pe.position) >= 2.4f, "Separate exit lanes at Entrance (1)/Door, 2.5m apart");
                Check(Anchor(ticket, "movieUiAnchor").parent.name == "Cube (1)" && ((TicketSellButton)Get(ticket, "humanButton")).transform.parent.name == "Cube", "Ticket controls attached to requested wall and countertop");
                Check(Anchor(popcorn, "cashierUiAnchor").parent.parent.name == "Moniter" && Anchor(popcorn, "popcornMakerUiAnchor").parent.name == "PopcornTasteSelect", "Popcorn screens attached to requested props");
                if (host)
                {
                    foreach (var p in All<PlayerHealth>()) Object.Destroy(p.gameObject);
                    var network = new GameObject("Local Host Validation");
                    var transport = network.AddComponent<kcp2k.KcpTransport>();
                    transport.Port = 17993;
                    var manager = network.AddComponent<NetworkManager>();
                    manager.transport = transport; manager.playerPrefab = net.playerPrefab;
                    manager.spawnPrefabs = new List<GameObject>(net.spawnPrefabs);
                    manager.autoCreatePlayer = true;
                    stage = 1; frames = 0; return;
                }
                stage = 2;
            }
            if (stage == 1)
            { NetworkManager.singleton.StartHost(); stage = 2; frames = 0; return; }
            if (stage == 2)
            {
                player = PlayerHealth.LocalInstance;
                if (player == null || (host && NetworkClient.localPlayer == null)) return;
                camera = player.GetComponentInChildren<Camera>();
                Check(All<PlayerHealth>().Length == 1, "Exactly one local player after spawn");
                Check(camera != null && player.GetComponent<PlayerMovement>() != null && player.GetComponent<PlayerStamina>() != null && player.GetComponent<PlayerInteractor>() != null, "Player movement, camera, stamina, health and interaction present");
                Check(NavMesh.SamplePosition(Anchor(ghostManager, "spawnPoint").position, out _, 1, NavMesh.AllAreas), "Ghost spawn lies on rebuilt navigation");
                foreach (var eye in new[] { new Vector3(-8.6f, 1.7f, 11.6f), new Vector3(-18.2f, 1.7f, 12f) })
                {
                    var path = new NavMeshPath();
                    bool found = NavMesh.CalculatePath(player.transform.position, new Vector3(eye.x, 0, eye.z), NavMesh.AllAreas, path);
                    Check(found && path.status == NavMeshPathStatus.PathComplete, "Player can walk to station " + eye);
                }
                for (int i = 0; i < 12 && ticket.State.stage != TicketCustomerStage.Waiting; i++) Call(ticket, "Advance", 100f);
                if (host) ((TicketNetSync)Get(ticket, "networkSync")).Publish(ticket.State);
                Call(ticket, "DrawCustomer", ticket.VisibleState); Call(ticket, "RefreshMoviePanel");
                var choices = (WorldButtonInteractable[])Get(ticket, "movieHighlights");
                var interactor = player.GetComponent<PlayerInteractor>();
                Aim(new Vector3(-8.6f, 1.7f, 11.6f), choices[ticket.State.requestedMovieIndex].transform);
                Check(ReferenceEquals(interactor.GetInteractableAlongRay(new Ray(camera.transform.position, camera.transform.forward)), choices[ticket.State.requestedMovieIndex]), "Movie panel visible to interaction ray from booth");
                Call(interactor, "RequestInteract", choices[ticket.State.requestedMovieIndex]);
                stage = 3; frames = 0; return;
            }
            if (stage == 3)
            {
                Check(ticket.CanServe, "Movie choice unlocks ticket sale");
                var interactor = player.GetComponent<PlayerInteractor>();
                foreach (string field in new[] { "humanButton", "ghostButton" })
                {
                    var button = (TicketSellButton)Get(ticket, field);
                    Aim(new Vector3(-8.6f, 1.7f, 11.6f), button.transform);
                    Check(ReferenceEquals(interactor.GetInteractableAlongRay(new Ray(camera.transform.position, camera.transform.forward)), button), "Ticket button reachable: " + field);
                }
                if (!host) Screenshot("cinema-tickets", new Vector3(-8.1f, 1.75f, 12f), new Vector3(-10f, 1.35f, 11.4f));
                player.transform.position = new Vector3(-8.6f, .1f, 11.6f);
                var correct = (TicketSellButton)Get(ticket, ticket.State.ghost ? "ghostButton" : "humanButton");
                Call(interactor, "RequestInteract", correct);
                stage = 4; frames = 0; return;
            }
            if (stage == 4)
            {
                Check(ticket.State.score == 1 && ticket.State.stage == TicketCustomerStage.WalkingOut, "Correct ticket earns a point and customer departs");
                for (int i = 0; i < 10 && ticket.State.stage != TicketCustomerStage.BetweenCustomers; i++) Call(ticket, "Advance", 100f);
                Check(Vector3.Distance(ticket.State.position, Anchor(ticket, "exitPoint").position) < .01f, "Ticket customer reaches assigned exit");
                if (slot.ActiveCustomer == null) { PopcornNetSync.Instance.ServerSeatCustomer(); Call(popcornManager, "SeatCustomerFromSync"); }
                var customer = slot.ActiveCustomer;
                foreach (var p in ((Transform[])Get(popcorn, "customerApproachPath")).Concat(new[] { Anchor(popcorn, "customerWaitPoint") }))
                { customer.transform.position = p.position; Call(customer, "Update"); }
                Check(customer.CanInteract(), "Popcorn customer follows approach points and waits");
                var flavor = GameObject.Find((customer.Order == PopcornFlavor.Ghost ? "GHOST FLAVOR" : customer.Order == PopcornFlavor.Cheese ? "CHEESE" : "BBQ") + " Button").GetComponent<WorldButtonInteractable>();
                var interactor = player.GetComponent<PlayerInteractor>();
                Aim(new Vector3(-18.2f, 1.7f, 12f), flavor.transform);
                Check(ReferenceEquals(interactor.GetInteractableAlongRay(new Ray(camera.transform.position, camera.transform.forward)), flavor), "Flavor panel reachable on PopcornTasteSelect");
                Call(interactor, "RequestInteract", flavor);
                if (!host)
                {
                    Screenshot("cinema-order-screen", new Vector3(-18.5f, 1.95f, 12.1f), Anchor(popcorn, "cashierUiAnchor").position);
                    Screenshot("cinema-flavor-screen", new Vector3(-18.2f, 1.95f, 12f), Anchor(popcorn, "popcornMakerUiAnchor").position);
                }
                Call(interactor, "RequestInteract", GameObject.Find("Make Button").GetComponent<WorldButtonInteractable>());
                Check(Find<ItemHoldingSystem>().HasItem, "Flavor selection and Make equip popcorn");
                Aim(new Vector3(-18.2f, 1.7f, 12f), customer.transform);
                Check(ReferenceEquals(interactor.GetInteractableAlongRay(new Ray(camera.transform.position, camera.transform.forward)), customer), "Waiting popcorn customer can be reached across counter");
                if (!host) Screenshot("cinema-popcorn", new Vector3(-19.3f, 2f, 12.5f), new Vector3(-17.5f, 1.2f, 11.5f));
                player.transform.position = new Vector3(-18.2f, .1f, 12f);
                Call(interactor, "RequestInteract", customer);
                stage = 5; frames = 0; return;
            }
            if (stage == 5)
            {
                Check(PopcornNetSync.Instance.Score == 1 && !PopcornNetSync.Instance.CustomerWaiting && !slot.ActiveCustomer.CanInteract(), "Popcorn command scores and clears shared customer");
                var customer = slot.ActiveCustomer;
                foreach (var p in ((Transform[])Get(popcorn, "customerDeparturePath")).Concat(new[] { Anchor(popcorn, "customerExitPoint") }))
                { customer.transform.position = p.position; Call(customer, "Update"); }
                Check(!slot.IsOccupied, "Popcorn customer vacates at assigned exit");
                Call(ghostManager, "SpawnGhost"); stage = 6; frames = 0; return;
            }
            if (stage == 6)
            {
                var ghost = (GameObject)Get(ghostManager, "currentGhostInstance");
                Check(ghost != null && ghost.GetComponent<NavMeshAgent>().isOnNavMesh, "Ghost spawns and navigates on Cinema mesh");
                if (host) Check(ghost.GetComponent<NetworkIdentity>().netId != 0, "Host ghost is network-spawned");
                ghostManager.PlayerToggleLights(false);
                Check(((Light[])Get(ghostManager, "allLights")).All(l => !l.enabled), "Ghost light state switches all lights");
                ghostManager.PlayerToggleLights(true);
                Check(((Light[])Get(ghostManager, "allLights")).All(l => l.enabled), "Lights restore correctly");
                Finish();
            }
        }
        catch (Exception e) { Check(false, e.ToString()); Finish(); }
    }
    static void Finish()
    {
        SessionState.SetBool("CinemaChecks", false);
        File.WriteAllLines(host ? "cinema-host-results.txt" : "cinema-offline-results.txt", results);
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }
}
