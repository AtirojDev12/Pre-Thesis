using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 17 Nov 2026 (Mr.k, Task 5): the mops glow through walls (same DBD-style aura as the projector)
/// shortly before each cleaning time: 75 s before, for 5 s (FilmMopTuning). Cleaning is a pure task,
/// so nothing happens if it is missed.
///
/// Local only, no network: every PC reads the same match clock and the same chore schedule.
/// Made by itself when the game starts (no scene object needed).
/// </summary>
public sealed class MopGlow : MonoBehaviour
{
    private readonly List<int> cleaningTimes = new List<int>();
    private readonly Dictionary<CarryProp, SeeThroughGlow> glows = new Dictionary<CarryProp, SeeThroughGlow>();
    private readonly List<CarryProp> stale = new List<CarryProp>();
    private int knownChores = -1;
    private float nextCheck;
    private bool active;
    private bool logGlow;
    private readonly List<int> glowDone = new List<int>(); // cleaning times already warned about
    private float glowEnd;
    private MatchDirector seenMatch;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (FindAnyObjectByType<MopGlow>() != null) return;
        var go = new GameObject("Mop Glow (local)");
        DontDestroyOnLoad(go);
        go.AddComponent<MopGlow>();
    }

    private void Update()
    {
        FilmMopTuning tuning = FilmMopTuning.Current;
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.1f;
            active = tuning.mopsGlow && GlowTimeNow(tuning);
            if (logGlow != active) { logGlow = active; if (active) Debug.Log("[MopGlow] Cleaning soon: the mops glow for " + tuning.glowSeconds + " s."); }
            // Mops from an unloaded scene are gone: forget them.
            if (glows.Count > 0)
            {
                stale.Clear();
                foreach (CarryProp key in glows.Keys) if (key == null) stale.Add(key);
                foreach (CarryProp key in stale) glows.Remove(key);
            }
        }

        foreach (CarryProp prop in CarryProp.All)
        {
            if (prop == null || prop.CarryId != CarryProp.Mop) continue;
            bool mine = prop.IsCarried && CarryProp.LocalHandProp == prop; // not over your own view
            bool show = active && !mine;
            if (!glows.TryGetValue(prop, out SeeThroughGlow glow))
            {
                if (!show) continue;
                glow = SeeThroughGlow.Build(new[] { prop.transform }, tuning.glowColor);
                glows[prop] = glow;
            }
            glow?.SetVisible(show, tuning.glowColor, 0.7f + 0.3f * Mathf.Sin(Time.time * 6f));
        }
    }

    /// <summary>
    /// 17 Nov fix: the same way as the projector: the glow starts the FIRST moment we are within
    /// glowSecondsBefore of a cleaning time (also right at the start of the night if the time is that
    /// close), then lasts glowSeconds. Before, it needed an exact 5 s window and could be missed.
    /// </summary>
    private bool GlowTimeNow(FilmMopTuning tuning)
    {
        MatchDirector match = MatchDirector.Instance;
        if (match == null || !match.RoundRunning) return false;
        if (match != seenMatch) { seenMatch = match; glowDone.Clear(); glowEnd = 0f; knownChores = -1; }
        if (knownChores != ZoneChoreSpot.All.Count) CollectTimes();
        foreach (int time in cleaningTimes)
        {
            if (glowDone.Contains(time)) continue;
            float seconds = FilmMopTuning.SecondsUntil(match, time);
            if (seconds > tuning.glowSecondsBefore || seconds < -1f) continue;
            glowDone.Add(time);
            glowEnd = Time.time + tuning.glowSeconds;
        }
        return Time.time < glowEnd;
    }

    private void CollectTimes()
    {
        knownChores = ZoneChoreSpot.All.Count;
        cleaningTimes.Clear();
        foreach (ZoneChoreSpot spot in ZoneChoreSpot.All)
        {
            if (spot == null || spot.NeedsItem != CarryProp.Mop) continue;
            foreach (int time in spot.ScheduleMinutes())
                if (!cleaningTimes.Contains(time)) cleaningTimes.Add(time);
        }
    }

    private void OnDestroy()
    {
        foreach (SeeThroughGlow glow in glows.Values) glow?.Destroy();
        glows.Clear();
    }
}
