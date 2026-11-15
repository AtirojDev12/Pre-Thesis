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
        level == DifficultyLevel.Hard || level == DifficultyLevel.ThirteenRules;

    /// <summary>Label ending for a locked choice in a list.</summary>
    public const string LockedSuffix = "  (Locked)";
}
