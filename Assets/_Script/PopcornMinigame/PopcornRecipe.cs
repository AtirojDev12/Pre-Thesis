using UnityEngine;

/// <summary>Shared recipe rules for local play and the server's order resolution.</summary>
public static class PopcornRecipe
{
    public const float DefaultUnfinishedQuestChance = 0.3f;

    public static PopcornOrderState RandomCustomerOrder(uint id, PopcornCustomerType type, float doubleChance = 0.3f,
        float unfinishedQuestChance = DefaultUnfinishedQuestChance)
    {
        PopcornFlavor first = RandomOrder();
        // Bias only the first item. The optional second item stays fully random.
        if (Random.value < Mathf.Clamp01(unfinishedQuestChance))
        {
            int matches = 0;
            for (int index = 0; index < 7; index++)
            {
                PopcornFlavor candidate = OrderAt(index);
                ZoneTaskKind kind = IsDrink(candidate) ? ZoneTaskKind.Water : ZoneTaskKind.Popcorn;
                for (int customer = 0; customer < 2; customer++)
                {
                    bool ghost = customer == 1;
                    if (!ZoneTaskList.HasUnfinishedSale(kind, candidate, -1, ghost)) continue;
                    // Uniform among matching menu/customer pairs, even with overlapping task lines.
                    if (Random.Range(0, ++matches) != 0) continue;
                    first = candidate;
                    type = ghost ? PopcornCustomerType.Ghost : PopcornCustomerType.Human;
                }
            }
        }
        PopcornFlavor second = Random.value < Mathf.Clamp01(doubleChance) ? RandomOrder() : PopcornFlavor.None;
        return PopcornOrderState.Create(id, type, first, second);
    }

    public static string ItemLabel(PopcornFlavor flavor) => IsDrink(flavor)
        ? UiFactory.FlavorName(flavor).ToUpperInvariant()
        : UiFactory.FlavorName(flavor).ToUpperInvariant() + " POPCORN";
    public static bool IsDrink(PopcornFlavor flavor) => flavor == PopcornFlavor.Drink ||
        flavor == PopcornFlavor.Pepsi || flavor == PopcornFlavor.Fanta || flavor == PopcornFlavor.OrangeJuice;
    public static bool IsOrder(PopcornFlavor flavor) => flavor == PopcornFlavor.Cheese ||
        flavor == PopcornFlavor.BBQ || flavor == PopcornFlavor.Paprika || IsDrink(flavor);

    public static PopcornFlavor RandomOrder() => OrderAt(Random.Range(0, 7));

    private static PopcornFlavor OrderAt(int index)
    {
        switch (index)
        {
            case 0: return PopcornFlavor.Cheese;
            case 1: return PopcornFlavor.BBQ;
            case 2: return PopcornFlavor.Paprika;
            case 3: return PopcornFlavor.Drink;
            case 4: return PopcornFlavor.Pepsi;
            case 5: return PopcornFlavor.Fanta;
            default: return PopcornFlavor.OrangeJuice;
        }
    }

    public static bool Matches(PopcornFlavor item, bool ghostMixed, PopcornFlavor order, PopcornCustomerType type) =>
        IsOrder(item) && item == order && ghostMixed == (type == PopcornCustomerType.Ghost);

    public static string OrderLabel(PopcornCustomerType type, PopcornFlavor order) =>
        type.ToString().ToUpperInvariant() + " CUSTOMER\n" + UiFactory.ItemName(order) +
        (type == PopcornCustomerType.Ghost ? "\n+ GHOST FLAVOR REQUIRED" : "\nNO GHOST FLAVOR");
}
