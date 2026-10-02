using System;
using UnityEngine;

/// <summary>One atomic snapshot: partial deliveries update the same customer.</summary>
[Serializable]
public struct PopcornOrderState
{
    public uint id;
    public PopcornCustomerType customerType;
    public PopcornFlavor first, second;
    public byte delivered;
    public bool waiting;
    public int Count => second == PopcornFlavor.None ? 1 : 2;
    public int DeliveredCount => ((delivered & 1) != 0 ? 1 : 0) + ((delivered & 2) != 0 ? 1 : 0);
    public bool Complete => DeliveredCount >= Count;

    public static PopcornOrderState Create(uint id, PopcornCustomerType type, PopcornFlavor first, PopcornFlavor second = PopcornFlavor.None) =>
        new PopcornOrderState { id = id, customerType = type, first = first, second = second, waiting = true };

    public int Match(PopcornFlavor flavor, bool ghostMixed)
    {
        if (!waiting || ghostMixed != (customerType == PopcornCustomerType.Ghost)) return -1;
        if ((delivered & 1) == 0 && first == flavor) return 0;
        if (second != PopcornFlavor.None && (delivered & 2) == 0 && second == flavor) return 1;
        return -1;
    }

    public void Accept(int index) => delivered |= (byte)(1 << index);

    public string Label()
    {
        string items;
        if (second == first)
            items = PopcornRecipe.ItemLabel(first) + " ×2  " + DeliveredCount + "/2";
        else
        {
            items = PopcornRecipe.ItemLabel(first) + "  " + (((delivered & 1) != 0) ? "✓" : "0/1");
            if (second != PopcornFlavor.None)
                items += "\n" + PopcornRecipe.ItemLabel(second) + "  " + (((delivered & 2) != 0) ? "✓" : "0/1");
        }
        return customerType.ToString().ToUpperInvariant() + " CUSTOMER\n" + items +
            (customerType == PopcornCustomerType.Ghost ? "\n+ GHOST FLAVOR ON EACH ITEM" : "\nNO GHOST FLAVOR");
    }
}
