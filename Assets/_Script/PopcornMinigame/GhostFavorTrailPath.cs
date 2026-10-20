using UnityEngine;
using UnityEngine.AI;

/// <summary>Shared route policy. Only the server/offline authority calls navigation.</summary>
public static class GhostFavorTrailPath
{
    public const int MaximumCorners = 128;
    private static readonly RaycastHit[] FloorHits = new RaycastHit[32];
    private static readonly NavMeshPath Path = new NavMeshPath();

    public static NavMeshQueryFilter Filter(GhostFavorTrailSettings settings) =>
        new NavMeshQueryFilter { agentTypeID = settings.agentTypeId, areaMask = settings.areaMask };

    public static bool TryCalculate(Vector3 home, Vector3 item, Transform homeApproach,
        Transform pickupPoint, Transform ignoredItem, GhostFavorTrailSettings settings, out Vector3[] corners)
    {
        corners = null;
        var filter = Filter(settings);
        // Try several nearby approach positions, not just a point inside a counter.
        int attempts = pickupPoint != null ? 1 : 13;
        for (int source = 0; source < (homeApproach != null ? 1 : 13); source++)
        {
            Vector3 startProbe = homeApproach != null ? homeApproach.position : Probe(home, source, settings);
            if (!TrySample(home, startProbe, ignoredItem, settings, filter, out Vector3 start)) continue;
            for (int i = 0; i < attempts; i++)
            {
                Vector3 probe = pickupPoint != null ? pickupPoint.position : Probe(item, i, settings);
                if (!TrySample(item, probe, ignoredItem, settings, filter, out Vector3 end)) continue;
                if (!NavMesh.CalculatePath(start, end, filter, Path) || Path.status != NavMeshPathStatus.PathComplete) continue;
                Vector3[] candidate = Path.corners;
                if (candidate.Length < 2 || candidate.Length > MaximumCorners || !IsWalkable(candidate, settings)) continue;
                corners = candidate;
                return true;
            }
        }
        return false;
    }

    private static Vector3 Probe(Vector3 item, int i, GhostFavorTrailSettings settings)
    {
        if (i == 0) return item;
        float angle = (i - 1) * Mathf.PI / 6f;
        float radius = Mathf.Min(settings.pickupReach * .65f, 1.2f);
        return item + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
    }

    private static bool TrySample(Vector3 item, Vector3 probe, Transform ignoredItem,
        GhostFavorTrailSettings settings, NavMeshQueryFilter filter, out Vector3 point)
    {
        point = default;
        if (!NavMesh.SamplePosition(probe, out NavMeshHit nav, Mathf.Max(.1f, settings.navMeshSampleRadius), filter)) return false;
        if (Mathf.Abs(item.y - nav.position.y) > settings.maximumFloorHeightDifference ||
            Vector3.Distance(item, nav.position) > settings.pickupReach) return false;
        if (!TryFloor(nav.position, ignoredItem, settings, out _, out _)) return false;
        Vector3 eye = nav.position + Vector3.up * 1.25f;
        Vector3 toItem = item + Vector3.up * .15f - eye;
        RaycastHit[] hits = Physics.RaycastAll(eye, toItem.normalized, toItem.magnitude,
            settings.groundLayers, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            if (Ignore(hit.collider, ignoredItem)) continue;
            // Only tolerate the surface immediately next to the item, not a wall in between.
            if (hit.distance < toItem.magnitude - .2f) return false;
        }
        point = nav.position;
        return true;
    }

    public static bool IsWalkable(Vector3[] corners, GhostFavorTrailSettings settings)
    {
        if (corners == null || corners.Length < 2) return false;
        var filter = Filter(settings);
        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 a = corners[i - 1], b = corners[i];
            if (!NavMesh.Raycast(a, b, out NavMeshHit hit, filter)) continue;
            // Path corners lie exactly on polygon boundaries. A ray starting on
            // that boundary can hit its own starting edge although the complete
            // path is valid. Check the segment's interior before rejecting it.
            if (Vector3.Distance(hit.position, a) > .1f && Vector3.Distance(hit.position, b) > .1f) return false;
            int samples = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(a, b) / .4f), 2, 64);
            for (int sample = 1; sample < samples; sample++)
            {
                Vector3 expected = Vector3.Lerp(a, b, (float)sample / samples);
                if (!NavMesh.SamplePosition(expected, out NavMeshHit onMesh, .08f, filter) ||
                    (onMesh.position - expected).sqrMagnitude > .0064f) return false;
            }
        }
        // Off-mesh jumps are deliberately rejected: a floor trail must not cross open air.
        return true;
    }

    public static bool TryFloor(Vector3 navPoint, Transform ignoredItem, GhostFavorTrailSettings settings,
        out Vector3 position, out Vector3 normal)
    {
        Vector3 origin = navPoint + Vector3.up * settings.groundProbeHeight;
        float length = settings.groundProbeHeight + settings.groundProbeDepth;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, FloorHits, length,
            settings.groundLayers, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = FloorHits;
        if (count == hits.Length)
        {
            hits = Physics.RaycastAll(origin, Vector3.down, length, settings.groundLayers, QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }
        float closest = float.PositiveInfinity;
        position = normal = default;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (Ignore(hit.collider, ignoredItem) || hit.normal.y < .6f ||
                Mathf.Abs(hit.point.y - navPoint.y) > settings.maximumSurfaceError || hit.distance >= closest) continue;
            closest = hit.distance;
            position = hit.point + hit.normal * settings.surfaceOffset;
            normal = hit.normal;
        }
        return closest < float.PositiveInfinity;
    }

    private static bool Ignore(Collider collider, Transform item) => collider == null ||
        (item != null && collider.transform.IsChildOf(item)) ||
        collider.GetComponentInParent<PlayerHealth>() != null;

    public static float Length(Vector3[] corners)
    {
        float total = 0;
        if (corners != null)
            for (int i = 1; i < corners.Length; i++) total += Vector3.Distance(corners[i - 1], corners[i]);
        return total;
    }
}
