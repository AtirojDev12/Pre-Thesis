using UnityEngine;

[RequireComponent(typeof(BoxCollider)), DisallowMultipleComponent]
public sealed class AmbientZone : MonoBehaviour
{
    public AudioClip clip;
    [Range(0, 1)] public float volume = 0.5f;
    public int priority;

    [Header("Additional areas sharing this ambience")]
    [Tooltip("Optional extra boxes. Moving between these areas keeps the same loop and event timer.")]
    public BoxCollider[] additionalAreas;

    [Header("Occasional sound")]
    public AudioClip occasionalClip;
    [Range(0, 1)] public float occasionalVolume = 0.35f;
    [Tooltip("Quiet time in seconds, measured after the previous clip finishes.")]
    public Vector2 quietSeconds = new Vector2(50, 100);
    [Tooltip("Chance that a normal event starts a short cluster of extra plays.")]
    [Range(0, 1)] public float burstChance = 0.15f;
    public Vector2Int burstExtraPlays = new Vector2Int(1, 2);
    public Vector2 burstGapSeconds = new Vector2(3, 8);

    public bool Contains(Vector3 position)
    {
        if (!isActiveAndEnabled) return false;
        if (Contains(GetComponent<BoxCollider>(), position)) return true;
        if (additionalAreas != null)
            foreach (var area in additionalAreas)
                if (Contains(area, position)) return true;
        return false;
    }

    private static bool Contains(BoxCollider box, Vector3 position)
    {
        if (box == null || !box.enabled || !box.gameObject.activeInHierarchy) return false;
        Vector3 local = box.transform.InverseTransformPoint(position) - box.center;
        Vector3 half = box.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    private void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.8f);
        DrawArea(GetComponent<BoxCollider>());
        if (additionalAreas != null)
            foreach (var area in additionalAreas) DrawArea(area);
    }

    private static void DrawArea(BoxCollider area)
    {
        if (area == null) return;
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = area.transform.localToWorldMatrix;
        Gizmos.DrawWireCube(area.center, area.size);
        Gizmos.matrix = previous;
    }
}
