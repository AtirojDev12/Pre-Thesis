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

    public bool IsEmpty => string.IsNullOrEmpty(itemId);

    public static InventorySlot Empty => new InventorySlot { itemId = string.Empty, poweredOn = false, spare = false };

    public static InventorySlot Of(string id, bool powered = true) =>
        new InventorySlot { itemId = id, poweredOn = powered };
}
