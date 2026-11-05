using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 5 Nov 2026 (Mr.k): a TUTORIAL ROOM for one map, inside the Lobby scene.
///
/// The lobby's tutorial door sends each player to the room whose <see cref="MapID"/>
/// matches the map chosen when the room was created (RoomConfig.mapID, e.g.
/// "demo_map_01" = Cinema). A NEW MAP only needs a new TutorialRoom with its map ID:
///   Tools > Pre-Thesis > Scenes > Lobby: Add Tutorial Room For A Map...
/// No code change: the door finds rooms by map ID at run time.
///
/// Local only (no network): each player walks in and out on their own.
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialRoom : MonoBehaviour
{
    [Tooltip("RoomConfig map ID this room teaches, e.g. \"demo_map_01\" (Cinema).")]
    [SerializeField] private string mapID = RoomConfig.DemoMapID;
    [Tooltip("Where players appear when they enter (blue arrow = facing).")]
    [SerializeField] private Transform arrivalPoint;

    private static readonly List<TutorialRoom> all = new List<TutorialRoom>();

    public string MapID => mapID;
    public Transform ArrivalPoint => arrivalPoint != null ? arrivalPoint : transform;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => all.Clear();

    private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    private void OnDisable() => all.Remove(this);

    /// <summary>The tutorial room for this map, or null if none was made yet.</summary>
    public static TutorialRoom For(string map)
    {
        if (string.IsNullOrEmpty(map)) map = RoomConfig.DemoMapID;
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && all[i].mapID == map) return all[i];
        return null;
    }

    /// <summary>Map of the room this player is in (host or joiner). No room (offline test) = the demo map.</summary>
    public static string CurrentMapID()
    {
        LobbyController lobby = LobbyController.Instance;
        string map = lobby != null && lobby.CurrentRoom != null ? lobby.CurrentRoom.mapID : null;
        return string.IsNullOrEmpty(map) ? RoomConfig.DemoMapID : map;
    }

    /// <summary>Editor tool.</summary>
    public void Set(string map, Transform arrival)
    {
        mapID = map;
        arrivalPoint = arrival;
    }

    private void OnDrawGizmos()
    {
        Transform a = ArrivalPoint;
        Gizmos.color = new Color(0.3f, 0.7f, 1f);
        Gizmos.DrawWireSphere(a.position + Vector3.up * 0.1f, 0.4f);
        Gizmos.DrawLine(a.position + Vector3.up * 0.1f, a.position + Vector3.up * 0.1f + a.forward);
    }
}
