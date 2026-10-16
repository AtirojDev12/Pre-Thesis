using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// 16 Oct 2026 (Mr.k) — TOILET rule 2 "The last stall" (ห้องน้ำห้องท้าย), WOMEN'S toilet.
/// If the last stall is closed and someone is crying inside:
///   1. Turn off the toilet light at once.
///   2. Stay still for a while.
/// Punishment (too slow, light back on, or someone moves): a ghost comes out of
/// the stall and chases you (damage + sanity on touch).
/// </summary>
public sealed class RuleCryingStall : ZoneRuleBase
{
    [Header("Crying stall")]
    [SerializeField] private ToiletLight toiletLight;
    [Tooltip("Closed stall door mockup (prefab \"RM_ClosedStallDoor\"). Shown while crying.")]
    [SerializeField] private GameObject closedDoorLook;
    [Tooltip("Crying sound (prefab \"RM_CryingSound\"). Empty clip = generated placeholder.")]
    [SerializeField] private AudioSource crying;
    [Tooltip("Where the ghost comes out (inside the last stall).")]
    [SerializeField] private Transform stallPoint;
    [SerializeField] private RuleGhost stallGhost;

    [Header("When")]
    [SerializeField, Min(1f)] private float checkEverySeconds = 6f;
    [SerializeField, Range(0f, 1f)] private float chance = 0.25f;
    [SerializeField, Min(0f)] private float cooldownSeconds = 45f;

    [Header("What players must do")]
    [Tooltip("Time to turn the light off (seconds).")]
    [SerializeField, Min(1f)] private float darkWithinSeconds = 10f;
    [Tooltip("Then stay still this long in the dark.")]
    [SerializeField, Min(1f)] private float stillSeconds = 5f;
    [SerializeField, Min(0.05f)] private float moveTolerance = 0.35f;

    [Header("Punishment")]
    [SerializeField, Min(1f)] private float chaseSeconds = 20f;

    [SyncVar(hook = nameof(OnCryingChanged))] private bool isCrying;

    private enum Phase { Idle, WaitDark, Still }
    private Phase phase;
    private double phaseEnd;
    private double nextRoll;
    private readonly StillnessWatch watch = new StillnessWatch();

    private void Start() => Apply(isCrying);

    private void Update()
    {
        if (!IsServerSide) return;
        if (!ServerRuleLive || toiletLight == null) { if (phase != Phase.Idle) End(); return; }

        List<PlayerHealth> players = ServerPlayersInside();
        switch (phase)
        {
            case Phase.Idle:
                if (players.Count == 0 || Now < nextRoll || toiletLight.IsDark) return;
                nextRoll = Now + checkEverySeconds;
                if (Random.value < chance) Begin();
                break;

            case Phase.WaitDark:
                if (toiletLight.IsDark)
                {
                    phase = Phase.Still;
                    phaseEnd = Now + stillSeconds;
                    watch.Start(players);
                    Log("Light is off. Stay still.");
                }
                else if (Now >= phaseEnd) Fail(players, null);
                break;

            case Phase.Still:
                watch.AddLateComers(players);
                PlayerHealth mover = watch.FirstMover(zone, moveTolerance);
                if (!toiletLight.IsDark) { Log("The light came back on."); Fail(players, null); }
                else if (mover != null) { Log($"{mover.name} moved."); Fail(players, mover); }
                else if (Now >= phaseEnd) { Log("The crying stops."); End(); }
                break;
        }
    }

    private void Begin()
    {
        isCrying = true;
        Apply(true);
        phase = Phase.WaitDark;
        phaseEnd = Now + darkWithinSeconds;
        Log("The last stall is closed. Someone is crying inside.");
    }

    private void Fail(List<PlayerHealth> players, PlayerHealth culprit)
    {
        PlayerHealth victim = culprit;
        if (victim == null && players.Count > 0) victim = players[Random.Range(0, players.Count)];
        if (victim != null && stallGhost != null)
        {
            Vector3 from = stallPoint != null ? stallPoint.position : zone.Box.center;
            stallGhost.ServerChase(victim, from, null, chaseSeconds, null);
        }
        for (int i = 0; i < players.Count; i++) ServerShow(players[i], false, "Something came out of the last stall!");
        End();
    }

    private void End()
    {
        isCrying = false;
        Apply(false);
        phase = Phase.Idle;
        watch.Clear();
        nextRoll = Now + cooldownSeconds;
    }

    public override void ServerDevTrigger()
    {
        if (IsServerSide && phase == Phase.Idle) Begin();
    }

    private void OnCryingChanged(bool oldValue, bool newValue) => Apply(newValue);

    private void Apply(bool on)
    {
        if (closedDoorLook != null && closedDoorLook.activeSelf != on) closedDoorLook.SetActive(on);
        if (crying == null) return;
        if (on)
        {
            if (crying.clip == null) crying.clip = RuleFx.PlaceholderClip(RuleFx.PlaceholderSound.Crying);
            crying.loop = true;
            if (!crying.isPlaying) crying.Play();
        }
        else if (crying.isPlaying) crying.Stop();
    }
}
