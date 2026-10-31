using UnityEngine;

/// <summary>
/// 31 Oct 2026 (Mr.k): lets a ghost hold a counter so no new customer walks in.
///
/// SERVER ONLY (clients never set it, so their copies stay false).
///   - TicketMinigame does not start its next customer while TicketHeld is true.
///   - PopcornGameManager does not seat its next customer while PopcornHeld is true.
/// A customer already at the counter is not touched: it finishes as normal.
///
/// Used by MissingPartsGhost (it stands at the counter until a player helps it).
/// </summary>
public static class GhostCounterGate
{
    /// <summary>True = the ticket counter must not start a new customer.</summary>
    public static bool TicketHeld;

    /// <summary>True = the popcorn counter must not seat a new customer.</summary>
    public static bool PopcornHeld;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        TicketHeld = false;
        PopcornHeld = false;
    }
}

/// <summary>31 Oct 2026: the upper-body parts the Missing Parts ghost can lose. Bit flags.</summary>
[System.Flags]
public enum GhostBodyPart : byte
{
    None = 0,
    LeftArm = 1,
    RightArm = 2,
    Head = 4,
    UpperBody = 8,
}

/// <summary>31 Oct 2026: item IDs, names and world prefabs of the ghost's body parts.</summary>
public static class GhostBodyParts
{
    public static readonly GhostBodyPart[] All =
        { GhostBodyPart.LeftArm, GhostBodyPart.RightArm, GhostBodyPart.Head, GhostBodyPart.UpperBody };

    public static string ItemId(GhostBodyPart part)
    {
        switch (part)
        {
            case GhostBodyPart.LeftArm: return ItemCatalog.GhostLeftArm;
            case GhostBodyPart.RightArm: return ItemCatalog.GhostRightArm;
            case GhostBodyPart.Head: return ItemCatalog.GhostHead;
            case GhostBodyPart.UpperBody: return ItemCatalog.GhostUpperBody;
            default: return null;
        }
    }

    public static GhostBodyPart FromItem(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return GhostBodyPart.None;
        foreach (GhostBodyPart part in All)
            if (ItemId(part) == itemId) return part;
        return GhostBodyPart.None;
    }

    public static string Name(GhostBodyPart part)
    {
        switch (part)
        {
            case GhostBodyPart.LeftArm: return "Left Arm";
            case GhostBodyPart.RightArm: return "Right Arm";
            case GhostBodyPart.Head: return "Head";
            case GhostBodyPart.UpperBody: return "Upper Body";
            default: return "";
        }
    }

    /// <summary>File name part of the world prefab (Resources/Items/WorldGhostPart_X).</summary>
    public static string Suffix(GhostBodyPart part) => Name(part).Replace(" ", "");

    public static string WorldPrefabPath(GhostBodyPart part) => "Items/WorldGhostPart_" + Suffix(part);
}
