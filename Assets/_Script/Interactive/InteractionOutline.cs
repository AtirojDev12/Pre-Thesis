using System.Collections.Generic;
using UnityEngine;

/// <summary>Local hover selection. The renderer feature draws edges without changing authored materials.</summary>
public sealed class InteractionOutline : MonoBehaviour
{
    private static readonly HashSet<InteractionOutline> Visible = new HashSet<InteractionOutline>();
    private Camera ownerCamera;
    private float selectedAt;
    private readonly List<Renderer> renderers = new List<Renderer>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetVisible() => Visible.Clear();

    public void SetVisible(bool visible, Camera camera = null)
    {
        if (!visible)
        {
            Visible.Remove(this);
            return;
        }
        if (!Visible.Contains(this)) selectedAt = Time.unscaledTime;
        ownerCamera = camera;
        // Refresh on selection: placed items and station contents can change at runtime.
        GetComponentsInChildren(true, renderers);
        Visible.Add(this);
    }

    public static float SelectionStartedAt(Camera camera)
    {
        foreach (InteractionOutline outline in Visible)
            if (outline != null && outline.isActiveAndEnabled &&
                (outline.ownerCamera == null || outline.ownerCamera == camera)) return outline.selectedAt;
        return Time.unscaledTime;
    }

    public static void CollectRenderers(Camera camera, List<Renderer> destination)
    {
        destination.Clear();
        foreach (InteractionOutline outline in Visible)
        {
            if (outline == null || !outline.isActiveAndEnabled) continue;
            if (outline.ownerCamera != null && outline.ownerCamera != camera) continue;
            foreach (Renderer source in outline.renderers)
            {
                if (source == null || !source.enabled || source.forceRenderingOff || !source.gameObject.activeInHierarchy) continue;
                if (!(source is MeshRenderer) && !(source is SkinnedMeshRenderer)) continue;
                if ((camera.cullingMask & (1 << source.gameObject.layer)) == 0) continue;
                if (source.GetComponentInParent<InteractionOutline>() != outline) continue;
                destination.Add(source);
            }
        }
    }

    private void OnDisable() => Visible.Remove(this);
    private void OnDestroy() => Visible.Remove(this);
}
