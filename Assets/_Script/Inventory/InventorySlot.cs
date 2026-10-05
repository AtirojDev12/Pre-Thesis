using System;

/// <summary>
/// One hotbar slot. Replicated by PlayerInventory's SyncList, so it must stay a
/// plain struct of simple fields (Mirror generates its reader/writer).
/// </summary>
[Serializable]
public struct InventorySlot
{
    /// <summary>ItemCatalog ID. Empty = empty slot.</summary>
    public string itemId;

    /// <summary>For radios: switched on. Ignored for other items.</summary>
    public bool poweredOn;

    /// <summary>
    /// A SECOND copy of an item you already carry (2 Oct). Cannot be used, only
    /// carried and dropped (Q) for a friend. Becomes usable if your first copy goes.
    /// </summary>
    public bool spare;

    /// <summary>
    /// Flashlights (5 Oct): battery left, 0..1. Lives in the slot so it travels with
    /// the item when it is dropped and picked up. Ignored for other items.
    /// </summary>
    public float charge;

    /// <summary>
    /// Stackable items (5 Oct, Battery): how many are in this ONE slot. Normal items: 1.
    /// Use <see cref="Units"/> to read it (old data may hold 0).
    /// </summary>
    public int count;

    public int Units => IsEmpty ? 0 : (count < 1 ? 1 : count);

    public bool IsEmpty => string.IsNullOrEmpty(itemId);

    public static InventorySlot Empty => new InventorySlot { itemId = string.Empty, poweredOn = false, spare = false, charge = 0f, count = 0 };

    /// <summary>A new item. Flashlights start OFF with a full battery; radios start ON.</summary>
    public static InventorySlot Of(string id, bool powered = true) =>
        new InventorySlot { itemId = id, poweredOn = powered && !ItemCatalog.IsFlashlight(id), charge = 1f, count = 1 };
}
