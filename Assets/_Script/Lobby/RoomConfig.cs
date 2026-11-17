using System;

/// <summary>
/// Plain data contract describing a single room, as configured by the host on the
/// Room Creation / Matchmaking screen. This class carries no logic beyond basic
/// validation and no networking of its own — something authoritative (the host's
/// LobbyController, or a server-owned NetworkBehaviour once the match starts) is
/// responsible for validating it and replicating the *validated* result to clients.
/// Never trust a RoomConfig that arrived from a client without re-validating it
/// server-side first.
///
/// Pre-Thesis demo scope: only one map exists, so MapID is fixed to DemoMapID.
/// It stays a string key (not an index or a hardcoded enum) on purpose — the full
/// thesis build can drop in a real map registry/ScriptableObject list later
/// without changing any call site that reads RoomConfig.mapID.
/// </summary>
[Serializable]
public class RoomConfig
{
    public const string DemoMapID = "demo_map_01";

    public const int MinPlayers = 1;
    public const int MaxPlayers = 6;
    public const int MaxRoomNameLength = 24;

    public string mapID = DemoMapID;
    /// <summary>Name the host gave the room; shown and searchable in the Room Browser.</summary>
    public string roomName = string.Empty;
    public DifficultyLevel difficulty = DifficultyLevel.Normal;
    public int playerLimit = MaxPlayers;
    public bool isPrivate = false;
    public string password = string.Empty;
    /// <summary>17 Nov (Task 8): minutes before 00:00 the team enters the map (0 = 00:00, 60 = 23:00). Host picks it on the Lobby Board.</summary>
    public int entryMinutesBefore = 0;

    /// <summary>17 Nov: the entry times the host can pick (minutes before 00:00).</summary>
    public static readonly int[] EntryChoices = { 60, 45, 30, 15, 0 };

    /// <summary>Clamps to one of <see cref="EntryChoices"/>.</summary>
    public static int CleanEntry(int minutes)
    {
        int best = 0, bestGap = int.MaxValue;
        foreach (int choice in EntryChoices)
        {
            int gap = Math.Abs(choice - minutes);
            if (gap < bestGap) { bestGap = gap; best = choice; }
        }
        return best;
    }

    /// <summary>"11:30 PM" for an entry time.</summary>
    public static string EntryLabel(int minutesBefore)
    {
        int total = (24 * 60 - CleanEntry(minutesBefore)) % (24 * 60);
        return MatchHUD.FormatClock(total / 60, total % 60);
    }

    /// <summary>
    /// Clamps values into range and enforces "private rooms need a password".
    /// 15 Nov: also refuses a locked difficulty or a locked / unknown map (RoomLocks, MapCatalog).
    /// Call this on the HOST/server side immediately before CreateLobby — treat
    /// any RoomConfig built from UI input as untrusted until it passes this.
    /// </summary>
    public bool Validate(out string error)
    {
        playerLimit = Math.Clamp(playerLimit, MinPlayers, MaxPlayers);
        roomName = SanitizeRoomName(roomName);
        entryMinutesBefore = CleanEntry(entryMinutesBefore);

        // 15 Nov (Mr.k): Hard and 13 Rules are locked for now; only the Cinema map is open.
        if (RoomLocks.IsLocked(difficulty))
        {
            error = RoomDisplay.Difficulty(difficulty) + " is locked for now.";
            return false;
        }
        if (MapCatalog.IsLocked(mapID))
        {
            error = RoomDisplay.MapName(mapID) + " is locked for now.";
            return false;
        }

        if (isPrivate && string.IsNullOrWhiteSpace(password))
        {
            error = "Private rooms require a password.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Trimmed, max MaxRoomNameLength characters. Empty stays empty (the UI fills a default).</summary>
    public static string SanitizeRoomName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string trimmed = raw.Trim();
        return trimmed.Length > MaxRoomNameLength ? trimmed.Substring(0, MaxRoomNameLength) : trimmed;
    }
}
