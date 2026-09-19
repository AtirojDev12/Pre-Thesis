using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// One place to ask "who is playing right now".
///
/// WHY THIS EXISTS
/// ---------------
/// The pattern it replaces is GameObject.FindGameObjectWithTag("Player") called
/// once in Start. That works perfectly in a solo test scene and fails silently
/// in a real match, in two different ways:
///
///   1. It returns ONE player. With six in the building, a ghost using it will
///      chase whichever one Unity happened to hand back and completely ignore
///      the other five. No error, no warning — it just looks stupid.
///
///   2. It runs once. Anyone who spawns later (Mirror spawns players as they
///      connect, and the offline spawner runs a frame or two in) is invisible
///      to anything that already cached its target.
///
/// So nothing in this project should look up players by tag. Ask here instead.
///
/// The list is rebuilt on a slow tick rather than wired into PlayerHealth,
/// because PlayerHealth has no server-side spawn/death event to subscribe to
/// and adding one would mean editing a file two people are working in. At six
/// players the cost is irrelevant.
/// </summary>
public static class PlayerRegistry
{
    private static readonly List<PlayerHealth> cache = new List<PlayerHealth>();
    private static float nextRefreshTime;

    /// <summary>How often the list is rebuilt. Players do not join mid-round, so this is generous.</summary>
    private const float RefreshInterval = 0.5f;

    /// <summary>
    /// Every player currently in the map, dead or alive. The returned list is
    /// the shared cache — read it, do not hold or modify it.
    /// </summary>
    public static IReadOnlyList<PlayerHealth> All
    {
        get
        {
            Refresh();
            return cache;
        }
    }

    /// <summary>
    /// The closest LIVING player to a point, or null if everyone is dead or gone.
    /// Downed players still count as living — they are still in the building and
    /// a ghost standing over a downed teammate is exactly the pressure the
    /// revive rule is built around.
    /// </summary>
    public static PlayerHealth ClosestTo(Vector3 position, float maxDistance = float.MaxValue)
    {
        Refresh();

        PlayerHealth best = null;
        float bestSqr = maxDistance * maxDistance;

        for (int i = 0; i < cache.Count; i++)
        {
            PlayerHealth candidate = cache[i];
            if (candidate == null || candidate.IsDead) continue;

            float sqr = (candidate.transform.position - position).sqrMagnitude;
            if (sqr > bestSqr) continue;

            bestSqr = sqr;
            best = candidate;
        }

        return best;
    }

    /// <summary>
    /// The closest living player that also passes a caller-supplied test — line
    /// of sight, a view cone, whatever the asking system cares about.
    ///
    /// Enemies use this rather than ClosestTo so that a player hiding behind a
    /// wall does not get picked as the target just for being nearest.
    /// </summary>
    public static PlayerHealth ClosestVisibleTo(
        Vector3 position, float maxDistance, System.Func<PlayerHealth, bool> canSee)
    {
        Refresh();

        PlayerHealth best = null;
        float bestSqr = maxDistance * maxDistance;

        for (int i = 0; i < cache.Count; i++)
        {
            PlayerHealth candidate = cache[i];
            if (candidate == null || candidate.IsDead) continue;

            float sqr = (candidate.transform.position - position).sqrMagnitude;
            if (sqr > bestSqr) continue;
            if (canSee != null && !canSee(candidate)) continue;

            bestSqr = sqr;
            best = candidate;
        }

        return best;
    }

    /// <summary>Forces the next query to rebuild. Call after spawning a player if you need it visible immediately.</summary>
    public static void Invalidate() => nextRefreshTime = 0f;

    private static void Refresh()
    {
        if (Time.unscaledTime < nextRefreshTime && cache.Count > 0) return;
        nextRefreshTime = Time.unscaledTime + RefreshInterval;

        cache.Clear();

        if (NetworkServer.active)
        {
            // Authoritative source in a match: the connections Mirror is holding.
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
            {
                if (conn == null || conn.identity == null) continue;

                PlayerHealth health = conn.identity.GetComponent<PlayerHealth>();
                if (health != null) cache.Add(health);
            }

            return;
        }

        // Offline sandbox, or a client asking for something cosmetic. There are
        // no connections, but there is a player in the scene.
        cache.AddRange(Object.FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None));
    }

    // Statics survive between Play sessions when Unity 6's domain reload is
    // disabled, which would otherwise leave last run's destroyed players here.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cache.Clear();
        nextRefreshTime = 0f;
    }
}
