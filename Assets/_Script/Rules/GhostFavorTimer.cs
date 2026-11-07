using System.Collections;
using Mirror;
using UnityEngine;

/// <summary>
/// 7 Nov 2026 (Mr.k): Ticket Zone rule 3 "The missing bucket" on its OWN trigger, so it is
/// not chained to rule 4 (the timed ghost). Difficulty can later turn either rule off without
/// losing the other.
///
///   NO timed ghost in this match: every 2 minutes (real time) the lights flicker, and 10 s
///     later the Ticket Zone lights CUT OUT (zone switch off) and the special ingredient
///     bucket vanishes. Players follow its trail, bring it back, and switch the lights on.
///   Timed ghost in this match (GhostManager active): this timer does nothing. The bucket
///     vanishes each time the ghost arrives (GhostManager: short 0.3 s cut, lights stay as left).
///   Always: switching the Ticket Zone lights off also makes it vanish (GhostFavorRecovery).
///
/// Server / offline only. Builds itself at start-up; no scene setup. Light zone = the
/// BuildingLightManager zone around the bucket's home (TicketAndPopcorn in Cinema).
/// </summary>
public sealed class GhostFavorTimer : MonoBehaviour
{
    /// <summary>Real seconds between two vanish events (warning flicker starts).</summary>
    public const float IntervalSeconds = 120f;
    /// <summary>Seconds from the flicker to the light cut + vanish.</summary>
    public const float WarningSeconds = 10f;

    private static GhostFavorTimer instance;
    private float timer;
    private bool running;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null || Application.isBatchMode) return;
        var go = new GameObject("[Ghost Favor Timer]") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        instance = go.AddComponent<GhostFavorTimer>();
    }

    private static bool HasAuthority => !NetworkMode.SessionEnding && (NetworkServer.active || NetworkMode.IsOffline);

    /// <summary>The timed ghost (rule 4) runs in this match: it owns the vanish timing instead.</summary>
    public static bool TimedGhostInMatch => GhostManager.Instance != null && GhostManager.Instance.isActiveAndEnabled;

    private void Update()
    {
        GhostFavorRecovery favor = GhostFavorRecovery.Instance;
        MatchDirector match = MatchDirector.Instance;
        bool active = HasAuthority && favor != null && match != null && match.RoundRunning && !TimedGhostInMatch;
        if (!active)
        {
            timer = 0f;
            return;
        }
        if (running) return;
        timer += Time.deltaTime;
        if (timer >= IntervalSeconds)
        {
            timer = 0f;
            StartCoroutine(VanishSequence(favor));
        }
    }

    private IEnumerator VanishSequence(GhostFavorRecovery favor)
    {
        running = true;
        BuildingLightManager power = BuildingLightManager.Instance;
        string zone = ZoneAround(power, favor.HomePosition);

        // Warning: lights on (power + zone), flicker, stay on.
        if (power != null)
        {
            power.SetLights(true);
            if (zone != null) power.SetZoneLights(zone, true);
            power.SetLights(false); yield return new WaitForSeconds(0.2f);
            power.SetLights(true);  yield return new WaitForSeconds(0.3f);
            power.SetLights(false); yield return new WaitForSeconds(0.2f);
            power.SetLights(true);  yield return new WaitForSeconds(0.3f);
            power.SetLights(false); yield return new WaitForSeconds(0.1f);
            power.SetLights(true);
        }
        else yield return new WaitForSeconds(1.1f);

        yield return new WaitForSeconds(WarningSeconds);

        // Cut: the Ticket Zone lights go out (players switch them back on), the bucket vanishes.
        if (TimedGhostInMatch || NetworkMode.SessionEnding) { running = false; yield break; }
        if (power != null && zone != null) power.SetZoneLights(zone, false);
        if (GhostFavorRecovery.Instance != null) GhostFavorRecovery.Instance.ServerOnGhostAppeared();
        Debug.Log("[GhostFavorTimer] Lights cut — the special ingredient vanished (no timed ghost in this match).");
        running = false;
    }

    /// <summary>The smallest light zone that holds this point, or null.</summary>
    private static string ZoneAround(BuildingLightManager power, Vector3 point)
    {
        if (power == null || power.Zones == null) return null;
        string best = null;
        float smallest = float.PositiveInfinity;
        foreach (BuildingLightManager.LightZone z in power.Zones)
        {
            if (z == null || string.IsNullOrWhiteSpace(z.zoneId) || !z.ContainsPosition(point)) continue;
            float volume = z.areaSize.x * z.areaSize.y * z.areaSize.z;
            if (volume < smallest) { smallest = volume; best = z.zoneId; }
        }
        return best;
    }
}
