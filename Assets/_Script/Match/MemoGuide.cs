using System.Collections.Generic;
using UnityEngine;

/// <summary>17 Nov 2026 (Task 8): how dangerous a rule step is (higher = shown first).</summary>
public enum GuideSeverity : byte
{
    None = 0,
    TaskBlock = 1,     // e.g. Ghost Flavor gone
    SanityDrain = 2,   // e.g. ghost room film late (1st miss)
    GhostRoam = 3,     // e.g. Staff time static
    ChaseOrHp = 4,     // ghost attack / chase, or HP damage
    InstantDeath = 5,
}

/// <summary>One step the Memo Book can show. A new <see cref="key"/> = a new step (costs sanity once).</summary>
public struct GuideEntry
{
    public bool mapRule;   // Map Rules always win over Zone Rules (none exist yet)
    public GuideSeverity severity;
    public string key;
    public string text;

    public bool IsSet => severity != GuideSeverity.None && !string.IsNullOrEmpty(text);

    /// <summary>Map Rules first, then the more dangerous one.</summary>
    public bool BetterThan(GuideEntry other)
    {
        if (!IsSet) return false;
        if (!other.IsSet) return true;
        if (mapRule != other.mapRule) return mapRule;
        return severity > other.severity;
    }

    public static GuideEntry Make(GuideSeverity severity, string key, string text, bool mapRule = false) =>
        new GuideEntry { severity = severity, key = key, text = text, mapRule = mapRule };
}

/// <summary>
/// 17 Nov 2026 (Mr.k, Task 8): the LIVE Memo Book.
///
///   From 00:00, in its own map, the OWNER's Memo Book shows ONE step at a time:
///     - a rule that is happening to you right now (survival steps, the most dangerous first:
///       Map Rules > Zone Rules; Instant Death > Ghost Attack/Chase/HP > Ghost Roam > Sanity > Task Block), else
///     - inside a zone box: the next step of the zone's first unfinished task line.
///   Never shows: who is alive, where players are, open exits, which zones are done, the time.
///   Reading a NEW step costs 2 sanity (above 50) or 1 (50 or less).
///
/// SERVER: rule steps are worked out on the host (it knows every rule's state) and sent to the
///   reader's PC only when they change (PlayerInventory TargetRpc).
/// CLIENT: task steps are worked out on the reader's PC from synced data (boards, zone boxes,
///   the item in your hands, chores).
/// Words: Resources/Tuning/MemoGuideTexts (Designer Settings > Memo Book Texts).
/// </summary>
public static class MemoGuide
{
    private static MemoGuideTexts T => MemoGuideTexts.Current;

    // ---- Server: the rule step for one player -------------------------------------------------

    private static int flavorEvents;
    private static bool flavorWasHome = true;

    /// <summary>SERVER. The most important rule step for this player right now (false = none).</summary>
    public static bool ServerBestRule(PlayerHealth p, out GuideEntry best)
    {
        best = default;
        if (p == null || p.IsDead) return false;

        foreach (ZoneRuleBase rule in ZoneRuleBase.All)
            if (rule != null && rule.ActiveThisRound && rule.ServerGuideFor(p, out GuideEntry e) && e.BetterThan(best)) best = e;

        foreach (FilmDeadline film in FilmDeadline.All)
            if (film != null && film.ServerGuideFor(p, out GuideEntry e) && e.BetterThan(best)) best = e;

        if (TicketGhostVisit.Instance != null && TicketGhostVisit.Instance.ServerGuideFor(p, out GuideEntry visit) && visit.BetterThan(best)) best = visit;

        foreach (MissingPartsGhost ghost in Object.FindObjectsByType<MissingPartsGhost>(FindObjectsSortMode.None))
            if (ghost != null && ghost.ServerGuideFor(p, out GuideEntry e) && e.BetterThan(best)) best = e;

        // The Ghost Flavor (Atiroj's popcorn code, read only).
        GhostFavorRecovery flavor = GhostFavorRecovery.Instance;
        if (flavor != null)
        {
            bool home = flavor.IsHome;
            if (!home && flavorWasHome) flavorEvents++;
            flavorWasHome = home;
            if (!home && InZoneOrNoBox(ZoneTaskList.TicketZoneID, p.transform.position))
            {
                GuideEntry e = GuideEntry.Make(GuideSeverity.TaskBlock, "flavor:" + flavorEvents, T.flavorFind);
                if (e.BetterThan(best)) best = e;
            }
        }
        return best.IsSet;
    }

    /// <summary>Inside that quest zone's box, or the map has no box for it (then: everywhere).</summary>
    public static bool InZoneOrNoBox(string zoneId, Vector3 point)
    {
        bool hasBox = false;
        foreach (RuleZone zone in RuleZone.All)
        {
            if (zone == null || zone.ZoneID != zoneId) continue;
            hasBox = true;
            if (zone.Contains(point)) return true;
        }
        return !hasBox;
    }

    // ---- Client: what the reader sees ----------------------------------------------------------

    /// <summary>The rule step the host sent for the local player (empty = none).</summary>
    public static GuideEntry LocalRule { get; private set; }

    private static string lastCharged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        LocalRule = default;
        lastCharged = null;
        flavorEvents = 0;
        flavorWasHome = true;
    }

    /// <summary>LOCAL (from the host). The local player's rule step changed.</summary>
    public static void LocalSetRule(string key, string text, GuideSeverity severity) =>
        LocalRule = GuideEntry.Make(severity, key, text);

    /// <summary>
    /// LOCAL. The held book is live: your own Memo Book, of this map, and the shift is running.
    /// Before 00:00, in the lobby, or someone else's copy = a normal book.
    /// </summary>
    public static bool LocalLive()
    {
        PlayerInventory inventory = PlayerInventory.Local;
        MatchDirector match = MatchDirector.Instance;
        PlayerHealth me = PlayerHealth.LocalInstance;
        if (inventory == null || match == null || me == null || PlayerInventory.InLobby || !match.RoundRunning) return false;
        InventorySlot held = inventory.HeldSlot;
        if (!MemoBook.OwnedBy(held, me.gameObject)) return false;
        return ItemCatalog.MemoBookMap(held.itemId) == match.MapID;
    }

    /// <summary>LOCAL. The step to show: a rule step if any, else the task step. Danger = a rule step.</summary>
    public static void LocalStep(out string key, out string text, out bool danger)
    {
        GuideEntry rule = LocalRule;
        if (rule.IsSet)
        {
            key = "rule:" + rule.key;
            text = rule.text;
            danger = true;
            return;
        }
        danger = false;
        LocalTaskStep(out key, out text);
    }

    /// <summary>LOCAL. A step is on screen with the book open: charge its sanity cost once.</summary>
    public static void LocalStepShown(string key)
    {
        if (string.IsNullOrEmpty(key) || key == lastCharged) return;
        lastCharged = key;
        // "Walk into a zone" / "nothing here" / "wait for the glow" are not steps: free.
        if (key.EndsWith(":none") || key.EndsWith(":nothing") || key.EndsWith(":wait")) return;
        if (PlayerInventory.Local != null) PlayerInventory.Local.RequestMemoStepRead(key);
    }

    // ---- Client: task steps ------------------------------------------------------------------

    /// <summary>LOCAL. The next step of the first unfinished task line of the zone you stand in.</summary>
    private static void LocalTaskStep(out string key, out string text)
    {
        PlayerHealth me = PlayerHealth.LocalInstance;
        RuleZone zone = me != null ? RuleZone.At(me.transform.position) : null;
        if (zone == null || string.IsNullOrEmpty(zone.ZoneID))
        {
            key = "task:none";
            text = T.noZone;
            return;
        }

        ZoneTask task = null;
        foreach (ZoneTaskList board in ZoneTaskList.All)
        {
            if (board == null || (board.CountedZoneID != zone.ZoneID && board.ZoneID != zone.ZoneID)) continue;
            for (int i = 0; i < board.TaskCount; i++)
                if (!board.IsTaskDone(i)) { task = board.TaskAt(i); break; }
            if (task != null) break;
        }
        if (task == null)
        {
            key = "task:" + zone.ZoneID + ":nothing";
            text = T.nothingHere;
            return;
        }

        string head = "<b>" + task.label + "</b>\n";
        switch (task.kind)
        {
            case ZoneTaskKind.Ticket:
                key = "task:ticket";
                text = head + T.ticketSell;
                return;

            case ZoneTaskKind.Popcorn:
            case ZoneTaskKind.Water:
            {
                TaskHeldState held = me.TryGetComponent(out PlayerHeldItems items) ? items.TaskItem : default;
                int step;
                string line;
                if (!held.IsInHand) { step = 0; line = T.foodPick; }
                else if (held.IsReady) { step = 3; line = T.foodServe; }
                else if (held.isCup) { step = 1; line = T.drinkFill; }
                else if (!held.hasPopcorn) { step = 1; line = T.popcornFill; }
                else { step = 2; line = T.popcornFlavor; }
                key = "task:food:" + step;
                text = head + line;
                return;
            }

            case ZoneTaskKind.FilmChange:
            {
                ZoneChoreSpot film = null;
                foreach (ZoneChoreSpot spot in ZoneChoreSpot.All)
                    if (spot != null && spot.Kind == ZoneTaskKind.FilmChange && spot.CountsForZoneBoard) { film = spot; break; }
                if (film == null || !film.Needed || string.IsNullOrEmpty(film.DueLabel))
                {
                    key = "task:film:wait";
                    text = head + T.filmWait;
                    return;
                }
                bool holding = CarryProp.LocalHeldLabel(film.NeedsItem) == film.DueLabel;
                key = "task:film:" + film.DueLabel + ":" + (holding ? 1 : 0);
                text = head + (holding ? T.filmInsert : T.filmTake).Replace("{film}", film.DueLabel);
                return;
            }

            default: // cleaning
            {
                bool stains = false;
                foreach (ZoneChoreSpot spot in ZoneChoreSpot.All)
                    if (spot != null && spot.Kind == task.kind && spot.Needed && spot.CountsForZoneBoard) { stains = true; break; }
                if (!stains)
                {
                    key = "task:clean:wait";
                    text = head + T.cleanWait;
                    return;
                }
                bool mop = CarryProp.LocalHolds(CarryProp.Mop);
                key = "task:clean:" + (mop ? 1 : 0);
                text = head + (mop ? T.cleanStains : T.cleanGetMop);
                return;
            }
        }
    }

    /// <summary>The zone list for the live page: "name - where" (no map, no done / not done).</summary>
    public static List<string> ZoneLines(MapGuide guide)
    {
        var lines = new List<string>();
        if (guide == null || guide.zones == null) return lines;
        foreach (MapGuide.ZoneText z in guide.zones)
            if (z != null) lines.Add(string.IsNullOrWhiteSpace(z.where) ? z.name : z.name + " - " + z.where);
        return lines;
    }
}
