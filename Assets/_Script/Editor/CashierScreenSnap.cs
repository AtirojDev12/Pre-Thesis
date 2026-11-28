using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 28 Nov 2026 (Atiroj): Tools > Pre-Thesis > Scenes > Map: Snap Cashier UI Anchor To Monitor.
///
/// The Cashier UI Anchor is a child of "Cube (2)" (Moniter), which is squashed (scale y 0.138,
/// z 1.78) and tilted. A RectTransform turned at an odd angle under it is sheared: its cyan gizmo
/// looks flat on the screen, but the real rotation the game reads points elsewhere (the white
/// frame in Play mode).
///
/// This command keeps the anchor's size and the side it faces, and:
///   1. turns it to lie exactly in the monitor panel's plane (lined up with the parent's axes,
///      so the squashed parent can no longer shear it);
///   2. moves it flat onto the nearest VISIBLE monitor surface in front of / behind it
///      (3 mm in front, so it does not flicker into the mesh);
///   3. gives it an even world scale (Width / Height on the RectTransform = metres).
/// Undo works (Ctrl+Z). Save the scene afterwards.
/// </summary>
public static class CashierScreenSnap
{
    private const float SurfaceGap = 0.003f;   // metres in front of the screen
    private const float SearchDistance = 0.6f; // how far in front / behind to look for the screen

    [MenuItem("Tools/Pre-Thesis/Scenes/Map: Snap Cashier UI Anchor To Monitor")]
    private static void Snap()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Cashier UI Anchor", "Stop Play mode first.", "OK");
            return;
        }

        PopcornMinigameBootstrap boot = Object.FindAnyObjectByType<PopcornMinigameBootstrap>(FindObjectsInactive.Include);
        RectTransform anchor = null;
        if (boot != null)
            anchor = new SerializedObject(boot).FindProperty("cashierUiAnchor")?.objectReferenceValue as RectTransform;
        if (anchor == null)
        {
            EditorUtility.DisplayDialog("Cashier UI Anchor",
                "No Popcorn Minigame Systems with a RectTransform 'Cashier UI Anchor' in the open scene.", "OK");
            return;
        }

        Transform parent = anchor.parent;
        Rect rect = anchor.rect;
        Vector3 centre = anchor.TransformPoint(rect.center);
        Vector3 across = anchor.TransformVector(Vector3.right * rect.width);
        Vector3 upward = anchor.TransformVector(Vector3.up * rect.height);
        Vector3 facing = Vector3.Cross(across, upward);
        if (across.sqrMagnitude < 1e-10f || upward.sqrMagnitude < 1e-10f || facing.sqrMagnitude < 1e-12f)
        {
            EditorUtility.DisplayDialog("Cashier UI Anchor", "The anchor has no size. Give it a Width and Height first.", "OK");
            return;
        }
        float width = across.magnitude;
        float height = upward.magnitude;

        // 1. The panel plane = the parent's two longest axes; its normal = the thinnest axis.
        Vector3 forward = facing.normalized;
        Vector3 up = upward.normalized;
        if (parent != null)
        {
            Vector3[] axes = { parent.right, parent.up, parent.forward };
            Vector3 lossy = parent.lossyScale;
            float[] size = { Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z) };
            int thin = size[0] <= size[1] && size[0] <= size[2] ? 0 : size[1] <= size[2] ? 1 : 2;
            // Only snap to the parent's axes when it really is a thin panel; otherwise keep
            // the anchor's own facing (no shear problem then anyway).
            float others = Mathf.Min(size[(thin + 1) % 3], size[(thin + 2) % 3]);
            if (size[thin] < others * 0.75f)
            {
                forward = Closest(axes[thin], forward);
                up = Closest(axes[(thin + 1) % 3], axes[(thin + 2) % 3], up);
            }
        }
        up = Vector3.ProjectOnPlane(up, forward).normalized;
        Quaternion rotation = Quaternion.LookRotation(forward, up);

        // 2. Flat onto the nearest visible monitor surface along the normal.
        Transform monitorRoot = parent != null && parent.parent != null ? parent.parent : parent;
        bool onSurface = TryFindSurface(monitorRoot, anchor, centre, forward, out Vector3 surface);
        if (onSurface) centre = surface - forward * SurfaceGap; // viewer is on the -forward side

        Undo.RecordObject(anchor, "Snap Cashier UI Anchor To Monitor");
        anchor.rotation = rotation;
        // 3. Even world scale: 1 unit of Width / Height = 1 metre.
        Vector3 local = Vector3.one;
        if (parent != null)
            for (int i = 0; i < 3; i++)
            {
                Vector3 axis = i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
                float along = parent.TransformVector(anchor.localRotation * axis).magnitude;
                local[i] = along > 1e-6f ? 1f / along : 1f;
            }
        anchor.localScale = local;
        anchor.pivot = new Vector2(0.5f, 0.5f);
        anchor.sizeDelta = new Vector2(width, height);
        anchor.position = centre;

        EditorUtility.SetDirty(anchor);
        EditorSceneManager.MarkSceneDirty(anchor.gameObject.scene);
        Selection.activeTransform = anchor;
        Debug.Log($"[CashierScreenSnap] Cashier UI Anchor lined up with the monitor ({width:F3} x {height:F3} m)" +
                  (onSurface ? ", placed flat on the screen surface." : ". No visible screen surface found along its normal: position kept.") +
                  " Save the scene.", anchor);
    }

    /// <summary>The axis (either sign) closest to the wanted direction.</summary>
    private static Vector3 Closest(Vector3 axis, Vector3 wanted) =>
        Vector3.Dot(axis, wanted) >= 0f ? axis : -axis;

    private static Vector3 Closest(Vector3 a, Vector3 b, Vector3 wanted) =>
        Mathf.Abs(Vector3.Dot(a, wanted)) >= Mathf.Abs(Vector3.Dot(b, wanted)) ? Closest(a, wanted) : Closest(b, wanted);

    /// <summary>
    /// Ray along the normal through the anchor centre, against the visible meshes of the monitor
    /// (renderers that are on). Returns the hit nearest to the anchor.
    /// </summary>
    private static bool TryFindSurface(Transform root, Transform anchor, Vector3 centre, Vector3 forward, out Vector3 point)
    {
        point = default;
        if (root == null) return false;
        Vector3 origin = centre - forward * SearchDistance;
        float best = float.PositiveInfinity;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null || filter.transform.IsChildOf(anchor)) continue;
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled || !filter.gameObject.activeInHierarchy) continue;
            Mesh mesh = filter.sharedMesh;
            vertices.Clear();
            mesh.GetVertices(vertices);
            Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                triangles.Clear();
                mesh.GetTriangles(triangles, s);
                for (int t = 0; t + 2 < triangles.Count; t += 3)
                {
                    Vector3 a = toWorld.MultiplyPoint3x4(vertices[triangles[t]]);
                    Vector3 b = toWorld.MultiplyPoint3x4(vertices[triangles[t + 1]]);
                    Vector3 c = toWorld.MultiplyPoint3x4(vertices[triangles[t + 2]]);
                    if (!RayTriangle(origin, forward, a, b, c, out float distance)) continue;
                    if (distance > SearchDistance * 2f) continue;
                    // The face the viewer sees: the first one met coming from the viewer's side.
                    if (distance < best) { best = distance; point = origin + forward * distance; }
                }
            }
        }
        return !float.IsPositiveInfinity(best);
    }

    // Möller–Trumbore, both faces.
    private static bool RayTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance)
    {
        distance = 0f;
        Vector3 e1 = b - a, e2 = c - a;
        Vector3 p = Vector3.Cross(direction, e2);
        float det = Vector3.Dot(e1, p);
        if (Mathf.Abs(det) < 1e-9f) return false;
        float inv = 1f / det;
        Vector3 s = origin - a;
        float u = Vector3.Dot(s, p) * inv;
        if (u < 0f || u > 1f) return false;
        Vector3 q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(direction, q) * inv;
        if (v < 0f || u + v > 1f) return false;
        distance = Vector3.Dot(e2, q) * inv;
        return distance > 0f;
    }
}
