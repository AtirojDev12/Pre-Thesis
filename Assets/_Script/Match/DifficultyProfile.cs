using UnityEngine;

/// <summary>
/// One asset per difficulty tier. This is the reason the project does NOT need a
/// separate scene per difficulty: the cinema is the same cinema in Easy and Hard,
/// so only these numbers change and the scene configures itself from them at
/// runtime.
///
/// Create via: Assets > Create > 13RoH > Difficulty Profile.
/// Make exactly three (Easy / Normal / Hard) and drop them into MatchDirector's
/// profile list in the GamePlay scene.
///
/// Design owns these values, not programming. Once the three assets exist, the
/// designer can rebalance the whole game from the Inspector without a programmer
/// touching anything and without a new build.
///
/// SERVER ONLY. Clients must never read a profile to compute their own state —
/// the server resolves the profile and replicates the *results* as SyncVars on
/// MatchDirector. A client that computed its own sanity drain could simply set
/// it to zero.
/// </summary>
[CreateAssetMenu(fileName = "Difficulty_", menuName = "13RoH/Difficulty Profile")]
public class DifficultyProfile : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Which tier this asset describes. MatchDirector finds its profile by matching this against the room's chosen difficulty.")]
    public DifficultyLevel level = DifficultyLevel.Normal;

    [Header("Objectives")]
    [Tooltip("How many area tasks the team must finish before the exits unlock.")]
    [Min(1)] public int tasksRequiredToOpenExits = 6;

    [Tooltip("Extra tasks required for every player beyond the first. 0 = a solo player and a full team of six face the same workload; raise it if six players clearing the map in three minutes feels wrong. Design decision — start at 0 and tune from playtests.")]
    [Min(0)] public int extraTasksPerAdditionalPlayer = 0;

    [Header("Clock")]
    [Tooltip("Real seconds per in-game hour. The GDD sets 150s (2:30) as the baseline, so Normal should stay at 150. A lower value makes the night pass faster, which is how difficulty shortens the round without changing the map.")]
    [Min(10f)] public float secondsPerInGameHour = 150f;

    [Header("Pressure")]
    [Tooltip("Multiplier on every sanity drain source — falling objects, haunting sounds, the flickering monitor, darkness. 1.0 is the authored baseline.")]
    [Min(0f)] public float sanityDrainMultiplier = 1f;

    [Tooltip("How many ghosts are active this round. The scene holds every ghost; the server enables only this many, so one scene covers all three tiers.")]
    [Min(0)] public int activeGhostCount = 2;

    [Header("Downed / revive")]
    [Tooltip("How long a downed player survives before true death, measured in IN-GAME hours. The GDD specifies 1 in-game hour, which at 150s/hour is 150 real seconds — that is the shipping value. PlayerHealth's downedDuration = 30f is a deliberate TEST value, kept short so the downed system can be exercised without waiting 2.5 minutes every run. Once PlayerHealth reads MatchDirector.DownedDurationSeconds, this profile becomes the single source and the test value goes away.")]
    [Min(0.05f)] public float downedDurationInGameHours = 1f;

    /// <summary>
    /// Real seconds a downed player gets, derived from the in-game hour figure so
    /// the two can never drift apart. Server-side use only.
    /// </summary>
    public float DownedDurationSeconds => downedDurationInGameHours * secondsPerInGameHour;

    /// <summary>
    /// Task requirement for a given headcount. Kept here rather than in
    /// MatchDirector so the whole difficulty curve lives in one asset a designer
    /// can read top to bottom.
    /// </summary>
    public int TasksRequiredFor(int playerCount)
    {
        int extraPlayers = Mathf.Max(0, playerCount - 1);
        return tasksRequiredToOpenExits + extraPlayers * extraTasksPerAdditionalPlayer;
    }
}
