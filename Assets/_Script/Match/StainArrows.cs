using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 17 Nov 2026 (Mr.k, Task 5): while YOU hold a mop, each stain that needs cleaning, is within
/// 15 m and that you can see (nothing in between) gets a floating arrow above it.
/// Local only: other players do not see your arrows. Nothing goes over the network.
/// Range and colour: Resources/Tuning/FilmMopTuning.
/// Made by itself when the game starts (no scene object needed).
/// </summary>
public sealed class StainArrows : MonoBehaviour
{
    private const float CheckEvery = 0.12f;

    private readonly List<GameObject> arrows = new List<GameObject>();
    private readonly List<ZoneChoreSpot> shown = new List<ZoneChoreSpot>();
    private readonly RaycastHit[] hits = new RaycastHit[16];
    private Mesh arrowMesh;
    private Material arrowMaterial;
    private float nextCheck;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (FindAnyObjectByType<StainArrows>() != null) return;
        var go = new GameObject("Stain Arrows (local)");
        DontDestroyOnLoad(go);
        go.AddComponent<StainArrows>();
    }

    private void Update()
    {
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + CheckEvery;
            FindVisibleStains();
        }
        Place();
    }

    private void FindVisibleStains()
    {
        shown.Clear();
        PlayerHealth me = PlayerHealth.LocalInstance;
        if (me == null || me.IsDead || me.IsDowned || !CarryProp.LocalHolds(CarryProp.Mop)) return;
        Camera cam = RuleFx.LocalCamera();
        if (cam == null) return;
        Vector3 eye = cam.transform.position;
        float range = FilmMopTuning.Current.arrowRange;

        foreach (ZoneChoreSpot spot in ZoneChoreSpot.All)
        {
            if (spot == null || !spot.Needed || spot.NeedsItem != CarryProp.Mop) continue;
            if (spot.Kind != ZoneTaskKind.CleanScreenRoom && spot.Kind != ZoneTaskKind.CleanToilet) continue;
            Vector3 target = spot.transform.position + Vector3.up * 0.25f;
            Vector3 toTarget = target - eye;
            float distance = toTarget.magnitude;
            if (distance > range || distance < 0.01f) continue;
            if (Blocked(eye, toTarget / distance, distance - 0.15f, me.transform, spot.transform)) continue;
            shown.Add(spot);
        }
    }

    /// <summary>Something solid between the eye and the stain (not your own body, not the stain).</summary>
    private bool Blocked(Vector3 from, Vector3 direction, float distance, Transform self, Transform stain)
    {
        if (distance <= 0f) return false;
        int count = Physics.RaycastNonAlloc(from, direction, hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform t = hits[i].collider != null ? hits[i].collider.transform : null;
            if (t == null || t.IsChildOf(self) || t.IsChildOf(stain)) continue;
            if (t.GetComponentInParent<CarryProp>() != null) continue; // the mop in your hand
            return true;
        }
        return false;
    }

    private void Place()
    {
        while (arrows.Count < shown.Count) arrows.Add(MakeArrow());
        FilmMopTuning tuning = FilmMopTuning.Current;
        if (arrowMaterial != null) arrowMaterial.color = tuning.stainArrowColor;
        float bob = Mathf.Sin(Time.time * 3f) * 0.06f;
        Vector3 scale = Vector3.one * tuning.arrowSize;
        Quaternion spin = Quaternion.Euler(0f, Time.time * 90f, 0f);
        for (int i = 0; i < arrows.Count; i++)
        {
            GameObject arrow = arrows[i];
            bool on = i < shown.Count && shown[i] != null;
            if (arrow.activeSelf != on) arrow.SetActive(on);
            if (!on) continue;
            arrow.transform.SetPositionAndRotation(shown[i].transform.position + Vector3.up * (tuning.arrowHeight + bob), spin);
            arrow.transform.localScale = scale;
        }
    }

    private GameObject MakeArrow()
    {
        if (arrowMesh == null) arrowMesh = BuildArrowMesh();
        if (arrowMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            arrowMaterial = new Material(shader) { name = "Stain Arrow (runtime)", color = FilmMopTuning.Current.stainArrowColor };
        }
        var go = new GameObject("Stain Arrow");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = arrowMesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = arrowMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.SetActive(false);
        return go;
    }

    /// <summary>17 Nov: only an arrow HEAD pointing down (no tail): a small pyramid, about 15 cm tall.</summary>
    private static Mesh BuildArrowMesh()
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        const float half = 0.08f, height = 0.15f;
        Vector3 tip = Vector3.zero;
        Vector3 h0 = new Vector3(-half, height, -half), h1 = new Vector3(half, height, -half), h2 = new Vector3(half, height, half), h3 = new Vector3(-half, height, half);
        Tri(v, t, h0, h1, tip); Tri(v, t, h1, h2, tip); Tri(v, t, h2, h3, tip); Tri(v, t, h3, h0, tip); Quad(v, t, h0, h3, h2, h1);
        var mesh = new Mesh { name = "Stain Arrow" };
        mesh.SetVertices(v);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void Quad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        t.Add(i); t.Add(i + 1); t.Add(i + 2);
        t.Add(i); t.Add(i + 2); t.Add(i + 3);
    }

    private static void Tri(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c)
    {
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(c);
        t.Add(i); t.Add(i + 1); t.Add(i + 2);
    }

    private void OnDestroy()
    {
        if (arrowMaterial != null) Destroy(arrowMaterial);
        if (arrowMesh != null) Destroy(arrowMesh);
    }
}
