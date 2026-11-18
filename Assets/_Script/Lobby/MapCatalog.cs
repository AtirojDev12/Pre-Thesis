using System.Collections.Generic;

/// <summary>
/// 15 Nov 2026 (Mr.k): every map the game knows, and which ones can be chosen.
/// The Create Room screen lists them all; a locked map is shown greyed ("Locked")
/// and the host's own check (RoomConfig.Validate) refuses it too.
///
/// To add a map later: add one line here (same string ID as RoomConfig.mapID,
/// MapGuide, TutorialRoom). To open a map: set locked = false.
/// A List, not a Dictionary (project rule).
/// </summary>
public static class MapCatalog
{
    public sealed class MapInfo
    {
        public string id;
        public string displayName;
        /// <summary>Shown in the list but cannot be chosen yet.</summary>
        public bool locked;
    }

    public const string ForestMapID = "map_forest";

    private static readonly List<MapInfo> maps = new List<MapInfo>
    {
        new MapInfo { id = RoomConfig.DemoMapID, displayName = "Cinema", locked = false },
        new MapInfo { id = ForestMapID, displayName = "The Forest", locked = true },
    };

    /// <summary>All maps, in the order the Create Room list shows them.</summary>
    public static IReadOnlyList<MapInfo> All => maps;

    public static MapInfo Find(string mapID)
    {
        for (int i = 0; i < maps.Count; i++)
            if (maps[i].id == mapID) return maps[i];
        return null;
    }

    /// <summary>Unknown maps count as locked.</summary>
    public static bool IsLocked(string mapID)
    {
        MapInfo info = Find(mapID);
        return info == null || info.locked;
    }
}

/// <summary>
/// 15 Nov 2026 (Mr.k): difficulties that cannot be chosen for now (Hard, 13 Rules).
/// The one place to unlock them later.
/// </summary>
public static class RoomLocks
{
    public static bool IsLocked(DifficultyLevel level) =>
        !DevUnlocked && (level == DifficultyLevel.Hard || level == DifficultyLevel.ThirteenRules);

    /// <summary>
    /// 16 Nov 2026 (Mr.k): DEV ONLY. Lets this PC host Hard / 13 Rules for testing.
    /// Unity Editor and Development Builds only: a normal build always returns false.
    /// Kept on this PC (PlayerPrefs). Switch it in the F1 cheat panel, "Match" section.
    /// Maps stay locked.
    /// </summary>
    public static bool DevUnlocked
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        get => UnityEngine.PlayerPrefs.GetInt(DevUnlockKey, 0) == 1;
        set { UnityEngine.PlayerPrefs.SetInt(DevUnlockKey, value ? 1 : 0); UnityEngine.PlayerPrefs.Save(); }
#else
        get => false;
        set { }
#endif
    }

    private const string DevUnlockKey = "dev_unlock_difficulty";

    /// <summary>Label ending for a locked choice in a list.</summary>
    public const string LockedSuffix = "  (Locked)";
}
