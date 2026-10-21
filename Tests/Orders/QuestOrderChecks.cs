// Run in an isolated Unity project with -executeMethod QuestOrderChecks.Run.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;
using Object = UnityEngine.Object;

public static class QuestOrderChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> results = new List<string>();
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Private).SetValue(obj, value);
    static void Check(bool pass, string label)
    {
        results.Add((pass ? "PASS " : "FAIL ") + label);
        if (!pass) throw new Exception(label);
    }
    static float SampleFood(float bias, out float doubles)
    {
        int matching = 0, two = 0;
        Random.InitState(12345);
        for (int i = 0; i < 40000; i++)
        {
            var type = Random.value < .5f ? PopcornCustomerType.Human : PopcornCustomerType.Ghost;
            var order = PopcornRecipe.RandomCustomerOrder((uint)i, type, .3f, bias);
            if (order.first == PopcornFlavor.Cheese && order.customerType == PopcornCustomerType.Ghost) matching++;
            if (order.Count == 2) two++;
        }
        doubles = two / 40000f;
        return matching / 40000f;
    }
    public static void Run()
    {
        GameObject boardObject = null, ticketObject = null;
        var savedRandom = Random.state;
        int exit = 0;
        try
        {
            // Avoid scene boards affecting these controlled targets.
            foreach (var board in Object.FindObjectsByType<ZoneTaskList>(FindObjectsSortMode.None))
            {
                board.enabled = false;
                typeof(ZoneTaskList).GetMethod("OnDisable", Private).Invoke(board, null);
            }
            boardObject = new GameObject("Order bias test board");
            var tasks = boardObject.AddComponent<ZoneTaskList>();
            typeof(ZoneTaskList).GetMethod("OnEnable", Private).Invoke(tasks, null);
            Set(tasks, "tasks", new List<ZoneTask> {
                new ZoneTask { kind = ZoneTaskKind.Popcorn, flavor = PopcornFlavor.Cheese, customer = ZoneTaskCustomer.Ghost, target = 5 },
                new ZoneTask { kind = ZoneTaskKind.Water, flavor = PopcornFlavor.Pepsi, target = 5 },
                new ZoneTask { kind = ZoneTaskKind.Ticket, movie = 2, customer = ZoneTaskCustomer.Human, target = 5 }
            });
            // Use tonight's targets, including difficulty extras, rather than base targets.
            Set(tasks, "offlineTargets", new[] { 10, 5, 5 });
            Set(tasks, "offlineProgress", new[] { 5, 5, 0 });
            Check(ZoneTaskList.HasUnfinishedSale(ZoneTaskKind.Popcorn, PopcornFlavor.Cheese, -1, true), "Difficulty-adjusted unfinished target is eligible");
            Check(!ZoneTaskList.HasUnfinishedSale(ZoneTaskKind.Water, PopcornFlavor.Pepsi, -1, false), "Completed drink quest excluded");
            Check(!ZoneTaskList.HasUnfinishedSale(ZoneTaskKind.Popcorn, PopcornFlavor.Cheese, -1, false), "Customer restriction respected");
            float normal = SampleFood(0, out _);
            float biased = SampleFood(.3f, out float doubles);
            Check(Math.Abs(normal - 1f / 14) < .01f, "Zero bias keeps normal menu/customer distribution");
            Check(Math.Abs(biased - .35f) < .01f, "30 percent bias yields about 35 percent for one remaining ghost/menu pair: " + biased.ToString("P2"));
            Check(Math.Abs(doubles - .3f) < .01f, "Two-item order probability remains 30 percent");
            Check(SampleFood(1, out _) == 1, "Full bias always chooses an eligible menu/customer pair");

            ticketObject = new GameObject("Ticket bias test");
            ticketObject.SetActive(false); // No route/UI initialization required for selection checks.
            var ticket = ticketObject.AddComponent<TicketMinigame>();
            var pick = typeof(TicketMinigame).GetMethod("BiasUnfinishedTicketQuest", Private);
            int hit = 0;
            Random.InitState(7654);
            for (int i = 0; i < 40000; i++)
            {
                Set(ticket, "state", new TicketCustomerState { ghost = Random.value < .5f, requestedMovieIndex = Random.Range(0, 3) });
                pick.Invoke(ticket, null);
                if (!ticket.State.ghost && ticket.State.requestedMovieIndex == 1) hit++;
            }
            Check(Math.Abs(hit / 40000f - (.3f + .7f / 6)) < .01f, "Tickets use the same 30 percent quest mixture");
            Set(ticket, "unfinishedQuestChance", 1f);
            bool validTickets = true;
            for (int i = 0; i < 100; i++)
            {
                pick.Invoke(ticket, null);
                validTickets &= !ticket.State.ghost && ticket.State.requestedMovieIndex == 1;
            }
            Check(validTickets, "Ticket movie and customer restrictions respected");
            Set(tasks, "offlineProgress", new[] { 10, 5, 5 });
            Check(Math.Abs(SampleFood(1, out _) - 1f / 14) < .01f, "All quests finished falls back to normal food orders");
            var original = new TicketCustomerState { ghost = true, requestedMovieIndex = 2 };
            Set(ticket, "state", original);
            pick.Invoke(ticket, null);
            Check(ticket.State.ghost && ticket.State.requestedMovieIndex == 2, "All quests finished preserves normal ticket selection");
            tasks.enabled = false;
            typeof(ZoneTaskList).GetMethod("OnDisable", Private).Invoke(tasks, null);
            Check(Math.Abs(SampleFood(1, out _) - 1f / 14) < .01f, "No active boards falls back to normal orders");
            tasks.enabled = true;
            typeof(ZoneTaskList).GetMethod("OnEnable", Private).Invoke(tasks, null);
            Set(tasks, "offlineProgress", new[] { 0, 5, 5 });
            tasks.TaskAt(0).flavor = PopcornFlavor.None;
            tasks.TaskAt(0).customer = ZoneTaskCustomer.Any;
            bool validFood = true;
            for (int i = 0; i < 1000; i++)
            {
                var order = PopcornRecipe.RandomCustomerOrder((uint)i, PopcornCustomerType.Human, 0, 1);
                validFood &= !PopcornRecipe.IsDrink(order.first);
            }
            Check(validFood, "Any-flavor quest respects popcorn category");
        }
        catch (Exception e) { results.Add("FAIL " + e); exit = 1; }
        finally
        {
            if (ticketObject != null) Object.DestroyImmediate(ticketObject);
            if (boardObject != null) Object.DestroyImmediate(boardObject);
            Random.state = savedRandom;
            File.WriteAllLines("quest-order-results.txt", results);
            EditorApplication.Exit(exit);
        }
    }
}
