using UnityEngine;

[RequireComponent(typeof(BoxCollider)), DisallowMultipleComponent]
public sealed class AmbientZone : MonoBehaviour
{
    public AudioClip clip;
    [Range(0, 1)] public float volume = 0.5f;
    public int priority;
    [Tooltip("Metres OUTSIDE the box over which this ambience fades out. Walking through a doorway " +
             "then crossfades by position instead of switching at the edge. 0 = hard edge.")]
    [Min(0)] public float blendDistance = 2.5f;

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

    /// <summary>
    /// 1 inside any box, falling linearly to 0 at <see cref="blendDistance"/> metres outside the
    /// nearest box, 0 further away (or when disabled).
    /// </summary>
    public float Weight(Vector3 position)
    {
        if (!isActiveAndEnabled) return 0;
        float weight = Weight(GetComponent<BoxCollider>(), position);
        if (additionalAreas != null)
            foreach (var area in additionalAreas)
                if (weight < 1) weight = Mathf.Max(weight, Weight(area, position));
        return weight;
    }

    private float Weight(BoxCollider box, Vector3 position)
    {
        if (box == null || !box.enabled || !box.gameObject.activeInHierarchy) return 0;
        Vector3 local = box.transform.InverseTransformPoint(position) - box.center;
        Vector3 half = box.size * 0.5f;
        Vector3 clamped = new Vector3(Mathf.Clamp(local.x, -half.x, half.x),
            Mathf.Clamp(local.y, -half.y, half.y), Mathf.Clamp(local.z, -half.z, half.z));
        if (clamped == local) return 1;
        if (blendDistance <= 0) return 0;
        float outside = Vector3.Distance(position, box.transform.TransformPoint(clamped + box.center));
        return Mathf.Clamp01(1 - outside / blendDistance);
    }

    private static bool Contains(BoxCollider box, Vector3 position)
    {
        if (box == null || !box.enabled || !box.gameObject.activeInHierarchy) return false;
        Vector3 local = box.transform.InverseTransformPoint(position) - box.center;
        Vector3 half = box.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    private void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    // Always drawn (like RuleZone's yellow box) so every ambience area is visible in
    // the Scene view; selecting the zone adds a translucent fill. Toggle it from the
    // Scene view Gizmos menu (AmbientZone) if it gets in the way.
    private static readonly Color OutlineColor = new Color(0.2f, 0.85f, 1f, 0.9f);
    private static readonly Color FillColor = new Color(0.2f, 0.85f, 1f, 0.12f);

    private void OnDrawGizmos()
    {
        Gizmos.color = OutlineColor;
        DrawAll(false);
#if UNITY_EDITOR
        var box = GetComponent<BoxCollider>();
        if (box != null)
        {
            string label = name + (clip != null ? "\n\u266A " + clip.name : "\n(no clip)");
            var style = new GUIStyle(UnityEditor.EditorStyles.boldLabel) { normal = { textColor = OutlineColor } };
            UnityEditor.Handles.Label(box.transform.TransformPoint(box.center), label, style);
        }
#endif
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = FillColor;
        DrawAll(true);
        if (blendDistance <= 0) return;
        // Faint outer box: where this ambience has faded to silence.
        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.35f);
        DrawBlend(GetComponent<BoxCollider>());
        if (additionalAreas != null)
            foreach (var area in additionalAreas) DrawBlend(area);
    }

    private void DrawBlend(BoxCollider area)
    {
        if (area == null) return;
        Vector3 s = area.transform.lossyScale;
        Vector3 pad = new Vector3(blendDistance / Mathf.Max(0.0001f, Mathf.Abs(s.x)),
            blendDistance / Mathf.Max(0.0001f, Mathf.Abs(s.y)), blendDistance / Mathf.Max(0.0001f, Mathf.Abs(s.z))) * 2;
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = area.transform.localToWorldMatrix;
        Gizmos.DrawWireCube(area.center, area.size + pad);
        Gizmos.matrix = previous;
    }

    private void DrawAll(bool solid)
    {
        DrawArea(GetComponent<BoxCollider>(), solid);
        if (additionalAreas != null)
            foreach (var area in additionalAreas) DrawArea(area, solid);
    }

    private static void DrawArea(BoxCollider area, bool solid)
    {
        if (area == null) return;
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = area.transform.localToWorldMatrix;
        if (solid) Gizmos.DrawCube(area.center, area.size);
        else Gizmos.DrawWireCube(area.center, area.size);
        Gizmos.matrix = previous;
    }
}
