using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ghosts belonging to ONE map. Create via:
/// Assets > Create > 13RoH > Map Ghost Roster.
///
/// Why this is a separate asset from DifficultyProfile
/// ---------------------------------------------------
/// Two different questions were previously answered by one number:
///
///   1. "Which ghosts is this cinema haunted by?"   -> a property of the MAP
///   2. "How much of the unknown gets added tonight?" -> a property of the DIFFICULTY
///
/// The cinema's own ghosts are part of what the cinema IS. Stripping them out on
/// Easy would not make an easier version of the map, it would make a different
/// map — players could never learn the building, and the tutorial value of a
/// haunting that behaves the same way every time would be gone. So guaranteed
/// ghosts live here, spawn on every difficulty, and are NOT counted against
/// DifficultyProfile.randomGhostCount.
///
/// The random pool is the opposite: it is what players cannot prepare for. With
/// a large pool and a difficulty-driven draw count, the same cinema produces a
/// different night every run, and Hard is not "the same ghosts but angrier" —
/// it is more unknowns in the building at once.
///
/// SERVER ONLY. The server draws the roster and spawns it; clients receive the
/// spawned NetworkIdentities. A client that drew its own roster would know
/// exactly which ghosts are in the building, which is the one thing this whole
/// structure exists to hide.
/// </summary>
[CreateAssetMenu(fileName = "GhostRoster_", menuName = "13RoH/Map Ghost Roster")]
public class MapGhostRoster : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Which map this roster belongs to. Must match RoomConfig.mapID — the same string the lobby advertises.")]
    public string mapID = RoomConfig.DemoMapID;

    [Header("Always present")]
    [Tooltip("The map's own ghosts. EVERY one of these spawns on EVERY difficulty, including Easy, and none of them count towards the difficulty's random draw.\n\nKeep this list short. These are the ghosts players are meant to learn — the ones that make this cinema recognisable. Two or three is usually enough.")]
    public List<GameObject> guaranteedGhosts = new List<GameObject>();

    [Header("Random pool")]
    [Tooltip("Candidates for the difficulty-driven draw. The server picks DifficultyProfile.randomGhostCount of these at random with no repeats.\n\nThis list wants to be LARGE. The number of possible nights is the number of ways to choose randomGhostCount entries from this pool, so every ghost added here multiplies the variety of every difficulty at once, on every map that references it.")]
    public List<GameObject> randomGhostPool = new List<GameObject>();

    /// <summary>
    /// Builds tonight's ghost list: every guaranteed ghost, plus <paramref name="randomCount"/>
    /// distinct picks from the random pool.
    ///
    /// SERVER ONLY. Pass a seed so a round can be reproduced exactly when
    /// reporting a bug — same seed, same ghosts, same night.
    /// </summary>
    public List<GameObject> BuildRosterFor(int randomCount, int seed)
    {
        var result = new List<GameObject>(guaranteedGhosts.Count + Mathf.Max(0, randomCount));

        // Guaranteed first, so the spawner can rely on a stable ordering when it
        // assigns them to the map's fixed haunt points.
        for (int i = 0; i < guaranteedGhosts.Count; i++)
        {
            if (guaranteedGhosts[i] != null) result.Add(guaranteedGhosts[i]);
        }

        if (randomCount <= 0 || randomGhostPool.Count == 0) return result;

        // Draw without repeats. A partial Fisher-Yates over a copy of the indices
        // rather than "pick a random index and retry if already used" — the retry
        // version runs unboundedly long once the draw approaches the pool size.
        var indices = new List<int>(randomGhostPool.Count);
        for (int i = 0; i < randomGhostPool.Count; i++)
        {
            if (randomGhostPool[i] != null) indices.Add(i);
        }

        var rng = new System.Random(seed);
        int draws = Mathf.Min(randomCount, indices.Count);

        for (int i = 0; i < draws; i++)
        {
            int swap = i + rng.Next(indices.Count - i);
            (indices[i], indices[swap]) = (indices[swap], indices[i]);
            result.Add(randomGhostPool[indices[i]]);
        }

        if (draws < randomCount)
        {
            Debug.LogWarning(
                $"[{name}] The difficulty asked for {randomCount} random ghosts but the pool only holds " +
                $"{indices.Count} usable entries, so tonight spawns {draws}. Add more ghosts to the pool " +
                "or lower randomGhostCount on the profile.", this);
        }

        return result;
    }

    /// <summary>Total ghosts in the building at a given difficulty, for HUD, logging and balance sanity checks.</summary>
    public int TotalGhostsFor(int randomCount) =>
        guaranteedGhosts.Count + Mathf.Min(Mathf.Max(0, randomCount), randomGhostPool.Count);
}
