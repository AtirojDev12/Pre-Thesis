using Mirror;
using UnityEngine;

/// <summary>
/// 17 Nov 2026 (Mr.k, Task 8): who owns a Memo Book copy, and what happens when the owner is gone.
///
///   - In a match, the books that came from a player's own save carry that player's tag
///     (InventorySlot.charge = <see cref="OwnerTag"/>). It travels with the item when dropped.
///   - Only the OWNER's copy can become the live guide. Anyone else holding it reads a normal book.
///   - The owner dies or leaves the match: every copy with their tag is destroyed (in hands and
///     on the floor). Nobody else keeps it (#72).
/// </summary>
public static class MemoBook
{
    /// <summary>The tag of a player (netId; 1 when offline).</summary>
    public static float OwnerTag(GameObject player)
    {
        if (NetworkMode.IsOffline || player == null) return 1f;
        NetworkIdentity id = player.GetComponent<NetworkIdentity>();
        return id != null && id.netId != 0 ? id.netId : 1f;
    }

    /// <summary>Is this slot a Memo Book that belongs to this player (and not a spare copy)?</summary>
    public static bool OwnedBy(InventorySlot slot, GameObject player) =>
        !slot.IsEmpty && !slot.spare && ItemCatalog.IsMemoBook(slot.itemId) && Mathf.Approximately(slot.charge, OwnerTag(player));

    /// <summary>
    /// SERVER. The owner is gone (died / left the match): destroy every copy with their tag,
    /// in every hotbar and on the floor. Returns how many were destroyed.
    /// </summary>
    public static int ServerDestroyCopiesOf(float tag)
    {
        int gone = 0;
        foreach (PlayerInventory inventory in Object.FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None))
        {
            for (int i = PlayerInventory.SlotCount - 1; i >= 0; i--)
            {
                InventorySlot s = inventory.GetSlot(i);
                if (s.IsEmpty || !ItemCatalog.IsMemoBook(s.itemId) || !Mathf.Approximately(s.charge, tag)) continue;
                if (inventory.ServerRemoveAt(i) != null) gone++;
            }
        }
        foreach (WorldInventoryItem world in Object.FindObjectsByType<WorldInventoryItem>(FindObjectsSortMode.None))
        {
            InventorySlot s = world.Item;
            if (s.IsEmpty || !ItemCatalog.IsMemoBook(s.itemId) || !Mathf.Approximately(s.charge, tag)) continue;
            if (NetworkServer.active && world.netIdentity != null && world.netIdentity.netId != 0) NetworkServer.Destroy(world.gameObject);
            else Object.Destroy(world.gameObject);
            gone++;
        }
        if (gone > 0) Debug.Log($"[MemoBook] The owner is gone: {gone} Memo Book copy(ies) destroyed.");
        return gone;
    }
}
