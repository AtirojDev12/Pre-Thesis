/// <summary>
/// The stages of one round.
///
/// The night runs 00:00 -> 06:00. What happens AT 06:00 depends on two things:
/// whether every quest zone is finished, and how many players have died.
///
///                         all zones done?
///                        /               \
///                     yes                 no
///                      |                   |
///                   ESCAPE          2+ players dead?
///                                   /             \
///                                yes               no
///                                 |                 |
///                             OVERTIME           LOCKED_IN
///                          (finish, then        (Ghost Key or
///                            ESCAPE)                 die)
/// </summary>
public enum MatchPhase
{
    /// <summary>00:00 -> 06:00. Exits locked. The team works the zones against a deadline it cannot move.</summary>
    Night = 0,

    /// <summary>
    /// 06:00 -> 07:00. Exits open, two people per exit, one in-game hour to get
    /// out. Reached only by finishing every quest zone.
    /// </summary>
    Escape = 1,

    /// <summary>
    /// Zones unfinished at 06:00 with two or more players dead.
    ///
    /// The main clock FREEZES at 06:00 — dawn simply does not arrive. A second
    /// clock starts counting up instead, and the ghosts grow more aggressive the
    /// longer it runs. There is no deadline; there is only the cost of every
    /// extra minute. Finishing the zones unfreezes the main clock and moves to
    /// Escape.
    /// </summary>
    Overtime = 2,

    /// <summary>
    /// Zones unfinished at 06:00 with fewer than two players dead.
    ///
    /// Everyone is sealed in for one in-game hour. The only survival is the
    /// Ghost Key and the one secret room that is really open tonight. Ghosts
    /// hunt harder throughout. Anyone not inside that room when the hour ends
    /// dies.
    /// </summary>
    LockedIn = 3,

    /// <summary>The round is resolved. Nobody is still playing.</summary>
    Ended = 4,
}
