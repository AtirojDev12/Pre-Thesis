/// <summary>
/// Turns room data into the words the player sees. One place, so the browser,
/// the create panel and the waiting lobby never disagree on a label.
/// </summary>
public static class RoomDisplay
{
    public static string MapName(string mapID)
    {
        switch (mapID)
        {
            case RoomConfig.DemoMapID: return "Cinema";
            default: return string.IsNullOrEmpty(mapID) ? "Unknown map" : mapID;
        }
    }

    public static string Difficulty(DifficultyLevel level)
    {
        switch (level)
        {
            case DifficultyLevel.Easy: return "Easy";
            case DifficultyLevel.Normal: return "Normal";
            case DifficultyLevel.Hard: return "Hard";
            case DifficultyLevel.ThirteenRules: return "13 Rules";
            default: return level.ToString();
        }
    }

    /// <summary>Dropdown order. Index in this array == (int)DifficultyLevel.</summary>
    public static readonly string[] DifficultyOptions = { "Easy", "Normal", "Hard", "13 Rules" };
}
