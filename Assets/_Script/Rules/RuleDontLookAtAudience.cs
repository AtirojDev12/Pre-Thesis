using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 16 Oct 2026 (Mr.k) — SCREEN ROOM rule 2 "Don't look at the customers" (ห้ามมองลูกค้า).
/// While you change the film or clean, do not look at the ghost customers who
/// are watching the movie. Punishment: you lose health for every second you look.
///
/// Each player's own PC checks "is my camera on a customer's head, with nothing
/// in between" and tells the server; the server applies the damage.
/// </summary>
public sealed class RuleDontLookAtAudience : ZoneRuleBase
{
    [Header("Don't look")]
    [SerializeField] private GhostAudience audience;
    [Tooltip("Health lost per second of looking.")]
    [SerializeField, Min(0f)] private float damagePerSecond = 5f;
    [Tooltip("How exactly you must look at a head to count (degrees).")]
    [SerializeField, Range(2f, 30f)] private float lookAngle = 9f;
    [SerializeField, Min(1f)] private float lookDistance = 25f;

    private readonly Dictionary<PlayerHealth, float> nextHit = new Dictionary<PlayerHealth, float>();
    private float checkTimer;
    private bool localLooking;

    private void Update()
    {
        LocalCheck();
        if (IsServerSide && ServerRuleLive) ServerTick();
    }

    private void LocalCheck()
    {
        checkTimer -= Time.deltaTime;
        if (checkTimer > 0f) { LocalReportLooking(localLooking); return; }
        checkTimer = 0.15f;

        localLooking = false;
        if (audience != null && audience.Visible && !audience.Standing && LocalInside)
        {
            Camera cam = RuleFx.LocalCamera();
            Transform own = LocalBody != null ? LocalBody.transform : null;
            for (int i = 0; i < audience.Count && !localLooking; i++)
            {
                Transform m = audience.Member(i);
                if (m != null && m.gameObject.activeInHierarchy &&
                    RuleFx.IsLookingAt(cam != null ? cam.transform : null, audience.HeadOf(i), lookAngle, lookDistance, m, own))
                    localLooking = true;
            }
        }
        LocalReportLooking(localLooking);
    }

    private void ServerTick()
    {
        if (audience == null || !audience.Visible || audience.Standing) return;
        List<PlayerHealth> players = ServerPlayersInside();
        for (int i = 0; i < players.Count; i++)
        {
            PlayerHealth p = players[i];
            if (p.IsDowned || !ServerIsLooking(p)) continue;
            // One hit per second (PlayerHealth ignores hits closer than 0.5 s apart).
            if (nextHit.TryGetValue(p, out float next) && Time.time < next) continue;
            bool first = !nextHit.ContainsKey(p) || Time.time - next > 3f;
            nextHit[p] = Time.time + 1f;
            ServerPunish(p, damagePerSecond, 0f, false, first ? "Don't look at them..." : null);
        }
    }
}
