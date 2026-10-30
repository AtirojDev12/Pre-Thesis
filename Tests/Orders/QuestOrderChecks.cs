// Run in an isolated Unity project with -executeMethod QuestOrderChecks.Run.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Mirror;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;
using Object = UnityEngine.Object;

public static class QuestOrderChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> results = new List<string>();
    static ZoneTaskList board;
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Private).SetValue(obj, value);
    static void Check(bool pass, string label)
    {
        results.Add((pass ? "PASS " : "FAIL ") + label);
        if (!pass) throw new Exception(label);
    }
    static void Board(params ZoneTask[] tasks)
    {
        Set(board, "tasks", new List<ZoneTask>(tasks));
        int[] targets = new int[tasks.Length];
        for (int i = 0; i < tasks.Length; i++) targets[i] = tasks[i].target;
        Set(board, "offlineTargets", targets);
        Set(board, "offlineProgress", new int[tasks.Length]);
    }
    static ZoneTask Food(PopcornFlavor flavor, int target = 3, ZoneTaskCustomer customer = ZoneTaskCustomer.Any) =>
        new ZoneTask { kind = PopcornRecipe.IsDrink(flavor) ? ZoneTaskKind.Water : ZoneTaskKind.Popcorn,
            flavor = flavor, target = target, customer = customer };
    static void Misses(QuestOrderDirector director, int count)
    {
        // Fixture: each iteration represents a fresh ordinary, irrelevant customer.
        for (int i = 0; i < count; i++)
        {
            Set(director, "guaranteed", false);
            Set(director, "advanced", false);
            Set(director, "choice", false);
            director.ResolveCustomer(true);
        }
    }
    static void AvoidChoice()
    {
        for (int seed = 1; ; seed++)
        {
            Random.InitState(seed);
            var state = Random.state;
            if (Random.value > .5f) { Random.state = state; return; }
        }
    }
    static void ChoiceRate(float expected)
    {
        var director = new QuestOrderDirector();
        int choices = 0, humans = 0, doubles = 0, normals = 0;
        for (uint i = 0; i < 40000; i++)
        {
            var order = director.NextFood(i, Random.value < .5f ? PopcornCustomerType.Human : PopcornCustomerType.Ghost);
            if (order.chooseAny) { choices++; if (order.customerType == PopcornCustomerType.Human) humans++; }
            else { normals++; if (order.Count == 2) doubles++; }
        }
        Check(Math.Abs(choices / 40000f - expected) < .01f, "Combined choice probability " + expected.ToString("P0"));
        Check(Math.Abs(humans / (float)choices - .5f) < .06f, "Choice types split evenly");
        Check(Math.Abs(doubles / (float)normals - .3f) < .01f, "Normal two-item probability preserved");
    }
    public static void Run()
    {
        GameObject boardObject = null;
        var savedRandom = Random.state;
        int exit = 0;
        try
        {
            foreach (var existing in Object.FindObjectsByType<ZoneTaskList>(FindObjectsSortMode.None))
            {
                existing.enabled = false;
                typeof(ZoneTaskList).GetMethod("OnDisable", Private).Invoke(existing, null);
            }
            boardObject = new GameObject("Quest guarantee test board");
            board = boardObject.AddComponent<ZoneTaskList>();
            typeof(ZoneTaskList).GetMethod("OnEnable", Private).Invoke(board, null);
            Random.InitState(12345);
            Board(Food(PopcornFlavor.Cheese, 5, ZoneTaskCustomer.Ghost));
            Set(board, "offlineTargets", new[] { 10 });
            Set(board, "offlineProgress", new[] { 5 });
            Check(ZoneTaskList.HasUnfinishedSale(ZoneTaskKind.Popcorn, PopcornFlavor.Cheese, -1, true), "Difficulty-adjusted target remains eligible");
            int matches = 0;
            for (uint i = 0; i < 40000; i++)
            {
                var order = PopcornRecipe.RandomCustomerOrder(i, Random.value < .5f ? PopcornCustomerType.Human : PopcornCustomerType.Ghost);
                if (order.first == PopcornFlavor.Cheese && order.customerType == PopcornCustomerType.Ghost) matches++;
            }
            Check(Math.Abs(matches / 40000f - 1f / 14) < .01f, "Ordinary orders have no hidden 30 percent quest bias");
            Board(Food(PopcornFlavor.Drink), Food(PopcornFlavor.Pepsi)); ChoiceRate(.02f);
            Board(Food(PopcornFlavor.Drink)); ChoiceRate(.1f);
            Set(board, "offlineProgress", new[] { 2 }); ChoiceRate(.3f);
            Set(board, "offlineProgress", new[] { 3 }); ChoiceRate(.02f);

            foreach (var type in new[] { PopcornCustomerType.Human, PopcornCustomerType.Ghost })
                for (int i = 0; i < 7; i++)
                {
                    var flavor = PopcornRecipe.OrderAt(i);
                    var order = PopcornOrderState.Create(71, type, PopcornFlavor.Cheese);
                    order.chooseAny = true;
                    bool ghost = type == PopcornCustomerType.Ghost;
                    Check(order.Count == 1 && order.Match(flavor, ghost) == 0 && order.Match(flavor, !ghost) == -1,
                        "Choice accepts " + flavor + " only with correct seasoning for " + type);
                    Check(order.Match(PopcornFlavor.None, ghost) == -1, "Choice rejects empty recipe");
                    using (var writer = NetworkWriterPool.Get())
                    {
                        writer.Write(order);
                        using (var reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                        {
                            var copy = reader.Read<PopcornOrderState>();
                            Check(copy.chooseAny && copy.id == order.id && copy.customerType == type && copy.Match(flavor, ghost) == 0,
                                "Mirror snapshot preserves choice matching");
                        }
                    }
                    order.Accept(0);
                    Check(order.Complete && order.Match(flavor, ghost) == -1 && order.Label().Contains("ANY 1 PREPARED ITEM"),
                        "Choice completes once with explicit UI instructions");
                }

            Board(Food(PopcornFlavor.Cheese, 10, ZoneTaskCustomer.Ghost));
            var director = new QuestOrderDirector();
            for (uint i = 1; i <= 5; i++)
            {
                director.NextFood(i, PopcornCustomerType.Human, 0);
                director.ResolveCustomer(true);
                Check(director.QuestMisses == i, "Completed irrelevant customer increments drought " + i);
            }
            var forced = director.NextFood(6, PopcornCustomerType.Human, 0);
            Check(forced.customerType == PopcornCustomerType.Ghost && (forced.chooseAny || forced.first == PopcornFlavor.Cheese),
                "Sixth food customer guarantees eligible menu and customer type");
            director.ResolveCustomer(false);
            Check(director.QuestMisses == 0, "Wrong guaranteed order spends its opportunity");
            director.NextFood(7, PopcornCustomerType.Human); director.ResolveCustomer(false);
            Check(director.QuestMisses == 0, "Wrong ordinary order cannot farm drought");
            Misses(director, 4); director.NextFood(8, PopcornCustomerType.Human);
            director.RecordSale(ZoneTaskKind.Popcorn, PopcornFlavor.Cheese, -1, true);
            Check(director.QuestMisses == 0, "Relevant partial delivery immediately resets drought");
            director.ResolveCustomer(false);
            Check(director.QuestMisses == 0, "Later wrong item preserves earlier quest progress reset");
            director.Reset(); Misses(director, 5);
            director.NextFood(9, PopcornCustomerType.Human);
            director.RecordSale(ZoneTaskKind.Popcorn, PopcornFlavor.Cheese, -1, true);
            director.ResolveCustomer(true); Misses(director, 5);
            forced = director.NextFood(10, PopcornCustomerType.Human, 0);
            Check(forced.customerType == PopcornCustomerType.Ghost && (forced.chooseAny || forced.first == PopcornFlavor.Cheese),
                "Guarantee repeats until task completion");
            Board(Food(PopcornFlavor.Cheese), Food(PopcornFlavor.Drink));
            director.Reset(); Misses(director, 5);
            AvoidChoice();
            forced = director.NextFood(11, PopcornCustomerType.Human, 0);
            Check(forced.chooseAny || forced.first == PopcornFlavor.Cheese, "Unseen task receives first opportunity");
            director.ResolveCustomer(false); Misses(director, 5);
            AvoidChoice();
            forced = director.NextFood(12, PopcornCustomerType.Human, 0);
            Check(forced.chooseAny || forced.first == PopcornFlavor.Drink, "Oldest opportunity rotates between unfinished tasks");

            Board(Food(PopcornFlavor.Drink)); director.Reset();
            director.ChoiceChance();
            Set(board, "offlineProgress", new[] { 2 }); director.ChoiceChance();
            Misses(director, 10);
            Check(director.ChoiceMisses == 10, "Final-item stage counts ten completed normal customers");
            forced = director.NextFood(13, PopcornCustomerType.Human);
            Check(forced.chooseAny && forced.Count == 1 && director.ChoiceMisses == 0, "Eleventh customer is choice; appearance resets counter");
            director.ResolveCustomer(false);
            Check(director.ChoiceMisses == 0, "Wrong choice delivery cannot advance special guarantee");
            director.Reset(); director.ChoiceChance(); director.ResolveCustomer(false);
            Check(director.ChoiceMisses == 0, "Wrong ordinary delivery cannot advance special guarantee");
            director.RecordSale(ZoneTaskKind.Water, PopcornFlavor.Drink, -1, false);
            ZoneTaskList.ServerReportSale(ZoneTaskKind.Water, PopcornFlavor.Drink, -1, false);
            director.ResolveCustomer(true);
            Check(director.ChoiceMisses == 0 && Math.Abs(director.ChoiceChance() - .02f) < .001f,
                "Completion stops guarantees and returns base choice rate");

            Board(new ZoneTask { kind = ZoneTaskKind.Ticket, movie = 2, customer = ZoneTaskCustomer.Human, target = 3 });
            var tickets = new QuestOrderDirector(true); Misses(tickets, 5);
            int movie = 0; bool ghostTicket = true;
            tickets.NextTicket(3, ref movie, ref ghostTicket);
            Check(movie == 1 && !ghostTicket, "Ticket guarantee matches movie and customer restriction");
            tickets.RecordSale(ZoneTaskKind.Ticket, PopcornFlavor.None, movie, ghostTicket); tickets.ResolveCustomer(true);
            Check(tickets.QuestMisses == 0 && director.QuestMisses == 0, "Food and ticket droughts reset independently");
            Board(new ZoneTask { kind = ZoneTaskKind.Ticket, movie = 0, customer = ZoneTaskCustomer.Ghost, target = 3 });
            tickets.Reset(); Misses(tickets, 5); movie = 1; ghostTicket = false;
            tickets.NextTicket(3, ref movie, ref ghostTicket);
            Check(movie >= 0 && movie < 3 && ghostTicket, "Wildcard ticket guarantee respects movie range and ghost type");
            Board(new ZoneTask { kind = ZoneTaskKind.Popcorn, flavor = PopcornFlavor.None, customer = ZoneTaskCustomer.Ghost, target = 3 });
            director.Reset(); Misses(director, 5); forced = director.NextFood(14, PopcornCustomerType.Human);
            Check(forced.customerType == PopcornCustomerType.Ghost && (forced.chooseAny || !PopcornRecipe.IsDrink(forced.first)),
                "Wildcard food guarantee respects category and type");
            Board(new ZoneTask { kind = ZoneTaskKind.CleanToilet, target = 6 });
            Check(Math.Abs(director.ChoiceChance() - .02f) < .001f, "Chores do not affect choice stages");
            board.enabled = false; typeof(ZoneTaskList).GetMethod("OnDisable", Private).Invoke(board, null);
            director.Reset(); Misses(director, 5); director.NextFood(15, PopcornCustomerType.Human);
            Check(director.QuestMisses == 0, "No active boards safely disables quest guarantee");
            director.Reset();
            Check(director.QuestMisses == 0 && director.ChoiceMisses == 0, "New session clears both counters");
        }
        catch (Exception e) { results.Add("FAIL " + e); exit = 1; }
        finally
        {
            if (boardObject != null) Object.DestroyImmediate(boardObject);
            Random.state = savedRandom;
            File.WriteAllLines("quest-order-results.txt", results);
            EditorApplication.Exit(exit);
        }
    }
}
