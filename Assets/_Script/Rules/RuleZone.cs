using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 16 Oct 2026 (Mr.k): the area a ZONE RULE watches (one screen room, one toilet, the Ticket Zone).
/// The yellow wire box in the Scene view.
///
/// 15 Nov 2026 (Mr.k): the box now BELONGS TO THIS OBJECT.
///   - Center / Size are in this object's own space: move, rotate or scale the object and the
///     box follows. Everything parented under it (rules, rule ghosts, chores, task boards)
///     moves with it. Fine-tune their local positions after.
///   - Select the object: drag the dots on the box faces in the Scene view to resize it.
///   - More Boxes: extra boxes for rooms that are not one simple box (also in this object's space).
///   - Zone ID: the quest zone this room belongs to (zone_screen, zone_toilet, zone_ticket).
///     Several rooms can share one ID (both toilets = zone_toilet).
///   - A player is inside ONLY when the feet are inside a box (0.6 m of slack below / above
///     for the floor line, none sideways).
///
/// Zone Rules work ONLY inside their own zone. Map Rules (Hard / 13 Rules) are a different
/// system and do not use this.
///
/// No collider on purpose: the server checks player positions itself (the same way ExitPoint
/// does), so nothing here can block walking.
/// </summary>
[DisallowMultipleComponent]
public sealed class RuleZone : MonoBehaviour
{
    [Tooltip("Name shown in logs and rule messages, e.g. \"Ghost Room\" or \"Women's Toilet\".")]
    [SerializeField] private string zoneName = "Zone";
    [Tooltip("Quest zone this room belongs to: zone_screen, zone_toilet, zone_ticket. Rooms of one zone share the ID.")]
    [SerializeField] private string zoneID = "";
    [Tooltip("Box centre, in THIS object's space (0,0,0 = on the object).")]
    [SerializeField] private Vector3 center;
    [Tooltip("Box size in metres, in THIS object's space.")]
    [SerializeField] private Vector3 size = new Vector3(8f, 4f, 8f);
    [Tooltip("Optional extra boxes (same space) for rooms that are not one simple box.")]
    [SerializeField] private List<Bounds> moreBoxes = new List<Bounds>();

    /// <summary>Feet may sit a little under / over the floor line (metres).</summary>
    private const float FeetSlack = 0.6f;

    private static readonly List<RuleZone> all = new List<RuleZone>();
    /// <summary>Every enabled zone box in the scene.</summary>
    public static IReadOnlyList<RuleZone> All => all;

    public string ZoneName => zoneName;
    public string ZoneID => zoneID;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => all.Clear();

    private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    private void OnDisable() => all.Remove(this);

    /// <summary>The main box as a world-axis box (the smallest one that holds it, if the object is rotated).</summary>
    public Bounds Box => WorldBoundsOf(center, size);

    /// <summary>A world-axis box around the main box and every extra box.</summary>
    public Bounds WorldBounds
    {
        get
        {
            Bounds b = Box;
            for (int i = 0; i < moreBoxes.Count; i++) b.Encapsulate(WorldBoundsOf(moreBoxes[i].center, moreBoxes[i].size));
            return b;
        }
    }

    private Bounds WorldBoundsOf(Vector3 localCenter, Vector3 localSize)
    {
        Vector3 half = localSize * 0.5f;
        var b = new Bounds(transform.TransformPoint(localCenter), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
            b.Encapsulate(transform.TransformPoint(localCenter + corner));
        }
        return b;
    }

    /// <summary>True if a point (a player's feet) is inside the main box or an extra box.</summary>
    public bool Contains(Vector3 point)
    {
        Vector3 local = transform.InverseTransformPoint(point);
        float slack = FeetSlack / Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));
        if (Inside(local, center, size, slack)) return true;
        for (int i = 0; i < moreBoxes.Count; i++)
            if (Inside(local, moreBoxes[i].center, moreBoxes[i].size, slack)) return true;
        return false;
    }

    private static bool Inside(Vector3 local, Vector3 boxCenter, Vector3 boxSize, float slackY)
    {
        Vector3 d = local - boxCenter;
        return Mathf.Abs(d.x) <= boxSize.x * 0.5f && Mathf.Abs(d.z) <= boxSize.z * 0.5f && Mathf.Abs(d.y) <= boxSize.y * 0.5f + slackY;
    }

    public bool Contains(PlayerHealth player) => player != null && Contains(player.transform.position);

    /// <summary>
    /// SERVER (or offline). Living players in this round who stand inside.
    /// Fills <paramref name="into"/> (cleared first) so callers can reuse one list.
    /// </summary>
    public List<PlayerHealth> PlayersInside(List<PlayerHealth> into)
    {
        into.Clear();
        IReadOnlyList<PlayerHealth> players = PlayerRegistry.All;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerHealth p = players[i];
            if (p == null || p.IsDead || !PlayerRegistry.IsInRound(p)) continue;
            if (Contains(p.transform.position)) into.Add(p);
        }
        return into;
    }

    /// <summary>The first zone box that holds this point, or null.</summary>
    public static RuleZone At(Vector3 point)
    {
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && all[i].Contains(point)) return all[i];
        return null;
    }

    /// <summary>Is the point inside any box of this quest zone (zone_screen, zone_toilet, zone_ticket)?</summary>
    public static bool InZone(string questZoneID, Vector3 point)
    {
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && all[i].zoneID == questZoneID && all[i].Contains(point)) return true;
        return false;
    }

    /// <summary>Editor tool: set the main box from a world-axis box (the object must not be rotated).</summary>
    public void Set(string displayName, Bounds worldBox)
    {
        zoneName = displayName;
        center = transform.InverseTransformPoint(worldBox.center);
        Vector3 s = transform.lossyScale;
        size = new Vector3(worldBox.size.x / Mathf.Max(0.0001f, Mathf.Abs(s.x)),
                           worldBox.size.y / Mathf.Max(0.0001f, Mathf.Abs(s.y)),
                           worldBox.size.z / Mathf.Max(0.0001f, Mathf.Abs(s.z)));
    }

    /// <summary>A random point inside the main box, at the given floor height, for rule ghosts.</summary>
    public Vector3 RandomPointInside(float floorY)
    {
        var local = new Vector3(center.x + Random.Range(-0.5f, 0.5f) * size.x, center.y, center.z + Random.Range(-0.5f, 0.5f) * size.z);
        Vector3 world = transform.TransformPoint(local);
        world.y = floorY;
        return world;
    }

    private static readonly Color Outline = new Color(1f, 0.85f, 0.2f, 0.9f);

    private void OnDrawGizmos()
    {
        Gizmos.color = Outline;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(center, size);
        for (int i = 0; i < moreBoxes.Count; i++) Gizmos.DrawWireCube(moreBoxes[i].center, moreBoxes[i].size);
        Gizmos.matrix = Matrix4x4.identity;
#if UNITY_EDITOR
        var style = new GUIStyle(UnityEditor.EditorStyles.boldLabel) { normal = { textColor = Outline } };
        UnityEditor.Handles.Label(transform.TransformPoint(center + Vector3.up * (size.y * 0.5f)),
            zoneName + (string.IsNullOrEmpty(zoneID) ? "" : "  (" + zoneID + ")"), style);
#endif
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.1f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(center, size);
        for (int i = 0; i < moreBoxes.Count; i++) Gizmos.DrawCube(moreBoxes[i].center, moreBoxes[i].size);
        Gizmos.matrix = Matrix4x4.identity;
    }
}
