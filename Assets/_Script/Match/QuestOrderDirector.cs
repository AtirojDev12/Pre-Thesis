using System.Collections.Generic;
using UnityEngine;

/// <summary>One server/offline counter's drought protection. Never run on remote clients.</summary>
public sealed class QuestOrderDirector
{
    public const int QuestMissLimit = 5;
    public const int ChoiceMissLimit = 10;
    private readonly bool tickets;
    private readonly Dictionary<ZoneTask, int> lastOpportunity = new Dictionary<ZoneTask, int>();
    private int sequence;
    private int questMisses;
    private int choiceMisses;
    private bool guaranteed, advanced, choice;
    private ZoneTask finalTask;

    private struct Target
    {
        public ZoneTask task;
        public int remaining;
    }

    public QuestOrderDirector(bool tickets = false) { this.tickets = tickets; }
    public int QuestMisses => questMisses;
    public int ChoiceMisses => choiceMisses;

    public void Reset()
    {
        lastOpportunity.Clear();
        sequence = questMisses = choiceMisses = 0;
        guaranteed = advanced = choice = false;
        finalTask = null;
    }

    private bool Eligible(ZoneTask task, int movieCount)
    {
        if (tickets) return task.kind == ZoneTaskKind.Ticket && task.movie >= 0 && task.movie <= movieCount;
        if (task.kind != ZoneTaskKind.Popcorn && task.kind != ZoneTaskKind.Water) return false;
        for (int i = 0; i < 7; i++)
        {
            PopcornFlavor flavor = PopcornRecipe.OrderAt(i);
            if (task.Matches(PopcornRecipe.IsDrink(flavor) ? ZoneTaskKind.Water : ZoneTaskKind.Popcorn,
                flavor, -1, false) || task.Matches(PopcornRecipe.IsDrink(flavor) ? ZoneTaskKind.Water : ZoneTaskKind.Popcorn,
                flavor, -1, true)) return true;
        }
        return false;
    }

    private List<Target> OpenTargets(int movieCount)
    {
        var result = new List<Target>();
        foreach (ZoneTaskList board in ZoneTaskList.All)
        {
            if (board == null) continue;
            for (int i = 0; i < board.TaskCount; i++)
                if (!board.IsTaskDone(i) && Eligible(board.TaskAt(i), movieCount))
                    result.Add(new Target { task = board.TaskAt(i), remaining = board.TargetOf(i) - board.ProgressOf(i) });
        }
        return result;
    }

    private void RefreshFinal(List<Target> open)
    {
        ZoneTask next = open.Count == 1 && open[0].remaining == 1 ? open[0].task : null;
        if (next != finalTask) choiceMisses = 0;
        finalTask = next;
        if (open.Count == 0) questMisses = 0;
    }

    public float ChoiceChance()
    {
        var open = OpenTargets(0);
        RefreshFinal(open);
        return ChoiceChance(open);
    }

    private static float ChoiceChance(List<Target> open) =>
        open.Count != 1 ? 0.02f : open[0].remaining == 1 ? 0.30f : 0.10f;

    private ZoneTask Oldest(List<Target> open)
    {
        ZoneTask selected = null;
        int oldest = int.MaxValue;
        foreach (Target target in open)
        {
            int stamp = lastOpportunity.TryGetValue(target.task, out int value) ? value : 0;
            if (stamp < oldest) { oldest = stamp; selected = target.task; }
        }
        return selected;
    }

    public PopcornOrderState NextFood(uint id, PopcornCustomerType type, float doubleChance = 0.3f)
    {
        var open = OpenTargets(0);
        RefreshFinal(open);
        advanced = false;
        guaranteed = questMisses >= QuestMissLimit && open.Count > 0;
        choice = (finalTask != null && choiceMisses >= ChoiceMissLimit) || Random.value < ChoiceChance(open);
        PopcornFlavor first = PopcornRecipe.RandomOrder();
        if (guaranteed)
        {
            ZoneTask target = Oldest(open);
            int matches = 0;
            for (int i = 0; i < 7; i++)
                for (int customer = 0; customer < 2; customer++)
                {
                    PopcornFlavor flavor = PopcornRecipe.OrderAt(i);
                    if (!target.Matches(PopcornRecipe.IsDrink(flavor) ? ZoneTaskKind.Water : ZoneTaskKind.Popcorn,
                        flavor, -1, customer == 1) || Random.Range(0, ++matches) != 0) continue;
                    first = flavor;
                    type = customer == 1 ? PopcornCustomerType.Ghost : PopcornCustomerType.Human;
                }
        }
        var order = PopcornOrderState.Create(id, type, first,
            !choice && Random.value < Mathf.Clamp01(doubleChance) ? PopcornRecipe.RandomOrder() : PopcornFlavor.None);
        order.chooseAny = choice;
        if (choice) choiceMisses = 0; // An appearance spends the special-customer guarantee.
        sequence++;
        foreach (Target target in open)
        {
            bool ghost = type == PopcornCustomerType.Ghost;
            bool acceptsType = target.task.customer == ZoneTaskCustomer.Any ||
                target.task.customer == (ghost ? ZoneTaskCustomer.Ghost : ZoneTaskCustomer.Human);
            if (choice ? acceptsType : FoodMatches(target.task, order.first, ghost) ||
                (order.second != PopcornFlavor.None && FoodMatches(target.task, order.second, ghost)))
                lastOpportunity[target.task] = sequence;
        }
        return order;
    }

    private static bool FoodMatches(ZoneTask task, PopcornFlavor flavor, bool ghost) =>
        task.Matches(PopcornRecipe.IsDrink(flavor) ? ZoneTaskKind.Water : ZoneTaskKind.Popcorn, flavor, -1, ghost);

    public void NextTicket(int movieCount, ref int movie, ref bool ghost)
    {
        var open = OpenTargets(movieCount);
        RefreshFinal(open);
        advanced = choice = false;
        guaranteed = questMisses >= QuestMissLimit && open.Count > 0;
        if (guaranteed)
        {
            ZoneTask target = Oldest(open);
            movie = target.movie == 0 ? Random.Range(0, movieCount) : target.movie - 1;
            if (target.customer != ZoneTaskCustomer.Any) ghost = target.customer == ZoneTaskCustomer.Ghost;
        }
        sequence++;
        foreach (Target target in open)
            if (target.task.Matches(ZoneTaskKind.Ticket, PopcornFlavor.None, movie, ghost))
                lastOpportunity[target.task] = sequence;
    }

    /// <summary>Call BEFORE reporting a successful item to the boards, including partial deliveries.</summary>
    public void RecordSale(ZoneTaskKind kind, PopcornFlavor flavor, int movie, bool ghost)
    {
        if (!ZoneTaskList.HasUnfinishedSale(kind, flavor, movie, ghost)) return;
        advanced = true;
        questMisses = 0;
    }

    /// <summary>Call once per fully resolved customer. Wrong submissions cannot farm counters.</summary>
    public void ResolveCustomer(bool correct)
    {
        if (advanced || guaranteed) questMisses = 0;
        else if (correct) questMisses++;
        ZoneTask previousFinal = finalTask;
        RefreshFinal(OpenTargets(tickets ? int.MaxValue : 0));
        if (correct && !choice && finalTask != null && finalTask == previousFinal) choiceMisses++;
    }
}
