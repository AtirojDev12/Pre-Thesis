using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// 16 Oct 2026 (Mr.k) — TOILET rule 1 "Cracked mirror" (กระจกแตก).
/// When you go in and a mirror has a crack, look at that mirror for a while.
/// Punishment (nobody looks long enough): the mirror shatters — jumpscare,
/// damage and sanity loss for everyone in this toilet.
/// One copy of this rule per toilet room (men's, women's).
/// </summary>
public sealed class RuleCrackedMirror : ZoneRuleBase
{
    [Header("Cracked mirror")]
    [Tooltip("One point per mirror (on the glass, forward = out of the mirror).")]
    [SerializeField] private Transform[] mirrors;
    [Tooltip("Crack mockup (prefab \"RM_MirrorCrack\"), moved onto the chosen mirror.")]
    [SerializeField] private Transform crackLook;
    [Tooltip("Shattered mockup (prefab \"RM_MirrorShattered\").")]
    [SerializeField] private Transform shatteredLook;

    [Header("When")]
    [SerializeField, Min(1f)] private float checkEverySeconds = 6f;
    [SerializeField, Range(0f, 1f)] private float chance = 0.25f;
    [SerializeField, Min(0f)] private float cooldownSeconds = 40f;

    [Header("What players must do")]
    [Tooltip("Time to notice and look at the crack (seconds).")]
    [SerializeField, Min(2f)] private float windowSeconds = 20f;
    [Tooltip("Total looking time needed (any player inside, added together).")]
    [SerializeField, Min(0.5f)] private float lookSecondsNeeded = 2.5f;
    [SerializeField, Range(3f, 30f)] private float lookAngle = 12f;
    [SerializeField, Min(1f)] private float lookDistance = 8f;

    [Header("Punishment")]
    [SerializeField, Min(0f)] private float damage = 25f;
    [SerializeField, Min(0f)] private float sanityLoss = 20f;
    [Tooltip("How long the broken mirror stays before it resets.")]
    [SerializeField, Min(1f)] private float shatteredSeconds = 12f;

    [SyncVar(hook = nameof(OnStateChanged))] private int mirror = -1;
    [SyncVar(hook = nameof(OnShatterChanged))] private bool shattered;

    private double deadline;
    private double nextRoll;
    private double resetAt;
    private float looked;
    private float localTimer;
    private bool localLooking;

    private void Start() => Apply();

    private void Update()
    {
        LocalCheck();
        if (!IsServerSide) return;
        if (!ServerRuleLive) { if (mirror >= 0) Reset(); return; }

        List<PlayerHealth> players = ServerPlayersInside();
        if (shattered)
        {
            if (Now >= resetAt) Reset();
            return;
        }
        if (mirror < 0)
        {
            if (players.Count == 0 || Now < nextRoll || mirrors == null || mirrors.Length == 0) return;
            nextRoll = Now + checkEverySeconds;
            if (Random.value < chance) StartCrack();
            return;
        }

        for (int i = 0; i < players.Count; i++)
            if (!players[i].IsDowned && ServerIsLooking(players[i])) { looked += Time.deltaTime; break; }

        if (looked >= lookSecondsNeeded)
        {
            Log("Someone looked at the crack. It fades.");
            mirror = -1;
            Apply();
            nextRoll = Now + cooldownSeconds;
            return;
        }
        if (Now >= deadline)
        {
            shattered = true;
            Apply();
            resetAt = Now + shatteredSeconds;
            Log("Nobody looked. The mirror shatters.");
            for (int i = 0; i < players.Count; i++)
                ServerPunish(players[i], damage, sanityLoss, true, "The mirror shattered!");
        }
    }

    private void StartCrack()
    {
        mirror = Random.Range(0, mirrors.Length);
        looked = 0f;
        deadline = Now + windowSeconds;
        Apply();
        Log($"Mirror {mirror + 1} cracks.");
    }

    private void Reset()
    {
        mirror = -1;
        shattered = false;
        Apply();
        nextRoll = Now + cooldownSeconds;
    }

    public override void ServerDevTrigger()
    {
        if (IsServerSide && mirror < 0 && mirrors != null && mirrors.Length > 0) StartCrack();
    }

    // ---- Every machine ---------------------------------------------------------------------

    private void LocalCheck()
    {
        localTimer -= Time.deltaTime;
        if (localTimer <= 0f)
        {
            localTimer = 0.15f;
            localLooking = false;
            if (mirror >= 0 && !shattered && mirrors != null && mirror < mirrors.Length && LocalInside)
            {
                Camera cam = RuleFx.LocalCamera();
                localLooking = RuleFx.IsLookingAt(cam != null ? cam.transform : null, mirrors[mirror].position,
                    lookAngle, lookDistance, crackLook, LocalBody != null ? LocalBody.transform : null);
            }
        }
        LocalReportLooking(localLooking);
    }

    private void OnStateChanged(int oldValue, int newValue) => Apply();
    private void OnShatterChanged(bool oldValue, bool newValue) => Apply();

    private void Apply()
    {
        bool hasMirror = mirrors != null && mirror >= 0 && mirror < mirrors.Length && mirrors[mirror] != null;
        Transform at = hasMirror ? mirrors[mirror] : null;
        if (crackLook != null)
        {
            bool show = hasMirror && !shattered;
            if (show) crackLook.SetPositionAndRotation(at.position, at.rotation);
            if (crackLook.gameObject.activeSelf != show) crackLook.gameObject.SetActive(show);
        }
        if (shatteredLook != null)
        {
            bool show = hasMirror && shattered;
            if (show) shatteredLook.SetPositionAndRotation(at.position, at.rotation);
            if (shatteredLook.gameObject.activeSelf != show) shatteredLook.gameObject.SetActive(show);
        }
    }
}
