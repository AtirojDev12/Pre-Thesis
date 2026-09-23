// Run in an isolated project; Build authors the TicketSell scene before play-mode checks.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class TicketRegressionRunner
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> results = new List<string>();
    static int failures;
    static TicketRegressionRunner()
    {
        EditorApplication.playModeStateChanged += change =>
        {
            if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("TicketChecks", false))
            {
                var loop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
                var systems = loop.subSystemList.ToList();
                systems.Add(new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(TicketRegressionRunner), updateDelegate = CheckGameplay });
                loop.subSystemList = systems.ToArray();
                UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(loop);
                EditorApplication.update += EditorApplication.Step;
            }
        };
    }
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    static void Check(bool pass, string message)
    {
        results.Add((pass ? "PASS " : "FAIL ") + message);
        if (!pass) failures++;
    }
    static Transform Point(Transform parent, string name, Vector3 position)
    {
        var point = new GameObject(name).transform;
        point.SetParent(parent, false);
        point.position = position;
        return point;
    }
    public static void Build()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Z1_EnemyTest.unity");
        Transform root = GameObject.Find("TicketSell").transform;
        if (root.GetComponent<TicketMinigame>() != null)
        {
            SetupMoviePanel(root);
            EditorSceneManager.SaveScene(scene);
            SessionState.SetBool("TicketChecks", true);
            EditorApplication.EnterPlaymode();
            return;
        }
        GameObject human = root.Find("Cube (11)").gameObject;
        // Duplicate the authored button, retaining its mesh/material/collider and ProBuilder data.
        GameObject ghost = Object.Instantiate(human, root);
        ghost.name = "Cube (14)";
        ghost.transform.localPosition += Vector3.back * 1.3f;
        var manager = root.gameObject.AddComponent<TicketMinigame>();
        var humanButton = human.AddComponent<TicketSellButton>();
        var ghostButton = ghost.AddComponent<TicketSellButton>();
        var syncObject = new GameObject("Ticket Network State");
        syncObject.transform.SetParent(root, false);
        var sync = syncObject.AddComponent<TicketNetSync>();
        sync.syncInterval = 0.05f;
        Set(sync, "minigame", manager);
        Set(humanButton, "minigame", manager);
        Set(ghostButton, "minigame", manager);
        Set(ghostButton, "ghostTicket", true);
        Set(manager, "humanButton", humanButton);
        Set(manager, "ghostButton", ghostButton);
        Set(manager, "networkSync", sync);
        // The booth window faces +X; both buttons sit on the desk inside it.
        var route = Point(root, "Ticket Customer Route", root.position);
        Vector3 wait = root.position + new Vector3(2, 0, 0.6f);
        wait.y = 1f;
        Transform spawn = Point(route, "Spawn", wait + new Vector3(3, 0, -3));
        Transform approach = Point(route, "Approach", wait + new Vector3(1, 0, -1.5f));
        Transform counter = Point(route, "Counter (rotate to face player)", wait);
        counter.rotation = Quaternion.Euler(0, 270, 0);
        Transform departure = Point(route, "Departure", wait + new Vector3(2, 0, 1));
        Transform exit = Point(route, "Exit", wait + new Vector3(4, 0, 3));
        Set(manager, "spawnPoint", spawn);
        Set(manager, "approachPath", new[] { approach });
        Set(manager, "counterPoint", counter);
        Set(manager, "departurePath", new[] { departure });
        Set(manager, "exitPoint", exit);
        Transform score = Point(root, "Ticket Score Anchor", root.position + new Vector3(0.3f, 1f, 0.6f));
        score.rotation = Quaternion.Euler(0, 90, 0);
        Set(manager, "scoreAnchor", score);
        SetupMoviePanel(root);
        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(sync);
        EditorUtility.SetDirty(humanButton);
        EditorUtility.SetDirty(ghostButton);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        File.WriteAllText("ticket-layout.txt", string.Join("\n", root.GetComponentsInChildren<Renderer>().Select(r => r.name + " " + r.bounds)));
        SessionState.SetBool("TicketChecks", true);
        EditorApplication.EnterPlaymode();
    }
    static void SetupMoviePanel(Transform root)
    {
        var manager = root.GetComponent<TicketMinigame>();
        if (Get(manager, "movieUiAnchor") != null) return;
        Transform wall = root.Find("Cube (1)");
        Bounds bounds = wall.GetComponent<Renderer>().bounds;
        Transform anchor = Point(wall, "Movie Selection UI Anchor",
            new Vector3(bounds.center.x, 1.85f, bounds.min.z - 0.035f));
        anchor.rotation = Quaternion.identity;
        Set(manager, "movieUiAnchor", anchor);
        EditorUtility.SetDirty(manager);
    }
    static void CheckGameplay()
    {
        if (!SessionState.GetBool("TicketChecks", false)) return;
        SessionState.SetBool("TicketChecks", false);
        try
        {
            var manager = GameObject.Find("TicketSell").GetComponent<TicketMinigame>();
            var human = (TicketSellButton)Get(manager, "humanButton");
            var ghost = (TicketSellButton)Get(manager, "ghostButton");
            var playerObject = new GameObject("Ticket Test Player", typeof(NetworkIdentity), typeof(PlayerHealth));
            var player = playerObject.GetComponent<PlayerHealth>();
            foreach (var field in typeof(PlayerHealth).GetFields())
                if (typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(field.FieldType))
                    field.SetValue(player, Activator.CreateInstance(field.FieldType));
            Call(player, "Start");
            var interactor = playerObject.AddComponent<PlayerInteractor>();
            player.transform.position = human.transform.position;
            Check(manager.enabled && human.GetComponent<Collider>() != null && ghost.GetComponent<Collider>() != null, "Scene references and both physical buttons are wired");
            Check(human.name == "Cube (11)" && ghost.name == "Cube (14)", "Human/ghost button mapping");
            for (int type = 0; type < 2; type++)
            for (int correct = 0; correct < 2; correct++)
            for (int movieMatches = 0; movieMatches < 2; movieMatches++)
            {
                Set(manager, "ghostChance", (float)type);
                Set(manager, "state", new TicketCustomerState());
                Call(manager, "DrawCustomer", manager.State);
                Set(manager, "delay", 0f);
                Call(manager, "Advance", 0.1f);
                int preference = manager.State.requestedMovieIndex;
                var titles = (string[])Get(manager, "movies");
                Check(preference >= 0 && preference < titles.Length, "Customer receives a valid movie preference");
                Call(manager, "DrawCustomer", manager.State);
                Check(!human.CanInteract() && !((Canvas)Get(manager, "bubble")).enabled, "Walking customer has no request bubble and cannot be served");
                for (int step = 0; step < 20 && manager.State.stage != TicketCustomerStage.Waiting; step++) Call(manager, "Advance", 1f);
                Call(manager, "DrawCustomer", manager.State);
                Check(manager.CanChooseMovie && !manager.CanServe && ((Canvas)Get(manager, "bubble")).enabled, "Arrival reveals request but selling requires a movie");
                Check(((TMPro.TMP_Text)Get(manager, "requestText")).text.Contains("Movie: " + titles[preference]), "Request bubble names the preferred movie");
                manager.ResolveSale(type == 1, manager.State.round, player);
                Check(manager.State.stage == TicketCustomerStage.Waiting && manager.State.score == 0, "Server rejects selling before movie selection");
                manager.ResolveMovie(-1, manager.State.round, player);
                manager.ResolveMovie(999, manager.State.round, player);
                manager.ResolveMovie(0, manager.State.round - 1, player);
                Check(!manager.CanServe, "Invalid movies and stale selection rejected");
                player.transform.position += Vector3.one * 100;
                manager.ResolveMovie(0, manager.State.round, player);
                Check(!manager.CanServe, "Out-of-range movie selection rejected");
                player.transform.position = human.transform.position;
                Call(manager, "RefreshMoviePanel");
                Canvas.ForceUpdateCanvases();
                var movieControls = (WorldButtonInteractable[])Get(manager, "movieHighlights");
                Physics.SyncTransforms();
                Vector3 movieEye = manager.transform.position + new Vector3(-1.2f, 0.7f, 0.6f);
                var movieHit = interactor.GetInteractableAlongRay(new Ray(movieEye, (movieControls[0].transform.position - movieEye).normalized));
                Check(ReferenceEquals(movieHit, movieControls[0]), "Movie panel is reachable on the inside of Cube (1)");
                Call(interactor, "RequestInteract", movieControls[0]);
                Check(manager.CanServe && manager.State.movieIndex == 0, "Movie UI interaction unlocks ticket buttons");
                Call(interactor, "RequestInteract", movieControls[1]);
                Check(manager.State.movieIndex == 1, "Movie selection can be changed before selling");
                Check(manager.State.requestedMovieIndex == preference, "Player selection does not change the customer preference");
                Physics.SyncTransforms();
                Vector3 eye = manager.transform.position + new Vector3(-1.2f, 0.7f, 0.6f);
                foreach (var button in new[] { human, ghost })
                    Check(ReferenceEquals(interactor.GetInteractableAlongRay(new Ray(eye, (button.transform.position - eye).normalized)), button), "Button reachable through existing interaction ray: " + button.name);
                using (var writer = NetworkWriterPool.Get())
                {
                    writer.Write(manager.State);
                    using (var reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                    {
                        var snapshot = reader.Read<TicketCustomerState>();
                        Check(snapshot.stage == manager.State.stage && snapshot.ghost == manager.State.ghost && snapshot.position == manager.State.position && snapshot.hasMovie && snapshot.movieIndex == 1 && snapshot.requestedMovieIndex == preference, "Mirror customer, movie and preference snapshot serialization");
                    }
                }
                if (type == 0 && correct == 0 && movieMatches == 0)
                {
                    var cameraObject = new GameObject("Ticket Preview Camera", typeof(Camera));
                    var camera = cameraObject.GetComponent<Camera>();
                    camera.transform.position = eye;
                    camera.transform.LookAt(manager.State.position + Vector3.up * 0.6f);
                    camera.fieldOfView = 70;
                    ((Canvas)Get(manager, "bubble")).transform.rotation = camera.transform.rotation;
                    var target = new RenderTexture(1280, 720, 24);
                    camera.targetTexture = target;
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    RenderTexture.active = target;
                    var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                    texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                    texture.Apply();
                    File.WriteAllBytes("ticket-preview.png", texture.EncodeToPNG());
                    RenderTexture.active = null;
                    camera.targetTexture = null;
                    Object.Destroy(target);
                    Object.Destroy(texture);
                    Object.Destroy(cameraObject);
                }
                int round = manager.State.round;
                int selectedMovie = movieMatches == 1 ? preference : (preference + 1) % titles.Length;
                Call(interactor, "RequestInteract", movieControls[selectedMovie]);
                int expectedScore = correct == 1 && movieMatches == 1 ? 1 : 0;
                Set(player, "_invincibleUntil", float.NegativeInfinity);
                float hp = player.CurrentHealth;
                bool ticket = correct == 1 ? type == 1 : type != 1;
                manager.ResolveSale(ticket, round - 1, player);
                Check(manager.CanServe && manager.State.score == 0, "Stale round rejected");
                player.transform.position += Vector3.one * 100;
                manager.ResolveSale(ticket, round, player);
                Check(manager.CanServe, "Remote button press rejected");
                player.transform.position = human.transform.position;
                (ticket ? ghost : human).Interact(playerObject);
                Check(manager.State.score == expectedScore, $"Type={type}, ticketMatch={correct}, movieMatch={movieMatches}: both must match to score");
                Check(Mathf.Approximately(player.CurrentHealth, hp - (type == 1 && expectedScore == 0 ? 10 : 0)), "Incorrect movie or ticket damages ghosts only, once per sale");
                Check(manager.State.stage == TicketCustomerStage.WalkingOut && !((Canvas)Get(manager, "bubble")).enabled, "Sale hides bubble and starts departure");
                Check(!manager.State.hasMovie && !manager.CanServe, "Sale clears movie selection for the next customer");
                manager.ResolveSale(ticket, round, player);
                Check(manager.State.score == expectedScore, "Duplicate sale rejected");
                for (int step = 0; step < 20 && manager.State.stage != TicketCustomerStage.BetweenCustomers; step++) Call(manager, "Advance", 1f);
                Check(manager.State.stage == TicketCustomerStage.BetweenCustomers, "Customer follows departure route and vacates counter");
                Call(manager, "Advance", 0.1f);
                Check(manager.State.stage == TicketCustomerStage.BetweenCustomers, "Delay between customers respected");
            }
        }
        catch (Exception e) { Check(false, e.ToString()); }
        SessionState.SetBool("TicketChecks", false);
        File.WriteAllLines("ticket-results.txt", results);
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }
}
