using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

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

        // 23 Nov (Mr.k): Map Rules always win over Zone Rules (GuideEntry.mapRule).
        MapRuleVip vip = MapRuleVip.Instance;
        if (vip != null && vip.Running)
            Offer(ref best, GuideEntry.Make(GuideSeverity.SanityDrain, vip.Current == MapRuleVip.Stage.Seated ? "vip:crank" : "vip:bring",
                vip.Current == MapRuleVip.Stage.Seated ? T.vipCrank : T.vipBring, true));
        if (MapRuleHunter.Active) Offer(ref best, GuideEntry.Make(GuideSeverity.InstantDeath, "hunter", T.hunterHide, true));
        if (MapRuleForeign.HiddenLeft > 0) Offer(ref best, GuideEntry.Make(GuideSeverity.TaskBlock, "foreign:find", T.foreignFind, true));
        else if (MapRuleForeign.PestCount > 0) Offer(ref best, GuideEntry.Make(GuideSeverity.SanityDrain, "foreign:pest", T.foreignPest, true));
        if (FilmGhostEvent.GhostOut) Offer(ref best, GuideEntry.Make(GuideSeverity.ChaseOrHp, "filmghost", T.filmGhostAvoid));

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

    private static void Offer(ref GuideEntry best, GuideEntry e)
    {
        if (e.BetterThan(best)) best = e;
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
        nearestKey = nearestText = "";
        nextNearest = 0f;
        tickets = null;
        nextCounterLookup = 0f;
        popcornNames = null;
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
        // "Nothing here" / "wait until ..." are not steps: free. 25 Nov: "GO TO <zone>" costs sanity (a new step).
        if (key.EndsWith(":none") || key.EndsWith(":nothing") || key.EndsWith(":wait")) return;
        if (PlayerInventory.Local != null) PlayerInventory.Local.RequestMemoStepRead(key);
    }

    // ---- Client: task steps ------------------------------------------------------------------
    // 25 Nov 2026 (Mr.k): ONE short command per step (big blood letters), following what is in
    // front of you: the customer's order, the item in your hands, the next film time.
    // Not in a zone: "GO TO <nearest zone>" (walking distance on the NavMesh). It costs sanity too.

    /// <summary>LOCAL. The next step where you stand (or where to go).</summary>
    private static void LocalTaskStep(out string key, out string text)
    {
        PlayerHealth me = PlayerHealth.LocalInstance;
        RuleZone zone = me != null ? RuleZone.At(me.transform.position) : null;
        if (zone == null || string.IsNullOrEmpty(zone.ZoneID))
        {
            GoToNearest(me, out key, out text);
            return;
        }

        ZoneTask task = FirstOpenTask(zone.ZoneID);
        if (task == null)
        {
            key = "task:" + zone.ZoneID + ":nothing";
            text = T.nothingHere;
            return;
        }

        switch (task.kind)
        {
            case ZoneTaskKind.Ticket:
            case ZoneTaskKind.Popcorn:
            case ZoneTaskKind.Water:
                CounterStep(me, out key, out text);
                return;

            case ZoneTaskKind.FilmChange:
            {
                ZoneChoreSpot film = null;
                foreach (ZoneChoreSpot spot in ZoneChoreSpot.All)
                    if (spot != null && spot.Kind == ZoneTaskKind.FilmChange && spot.CountsForZoneBoard) { film = spot; break; }
                if (film == null || !film.Needed || string.IsNullOrEmpty(film.DueLabel))
                {
                    key = "task:film:wait";
                    text = T.filmWait.Replace("{time}", NextTime(film));
                    return;
                }
                bool holding = CarryProp.LocalHeldLabel(film.NeedsItem) == film.DueLabel;
                key = "task:film:" + film.DueLabel + ":" + (holding ? 1 : 0);
                text = (holding ? T.filmInsert : T.filmTake).Replace("{film}", film.DueLabel);
                return;
            }

            default: // cleaning
            {
                bool stains = false;
                ZoneChoreSpot any = null;
                foreach (ZoneChoreSpot spot in ZoneChoreSpot.All)
                {
                    if (spot == null || spot.Kind != task.kind || !spot.CountsForZoneBoard) continue;
                    if (any == null) any = spot;
                    if (spot.Needed) { stains = true; break; }
                }
                if (!stains)
                {
                    key = "task:clean:wait";
                    text = T.cleanWait.Replace("{time}", NextTime(any));
                    return;
                }
                bool mop = CarryProp.LocalHolds(CarryProp.Mop);
                key = "task:clean:" + (mop ? 1 : 0);
                text = mop ? T.cleanStains : T.cleanGetMop;
                return;
            }
        }
    }

    private static ZoneTask FirstOpenTask(string zoneId)
    {
        foreach (ZoneTaskList board in ZoneTaskList.All)
        {
            if (board == null || (board.CountedZoneID != zoneId && board.ZoneID != zoneId)) continue;
            for (int i = 0; i < board.TaskCount; i++)
                if (!board.IsTaskDone(i)) return board.TaskAt(i);
        }
        return null;
    }

    /// <summary>"02:00" = the next time this chore is due tonight, or "LATER".</summary>
    private static string NextTime(ZoneChoreSpot spot)
    {
        MatchDirector match = MatchDirector.Instance;
        if (spot == null || match == null) return "LATER";
        int now = 0;
        if (match.Phase == MatchPhase.Night) { match.GetInGameTime(out int h, out int m); now = h * 60 + m; }
        foreach (int at in spot.ScheduleMinutes())
            if (at > now) return ZoneChoreSpot.TimeLabel(at);
        return "LATER";
    }

    // ---- Ticket counter / popcorn counter --------------------------------------------------

    private static TicketMinigame tickets;
    private static TicketSellButton[] ticketButtons;
    private static PopcornStation[] popcornStations;
    private static float nextCounterLookup;

    private static void FindCounters()
    {
        if (Time.unscaledTime < nextCounterLookup && tickets != null) return;
        nextCounterLookup = Time.unscaledTime + 3f;
        tickets = Object.FindAnyObjectByType<TicketMinigame>();
        ticketButtons = Object.FindObjectsByType<TicketSellButton>(FindObjectsSortMode.None);
        popcornStations = Object.FindObjectsByType<PopcornStation>(FindObjectsSortMode.None);
    }

    private static float NearestDistance(Vector3 from, Component[] list)
    {
        float best = float.MaxValue;
        if (list == null) return best;
        foreach (Component c in list)
            if (c != null) best = Mathf.Min(best, (c.transform.position - from).sqrMagnitude);
        return best;
    }

    /// <summary>Ticket Zone: the ticket counter or the popcorn counter, whichever you stand closer to.</summary>
    private static void CounterStep(PlayerHealth me, out string key, out string text)
    {
        FindCounters();
        TaskHeldState held = me != null && me.TryGetComponent(out PlayerHeldItems items) ? items.TaskItem : default;
        Vector3 at = me != null ? me.transform.position : Vector3.zero;
        bool food = held.hasItem || NearestDistance(at, popcornStations) < NearestDistance(at, ticketButtons);
        if (food) FoodStep(held, out key, out text);
        else TicketStep(out key, out text);
    }

    private static void TicketStep(out string key, out string text)
    {
        if (tickets == null) { key = "task:ticket"; text = T.ticketSell; return; }
        TicketCustomerState c = tickets.VisibleState;
        if (c.stage != TicketCustomerStage.Waiting)
        {
            key = "task:ticket:wait";
            text = T.waitCustomer;
            return;
        }
        if (!c.hasMovie || c.movieIndex != c.requestedMovieIndex)
        {
            key = "task:ticket:" + c.round + ":movie";
            text = T.ticketPressMovie.Replace("{movie}", tickets.MovieName(c.requestedMovieIndex).ToUpperInvariant());
            return;
        }
        key = "task:ticket:" + c.round + ":sell";
        text = (c.ghost ? T.ticketPressGhost : T.ticketPressHuman).Replace("{ticket}", TicketName(c.ghost));
    }

    /// <summary>25 Nov: the name on the ticket counter in the game ("[E]To Give Normal Ticket" -> "GIVE NORMAL TICKET").</summary>
    private static string TicketName(bool ghost)
    {
        if (ticketButtons != null)
            foreach (TicketSellButton b in ticketButtons)
            {
                if (b == null || b.DisplayName.Contains("Ghost") != ghost) continue;
                InteractionNameLabel label = b.GetComponent<InteractionNameLabel>();
                if (label == null) label = b.GetComponentInParent<InteractionNameLabel>();
                if (label == null) label = b.GetComponentInChildren<InteractionNameLabel>();
                string name = label != null ? CleanName(label.DisplayName) : "";
                if (name.Length > 0) return name;
            }
        return ghost ? "GIVE GHOST TICKET" : "GIVE NORMAL TICKET";
    }

    /// <summary>"[E]To Give Normal Ticket" -> "GIVE NORMAL TICKET".</summary>
    private static string CleanName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string s = System.Text.RegularExpressions.Regex.Replace(raw, @"\[[^\]]*\]", "").Trim();
        if (s.StartsWith("To ", System.StringComparison.OrdinalIgnoreCase)) s = s.Substring(3).Trim();
        return s.ToUpperInvariant();
    }

    // ---- In-game names of the popcorn counter (Popcorn Minigame Systems > Focus Names) ----

    private static PopcornMinigameBootstrap popcornNames;
    private static readonly PopcornFocusSettings DefaultNames = new PopcornFocusSettings();

    private static PopcornFocusSettings Names()
    {
        if (popcornNames == null) popcornNames = Object.FindAnyObjectByType<PopcornMinigameBootstrap>();
        return popcornNames != null && popcornNames.FocusNames != null ? popcornNames.FocusNames : DefaultNames;
    }

    private static string Up(string s) => string.IsNullOrEmpty(s) ? "" : s.ToUpperInvariant();

    private static string FlavorStation(PopcornFlavor f)
    {
        PopcornFocusSettings n = Names();
        switch (f)
        {
            case PopcornFlavor.BBQ: return Up(n.bbq);
            case PopcornFlavor.Paprika: return Up(n.paprika);
            default: return Up(n.cheese);
        }
    }

    private static string Dispenser(PopcornFlavor f)
    {
        PopcornFocusSettings n = Names();
        switch (f)
        {
            case PopcornFlavor.Pepsi: return Up(n.pepsi);
            case PopcornFlavor.Fanta: return Up(n.fanta);
            case PopcornFlavor.OrangeJuice: return Up(n.orangeJuice);
            default: return Up(n.water);
        }
    }

    /// <summary>Fills {bucket} {tank} {ghost} {customer} {cup} {dispenser} with the in-game names.</summary>
    private static string Fill(string text, PopcornFlavor drink = PopcornFlavor.Drink)
    {
        PopcornFocusSettings n = Names();
        return text.Replace("{bucket}", Up(n.bucket)).Replace("{tank}", Up(n.tank)).Replace("{ghost}", Up(n.ghost))
            .Replace("{customer}", Up(n.customer)).Replace("{cup}", Up(n.cup)).Replace("{dispenser}", Dispenser(drink))
            .Replace("{drink}", Dispenser(drink));
    }

    private static void FoodStep(TaskHeldState held, out string key, out string text)
    {
        PopcornNetSync sync = PopcornNetSync.Instance;
        PopcornOrderState order = sync != null ? sync.Order : default;
        if (sync == null || !order.waiting)
        {
            key = "task:food:wait";
            text = T.waitCustomer;
            return;
        }
        bool ghost = order.customerType == PopcornCustomerType.Ghost;
        PopcornFlavor want = NextWanted(order);
        string id = "task:food:" + order.id + ":";

        // Something in your hands.
        if (held.IsInHand)
        {
            PopcornFlavor wantDrink = PopcornRecipe.IsDrink(want) ? want : PopcornFlavor.Drink;
            if (held.isRefill) { key = id + "refill"; text = T.foodWrong; return; }
            if (held.isCup)
            {
                if (!PopcornRecipe.IsDrink(held.flavor))
                {
                    key = id + "cup:place";
                    text = Fill(T.drinkPlaceCup, wantDrink);
                    return;
                }
                if (order.Match(held.flavor, held.ghostMixed) >= 0) { key = id + "cup:give"; text = Fill(T.foodServe); return; }
                if (ghost && !held.ghostMixed && order.Match(held.flavor, true) >= 0) { key = id + "cup:ghost"; text = Fill(T.addGhostFlavor); return; }
                key = id + "cup:wrong";
                text = T.foodWrong;
                return;
            }
            if (!held.hasPopcorn) { key = id + "pop:fill"; text = Fill(T.popcornFill); return; }
            if (!PopcornRecipe.IsOrder(held.flavor))
            {
                PopcornFlavor flavor = !PopcornRecipe.IsDrink(want) && want != PopcornFlavor.None ? want : FirstPopcorn(order);
                key = id + "pop:flavor:" + flavor;
                text = T.popcornAddFlavor.Replace("{flavor}", FlavorStation(flavor));
                return;
            }
            if (order.Match(held.flavor, held.ghostMixed) >= 0) { key = id + "pop:give"; text = Fill(T.foodServe); return; }
            if (ghost && !held.ghostMixed && order.Match(held.flavor, true) >= 0) { key = id + "pop:ghost"; text = Fill(T.addGhostFlavor); return; }
            key = id + "pop:wrong";
            text = T.foodWrong;
            return;
        }

        // A cup waits at a drink spot.
        if (sync.PlacedDrinks.Count > 0)
        {
            bool ready = false;
            foreach (var pair in sync.PlacedDrinks) if (pair.Value.IsReady) { ready = true; break; }
            if (ready) { key = id + "cup:take"; text = Fill(T.drinkTakeBack); return; }
            PopcornFlavor drink = PopcornRecipe.IsDrink(want) ? want : PopcornFlavor.Drink;
            key = id + "cup:press:" + drink;
            text = Fill(T.drinkPress, drink);
            return;
        }

        // Empty hands: start the next item of the order.
        if (PopcornRecipe.IsDrink(want)) { key = id + "cup:take:new"; text = Fill(T.drinkTakeCup, want); return; }
        key = id + "pop:take";
        text = Fill(T.foodTakeBucket);
    }

    /// <summary>The next item of the order that is not delivered yet.</summary>
    private static PopcornFlavor NextWanted(PopcornOrderState order)
    {
        if (order.chooseAny) return PopcornFlavor.Cheese;
        if ((order.delivered & 1) == 0) return order.first;
        if (order.second != PopcornFlavor.None && (order.delivered & 2) == 0) return order.second;
        return order.first;
    }

    private static PopcornFlavor FirstPopcorn(PopcornOrderState order)
    {
        if (!PopcornRecipe.IsDrink(order.first) && order.first != PopcornFlavor.None) return order.first;
        if (!PopcornRecipe.IsDrink(order.second) && order.second != PopcornFlavor.None) return order.second;
        return PopcornFlavor.Cheese;
    }

    // ---- Not in a zone: go to the nearest one ----------------------------------------------

    private static string nearestKey = "", nearestText = "";
    private static float nextNearest;
    private static NavMeshPath nearPath;

    /// <summary>"GO TO SCREEN ROOM": the zone with work left that is the shortest walk away (checked once a second).</summary>
    private static void GoToNearest(PlayerHealth me, out string key, out string text)
    {
        if (me == null) { key = "task:none"; text = T.noZone; return; }
        if (Time.unscaledTime >= nextNearest || nearestKey.Length == 0)
        {
            nextNearest = Time.unscaledTime + 1f;
            FindNearest(me.transform.position);
        }
        key = nearestKey;
        text = nearestText;
    }

    private static void FindNearest(Vector3 from)
    {
        if (nearPath == null) nearPath = new NavMeshPath();
        bool onMesh = NavMesh.SamplePosition(from, out NavMeshHit start, 3f, NavMesh.AllAreas);
        RuleZone best = null;
        float bestLength = float.MaxValue;
        bool anyWork = false;
        foreach (RuleZone zone in RuleZone.All)
        {
            if (zone == null || string.IsNullOrEmpty(zone.ZoneID) || FirstOpenTask(zone.ZoneID) == null) continue;
            anyWork = true;
            Bounds box = zone.Box;
            Vector3 target = new Vector3(box.center.x, box.min.y + 0.1f, box.center.z);
            float length = Vector3.Distance(from, target) * 1.5f; // no path: a straight line, a bit worse
            if (onMesh && NavMesh.SamplePosition(target, out NavMeshHit end, Mathf.Max(3f, box.extents.y + 2f), NavMesh.AllAreas)
                && NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, nearPath)
                && nearPath.status != NavMeshPathStatus.PathInvalid)
            {
                length = 0f;
                Vector3[] corners = nearPath.corners;
                for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
            }
            if (length < bestLength) { bestLength = length; best = zone; }
        }
        if (!anyWork || best == null)
        {
            nearestKey = anyWork ? "task:none" : "go:done:none";
            nearestText = anyWork ? T.noZone : T.allDone;
            return;
        }
        string name = best.ZoneID == ZoneTaskList.TicketZoneID ? T.ticketZoneName : best.ZoneName;
        nearestKey = "go:" + best.ZoneName;
        nearestText = T.goTo.Replace("{zone}", (name ?? "").ToUpperInvariant());
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
