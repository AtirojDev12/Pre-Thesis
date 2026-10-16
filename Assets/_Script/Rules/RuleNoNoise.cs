using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 16 Oct 2026 (Mr.k) — SCREEN ROOM rule 1 "No noise" (ห้ามส่งเสียง).
/// Inside the Ghost Screen Room, do not make noise over the limit
/// (talking loudly into the mic, sprinting, a loud radio...).
/// Punishment: a big sanity drop at once.
///
/// Uses the loudness the server already has for every player (PlayerNoise:
/// mic + footsteps + radio, 0..1). Walking = 0.3, sprinting = 0.6, crouch-walk = 0.
/// </summary>
public sealed class RuleNoNoise : ZoneRuleBase
{
    [Header("No noise")]
    [Tooltip("Loudness over this breaks the rule (0..1). Walk = 0.3, sprint = 0.6, loud talking ~0.5+.")]
    [SerializeField, Range(0.05f, 1f)] private float loudLimit = 0.5f;
    [Tooltip("Sanity lost each time.")]
    [SerializeField, Min(0f)] private float sanityLoss = 20f;
    [Tooltip("Seconds before the same player can be punished again.")]
    [SerializeField, Min(0.5f)] private float cooldownSeconds = 4f;

    private readonly Dictionary<PlayerHealth, float> nextAllowed = new Dictionary<PlayerHealth, float>();
    private float tick;

    private void Update()
    {
        if (!IsServerSide || !ServerRuleLive) return;
        tick += Time.deltaTime;
        if (tick < 0.2f) return;
        tick = 0f;

        List<PlayerHealth> players = ServerPlayersInside();
        for (int i = 0; i < players.Count; i++)
        {
            PlayerHealth p = players[i];
            if (p.IsDowned || !p.TryGetComponent(out PlayerNoise noise)) continue;
            if (noise.ServerLoudness <= loudLimit) continue;
            if (nextAllowed.TryGetValue(p, out float next) && Time.time < next) continue;
            nextAllowed[p] = Time.time + cooldownSeconds;
            ServerPunish(p, 0f, sanityLoss, false, "Too loud in the ghost room...");
        }
    }
}
