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
    Easy,
    Normal,
    Hard
}
