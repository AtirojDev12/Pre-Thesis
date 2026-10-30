using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Focused play-mode checks using the real authoritative serving resolver, without simulated keyboard input.</summary>
[InitializeOnLoad]
public static class QuestCustomerPlayChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> results = new List<string>();
    static int frames;
    static PlayerHealth player;
    static PopcornNetSync sync;
    static PopcornGameManager manager;
    static CounterSlot slot;
    static uint nextId = 10000;
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Private).SetValue(obj, value);
    static object Get(object obj, string field) => obj.GetType().GetField(field, Private).GetValue(obj);
    static void Call(object obj, string method) => obj.GetType().GetMethod(method, Private).Invoke(obj, null);
    static void Check(bool pass, string label)
    {
        results.Add((pass ? "PASS " : "FAIL ") + label);
        if (!pass) throw new Exception(label);
    }
    static QuestCustomerPlayChecks()
    {
        EditorApplication.playModeStateChanged += change =>
        {
            if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("QuestCustomerPlayChecks", false)) return;
            frames = 0;
            EditorApplication.update += Tick;
        };
    }
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
        EditorSceneManager.OpenScene("Assets/Scenes/Map/Cinema_GamePlay.unity");
        SessionState.SetBool("QuestCustomerPlayChecks", true);
        EditorApplication.EnterPlaymode();
    }
    static void Seat(PopcornOrderState order)
    {
        manager.StopAllCoroutines();
        if (slot.ActiveCustomer != null)
        {
            var previous = slot.ActiveCustomer;
            slot.Vacate(previous);
            previous.gameObject.SetActive(false);
            Object.Destroy(previous.gameObject);
        }
        Set(sync, "currentOrder", order);
        Call(manager, "SeatCustomerFromSync");
        var customer = slot.ActiveCustomer;
        var field = typeof(PopcornCustomer).GetField("state", Private);
        field.SetValue(customer, Enum.ToObject(field.FieldType, 1));
        player.transform.position = customer.transform.position + Vector3.back;
    }
    static uint Serve(PopcornFlavor flavor, bool ghost)
    {
        var held = player.GetComponent<PlayerHeldItems>();
        uint revision = held.Publish(new TaskHeldState { hasItem = true, isCup = PopcornRecipe.IsDrink(flavor),
            hasPopcorn = !PopcornRecipe.IsDrink(flavor), flavor = flavor, ghostMixed = ghost });
        sync.RequestServe(sync.Order.id, revision);
        return revision;
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        EditorApplication.Step();
        if (++frames < 25) return;
        EditorApplication.update -= Tick;
        SessionState.SetBool("QuestCustomerPlayChecks", false);
        int exit = 0;
        try
        {
            player = PlayerHealth.LocalInstance;
            sync = PopcornNetSync.Instance;
            manager = Object.FindAnyObjectByType<PopcornGameManager>();
            slot = Object.FindAnyObjectByType<CounterSlot>();
            Check(player != null && sync != null && manager != null && slot != null, "Cinema initializes real serving components offline");
            var foodBoard = new GameObject("Controlled food quests").AddComponent<ZoneTaskList>();
            Set(foodBoard, "tasks", new List<ZoneTask> { new ZoneTask { kind = ZoneTaskKind.Water,
                flavor = PopcornFlavor.OrangeJuice, target = 100 } });
            Set(foodBoard, "offlineTargets", new[] { 100 });
            Set(foodBoard, "offlineProgress", new[] { 0 });
            foreach (var type in new[] { PopcornCustomerType.Human, PopcornCustomerType.Ghost })
            {
                var order = PopcornOrderState.Create(++nextId, type, PopcornFlavor.Cheese);
                order.chooseAny = true;
                Seat(order);
                int score = sync.Score;
                int progress = foodBoard.ProgressOf(0);
                uint revision = Serve(PopcornFlavor.OrangeJuice, type == PopcornCustomerType.Ghost);
                Check(sync.Order.Complete && !sync.CustomerWaiting && sync.Score == score + 1 &&
                    !player.GetComponent<PlayerHeldItems>().TaskItem.hasItem, "Choice accepts actual prepared item exactly once for " + type);
                Check(foodBoard.ProgressOf(0) == progress + 1, "Choice credits actual Orange Juice instead of placeholder Cheese");
                sync.RequestServe(order.id, revision);
                Check(sync.Score == score + 1 && foodBoard.ProgressOf(0) == progress + 1, "Duplicate choice request cannot score or count twice");
            }
            foreach (var type in new[] { PopcornCustomerType.Human, PopcornCustomerType.Ghost })
            {
                var order = PopcornOrderState.Create(++nextId, type, PopcornFlavor.Cheese);
                order.chooseAny = true;
                Seat(order);
                int score = sync.Score;
                Serve(PopcornFlavor.Pepsi, type != PopcornCustomerType.Ghost);
                Check(!sync.CustomerWaiting && !sync.Order.Complete && sync.Score == score,
                    "Choice rejects incorrect seasoning for " + type);
            }
            // Isolate board matching so all five Cheese customers are irrelevant to quests.
            foreach (var board in Object.FindObjectsByType<ZoneTaskList>(FindObjectsSortMode.None))
                if (board != foodBoard) board.enabled = false;
            Set(foodBoard, "tasks", new List<ZoneTask> { new ZoneTask { kind = ZoneTaskKind.Water,
                flavor = PopcornFlavor.Drink, customer = ZoneTaskCustomer.Ghost, target = 100 } });
            Set(foodBoard, "offlineProgress", new[] { 0 });
            var director = (QuestOrderDirector)Get(sync, "orderDirector");
            director.Reset();
            for (int i = 1; i <= 5; i++)
            {
                Seat(PopcornOrderState.Create(++nextId, PopcornCustomerType.Human, PopcornFlavor.Cheese));
                Serve(PopcornFlavor.Cheese, false);
                Check(director.QuestMisses == i, "Authoritative completed irrelevant customer increments drought " + i);
            }
            sync.ServerSeatCustomer();
            var guaranteed = sync.Order;
            Check(guaranteed.customerType == PopcornCustomerType.Ghost && (guaranteed.chooseAny || guaranteed.first == PopcornFlavor.Drink),
                "Actual server spawn guarantees sixth customer against unfinished ghost Water task");
            sync.ServerSeatCustomer();
            Check(sync.Order.id == guaranteed.id, "Repeated spawn cannot replace a waiting order or spend another roll");
            Seat(guaranteed);
            Serve(PopcornFlavor.Drink, true);
            if (sync.CustomerWaiting) Serve(sync.Order.second, true);
            Check(director.QuestMisses == 0 && foodBoard.ProgressOf(0) >= 1, "Real guaranteed delivery advances quest and resets drought");

            sync.ServerSeatCustomer(); // Begin a fresh, non-guaranteed customer before fixing its test menu.
            Seat(PopcornOrderState.Create(++nextId, PopcornCustomerType.Human, PopcornFlavor.Cheese, PopcornFlavor.BBQ));
            Serve(PopcornFlavor.Cheese, false);
            Check(sync.CustomerWaiting && director.QuestMisses == 0, "First item of two-item customer does not count as a completed customer");
            uint lastRevision = Serve(PopcornFlavor.BBQ, false);
            Check(!sync.CustomerWaiting && director.QuestMisses == 1, "Two-item order increments drought only once on completion");
            sync.RequestServe(sync.Order.id, lastRevision);
            Check(director.QuestMisses == 1, "Duplicate final item cannot increment drought again");
        }
        catch (Exception e) { results.Add("FAIL " + e); exit = 1; }
        File.WriteAllLines("quest-customer-play-results.txt", results);
        EditorApplication.Exit(exit);
    }
}
