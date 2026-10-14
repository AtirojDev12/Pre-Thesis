using UnityEngine;

/// <summary>
/// 14 Oct 2026 (Mr.k). Lets the F1 dev panel summon a ghost. Every ghost
/// spawner / manager implements it, so new ghosts show up in the panel by
/// themselves. Both methods are SERVER ONLY; requester = the body of whoever
/// pressed the button (can be null).
///   - ServerDevSummonNow: the ghost appears / attacks now, skipping its trigger.
///   - ServerDevFireTrigger: its trigger happens ONCE (a count trigger counts 1,
///     a zone+time trigger counts as "entered the zone at the time", a timer
///     trigger counts as "the time has come"). The ghost then follows its own rules.
/// </summary>
public interface IDevGhostSource
{
    string DevGhostName { get; }
    string DevTriggerText { get; }
    void ServerDevSummonNow(GameObject requester);
    void ServerDevFireTrigger(GameObject requester);
}
