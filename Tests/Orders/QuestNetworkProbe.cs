// Copied into an isolated player build only. Files coordinate test phases;
// all orders, quest progress, held items and serve commands travel through Mirror/KCP.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mirror;
using TMPro;
using UnityEngine;

public sealed class QuestNetworkProbe : MonoBehaviour
{
    public GameObject playerPrefab;
    public List<GameObject> spawnPrefabs = new List<GameObject>();
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly List<string> results = new List<string>();
    string role, output;
    PopcornNetSync sync;
    PopcornGameManager game;
    CounterSlot counter;
    ZoneTaskList board;
    int bbq;
    bool success, finished;
    bool runtimeError;
    uint nextOrder = 50000;
    float started;
    static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    static void Call(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);
    string PathFor(string name) => Path.Combine(output, name + ".txt");
    void Mark(string name, string value = "ready") => File.WriteAllText(PathFor(name), value);
    bool Has(string name) => File.Exists(PathFor(name));
    string Read(string name) => Has(name) ? File.ReadAllText(PathFor(name)) : "";
    void Check(bool pass, string label)
    {
        results.Add((pass ? "PASS " : "FAIL ") + label);
        if (!pass) throw new Exception(label);
    }
    string Arg(string name, string fallback)
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }
    void Start()
    {
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;
        role = Arg("--quest-role", "host");
        output = Arg("--quest-output", Application.dataPath + "/..");
        Directory.CreateDirectory(output);
        started = Time.realtimeSinceStartup;
        Application.logMessageReceived += OnLog;
        var root = new GameObject("Quest network test " + role);
        var transport = root.AddComponent<kcp2k.KcpTransport>();
        transport.Port = 17996;
        var manager = root.AddComponent<NetworkManager>();
        manager.transport = transport;
        manager.playerPrefab = playerPrefab;
        manager.spawnPrefabs.AddRange(spawnPrefabs);
        manager.networkAddress = "127.0.0.1";
        manager.onlineScene = "Assets/Scenes/Map/Cinema_GamePlay.unity";
        if (role == "host") { manager.StartHost(); Mark("listening"); }
        else manager.StartClient();
        StartCoroutine(SafeRun(Run()));
    }
    IEnumerator SafeRun(IEnumerator first)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(first);
        while (stack.Count > 0)
        {
            object yielded = null;
            try
            {
                var current = stack.Peek();
                if (!current.MoveNext()) { stack.Pop(); continue; }
                yielded = current.Current;
            }
            catch (Exception e) { results.Add("FAIL " + e); Finish(false); yield break; }
            if (yielded is IEnumerator nested) stack.Push(nested);
            else yield return yielded;
        }
        Finish(true);
    }
    void Finish(bool passed)
    {
        if (finished) return;
        finished = true;
        passed &= !runtimeError;
        Application.logMessageReceived -= OnLog;
        File.WriteAllLines(PathFor("results-" + role), results);
        Mark("done-" + role, passed ? "PASS" : "FAIL");
        Application.Quit(passed ? 0 : 1);
    }
    void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Exception && !message.StartsWith("Failed to spawn server object") &&
            !message.StartsWith("Could not spawn assetId")) return;
        runtimeError = true;
        results.Add("FAIL Runtime: " + message + " " + stack);
    }
    IEnumerator Wait(Func<bool> condition, string label, float seconds = 35)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > deadline) throw new Exception("Timeout: " + label);
            yield return null;
        }
    }
    void Update()
    {
        if (finished || role == null) return;
        if (Time.realtimeSinceStartup - started > 210) { results.Add("FAIL Global timeout"); Finish(false); }
        var player = PlayerHealth.LocalInstance;
        if (player == null) return;
        var movement = player.GetComponent<PlayerMovement>();
        if (movement != null) movement.enabled = false;
        var body = player.GetComponent<Rigidbody>();
        if (body != null) body.isKinematic = true;
    }
    IEnumerator Run()
    {
        yield return Wait(() => NetworkClient.isConnected && NetworkClient.ready && PlayerHealth.LocalInstance != null &&
            PopcornNetSync.Instance != null && FindAnyObjectByType<CounterSlot>() != null, "network scene/player ready", 60);
        sync = PopcornNetSync.Instance;
        game = FindAnyObjectByType<PopcornGameManager>();
        counter = FindAnyObjectByType<CounterSlot>();
        Set(FindAnyObjectByType<PopcornMinigameBootstrap>(), "customerWalkSpeed", 1000f);
        board = ZoneTaskList.All.First(b => b.ZoneID == "zone_popcorn");
        bbq = Enumerable.Range(0, board.TaskCount).First(i => board.TaskAt(i).flavor == PopcornFlavor.BBQ);
        Check(role == "host" ? NetworkServer.active : !NetworkServer.active, "Runs in a separate " + role + " network process");
        if (role == "host") yield return Host();
        else if (role == "late") yield return LateObserver();
        else yield return Client();
    }
    void Seat(PopcornCustomerType type, PopcornFlavor first, PopcornFlavor second = PopcornFlavor.None, bool choice = false)
    {
        game.StopAllCoroutines();
        if (counter.ActiveCustomer != null)
        {
            var previous = counter.ActiveCustomer;
            counter.Vacate(previous);
            previous.gameObject.SetActive(false);
            Destroy(previous.gameObject);
        }
        var order = PopcornOrderState.Create(++nextOrder, type, first, second);
        order.chooseAny = choice;
        Set(sync, "currentOrder", order);
        // Reflection is only a deterministic fixture: explicitly mark the SyncVar
        // dirty, since reflection bypasses Mirror's generated assignment setter.
        sync.SetDirty();
        Call(game, "OnSyncOrderChanged");
    }
    IEnumerator Announce(string phase)
    {
        yield return Wait(() => counter.ActiveCustomer != null && counter.ActiveCustomer.CanInteract(), "host arrival " + phase);
        Mark("order-" + phase, sync.Order.id.ToString());
        Mark("phase", phase);
    }
    IEnumerator Host()
    {
        yield return Wait(() => Has("client-ready"), "remote client ready");
        // Keep the run focused on serving; no ghost hazards or automatic queue changes.
        game.StopAllCoroutines();
        var targets = (SyncList<int>)Get(board, "targets");
        var progress = (SyncList<int>)Get(board, "progress");
        for (int i = 0; i < board.TaskCount; i++) { targets[i] = 100; progress[i] = 0; }
        foreach (var ghost in FindObjectsByType<GhostManager>(FindObjectsSortMode.None)) ghost.enabled = false;
        int score = sync.Score;
        Seat(PopcornCustomerType.Human, PopcornFlavor.Cheese, choice: true);
        yield return Announce("human-choice");
        yield return Wait(() => Has("human-done") && !sync.CustomerWaiting, "human choice command");
        Check(sync.Score == score + 1 && progress[bbq] == 1, "Remote human choice scores once and credits actual BBQ on server");
        Seat(PopcornCustomerType.Ghost, PopcornFlavor.Cheese, choice: true);
        yield return Announce("ghost-choice");
        Mark("launch-late");
        yield return Wait(() => Has("late-choice-checked"), "late observer choice snapshot", 65);
        yield return Wait(() => Has("ghost-done") && !sync.CustomerWaiting, "ghost choice command");
        Check(sync.Score == score + 2 && progress[bbq] == 2, "Remote ghost choice accepts ghost BBQ and replicates progress");
        Seat(PopcornCustomerType.Ghost, PopcornFlavor.Cheese, choice: true);
        yield return Announce("wrong-choice");
        yield return Wait(() => Has("wrong-done") && !sync.CustomerWaiting, "wrong ghost choice command");
        Check(sync.Score == score + 2 && progress[bbq] == 2, "Wrong remote seasoning cannot earn score or task credit");
        Seat(PopcornCustomerType.Human, PopcornFlavor.Cheese, PopcornFlavor.BBQ);
        yield return Announce("partial");
        yield return Wait(() => Has("partial-done") && sync.Order.DeliveredCount == 1, "partial remote delivery");
        Check(sync.CustomerWaiting && sync.Score == score + 3, "Remote first item preserves original customer and partial order");
        yield return Wait(() => Has("late-checked"), "late observer initial snapshot", 65);
        Check(sync.Order.DeliveredCount == 1 && sync.CustomerWaiting, "Late join does not regenerate or reset active order");
        Mark("finish-partial");
        yield return Wait(() => Has("partial-finished") && !sync.CustomerWaiting, "second remote delivery");
        Check(sync.Score == score + 4 && progress[bbq] == 3, "Remote second item completes same order and credits BBQ once");

        // Exercise the drought through five real remote sales with only BBQ unfinished.
        for (int i = 0; i < board.TaskCount; i++) if (i != bbq) progress[i] = targets[i];
        var director = (QuestOrderDirector)Get(sync, "orderDirector");
        director.Reset();
        for (int i = 1; i <= 5; i++)
        {
            Seat(PopcornCustomerType.Human, PopcornFlavor.Cheese);
            yield return Announce("miss-" + i);
            int expected = i;
            yield return Wait(() => Has("miss-done-" + expected) && !sync.CustomerWaiting, "remote irrelevant sale " + i);
            Check(director.QuestMisses == i, "Only server counts completed remote customer " + i);
        }
        game.StopAllCoroutines();
        if (counter.ActiveCustomer != null)
        {
            var previous = counter.ActiveCustomer; counter.Vacate(previous); previous.gameObject.SetActive(false); Destroy(previous.gameObject);
        }
        sync.ServerSeatCustomer();
        Check(sync.Order.chooseAny || sync.Order.first == PopcornFlavor.BBQ, "Real sixth spawn helps last unfinished BBQ quest");
        yield return Announce("guaranteed");
        yield return Wait(() => Has("guaranteed-done") && !sync.CustomerWaiting, "remote guaranteed sale");
        Check(director.QuestMisses == 0 && progress[bbq] >= 4, "Remote guaranteed sale advances task and resets server counter");
        Mark("finish");
        yield return Wait(() => Has("client-checked") && Has("late-finished"), "both client final observations");
        Check(Read("done-client") != "FAIL" && Read("done-late") != "FAIL", "Both independent clients completed observations");
    }
    IEnumerator Order(string phase)
    {
        yield return Wait(() => Read("phase") == phase && Has("order-" + phase), "phase " + phase, 80);
        uint id = uint.Parse(Read("order-" + phase));
        yield return Wait(() => sync.Order.id == id && sync.CustomerWaiting && counter.ActiveCustomer != null &&
            counter.ActiveCustomer.OrderState.id == id && counter.ActiveCustomer.CanInteract(), "replicated arrival " + phase);
        var player = PlayerHealth.LocalInstance;
        player.transform.position = counter.ActiveCustomer.transform.position + Vector3.back;
        yield return new WaitForSeconds(.5f); // Allow real owner transform publication to reach server.
        Check(counter.ActiveCustomer.OrderState.chooseAny == sync.Order.chooseAny &&
            counter.ActiveCustomer.CustomerType == sync.Order.customerType, "Client customer matches authoritative snapshot: " + phase);
    }
    IEnumerator Submit(PopcornFlavor flavor, bool ghost, bool complete, bool expectCorrect = true)
    {
        var held = PlayerHealth.LocalInstance.GetComponent<PlayerHeldItems>();
        int responses = 0; bool correct = false;
        Action<bool, string> callback = (ok, message) => { responses++; correct = ok; };
        sync.LocalServeResult += callback;
        uint revision = held.Publish(new TaskHeldState { hasItem = true, hasPopcorn = true, flavor = flavor, ghostMixed = ghost });
        yield return Wait(() => held.TaskItem.revision == revision && held.TaskItem.IsReady, "held-item command and replication");
        uint id = sync.Order.id;
        sync.RequestServe(id, revision);
        yield return Wait(() => responses == 1, "targeted serve response");
        Check(correct == expectCorrect, "Real remote serve command result matches recipe rules");
        yield return Wait(() => !held.TaskItem.hasItem && (complete ? !sync.CustomerWaiting : sync.Order.DeliveredCount >= 1), "consume/order replication");
        int score = sync.Score;
        sync.RequestServe(id, revision);
        yield return Wait(() => responses == 2, "duplicate serve rejection");
        yield return new WaitForSeconds(.2f);
        Check(!correct && sync.Score == score, "Duplicate remote request is rejected without another score");
        sync.LocalServeResult -= callback;
    }
    IEnumerator Client()
    {
        Mark("client-ready");
        yield return Order("human-choice");
        Check(sync.Order.chooseAny && sync.Order.customerType == PopcornCustomerType.Human &&
            sync.Order.Label().Contains("ANY 1 PREPARED ITEM"), "Human choice flag, type and instructions replicate to remote client");
        uint originalId = sync.Order.id;
        sync.ServerSeatCustomer();
        Check(sync.Order.id == originalId, "Remote client cannot generate or replace authoritative customer");
        yield return Submit(PopcornFlavor.BBQ, false, true); Mark("human-done");
        yield return Wait(() => board.ProgressOf(bbq) == 1, "client BBQ quest progress after human choice");
        Check(board.ProgressOf(bbq) == 1, "Client task board receives actual-flavor credit");
        yield return Order("ghost-choice");
        Check(sync.Order.chooseAny && sync.Order.customerType == PopcornCustomerType.Ghost, "Ghost choice flag and type replicate");
        yield return Wait(() => Has("late-choice-checked"), "late observer choice confirmation", 90);
        yield return Submit(PopcornFlavor.BBQ, true, true); Mark("ghost-done");
        yield return Order("wrong-choice");
        float health = PlayerHealth.LocalInstance.CurrentHealth;
        yield return Submit(PopcornFlavor.BBQ, false, true, false); Mark("wrong-done");
        yield return Wait(() => PlayerHealth.LocalInstance.CurrentHealth <= health - 10, "ghost damage replication");
        Check(PlayerHealth.LocalInstance.CurrentHealth <= health - 10, "Wrong ghost choice applies server damage on remote client");
        yield return Order("partial");
        uint partialId = sync.Order.id;
        yield return Submit(PopcornFlavor.Cheese, false, false); Mark("partial-done");
        yield return Wait(() => Has("finish-partial"), "late observer confirmation", 90);
        Check(sync.Order.id == partialId && sync.Order.DeliveredCount == 1, "Existing client retains partial order while another client joins");
        yield return Submit(PopcornFlavor.BBQ, false, true); Mark("partial-finished");
        for (int i = 1; i <= 5; i++)
        {
            yield return Order("miss-" + i);
            yield return Submit(PopcornFlavor.Cheese, false, true); Mark("miss-done-" + i);
        }
        yield return Order("guaranteed");
        Check(sync.Order.chooseAny || sync.Order.first == PopcornFlavor.BBQ, "Client receives server's sixth guaranteed order");
        bool ghost = sync.Order.customerType == PopcornCustomerType.Ghost;
        yield return Submit(PopcornFlavor.BBQ, ghost, sync.Order.Count == 1);
        if (sync.CustomerWaiting) yield return Submit(sync.Order.second, ghost, true);
        Mark("guaranteed-done");
        yield return Wait(() => Read("phase") == "finish" || Has("finish"), "host finish");
        Check(((QuestOrderDirector)Get(sync, "orderDirector")).QuestMisses == 0, "Remote client never advances a local guarantee counter");
        Mark("client-checked");
    }
    IEnumerator LateObserver()
    {
        yield return Wait(() => sync.CustomerWaiting && sync.Order.chooseAny && sync.Order.customerType == PopcornCustomerType.Ghost &&
            counter.ActiveCustomer != null && counter.ActiveCustomer.CanInteract(), "late client's initial choice order");
        Check(sync.Order.chooseAny && counter.ActiveCustomer.OrderState.chooseAny && sync.Order.Label().Contains("ANY 1 PREPARED ITEM"),
            "Late client initial snapshot preserves choice flag, ghost type and instructions");
        yield return Wait(() => board.ProgressOf(bbq) == 1 && board.TargetOf(bbq) == 100, "late client's initial quest progress");
        Check(board.ProgressOf(bbq) == 1, "Late client initial task board includes earlier remote sale");
        Mark("late-choice-checked");
        yield return Wait(() => sync.CustomerWaiting && sync.Order.DeliveredCount == 1 && counter.ActiveCustomer != null &&
            counter.ActiveCustomer.CanInteract(), "late client's initial partial order");
        Check(sync.Order.Count == 2 && sync.Order.first == PopcornFlavor.Cheese && sync.Order.second == PopcornFlavor.BBQ,
            "Late client receives current two-item order with its delivered mask");
        Check(board.ProgressOf(bbq) == 2 && board.TargetOf(bbq) == 100, "Late client receives existing task progress and adjusted targets");
        Mark("late-checked");
        yield return Wait(() => Has("finish"), "host finish", 100);
        yield return Wait(() => board.ProgressOf(bbq) >= 4 && !sync.CustomerWaiting, "late observer final replication");
        Check(board.ProgressOf(bbq) >= 4, "Late observer receives subsequent remote sales and guaranteed quest progress");
        Mark("late-finished");
    }
}
