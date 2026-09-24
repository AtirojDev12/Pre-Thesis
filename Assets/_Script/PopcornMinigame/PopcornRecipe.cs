using UnityEngine;

/// <summary>Shared recipe rules for local play and the server's order resolution.</summary>
public static class PopcornRecipe
{
    public static bool IsOrder(PopcornFlavor flavor) => flavor == PopcornFlavor.Cheese ||
        flavor == PopcornFlavor.BBQ || flavor == PopcornFlavor.Paprika || flavor == PopcornFlavor.Drink;

    public static PopcornFlavor RandomOrder()
    {
        switch (Random.Range(0, 4))
        {
            case 0: return PopcornFlavor.Cheese;
            case 1: return PopcornFlavor.BBQ;
            case 2: return PopcornFlavor.Paprika;
            default: return PopcornFlavor.Drink;
        }
    }

    public static bool Matches(PopcornFlavor item, bool ghostMixed, PopcornFlavor order, PopcornCustomerType type) =>
        IsOrder(item) && item == order && ghostMixed == (type == PopcornCustomerType.Ghost);

    public static string OrderLabel(PopcornCustomerType type, PopcornFlavor order) =>
        type.ToString().ToUpperInvariant() + " CUSTOMER\n" + UiFactory.ItemName(order) +
        (type == PopcornCustomerType.Ghost ? "\n+ GHOST FLAVOR REQUIRED" : "\nNO GHOST FLAVOR");
}
