using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 5 Nov 2026 (Mr.k): everything a player must know about ONE map, for the lobby
/// Staff NPC's WORK SHEET (WorksheetReader) — and the map's zone goal numbers.
///
/// One asset per map: Assets/Resources/MapGuides/&lt;mapID&gt;.asset
/// (made by Tools > Pre-Thesis > Designer Settings > Map Guide (Cinema)). Designers edit
/// the text there. A NEW MAP = a new asset with its map ID (same as RoomConfig.mapID).
/// No asset yet = the built-in Cinema text below (so the sheet always works).
///
/// ZONE GOAL: "zones to finish = For One Player + Extra Per Player x (players - 1),
/// at most Max, never more than the zones in the map". MatchDirector uses these numbers
/// when the asset exists, so the sheet and the real round always agree.
/// </summary>
[CreateAssetMenu(menuName = "Pre-Thesis/Map Guide", fileName = "demo_map_01")]
public sealed class MapGuide : ScriptableObject
{
    [System.Serializable]
    public sealed class RuleText
    {
        public string title = "Rule";
        [TextArea(1, 4)] public string whatToDo = "";
        [TextArea(1, 3)] public string ifYouBreakIt = "";
    }

    [System.Serializable]
    public sealed class ZoneText
    {
        public string name = "Zone";
        [Tooltip("5 Nov: a picture of this zone for the work sheet (a screenshot is best). Empty = a drawn card.")]
        public Sprite picture;
        [TextArea(1, 3)] public string where = "";
        [TextArea(2, 6)] public string tasks = "";
        [Tooltip("Zone rules. On Easy / Normal only some are active each night (random); Hard / 13 Rules = all.")]
        public List<RuleText> rules = new List<RuleText>();
        [Tooltip("5 Nov: FOR NOW every rule of this zone is on, on every difficulty (Ticket Zone, until its rules are rewritten).")]
        public bool allRulesOn;

        /// <summary>How many of this zone's rules are on tonight.</summary>
        public int ActiveRules(DifficultyLevel difficulty)
        {
            int count = rules != null ? rules.Count : 0;
            return allRulesOn ? count : ZoneRuleBase.RulesForDifficulty(difficulty, count);
        }
        [TextArea(1, 4)] public string otherDangers = "";
    }

    [Header("Map")]
    public string mapID = RoomConfig.DemoMapID;
    public string mapName = "Cinema";

    [Header("Zone goal (also used by MatchDirector)")]
    [Min(1)] public int zonesForOnePlayer = 2;
    [Min(0)] public int extraZonesPerPlayer = 1;
    [Min(1)] public int maxZonesToFinish = 6;

    [Header("Work sheet text")]
    [Tooltip("5 Nov: a picture for the 'Your goal' page (a top-down map screenshot is best). Empty = a drawn card.")]
    public Sprite mapPicture;
    [Tooltip("One step per line.")]
    [TextArea(3, 10)] public string howToWin = "";
    public List<ZoneText> zones = new List<ZoneText>();
    [Tooltip("One tip per line.")]
    [TextArea(2, 8)] public string generalTips = "";

    private static readonly Dictionary<string, MapGuide> cache = new Dictionary<string, MapGuide>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => cache.Clear();

    /// <summary>The designer asset for this map, or null (no fallback).</summary>
    public static MapGuide LoadAsset(string map)
    {
        if (string.IsNullOrEmpty(map)) map = RoomConfig.DemoMapID;
        if (cache.TryGetValue(map, out MapGuide g) && g != null) return g;
        g = Resources.Load<MapGuide>("MapGuides/" + map);
        if (g != null) cache[map] = g;
        return g;
    }

    /// <summary>The asset, or the built-in Cinema guide for the demo map, or null.</summary>
    public static MapGuide For(string map)
    {
        MapGuide asset = LoadAsset(map);
        if (asset != null) return asset;
        if (string.IsNullOrEmpty(map) || map == RoomConfig.DemoMapID)
        {
            if (!cache.TryGetValue("__default", out MapGuide d) || d == null)
            {
                d = CreateInstance<MapGuide>();
                d.FillCinemaDefaults();
                d.hideFlags = HideFlags.DontSave;
                cache["__default"] = d;
            }
            return d;
        }
        return null;
    }

    /// <summary>Zones to finish for this many players (same formula as MatchDirector).</summary>
    public int ZonesToFinish(int players)
    {
        int wanted = zonesForOnePlayer + extraZonesPerPlayer * Mathf.Max(0, players - 1);
        int cap = Mathf.Clamp(wanted, 1, maxZonesToFinish);
        return zones.Count > 0 ? Mathf.Min(zones.Count, cap) : cap;
    }

    // ---- Helpers for the work sheet book (WorksheetReader) -------------------------------

    /// <summary>"2 of 4 active tonight (random)" / "all 4 active tonight".</summary>
    public static string ActiveRulesText(ZoneText zone, DifficultyLevel difficulty)
    {
        int count = zone.rules != null ? zone.rules.Count : 0;
        int active = zone.ActiveRules(difficulty);
        return active >= count ? "all " + count + " active tonight" : active + " of " + count + " active tonight (random)";
    }

    /// <summary>
    /// 5 Nov: puts the built-in Cinema text back into this asset (editor menu). Pictures are
    /// kept: the map picture, and each zone's picture by zone name.
    /// </summary>
    public void ResetCinemaText()
    {
        Sprite map = mapPicture;
        var pictures = new Dictionary<string, Sprite>();
        foreach (ZoneText z in zones)
            if (z != null && z.picture != null && !pictures.ContainsKey(z.name)) pictures[z.name] = z.picture;
        FillCinemaDefaults();
        mapPicture = map;
        foreach (ZoneText z in zones)
            if (pictures.TryGetValue(z.name, out Sprite s)) z.picture = s;
    }

    /// <summary>One short line: what the difficulty does to the rules.</summary>
    public static string RulesTonight(DifficultyLevel difficulty) => RulesLine(difficulty);

    /// <summary>Splits designer text into lines (drops empty lines and a leading "•" / "-").</summary>
    public static List<string> Lines(string text)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return list;
        foreach (string raw in text.Split('\n'))
        {
            string s = raw.Trim();
            if (s.StartsWith("•") || s.StartsWith("-")) s = s.Substring(1).Trim();
            if (s.Length > 0) list.Add(s);
        }
        return list;
    }

    private static string RulesLine(DifficultyLevel difficulty)
    {
        switch (difficulty)
        {
            case DifficultyLevel.Easy: return "Easy: each zone uses only a few of its rules tonight, picked at random. Learn them all. You will not know which ones are on.";
            case DifficultyLevel.Normal: return "Normal: each zone uses some of its rules tonight, picked at random. Learn them all. You will not know which ones are on.";
            default: return "Hard / 13 Rules: every zone rule is on tonight.";
        }
    }

    // ---- Built-in Cinema text (also the starting text of the asset) -----------------------

    public void FillCinemaDefaults()
    {
        mapID = RoomConfig.DemoMapID;
        mapName = "Cinema";
        zonesForOnePlayer = 2;
        extraZonesPerPlayer = 1;
        maxZonesToFinish = 6;

        howToWin =
            "• The night runs from 00:00 to 06:00. The exits stay locked during the night.\n" +
            "• Finish the zones you need (goal above). Each zone's TASK BOARD shows its lines; take a map from the map box to find your way.\n" +
            "• Zones done by 06:00 = the EXITS OPEN for one in-game hour. Walk out through an exit. Each exit lets only 2 people out, so split up.\n" +
            "• Not done by 06:00: if 2 or more players are dead, the night goes on (overtime) and the ghosts get angrier until you finish. " +
            "If fewer than 2 are dead, you are LOCKED IN for one hour.\n" +
            "• Downed players can be revived by a teammate (hold E). A downed player with a switched-on walkie can still call for help on it.\n" +
            "• Money = the tasks YOU finished. Dying loses the items you brought in.";

        zones = new List<ZoneText>
        {
            // 5 Nov (Mr.k): Tickets + Popcorn & Water are ONE zone (Zone Quest Detail: "Ticket Zone").
            // Its 5 rules are all on for every difficulty until the team rewrites them.
            new ZoneText
            {
                name = "Ticket Zone",
                where = "The front of the cinema: the ticket counter and the popcorn & drinks counter next to it.",
                tasks = "Sell tickets: HUMAN ticket for people, GHOST ticket for ghosts. Look at the customer before you sell.\n" +
                        "Make each customer's order (popcorn flavour + drink). Ghost customers need the SPECIAL INGREDIENT in their food.\n" +
                        "Keep selling until every line on the Ticket Zone board is done.",
                allRulesOn = true,
                rules = new List<RuleText>
                {
                    new RuleText { title = "Sell tickets", whatToDo = "Give the right ticket: a ghost ticket to a ghost, a human ticket to a person.", ifYouBreakIt = "sanity loss and no money for that sale; too many wrong tickets call a punishing ghost" },
                    new RuleText { title = "Sell food", whatToDo = "All food looks the same, but a ghost's food must have the special ingredient in it.", ifYouBreakIt = "HP and sanity loss" },
                    new RuleText { title = "The missing bucket", whatToDo = "Every 2 minutes the lights flicker; 10 seconds later the Ticket Zone lights cut out and the special ingredient bucket vanishes (if the returning ghost is around tonight, it vanishes when that ghost walks in instead). Switching the lights off also makes it vanish. Follow its trail, carry it back to its place, then switch the lights back on.", ifYouBreakIt = "no special ingredient: you cannot make ghost food, so ghost customers cannot be served" },
                    new RuleText { title = "The one who returns", whatToDo = "When the lights flicker, two RESTROOM BREAK signs appear. You have 10 seconds: pick each one up (E) and put it on its spot at the popcorn and the ticket counter (R), then switch the lights OFF and stay quiet. The ghost checks the popcorn sign, then the ticket sign, then walks around the Ticket Zone. In the light it sees anyone near it; in the dark it only hears you.", ifYouBreakIt = "a sign not on its spot: the ghost attacks the player at that counter. Seen or heard: it chases you; caught = jumpscare and damage" },
                    new RuleText { title = "The one looking for its parts", whatToDo = "Once a night (00:00-03:00) a ghost missing an arm, its head or its body blocks the ticket or popcorn counter. Press E on it and it follows you. Find its parts in the building, hold a part and hold E on the ghost for 3 s. You have 1 in-game hour (countdown above its head).", ifYouBreakIt = "-25 HP and -25% sanity for everyone near it" },
                },
            },
            new ZoneText
            {
                name = "Screen Rooms",
                where = "The two screen rooms in the theatre wing: the normal (human) room and the GHOST room.",
                tasks = "Change the film in the GHOST room at 02:00 and 04:00 (hold E 5 s at the projector). Clean the 3 dirt marks in the ghost room after 01:00 (hold E 3 s).\n" +
                        "Bonus (money only, not needed for the zone): clean the normal room and change its film at 00:30 and 02:30.",
                rules = new List<RuleText>
                {
                    new RuleText { title = "No noise", whatToDo = "Inside the ghost room, keep quiet: walk slowly, do not sprint, do not shout into your mic.", ifYouBreakIt = "big sanity loss" },
                    new RuleText { title = "Don't look at the audience", whatToDo = "Never look at the ghost customers sitting in the seats.", ifYouBreakIt = "you lose HP every second you look" },
                    new RuleText { title = "The audience stands", whatToDo = "If every customer stands up and stares at you: stand still. When they sit down again, leave the room at once.", ifYouBreakIt = "they chase you inside the room" },
                    new RuleText { title = "Staff time (static screen)", whatToDo = "If the screen turns to TV static and the ghosts vanish: stand on the black shadow in the seats within 13 s and stay until the static stops (3–6 s). One player is enough.", ifYouBreakIt = "a ghost comes out of the screen and roams the whole building until 06:00" },
                },
            },
            new ZoneText
            {
                name = "Toilets",
                where = "The men's and women's toilets.",
                tasks = "After 03:00 clean both toilets: 3 dirt marks in each (hold E 3 s).",
                rules = new List<RuleText>
                {
                    new RuleText { title = "Cracked mirror", whatToDo = "If a mirror has a crack, look at the crack for a few seconds.", ifYouBreakIt = "it shatters: jumpscare, HP and sanity loss for everyone inside" },
                    new RuleText { title = "The last stall (women's)", whatToDo = "If the last stall is closed and you hear crying: switch the light OFF at once and stand still.", ifYouBreakIt = "a ghost comes out and chases you" },
                    new RuleText { title = "Cigarette smoke (men's)", whatToDo = "If smoke comes out of a stall: knock on that stall (E), then switch the light OFF.", ifYouBreakIt = "HP loss for a while and the toilet's tasks are locked" },
                    new RuleText { title = "Ghost food", whatToDo = "When the toilet is fully cleaned: light OFF, throw the black box into the last stall, then everyone out fast.", ifYouBreakIt = "ghosts swarm the toilet" },
                },
            },
        };

        generalTips =
            "• Talk to your team: share which rules you saw tonight.\n" +
            "• Keep your flashlight charged; dark rooms hide ghosts.\n" +
            "• Low sanity? Hold C to chant, or eat a snack.\n" +
            "• Carry a walkie: it reaches every teammate with a switched-on walkie, and it still works when you are downed.";
    }
}
