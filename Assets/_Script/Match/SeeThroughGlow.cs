using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 17 Nov 2026 (Mr.k, Task 5): a glowing copy of an object's meshes that is seen THROUGH walls.
/// Local only (each PC builds its own copy). Used for the projector glow before a film change.
///
/// The copy uses the shader "Hidden/PreThesis/SeeThroughGlow" (Resources/Shaders). If that shader
/// is missing, it falls back to a normal see-through colour (then walls hide it).
/// </summary>
public sealed class SeeThroughGlow
{
    private const string ShaderResource = "Shaders/SeeThroughGlow";
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private readonly List<GameObject> parts = new List<GameObject>();
    private Material material;
    private bool visible;

    /// <summary>Copies every mesh under each source. Null when there is nothing to copy.</summary>
    public static SeeThroughGlow Build(Transform[] sources, Color color)
    {
        if (sources == null || sources.Length == 0) return null;
        Shader shader = Resources.Load<Shader>(ShaderResource);
        if (shader == null || !shader.isSupported) shader = Shader.Find("Sprites/Default");
        if (shader == null) return null;

        var glow = new SeeThroughGlow
        {
            material = new Material(shader) { name = "See-Through Glow (runtime)", renderQueue = 4000 },
        };
        glow.material.SetColor(ColorId, color);

        // Collect first: the copies are added under the same parts.
        var filters = new List<MeshFilter>();
        foreach (Transform source in sources)
            if (source != null)
                foreach (MeshFilter f in source.GetComponentsInChildren<MeshFilter>(true))
                    if (!filters.Contains(f)) filters.Add(f);
        foreach (MeshFilter filter in filters)
        {
            if (filter.sharedMesh == null || filter.GetComponent<MeshRenderer>() == null) continue;
            if (filter.GetComponent<TMPro.TMP_Text>() != null) continue;
            var part = new GameObject(filter.name + " (see-through glow)");
            part.transform.SetParent(filter.transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            MeshRenderer r = part.AddComponent<MeshRenderer>();
            var materials = new Material[Mathf.Max(1, filter.sharedMesh.subMeshCount)];
            for (int i = 0; i < materials.Length; i++) materials[i] = glow.material;
            r.sharedMaterials = materials;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            part.SetActive(false);
            glow.parts.Add(part);
        }
        if (glow.parts.Count == 0)
        {
            Object.Destroy(glow.material);
            return null;
        }
        return glow;
    }

    /// <summary>Show / hide. <paramref name="strength"/> 0-1 scales the colour's alpha (for a pulse).</summary>
    public void SetVisible(bool show, Color color, float strength = 1f)
    {
        if (material != null)
        {
            color.a *= Mathf.Clamp01(strength);
            material.SetColor(ColorId, color);
        }
        if (show == visible) return;
        visible = show;
        foreach (GameObject go in parts) if (go != null) go.SetActive(show);
    }

    public void Destroy()
    {
        foreach (GameObject go in parts) if (go != null) Object.Destroy(go);
        parts.Clear();
        if (material != null) Object.Destroy(material);
        material = null;
    }
}
