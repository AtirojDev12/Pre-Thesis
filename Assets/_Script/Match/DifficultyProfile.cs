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
    // Every zone in the map has RULES. Only the six quest zones also carry
    // TASKS, and a quest zone's task is a cumulative target rather than a
    // checklist — the ticket booth is finished when the sales total is reached,
    // not when some list is ticked off.
    //
    // TWO SEPARATE COUNTERS. Do not merge them.
    //
    //   ZONES completed  -> survival. ALL of them, on every difficulty.
    //   TASKS completed  -> money.    Paid PER PLAYER, for their own work.
    //
    // Difficulty does NOT change how many zones the team owes. It changes two
    // other things: how many rules are in force, and how much work sits inside
    // each zone. Everything in this section is one of those two.
    //
    // The zones are a DEADLINE, not a key. The exits open at 06:00 and only at
    // 06:00, so finishing every zone at 02:00 still leaves the team locked in
    // until dawn. What finishing buys is the right to walk out when dawn comes.
    //
    // Failing to finish splits two ways at 06:00, and MatchPhase documents both:
    // with two or more players dead the night simply refuses to end (Overtime);
    // with fewer, everyone is sealed in to hunt the Ghost Key (LockedIn).

    // NOTE: there is deliberately NO "zones required" field any more.
    //
    // EVERY quest zone must be finished, on every difficulty. Changing the
    // difficulty does not change how many zones the team owes — it changes how
    // many RULES apply and how much WORK each zone holds. That is the whole
    // shape of the difficulty curve now, and putting a zone count back here
    // would quietly reintroduce a second, contradictory answer.
    //
    // MatchDirector learns the zone total from the zones themselves as they
    // register, so a map with eight quest zones needs no change here.

    [Header("Objectives — work per zone")]

    [Tooltip("How many of the map's THIRTEEN place rules (กฎสถานที่) are in force. Place rules apply inside EVERY zone of the map, quest zones and rule-only zones alike — they are what the game is named after.\n\nEasy 0, Normal 0, Hard = a random subset of this size drawn from the thirteen, 13 Rules = 13.\n\nThese are separate from a zone's own rules. A zone's rules come from the zone; these come from the map and are layered on top, so on Hard a player has to hold both in their head at once.\n\nRandomising WHICH rules appear on Hard is what stops players memorising one correct routine: the cinema is the same building every night, but the laws it runs on are not.")]
    [Range(0, 13)] public int placeRuleCount = 0;

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
    [Tooltip("How long the whole night lasts in REAL minutes.\n\nTEAM DECISION (24 Sep 2026): 15 minutes on EVERY tier. Do not vary this per difficulty. Difficulty is carried by rules, ghosts, sanity drain and task load, not by round length.\n\nThe night is always 00:00 -> 06:00, six in-game hours, on every map and every difficulty. 15 minutes over six hours is the GDD's 150 real seconds per in-game hour, so the downed timer and the escape window are both 150s on every tier.\n\nAuthored in minutes on purpose: the designer thinks in 'a 15-minute round', not in 'seconds per in-game hour'. MatchDirector derives the per-hour rate from this.")]
    [Min(1f)] public float roundLengthMinutes = 15f;

    // =======================================================================
    //  PRESSURE
    // =======================================================================

    [Header("Pressure")]
    [Tooltip("Scales EVERY sanity drain source in the game at once. 1.0 is the authored baseline — whatever each source is worth in the GDD, that is what a player loses at 1.0.\n\nThis is a coefficient, never a replacement. A source keeps its own authored value and its own trigger; this only decides how hard that value lands tonight. 0.5 = Easy, half as punishing. 2.0 = Hard, twice as punishing. 0 disables sanity entirely, which is useful for testing a zone's tasks without the horror layer fighting you.\n\nThe server resolves this once at round start and replicates it, so all six machines drain at the same rate. A client that owned this number could set it to 0 and become immune.")]
    [Min(0f)] public float sanityDrainMultiplier = 1f;

    [Tooltip("How many ghosts the server draws AT RANDOM from the map's random pool.\n\nThis number does NOT include the map's guaranteed ghosts. Those live on the map's MapGhostRoster asset and appear on every difficulty, because they are part of what that cinema IS — remove them on Easy and Easy becomes a different building. Difficulty only decides how many unknowns get added on top.\n\nSo a map with 2 guaranteed ghosts running a profile with randomGhostCount = 3 spawns 5 ghosts: the 2 the players can learn, plus 3 they cannot predict.")]
    [Min(0)] public int randomGhostCount = 2;

    // =======================================================================
    //  GHOST CYCLE (GhostManager)
    // =======================================================================
    // GhostManager's lights-flicker -> countdown -> ghost -> despawn loop.
    // Defaults are the old hard-coded Normal values. Placeholder numbers per
    // tier until Game Design tunes them.

    [Header("Ghost cycle (GhostManager)")]
    [Tooltip("Seconds between the end of one ghost visit and the lights starting to flicker for the next. Lower = more visits per night.\n\nSuggested: Easy 90, Normal 60, Hard 45, 13 Rules 35.")]
    [Min(5f)] public float ghostSpawnIntervalSeconds = 60f;

    [Tooltip("Warning time after the flicker before the ghost appears. Lower = less time to hide.\n\nSuggested: Easy 12, Normal 10, Hard 7, 13 Rules 5.")]
    [Min(0f)] public float ghostWarningSeconds = 10f;

    [Tooltip("How long the ghost stays in the building each visit.\n\nSuggested: Easy 12, Normal 15, Hard 20, 13 Rules 25.")]
    [Min(1f)] public float ghostActiveSeconds = 15f;

    // NOTE: there is deliberately no downed-duration knob here. A downed player
    // survives exactly ONE in-game hour on every difficulty — a rule of the
    // world, not a balance value — so it lives as a constant on MatchDirector
    // and is derived from the clock. Difficulty decides how easily you go down,
    // through the sanity drain and the ghost count above. It never decides how
    // long you last once you have.
    //
    // No knob is needed because the derived value already self-balances:
    //
    //   Every tier: 15 min -> 150s per in-game hour -> a 150s revive window
    //   (team decision 24 Sep 2026: the round length is 15 min on all tiers)
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
        if (level == DifficultyLevel.ThirteenRules && placeRuleCount != 13)
        {
            Debug.LogWarning(
                $"[{name}] is the ThirteenRules tier but placeRuleCount is {placeRuleCount}. " +
                "That mode is defined by having all thirteen in force at once — set it to 13.", this);
        }

        if ((level == DifficultyLevel.Easy || level == DifficultyLevel.Normal) && placeRuleCount != 0)
        {
            Debug.LogWarning(
                $"[{name}] is {level} but has {placeRuleCount} place rules. Easy and Normal carry " +
                "zone rules only — place rules start at Hard.", this);
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
