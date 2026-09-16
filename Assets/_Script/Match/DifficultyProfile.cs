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
    /// <summary>
    /// Quest zones in the first map. Six: ticket &amp; snack counter, housekeeping,
    /// projection room, ticket check, staff room, electrical room. The other
    /// three zones (corridors, toilets, auditorium) carry rules but no tasks —
    /// EVERY zone has rules, only these six have tasks.
    ///
    /// Here purely so OnValidate can catch a zone requirement larger than the
    /// map. When a second map exists this moves onto a per-map asset.
    /// </summary>
    public const int QuestZonesInCinema = 6;

    [Header("Identity")]
    [Tooltip("Which tier this asset describes. MatchDirector finds its profile by matching this against the room's chosen difficulty.")]
    public DifficultyLevel level = DifficultyLevel.Normal;

    // =======================================================================
    //  OBJECTIVES
    // =======================================================================
    // A map holds far more tasks than any team will finish. Every zone has
    // Rules; only main zones carry Tasks, and a main zone carries several
    // (the ticket booth alone: sell 3 to customers, sell 5 to ghosts, plus the
    // popcorn and drink tasks that share the counter).
    //
    // TWO SEPARATE COUNTERS. Do not merge them.
    //
    //   ZONES completed  -> survival. Whether the team may leave at all.
    //   TASKS completed  -> money.    Paid PER PLAYER, for their own work.
    //
    // A task is one-shot: "sell three tickets" is one task, not three. Six quest
    // zones exist, each holding a handful of tasks, and a zone is complete only
    // when every task in it is done.
    //
    // Counting survival in zones rather than tasks is what keeps the map
    // buildable — a six-player Hard round needs four finished rooms, not eighty
    // finished chores — and it makes the objective something players can say out
    // loud. It also gives the zone-completion sound a precise meaning: every
    // chime the team hears across the cinema is one of the rooms they need.
    //
    // Method 1 is "finish the minimum BEFORE the gate opens" — the gate opens at
    // 06:00 and only at 06:00, so this is a DEADLINE the team races, never a
    // door it unlocks. A team that finishes every zone on the map at 02:00 is
    // still locked in until dawn.
    //
    // The deadline has teeth in both directions. Meeting it early buys nothing
    // except the right to leave; MISSING it means nobody may cross a gate, and
    // Method 2 is the team's only survival. A life-or-death threshold, not a
    // bonus objective.

    [Header("Objectives")]
    [Tooltip("How many quest ZONES the team must FULLY complete before 06:00. The cinema has six (ticket & snack counter, housekeeping, projection room, ticket check, staff room, electrical room), so this is a fraction of the map, not a task count.\n\nEasy 2 / Normal 3 / Hard 4 of 6.\n\nA zone counts only when every task in it is done. Half a zone is worth nothing toward survival — though its finished tasks are still money — so choosing which zones to take is a commitment the team has to live with.\n\nThis does NOT scale with player count. More players do not mean more zones; they mean the zones get bigger (see below). The map's shape stays the same, so the team can always say 'we need three' and mean it.\n\nThis is the survival condition, not a score. Missing it seals the team in: at 06:00 nobody may cross a gate, and Method 2 — dealing with the main ghost — becomes the only way anyone lives. Fail both and everyone inside dies.")]
    [Min(1)] public int zonesRequiredToClear = 3;

    [Tooltip("Extra tasks added to EVERY quest zone on this difficulty, regardless of team size.\n\nThis is how a tier makes the work itself heavier rather than just more dangerous. A room that holds four tasks on Easy can hold six on Hard — same room, more to do in it.\n\nIt is the ONLY task lever that reaches a solo player: extraTasksPerAdditionalPlayer is zero at one player by definition, so without this a lone player faces identical zone contents on Easy and on Hard, and only the ghosts and the drain differ.\n\nSuggested: Easy 0, Normal 1, Hard 2. Tune from playtests.")]
    [Min(0)] public int flatExtraTasksPerZone = 0;

    [Tooltip("Extra TASKS added to the ROUND for every player past the first — not extra zones.\n\nThe extras are spread across the map unpredictably. They might all land in one zone, or scatter across several, and the team cannot know which in advance. A zone that took four tasks last run might take nine this time.\n\nWhat this buys: the team cannot pre-plan 'I'll take the ticket booth, it's the quick one'. They commit to a zone, read the quest paper on the wall, and only then find out how deep it goes. Whoever finishes light has to go help whoever is drowning, which is a coordination problem that only appears at larger team sizes — exactly where the round would otherwise get easy.\n\nSo bigger teams face more work without the objective ever changing shape.\n\nSuggested: Easy 1, Normal 2, Hard 3 per additional player. Tune from playtests.")]
    [Min(0)] public int extraTasksPerAdditionalPlayer = 2;

    [Tooltip("The most RANDOM extras any single zone may receive (flatExtraTasksPerZone is not capped — it applies to every zone by design).\n\nIt stops the spread dumping everything into one room and making it unfinishable before dawn. But set it too LOW and the opposite failure appears: the extras no longer fit anywhere except spread evenly, and 'which room is the bad one' becomes 'all of them are'.\n\nSix zones x this cap = total the map can absorb. Compare against the extras a full lobby generates (5 x extraTasksPerAdditionalPlayer):\n\n  cap 4  -> 24 slots. 20 extras must fill 5 of 6 zones. Nearly uniform.\n  cap 6  -> 36 slots. 20 extras need only 4 zones.\n  cap 8  -> 48 slots. 20 extras fit in 3 zones — half the map can stay light.\n\nThe surprise lives in the INEQUALITY between zones, not the total. If raising the cap makes single zones too big to finish, lower extraTasksPerAdditionalPlayer instead — fewer extras concentrate more sharply and keep zones a sane size.\n\n0 means no cap. Do not ship with 0.")]
    [Min(0)] public int maxExtraTasksPerZone = 4;

    // =======================================================================
    //  CLOCK
    // =======================================================================

    [Header("Clock")]
    [Tooltip("How long the whole night lasts in REAL minutes. Easy = 10, Normal = 15, Hard = 20.\n\nHarder means LONGER, not shorter. The round length is not a time limit to race — it is how long the team has to stay inside the haunted building. A harder night is a bigger night: more tasks to clear before the exits unlock, more ghosts loose in the cinema, and more real minutes of exposure to both. Ten minutes on Easy is mercy; twenty on Hard is the punishment.\n\nThe night is always 00:00 -> 06:00, six in-game hours, on every map and every difficulty. So this value alone sets the pace of the clock — 15 minutes over six hours is the GDD's 150 real seconds per in-game hour.\n\nAuthored in minutes on purpose: the designer thinks in 'a 15-minute round', not in 'seconds per in-game hour'. MatchDirector derives the per-hour rate from this.\n\nRaise the task counts above whenever this goes up, or the extra minutes become dead time instead of pressure.")]
    [Min(1f)] public float roundLengthMinutes = 15f;

    // =======================================================================
    //  PRESSURE
    // =======================================================================

    [Header("Pressure")]
    [Tooltip("Scales EVERY sanity drain source in the game at once. 1.0 is the authored baseline — whatever each source is worth in the GDD, that is what a player loses at 1.0.\n\nThis is a coefficient, never a replacement. A source keeps its own authored value and its own trigger; this only decides how hard that value lands tonight. 0.5 = Easy, half as punishing. 2.0 = Hard, twice as punishing. 0 disables sanity entirely, which is useful for testing a zone's tasks without the horror layer fighting you.\n\nThe server resolves this once at round start and replicates it, so all six machines drain at the same rate. A client that owned this number could set it to 0 and become immune.")]
    [Min(0f)] public float sanityDrainMultiplier = 1f;

    [Tooltip("How many ghosts the server draws AT RANDOM from the map's random pool.\n\nThis number does NOT include the map's guaranteed ghosts. Those live on the map's MapGhostRoster asset and appear on every difficulty, because they are part of what that cinema IS — remove them on Easy and Easy becomes a different building. Difficulty only decides how many unknowns get added on top.\n\nSo a map with 2 guaranteed ghosts running a profile with randomGhostCount = 3 spawns 5 ghosts: the 2 the players can learn, plus 3 they cannot predict.")]
    [Min(0)] public int randomGhostCount = 2;

    // NOTE: there is deliberately no downed-duration knob here. A downed player
    // survives exactly ONE in-game hour on every difficulty — a rule of the
    // world, not a balance value — so it lives as a constant on MatchDirector
    // and is derived from the clock. Difficulty decides how easily you go down,
    // through the sanity drain and the ghost count above. It never decides how
    // long you last once you have.
    //
    // No knob is needed because the derived value already self-balances:
    //
    //   Easy   10 min -> 100s per in-game hour -> a 100s revive window
    //   Normal 15 min -> 150s                  -> 150s
    //   Hard   20 min -> 200s                  -> 200s
    //
    // The ESCAPE WINDOW after 06:00 is one in-game hour too, so those two
    // numbers are always identical. If a teammate goes down during the escape,
    // the time to revive them and the time to get yourself out are exactly the
    // same length — saving them costs precisely the margin you need to save
    // yourself. That is the cruellest decision in the round, and it stays
    // perfectly balanced at every tier because both sides derive from one clock.
    //
    // On Easy, going down at all is rare, so a short window costs the team
    // almost nothing. On Hard players go down constantly — and that is exactly
    // where the window is longest, so a downed teammate across the cinema is
    // still reachable. One rule, expressed once, and it lands correctly at every
    // tier without anyone tuning it.

    // =======================================================================
    //  DERIVED
    // =======================================================================

    /// <summary>Total real seconds in the night. Server-side use only.</summary>
    public float RoundLengthSeconds => roundLengthMinutes * 60f;

    /// <summary>
    /// How many EXTRA tasks this round sprinkles across the map for the team
    /// size. Solo returns 0 — a lone player faces the map's authored baseline,
    /// and every additional player inflates it.
    ///
    /// The quest system decides where they land, subject to
    /// <see cref="maxExtraTasksPerZone"/>; this asset only decides how many.
    /// </summary>
    public int ExtraTasksFor(int playerCount)
    {
        int extraPlayers = Mathf.Max(0, playerCount - 1);
        return extraPlayers * extraTasksPerAdditionalPlayer;
    }

    /// <summary>
    /// Total tasks a zone holds tonight, before any random extras land in it:
    /// the zone's own authored list plus this tier's flat addition.
    ///
    /// The quest system calls this when building each zone, so difficulty
    /// changes zone SIZE without anyone authoring three versions of every room.
    /// </summary>
    public int ZoneTaskCount(int authoredTasksInZone) =>
        Mathf.Max(0, authoredTasksInZone) + flatExtraTasksPerZone;

    /// <summary>
    /// How many random extras the whole map can absorb at this cap. Compare
    /// against ExtraTasksFor(6): if they are close, the spread is forced to be
    /// near-uniform and the "which room is heavy" surprise disappears.
    /// </summary>
    public int RandomExtraCapacity(int questZonesInMap) =>
        maxExtraTasksPerZone <= 0 ? int.MaxValue : questZonesInMap * maxExtraTasksPerZone;

#if UNITY_EDITOR
    private void OnValidate()
    {
        // A silent balance mistake here costs a whole playtest, so surface it
        // while the designer is still looking at the asset.
        if (zonesRequiredToClear > QuestZonesInCinema)
        {
            Debug.LogWarning(
                $"[{name}] zonesRequiredToClear is {zonesRequiredToClear}, but the cinema only has " +
                $"{QuestZonesInCinema} quest zones. This round is unwinnable — nobody can ever cross a gate.", this);
        }

        if (maxExtraTasksPerZone == 0 && extraTasksPerAdditionalPlayer > 0)
        {
            Debug.LogWarning(
                $"[{name}] maxExtraTasksPerZone is 0 (no cap) while extra tasks are enabled. " +
                "A full lobby's extras can all land in one room and make it impossible to finish before dawn. " +
                "Set a cap.", this);
        }

        // The opposite failure: the cap is so tight that a full lobby's extras
        // have nowhere to go but evenly, and every room ends up equally heavy.
        int fullLobbyExtras = ExtraTasksFor(RoomConfig.MaxPlayers);
        int capacity = RandomExtraCapacity(QuestZonesInCinema);

        if (maxExtraTasksPerZone > 0 && fullLobbyExtras > capacity * 0.75f)
        {
            int zonesForced = Mathf.CeilToInt(fullLobbyExtras / (float)maxExtraTasksPerZone);

            Debug.LogWarning(
                $"[{name}] A full lobby generates {fullLobbyExtras} random extras but the map can only hold " +
                $"{capacity} at this cap, so at least {zonesForced} of {QuestZonesInCinema} zones must be loaded — " +
                "close to uniform. The surprise lives in the inequality between zones, so either raise " +
                "maxExtraTasksPerZone or lower extraTasksPerAdditionalPlayer.", this);
        }
    }
#endif
}
