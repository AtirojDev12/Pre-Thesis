using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 16 Oct 2026 (Mr.k): the area a ZONE RULE watches (one screen room, one toilet).
/// A world-aligned box: the yellow wire box in the Scene view. Move / resize it
/// with the Inspector (Center / Size are in world metres, rotation is ignored).
///
/// Zone Rules work ONLY inside their own zone. Map Rules (Hard / 13 Rules) are a
/// different system and do not use this.
///
/// No collider on purpose: the server checks player positions itself
/// (the same way ExitPoint does), so nothing here can block walking.
/// </summary>
[DisallowMultipleComponent]
public sealed class RuleZone : MonoBehaviour
{
    [Tooltip("Name shown in logs and rule messages, e.g. \"Ghost Room\" or \"Women's Toilet\".")]
    [SerializeField] private string zoneName = "Zone";
    [Tooltip("World position of the box centre.")]
    [SerializeField] private Vector3 center;
    [Tooltip("Box size in metres (world axes).")]
    [SerializeField] private Vector3 size = new Vector3(8f, 4f, 8f);

    public string ZoneName => zoneName;
    public Bounds Box => new Bounds(center, size);

    /// <summary>True if a point (a player's feet) is inside. Feet may sit a little under the floor line.</summary>
    public bool Contains(Vector3 point)
    {
        Bounds b = Box;
        b.Expand(new Vector3(0f, 0.6f, 0f));
        return b.Contains(point);
    }

    public bool Contains(PlayerHealth player) => player != null && Contains(player.transform.position);

    /// <summary>
    /// SERVER (or offline). Living players in this round who stand inside.
    /// Fills <paramref name="into"/> (cleared first) so callers can reuse one list.
    /// </summary>
    public List<PlayerHealth> PlayersInside(List<PlayerHealth> into)
    {
        into.Clear();
        IReadOnlyList<PlayerHealth> all = PlayerRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            PlayerHealth p = all[i];
            if (p == null || p.IsDead || !PlayerRegistry.IsInRound(p)) continue;
            if (Contains(p.transform.position)) into.Add(p);
        }
        return into;
    }

    /// <summary>Editor tool: set the box.</summary>
    public void Set(string displayName, Bounds box)
    {
        zoneName = displayName;
        center = box.center;
        size = box.size;
    }

    /// <summary>A random point inside (same height as the centre's floor), for rule ghosts.</summary>
    public Vector3 RandomPointInside(float floorY)
    {
        Bounds b = Box;
        return new Vector3(Random.Range(b.min.x, b.max.x), floorY, Random.Range(b.min.z, b.max.z));
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.DrawWireCube(center, size);
    }
}
