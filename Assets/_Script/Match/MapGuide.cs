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
        [Tooltip("Zone rules. 23 Nov: all of them are on in every match.")]
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

    [Tooltip("23 Nov: the Map Rules (whole map, every player, from 3 AM). Shown on the 'Your goal' page.")]
    public List<RuleText> mapRules = CinemaMapRules();

    /// <summary>23 Nov: the Cinema's three Map Rules (Zone Quest Detail, 'Map Rules').</summary>
    public static List<RuleText> CinemaMapRules() => new List<RuleText>
    {
        new RuleText { title = "3 AM: VIP guest", whatToDo = "Lead it to its seat. Gold film in. Crank fast.", ifYouBreakIt = "Everyone loses half sanity." },
        new RuleText { title = "Lights out", whatToDo = "Stay out of the flashlight. Seen? Run.", ifYouBreakIt = "Caught = dead." },
        new RuleText { title = "Hidden things", whatToDo = "Find them by the walls. Hit them (bat or mop).", ifYouBreakIt = "They get in and block work." },
    };

    // 25 Nov: assets older than this get the short Cinema text once (then designers can edit again).
    private const int GuideVersion = 2;
    [SerializeField, HideInInspector] private int guideVersion;

    private static readonly Dictionary<string, MapGuide> cache = new Dictionary<string, MapGuide>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => cache.Clear();

    /// <summary>The designer asset for this map, or null (no fallback).</summary>
    public static MapGuide LoadAsset(string map)
    {
        if (string.IsNullOrEmpty(map)) map = RoomConfig.DemoMapID;
        if (cache.TryGetValue(map, out MapGuide g) && g != null) return g;
        g = Resources.Load<MapGuide>("MapGuides/" + map);
        if (g != null)
        {
            cache[map] = g;
            // 25 Nov: the Cinema guide with the old long text gets the short text once (pictures kept).
            if (g.guideVersion < GuideVersion && g.mapID == RoomConfig.DemoMapID)
            {
                g.ResetCinemaText();
                g.guideVersion = GuideVersion;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(g); // saved with the project: commit demo_map_01.asset
#endif
                Debug.Log("[MapGuide] Short Memo Book text applied to " + map + ".");
            }
        }
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
                d.guideVersion = GuideVersion;
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
            // 23 Nov (Mr.k): no difficulty levels any more: one setup for every match.
            default: return "Every zone rule is on, every night. From 3 AM the Map Rules start too (see Map Rules).";
        }
    }

    // ---- Built-in Cinema text (also the starting text of the asset) -----------------------

    public void FillCinemaDefaults()
    {
        mapRules = CinemaMapRules(); // 23 Nov
        mapID = RoomConfig.DemoMapID;
        mapName = "Cinema";
        zonesForOnePlayer = 2;
        extraZonesPerPlayer = 1;
        maxZonesToFinish = 6;

        // 25 Nov (Mr.k): short and plain. Every page must fit the paper without scrolling.
        howToWin =
            "Night: 00:00 to 06:00. Exits are locked.\n" +
            "Finish your zones. Each zone has a task board.\n" +
            "06:00: exits open. 2 people per exit.\n" +
            "Friend down? Hold E on them.\n" +
            "Money = your tasks. Die = lose your items.";

        zones = new List<ZoneText>
        {
            new ZoneText
            {
                name = "Ticket Zone",
                where = "Front of the cinema.",
                tasks = "Sell the right ticket.\nMake food and drinks to order.",
                allRulesOn = true,
                rules = new List<RuleText>
                {
                    new RuleText { title = "Tickets", whatToDo = "Human ticket for people, ghost ticket for ghosts.", ifYouBreakIt = "Lose sanity." },
                    new RuleText { title = "Ghost food", whatToDo = "Ghosts need Ghost Flavor in their food.", ifYouBreakIt = "Lose HP and sanity." },
                    new RuleText { title = "Missing flavor", whatToDo = "Lights cut out, Ghost Flavor vanishes. Follow the trail, bring it back.", ifYouBreakIt = "No ghost food." },
                    new RuleText { title = "The one who returns", whatToDo = "Signs on their spots. Lights off. Be quiet.", ifYouBreakIt = "It attacks." },
                    new RuleText { title = "Missing parts", whatToDo = "Find the ghost's lost part within 1 hour.", ifYouBreakIt = "Lose HP and sanity." },
                },
            },
            new ZoneText
            {
                name = "Screen Rooms",
                where = "Theatre wing: normal room and GHOST room.",
                tasks = "Ghost room: change film at 02:00 and 04:00.\nGhost room: mop 3 stains after 01:00.",
                rules = new List<RuleText>
                {
                    new RuleText { title = "No noise", whatToDo = "Walk slowly. Don't shout.", ifYouBreakIt = "Lose sanity." },
                    new RuleText { title = "Audience", whatToDo = "Don't look at the ghosts in the seats.", ifYouBreakIt = "Lose HP." },
                    new RuleText { title = "They stand up", whatToDo = "Freeze. When they sit, leave.", ifYouBreakIt = "They chase you." },
                    new RuleText { title = "Static screen", whatToDo = "Sit on the black shadow seat. Stay.", ifYouBreakIt = "A ghost roams till 06:00." },
                },
            },
            new ZoneText
            {
                name = "Toilets",
                where = "Men's and women's toilets.",
                tasks = "After 03:00: mop 3 stains in each.",
                rules = new List<RuleText>
                {
                    new RuleText { title = "Cracked mirror", whatToDo = "Look at the crack.", ifYouBreakIt = "It shatters on you." },
                    new RuleText { title = "Crying stall", whatToDo = "Light off. Stand still.", ifYouBreakIt = "It chases you." },
                    new RuleText { title = "Smoke", whatToDo = "Knock on the stall. Light off.", ifYouBreakIt = "Lose HP. Tasks locked." },
                    new RuleText { title = "Feed it", whatToDo = "When clean: light off, box in, get out.", ifYouBreakIt = "Ghosts swarm." },
                },
            },
        };

        generalTips =
            "Tell your team which rules you saw.\n" +
            "Keep your flashlight charged.\n" +
            "Low sanity? Hold C or eat a snack.\n" +
            "Carry a walkie. It works when you're down.";
    }
}
