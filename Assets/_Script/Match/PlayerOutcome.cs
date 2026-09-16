/// <summary>
/// How one player finished the round. The server decides this; the end-of-round
/// screen and the currency payout read it.
///
/// Three of these four are wins. Escaping through a gate and surviving inside a
/// map whose ghost was dealt with are worth the same — the GDD is explicit that
/// anyone still alive inside when the ghost is resolved counts as a winner just
/// like anyone who walked out. The performers get more, and only the performers.
/// </summary>
public enum PlayerOutcome
{
    /// <summary>Still playing, or the round has not resolved them yet.</summary>
    Unresolved = 0,

    /// <summary>Method 1 — walked out through an exit before 07:00. Paid for tasks completed.</summary>
    EscapedThroughGate = 1,

    /// <summary>
    /// Method 2, as a bystander — alive inside when the ghost was dealt with.
    /// A full win, paid for tasks, but no ritual bonus: they did not do it.
    /// </summary>
    SurvivedTheGhost = 2,

    /// <summary>
    /// Method 2, as a participant — helped execute the map's hidden method for
    /// dealing with the ghost. Paid for tasks PLUS the ritual bonus.
    /// </summary>
    PerformedTheRitual = 3,

    /// <summary>
    /// Died in the map — from damage, from a downed timer running out, or from
    /// still being inside at 07:00 with the ghost unresolved. No money, items
    /// lost. Dawn does not care which of those it was.
    /// </summary>
    Dead = 4,
}
