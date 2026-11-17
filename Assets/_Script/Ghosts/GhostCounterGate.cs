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
///
/// 15 Nov 2026 (Mr.k): a RESTROOM BREAK sign standing on its blue spot also closes its
/// counter (RestBreakSign). The minigames still read TicketHeld / PopcornHeld only.
/// </summary>
public static class GhostCounterGate
{
    /// <summary>True = the ticket counter must not start a new customer.</summary>
    public static bool TicketHeld => TicketHeldByGhost || TicketClosedBySign || BeforeShift;

    /// <summary>True = the popcorn counter must not seat a new customer.</summary>
    public static bool PopcornHeld => PopcornHeldByGhost || PopcornClosedBySign || BeforeShift;

    /// <summary>17 Nov (Task 8): the pre-shift (before 00:00): no customers yet.</summary>
    public static bool BeforeShift => MatchDirector.Instance != null && MatchDirector.Instance.Phase == MatchPhase.PreShift;

    /// <summary>The Missing Parts ghost stands at the counter.</summary>
    public static bool TicketHeldByGhost, PopcornHeldByGhost;

    /// <summary>The RESTROOM BREAK sign stands on the counter's blue spot (counter closed).</summary>
    public static bool TicketClosedBySign, PopcornClosedBySign;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        TicketHeldByGhost = false;
        PopcornHeldByGhost = false;
        TicketClosedBySign = false;
        PopcornClosedBySign = false;
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
