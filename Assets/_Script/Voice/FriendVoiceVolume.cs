using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 20 Oct 2026 (Mr.k): how loud each FRIEND's voice is on THIS PC (Tab player list).
///
/// Local only: it never goes over the network and never changes what others hear.
/// Saved in PlayerPrefs (like the other settings, NOT the encrypted save), keyed by
/// the friend's EOS id, so the choice is still there next time you play together.
///
/// 0 = muted, 1 = normal, up to <see cref="Max"/> (200%) for a quiet mic.
/// </summary>
public static class FriendVoiceVolume
{
    public const float Max = 2f;
    private const string Prefix = "settings.voiceVolume.";

    // A List, not a Dictionary (project style); at most a handful of friends.
    private struct Entry { public string key; public float value; }
    private static readonly List<Entry> cache = new List<Entry>(8);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => cache.Clear();

    /// <summary>Stable key of a seat: its EOS id, or its name until the id arrives.</summary>
    public static string KeyFor(RoHRoomPlayer seat)
    {
        if (seat == null) return string.Empty;
        return string.IsNullOrEmpty(seat.ProductUserId) ? "name:" + seat.DisplayName : seat.ProductUserId;
    }

    public static float Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return 1f;
        for (int i = 0; i < cache.Count; i++)
            if (cache[i].key == key) return cache[i].value;
        float value = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + key, 1f), 0f, Max);
        cache.Add(new Entry { key = key, value = value });
        return value;
    }

    /// <summary>Change one friend's volume (call GameSettings.Save() later to flush to disk).</summary>
    public static void Set(string key, float value)
    {
        if (string.IsNullOrEmpty(key)) return;
        value = Mathf.Clamp(value, 0f, Max);
        PlayerPrefs.SetFloat(Prefix + key, value);
        for (int i = 0; i < cache.Count; i++)
        {
            if (cache[i].key != key) continue;
            cache[i] = new Entry { key = key, value = value };
            return;
        }
        cache.Add(new Entry { key = key, value = value });
    }

    /// <summary>Volume for a game-network voice stream "net:&lt;talkerNetId&gt;".</summary>
    public static float ForTalker(uint talkerNetId)
    {
        RoHRoomPlayer seat = SeatOfTalker(talkerNetId);
        return seat != null ? Get(KeyFor(seat)) : 1f;
    }

    /// <summary>Volume for an EOS backup voice stream (participant id = EOS id).</summary>
    public static float ForEosId(string productUserId)
    {
        if (string.IsNullOrEmpty(productUserId)) return 1f;
        List<RoHRoomPlayer> seats = RoHRoomPlayer.All;
        for (int i = 0; i < seats.Count; i++)
            if (seats[i] != null && seats[i].ProductUserId == productUserId) return Get(productUserId);
        return 1f;
    }

    private static RoHRoomPlayer SeatOfTalker(uint talkerNetId)
    {
        if (talkerNetId == 0) return null;
        List<RoHRoomPlayer> seats = RoHRoomPlayer.All;
        for (int i = 0; i < seats.Count; i++)
        {
            RoHRoomPlayer seat = seats[i];
            if (seat != null && (seat.TalkerNetId == talkerNetId || seat.netId == talkerNetId)) return seat;
        }
        return null;
    }
}
