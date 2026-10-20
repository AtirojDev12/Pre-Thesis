using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Authority plans one route; clients draw pooled floor meshes and an automatic shader sweep.</summary>
[DisallowMultipleComponent]
public sealed class GhostFavorTrail : MonoBehaviour
{
    [SerializeField] private GhostFavorTrailSettings settings = new GhostFavorTrailSettings();
    [Tooltip("Optional walkable starting point next to the Ghost Flavor counter.")]
    [SerializeField] private Transform homeApproachPoint;
    [SerializeField] private Material trailMaterial;

    public GhostFavorTrailSettings Settings => settings;
    public bool RequiresReachableDestination => isActiveAndEnabled && settings.enabled;
    public GhostFavorTrailSnapshot VisibleSnapshot => !NetworkMode.IsOffline && PopcornNetSync.Instance != null
        ? PopcornNetSync.Instance.GhostTrail : snapshot;
    public int PooledChunkCount => chunks.Count;
    public int VisibleMarkCount { get; private set; }

    private GhostFavorRecovery recovery;
    private GhostFavorTrailSnapshot snapshot;
    private Vector3 trackedPosition;
    private bool tracking, routeReady;
    private double stableSince, nextPathCheck;
    private bool warned;
    private PopcornNetSync publishedSync;
    private uint drawnRevision = uint.MaxValue;
    private Material localMaterial;
    private float opacity;
    private float nextVisibilityCheck;
    private readonly List<Chunk> chunks = new List<Chunk>();
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Vector3> normals = new List<Vector3>();
    private readonly List<Vector2> uvs = new List<Vector2>();
    private readonly List<Vector2> routeUvs = new List<Vector2>();
    private readonly List<int> triangles = new List<int>();

    private sealed class Chunk
    {
        public GameObject root;
        public Mesh mesh;
        public MeshRenderer renderer;
        public bool used;
    }

    private static double Now => NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;
    public void Configure(GhostFavorRecovery owner) => recovery = owner;

    public bool TryPlanDestination(Transform destination, out Vector3[] corners)
    {
        corners = null;
        if (recovery == null || destination == null) return false;
        GhostFavorTrailDestination extra = destination.GetComponent<GhostFavorTrailDestination>();
        return GhostFavorTrailPath.TryCalculate(recovery.HomePosition, destination.position, homeApproachPoint,
            extra != null ? extra.pickupPoint : null, recovery.ItemTransform, settings, out corners);
    }

    public void AcceptRelocation(Vector3 position, Vector3[] corners)
    {
        if (recovery == null || !recovery.HasTrailAuthority || !RequiresReachableDestination) return;
        tracking = routeReady = true;
        trackedPosition = position;
        stableSince = Now;
        nextPathCheck = Now + Mathf.Max(.5f, settings.blockedPathCheckInterval);
        SetRoute(corners);
    }

    public void ReportNoReachableDestination()
    {
        if (warned) return;
        warned = true;
        Debug.LogWarning("[GhostFavorTrail] No complete walkable route to a relocation point. Check the baked NavMesh, " +
            "agent type, and optional Home Approach / Pickup Point. Ghost Flavor stays home; no trail through walls is drawn.", this);
    }

    private void Update()
    {
        if (recovery == null || NetworkMode.SessionEnding) { HideChunks(); return; }
        if (NetworkMode.IsOffline) publishedSync = null;
        else if (recovery.HasTrailAuthority && PopcornNetSync.Instance != null && publishedSync != PopcornNetSync.Instance)
        {
            publishedSync = PopcornNetSync.Instance;
            Publish(); // An offline -> host transition also needs an initial route snapshot.
        }
        if (recovery.HasTrailAuthority) UpdateAuthority(Now);
        UpdateVisuals(Now);
    }

    private void UpdateAuthority(double now)
    {
        if (!settings.enabled || recovery.IsHome)
        {
            tracking = routeReady = false;
            ClearRoute();
            return;
        }
        if (recovery.IsCarried)
        {
            tracking = routeReady = false;
            SetActive(false);
            return;
        }

        Vector3 position = recovery.AuthorityPosition;
        float threshold = Mathf.Max(.01f, settings.movementThreshold);
        if (!tracking || (position - trackedPosition).sqrMagnitude > threshold * threshold)
        {
            tracking = true;
            trackedPosition = position;
            stableSince = now;
            routeReady = false;
            SetActive(false);
        }
        if (recovery.IsMovingForTrail)
        {
            stableSince = now;
            routeReady = false;
            SetActive(false);
            return;
        }
        if (!routeReady && now - stableSince < Mathf.Max(.1f, settings.settleSeconds)) return;
        if (routeReady && now < nextPathCheck) return;
        nextPathCheck = now + Mathf.Max(.5f, settings.blockedPathCheckInterval);
        if (routeReady && snapshot.active && GhostFavorTrailPath.IsWalkable(snapshot.corners, settings)) return;

        routeReady = true;
        if (GhostFavorTrailPath.TryCalculate(recovery.HomePosition, position, homeApproachPoint,
            null, recovery.ItemTransform, settings, out Vector3[] corners))
        {
            warned = false;
            if (!SameRoute(snapshot.corners, corners)) SetRoute(corners);
            else SetActive(true);
        }
        else
        {
            SetActive(false);
            if (!warned)
            {
                warned = true;
                Debug.LogWarning("[GhostFavorTrail] Dropped item's route is unavailable. The guide is hidden and will " +
                    "retry at the path-check interval. Check floor access / NavMesh carving.", this);
            }
        }
    }

    private static bool SameRoute(Vector3[] a, Vector3[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if ((a[i] - b[i]).sqrMagnitude > .0025f) return false;
        return true;
    }

    private void SetRoute(Vector3[] corners)
    {
        if (corners == null || corners.Length < 2) { ClearRoute(); return; }
        snapshot = new GhostFavorTrailSnapshot
        {
            revision = snapshot.revision + 1, active = true, corners = corners,
            seed = unchecked((int)(snapshot.revision + 1) * 7919), pulseEpoch = Now
        };
        Publish();
    }

    private void SetActive(bool active)
    {
        if (snapshot.active == active) return;
        snapshot.active = active;
        Publish();
    }

    private void ClearRoute()
    {
        if (!snapshot.active && (snapshot.corners == null || snapshot.corners.Length == 0)) return;
        snapshot = new GhostFavorTrailSnapshot { revision = snapshot.revision + 1 };
        Publish();
    }

    private void Publish()
    {
        if (PopcornNetSync.Instance != null && PopcornNetSync.Instance.isServer)
            PopcornNetSync.Instance.PublishGhostTrail(snapshot);
    }

    private void UpdateVisuals(double now)
    {
        // A dedicated server owns the route, but does not allocate rendering objects.
        if ((NetworkServer.active && !NetworkClient.active) || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        GhostFavorTrailSnapshot visible = VisibleSnapshot;
        if (visible.revision != drawnRevision)
        {
            drawnRevision = visible.revision;
            opacity = 0;
            BuildMeshes(visible);
        }
        float target = settings.enabled && visible.active ? 1f : 0f;
        opacity = Mathf.MoveTowards(opacity, target, Time.deltaTime / Mathf.Max(.05f, settings.fadeSeconds));
        if (localMaterial == null) return;
        localMaterial.SetFloat("_Opacity", opacity);
        localMaterial.SetFloat("_TrailTime", (float)Math.Max(0, now - visible.pulseEpoch));
        localMaterial.SetColor("_BloodColor", settings.bloodColor);
        localMaterial.SetTexture("_BloodTexture", settings.bloodTexture != null ? settings.bloodTexture : Texture2D.whiteTexture);
        localMaterial.SetFloat("_UseBloodTexture", settings.bloodTexture != null ? 1 : 0);
        localMaterial.SetColor("_SweepColor", settings.sweepColor);
        localMaterial.SetFloat("_MinimumVisibility", settings.minimumVisibility);
        localMaterial.SetFloat("_SweepIntensity", target > 0 ? settings.sweepIntensity : 0);
        localMaterial.SetFloat("_SweepDuration", Mathf.Max(.2f, settings.sweepDuration));
        localMaterial.SetFloat("_SweepPause", Mathf.Max(.1f, settings.sweepPause));
        localMaterial.SetFloat("_SweepWidth", Mathf.Max(.1f, settings.sweepWidth));
        localMaterial.SetFloat("_VisibleDistance", Mathf.Max(2, settings.visibleDistance));
        localMaterial.SetFloat("_DistanceFade", Mathf.Max(.1f, settings.distanceFade));
        if (Time.unscaledTime < nextVisibilityCheck && opacity > 0 && target > 0) return;
        nextVisibilityCheck = Time.unscaledTime + .25f;
        Camera camera = Camera.main;
        Vector3 eye = camera != null ? camera.transform.position :
            PlayerHealth.LocalInstance != null ? PlayerHealth.LocalInstance.transform.position : Vector3.positiveInfinity;
        float range = Mathf.Max(2, settings.visibleDistance);
        foreach (Chunk chunk in chunks)
            chunk.renderer.enabled = chunk.used && opacity > 0 &&
                (chunk.renderer.bounds.ClosestPoint(eye) - eye).sqrMagnitude <= range * range;
    }

    private bool EnsureMaterial()
    {
        if (localMaterial != null) return true;
        Material template = trailMaterial != null ? trailMaterial : Resources.Load<Material>("GhostFavorTrail");
        if (template == null || template.shader == null)
        {
            Debug.LogWarning("[GhostFavorTrail] Assign the GhostFavorTrail material before drawing the guide.", this);
            return false;
        }
        localMaterial = new Material(template) { name = "Ghost Flavor Trail (local)", hideFlags = HideFlags.DontSave };
        return true;
    }

    private void BuildMeshes(GhostFavorTrailSnapshot visible)
    {
        HideChunks();
        VisibleMarkCount = 0;
        if (visible.corners == null || visible.corners.Length < 2 || !EnsureMaterial()) return;
        float length = GhostFavorTrailPath.Length(visible.corners);
        if (length < .05f) return;
        localMaterial.SetFloat("_RouteLength", length);
        var random = new System.Random(visible.seed);
        int budget = Mathf.Clamp(settings.maximumMarks, 8, 256);
        float spacing = Mathf.Max(.3f, settings.spacing, length / (budget - 1));
        int perChunk = Mathf.Clamp(settings.marksPerMesh, 8, 48);
        int segment = 1, chunkIndex = 0, inChunk = 0;
        float segmentStart = 0;
        ClearBuffers();
        for (int mark = 0; mark < budget; mark++)
        {
            float distance = Mathf.Min(mark * spacing, length);
            while (segment < visible.corners.Length - 1 &&
                segmentStart + Vector3.Distance(visible.corners[segment - 1], visible.corners[segment]) < distance)
            {
                segmentStart += Vector3.Distance(visible.corners[segment - 1], visible.corners[segment]);
                segment++;
            }
            Vector3 a = visible.corners[segment - 1], b = visible.corners[segment];
            float segmentLength = Vector3.Distance(a, b);
            Vector3 direction = (b - a).normalized;
            Vector3 point = Vector3.Lerp(a, b, segmentLength > 0 ? (distance - segmentStart) / segmentLength : 0);
            float scale = .8f + (float)random.NextDouble() * .4f;
            float seed = (float)random.NextDouble();
            float sideways = ((float)random.NextDouble() * 2 - 1) * settings.sidewaysVariation;
            point += Vector3.Cross(Vector3.up, direction).normalized * sideways;
            if (AppendMark(point, direction, scale, seed, distance, length))
            {
                VisibleMarkCount++;
                if (++inChunk == perChunk) { FlushChunk(chunkIndex++); ClearBuffers(); inChunk = 0; }
            }
            if (distance >= length) break;
        }
        if (inChunk > 0) FlushChunk(chunkIndex);
        if (VisibleMarkCount == 0)
            Debug.LogWarning("[GhostFavorTrail] The route has no drawable floor marks. Check Ground Layers, floor colliders and probe height/error settings.", this);
        nextVisibilityCheck = 0;
    }

    private bool AppendMark(Vector3 point, Vector3 direction, float scale, float seed, float distance, float routeLength)
    {
        if (!GhostFavorTrailPath.TryFloor(point, recovery.ItemTransform, settings, out Vector3 centre, out Vector3 normal)) return false;
        Vector3 forward = Vector3.ProjectOnPlane(direction, normal).normalized;
        if (forward.sqrMagnitude < .5f) return false;
        Vector3 right = Vector3.Cross(normal, forward).normalized;
        float halfWidth = Mathf.Max(.05f, settings.markSize.x) * scale * .5f;
        float halfLength = Mathf.Max(.05f, settings.markSize.y) * scale * .5f;
        Vector3 p0 = centre - right * halfWidth - forward * halfLength;
        Vector3 p1 = centre + right * halfWidth - forward * halfLength;
        Vector3 p2 = centre + right * halfWidth + forward * halfLength;
        Vector3 p3 = centre - right * halfWidth + forward * halfLength;
        // Conform each corner; skip edges/gaps rather than paint an airborne quad.
        if (!GhostFavorTrailPath.TryFloor(p0, recovery.ItemTransform, settings, out p0, out _) ||
            !GhostFavorTrailPath.TryFloor(p1, recovery.ItemTransform, settings, out p1, out _) ||
            !GhostFavorTrailPath.TryFloor(p2, recovery.ItemTransform, settings, out p2, out _) ||
            !GhostFavorTrailPath.TryFloor(p3, recovery.ItemTransform, settings, out p3, out _)) return false;
        int start = vertices.Count;
        vertices.Add(transform.InverseTransformPoint(p0)); vertices.Add(transform.InverseTransformPoint(p1));
        vertices.Add(transform.InverseTransformPoint(p2)); vertices.Add(transform.InverseTransformPoint(p3));
        Vector3 localNormal = transform.localToWorldMatrix.transpose.MultiplyVector(normal).normalized;
        for (int i = 0; i < 4; i++) normals.Add(localNormal);
        uvs.Add(Vector2.zero); uvs.Add(Vector2.right); uvs.Add(Vector2.one); uvs.Add(Vector2.up);
        float low = Mathf.Clamp01((distance - halfLength) / routeLength), high = Mathf.Clamp01((distance + halfLength) / routeLength);
        routeUvs.Add(new Vector2(low, seed)); routeUvs.Add(new Vector2(low, seed));
        routeUvs.Add(new Vector2(high, seed)); routeUvs.Add(new Vector2(high, seed));
        triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
        triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
        return true;
    }

    private void ClearBuffers() { vertices.Clear(); normals.Clear(); uvs.Clear(); routeUvs.Clear(); triangles.Clear(); }

    private void FlushChunk(int index)
    {
        while (chunks.Count <= index)
        {
            var root = new GameObject("Blood trail mesh " + chunks.Count, typeof(MeshFilter), typeof(MeshRenderer));
            root.transform.SetParent(transform, false);
            root.layer = 2; // Ignore Raycast; never block interaction or physics.
            var mesh = new Mesh { name = "Pooled blood marks", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            root.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = localMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            chunks.Add(new Chunk { root = root, mesh = mesh, renderer = renderer });
        }
        Chunk chunk = chunks[index];
        chunk.mesh.Clear();
        chunk.mesh.SetVertices(vertices); chunk.mesh.SetNormals(normals);
        chunk.mesh.SetUVs(0, uvs); chunk.mesh.SetUVs(1, routeUvs);
        chunk.mesh.SetTriangles(triangles, 0);
        chunk.mesh.RecalculateBounds();
        chunk.used = true;
    }

    private void HideChunks()
    {
        foreach (Chunk chunk in chunks) { chunk.used = false; chunk.renderer.enabled = false; }
    }

    private void OnEnable() { drawnRevision = uint.MaxValue; opacity = 0; tracking = routeReady = false; }
    private void OnValidate()
    {
        if (settings == null) settings = new GhostFavorTrailSettings();
        drawnRevision = uint.MaxValue;
        routeReady = false;
        nextPathCheck = 0;
    }
    private void OnDisable()
    {
        HideChunks();
        if (recovery != null && recovery.HasTrailAuthority) ClearRoute();
    }
    private void OnDestroy()
    {
        foreach (Chunk chunk in chunks) { Release(chunk.root); Release(chunk.mesh); }
        Release(localMaterial);
    }
    private static void Release(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
