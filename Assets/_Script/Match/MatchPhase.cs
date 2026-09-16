/// <summary>
/// The two halves of a round, plus the terminal state.
///
/// The split matters because the exits behave in opposite ways either side of
/// 06:00. During Night they are locked no matter how much work the team has
/// done — finishing early never opens a door. During Escape they are open no
/// matter how little work the team has done, and the only thing that is scarce
/// is time and door capacity.
/// </summary>
public enum MatchPhase
{
    /// <summary>00:00 -> 06:00. Exits locked. The team works tasks against a deadline it cannot move.</summary>
    Night = 0,

    /// <summary>06:00 -> 07:00. Exits open, two people per exit, one in-game hour to get out or finish the ritual.</summary>
    Escape = 1,

    /// <summary>07:00. Anyone still inside is out of time.</summary>
    Ended = 2,
}
