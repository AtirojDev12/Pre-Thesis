/// <summary>
/// Difficulty tiers selectable at Room Creation.
///
/// These are stored by NAME wherever they cross the network or save boundary
/// (EOS lobby attributes, SaveData, Mirror sync state) — never by raw ordinal.
/// If a difficulty is inserted or removed later (thesis build, more tiers),
/// existing saves and any in-flight lobby attribute strings stay correct instead
/// of silently reinterpreting as the wrong tier.
/// </summary>
public enum DifficultyLevel
{
    /// <summary>Fewest zone rules, least work per zone. No place rules.</summary>
    Easy,

    /// <summary>More zone rules and more work than Easy. Still no place rules.</summary>
    Normal,

    /// <summary>
    /// Same zone rules as Normal, more work than Normal, plus a RANDOM SUBSET of
    /// the map's 13 place rules — see DifficultyProfile.placeRuleCount.
    /// </summary>
    Hard,

    /// <summary>
    /// Everything Hard has, but all thirteen place rules are in force at once.
    /// The mode the game is named after.
    ///
    /// Added after Hard on purpose: the enum is stored by NAME everywhere it
    /// crosses the network or save boundary, so appending is safe, but keeping
    /// the order Easy -> Normal -> Hard -> ThirteenRules also means anything
    /// that sorts or compares by ordinal still reads as increasing difficulty.
    /// </summary>
    ThirteenRules
}
