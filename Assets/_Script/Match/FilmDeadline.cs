using Mirror;
using UnityEngine;

/// <summary>
/// 17 Nov 2026 (Mr.k, Task 5): the film change timers. Sits on a film-change ZoneChoreSpot.
///
///   GLOW (both screen rooms): 75 s (30 in-game min) before a film change is due, the projector
///   glows pale yellow for 5 s. Every player sees it, through walls (DBD generator style).
///
///   DEADLINE (ghost room only, "Missed Penalties" on): after it is due, players have 37.5 s
///   (15 in-game min).
///     Missed          -> penalty 1: the projector glows RED through walls, a map-wide scream,
///                        every living player loses Max(5, FloorToInt(sanity x 0.15)).
///                        The film can still be changed (that ends the red glow).
///     Still not done  -> penalty 2 at the NEXT film's glow time: every living player loses
///                        Max(5, FloorToInt(health x 0.5)), and the missed film is marked done
///                        (the board still moves, no money, it can no longer be changed).
///
/// All numbers: Resources/Tuning/FilmMopTuning (Designer Settings > Film Reels + Mops).
/// SERVER AUTHORITY: timers and penalties run on the host. Clients only draw the glows
/// (2 SyncVars) and play the scream (ClientRpc).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ZoneChoreSpot))]
public sealed class FilmDeadline : NetworkBehaviour
{
    [Tooltip("On = the ghost room: missing the deadline punishes every player. Off = glow only (normal room, bonus).")]
    [SerializeField] private bool missedPenalties = true;
    [Tooltip("The projector parts that glow. Set by the setup tool; change it if the glow sits on the wrong object.")]
    [SerializeField] private Transform[] glowParts;

    [SyncVar] private double glowUntil;
    [SyncVar] private bool missedGlow;

    // Server only.
    private ZoneChoreSpot chore;
    private int glowDoneForSlot = -1;
    private int stage;
    private int stageSlot = -1;

    // Local only.
    private SeeThroughGlow glow;
    private bool glowBuilt;
    private static AudioClip placeholderScream;

    private static double Now => NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;
    private static FilmMopTuning Tuning => FilmMopTuning.Current;

    // 17 Nov: a small id for Memo Book step keys (GetInstanceID is obsolete in Unity 6.5).
    private int guideUid;
    private static int nextGuideUid;
    private int GuideUid => guideUid != 0 ? guideUid : (guideUid = ++nextGuideUid);

    /// <summary>17 Nov (Task 8): every film timer (Memo Book).</summary>
    public static readonly System.Collections.Generic.List<FilmDeadline> All = new System.Collections.Generic.List<FilmDeadline>();
    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() => All.Remove(this);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => All.Clear();

    /// <summary>
    /// SERVER. 17 Nov (Task 8, #70): after the 1st miss (red glow) every player's Memo Book says the film is
    /// late (Sanity tier). The 2nd penalty happens at once, so it needs no step.
    /// </summary>
    public bool ServerGuideFor(PlayerHealth player, out GuideEntry entry)
    {
        entry = default;
        if (!missedPenalties || stage != 1 || chore == null || player == null) return false;
        entry = GuideEntry.Make(GuideSeverity.SanityDrain, "film-late:" + GuideUid + ":" + stageSlot,
            MemoGuideTexts.Current.filmLate.Replace("{film}", chore.DueLabel));
        return true;
    }

    private void Awake() => chore = GetComponent<ZoneChoreSpot>();

    private void Update()
    {
        if (NetworkMode.HasServerAuthority(this)) ServerTick();
        LocalGlow();
    }

    // ---- Server -----------------------------------------------------------------------------

    private void ServerTick()
    {
        MatchDirector match = MatchDirector.Instance;
        if (match == null || !match.RoundRunning || chore == null) return;

        // Yellow glow once per film change, glowSecondsBefore real seconds before it is due.
        int next = chore.ServerNextSlotMinute;
        if (next >= 0 && next != glowDoneForSlot && FilmMopTuning.SecondsUntil(match, next) <= Tuning.glowSecondsBefore)
        {
            glowDoneForSlot = next;
            glowUntil = Now + Tuning.glowSeconds;
        }

        if (!missedPenalties || !ZoneRuleBase.RulesActiveNow || !chore.Needed)
        {
            stage = 0;
            stageSlot = -1;
            if (missedGlow) missedGlow = false; // changed in time (or no penalties here)
            return;
        }

        int due = chore.ServerDueMinute;
        if (due != stageSlot)
        {
            // A new film is due (or the missed one was done and another one waits): start again.
            stageSlot = due;
            stage = 0;
            missedGlow = false;
        }
        if (due < 0) return;

        if (stage == 0 && Now - chore.NeededSince >= Tuning.deadlineSeconds)
        {
            stage = 1;
            missedGlow = true;
            ServerPenaltySanity();
        }
        else if (stage == 1)
        {
            // Penalty 2 when the NEXT film's glow would start (no next film tonight: the end of the night).
            int nextDue = chore.RepeatMinutes > 0 ? due + chore.RepeatMinutes : 6 * 60;
            if (FilmMopTuning.SecondsUntil(match, nextDue) <= Tuning.glowSecondsBefore)
            {
                stage = 2;
                missedGlow = false;
                ServerPenaltyHealth();
                chore.ServerAutoFinish();
            }
        }
    }

    private void ServerPenaltySanity()
    {
        int hit = 0;
        foreach (PlayerHealth p in PlayerRegistry.All)
        {
            if (!Punishable(p) || !p.TryGetComponent(out PlayerSanity sanity)) continue;
            sanity.ServerDrain(Tuning.SanityLoss(sanity.Value), "ghost room film missed", false);
            hit++;
        }
        Debug.Log($"[FilmDeadline] '{name}': film missed -> red glow, scream, sanity penalty for {hit} player(s).", this);
        ServerTellEveryone("The ghost room film was NOT changed. Everyone lost sanity. Change it now, before the next film, or it gets worse!", true);
    }

    private void ServerPenaltyHealth()
    {
        int hit = 0;
        foreach (PlayerHealth p in PlayerRegistry.All)
        {
            if (!Punishable(p)) continue;
            p.TakeDamage(Tuning.HealthLoss(p.CurrentHealth));
            hit++;
        }
        Debug.Log($"[FilmDeadline] '{name}': still not changed at the next film's warning -> health penalty for {hit} player(s), film marked done.", this);
        ServerTellEveryone("The missed film was never changed. Everyone got hurt. That film can no longer be changed.", false);
    }

    private static bool Punishable(PlayerHealth p) =>
        p != null && !p.IsDead && !p.IsDowned && PlayerRegistry.IsInRound(p);

    private void ServerTellEveryone(string text, bool scream)
    {
        if (NetworkMode.IsOffline) LocalTell(text, scream);
        else RpcTell(text, scream);
    }

    [ClientRpc]
    private void RpcTell(string text, bool scream) => LocalTell(text, scream);

    private void LocalTell(string text, bool scream)
    {
        if (scream) PlayScream();
        PlayerHealth me = PlayerHealth.LocalInstance;
        if (me == null || me.IsDead) return;
        RuleFx.Toast(text, 4.5f);
    }

    private void PlayScream()
    {
        if (AudioManager.Instance == null) return;
        AudioClip clip = Tuning.missedScream != null ? Tuning.missedScream : PlaceholderScream();
        AudioManager.Instance.PlayMapWide(clip, transform.position, Tuning.screamVolume, Tuning.screamReach, gameObject.scene);
    }

    /// <summary>A made-up scream (falling shriek) until a real clip is set in FilmMopTuning.</summary>
    private static AudioClip PlaceholderScream()
    {
        if (placeholderScream != null) return placeholderScream;
        const int rate = 22050;
        int length = (int)(rate * 2.2f);
        var data = new float[length];
        var random = new System.Random(13);
        float phase = 0f;
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)rate;
            float pitch = Mathf.Lerp(1400f, 520f, t / 2.2f) + 90f * Mathf.Sin(t * 38f);
            phase += 2f * Mathf.PI * pitch / rate;
            float envelope = Mathf.Clamp01(t / 0.08f) * Mathf.Clamp01((2.2f - t) / 0.6f);
            float voice = Mathf.Sin(phase) * 0.55f + Mathf.Sin(phase * 2.01f) * 0.25f + Mathf.Sin(phase * 3.02f) * 0.1f;
            float rasp = ((float)random.NextDouble() * 2f - 1f) * 0.18f;
            data[i] = (voice + rasp) * envelope * 0.6f;
        }
        placeholderScream = AudioClip.Create("Placeholder Film Scream", length, 1, rate, false);
        placeholderScream.SetData(data, 0);
        return placeholderScream;
    }

    // ---- Local glow -------------------------------------------------------------------------

    private void LocalGlow()
    {
        bool warning = glowUntil > 0 && Now < glowUntil;
        bool show = warning || missedGlow;
        if (show && !glowBuilt)
        {
            glowBuilt = true;
            Transform[] parts = glowParts != null && glowParts.Length > 0 ? glowParts : new[] { transform };
            glow = SeeThroughGlow.Build(parts, Tuning.glowColor);
        }
        if (glow == null) return;
        // The warning (yellow) wins for its 5 s; otherwise a missed film glows red.
        Color color = warning ? Tuning.glowColor : Tuning.missedGlowColor;
        float pulse = warning ? 0.7f + 0.3f * Mathf.Sin(Time.time * 6f) : 0.6f + 0.4f * Mathf.Sin(Time.time * 3f);
        glow.SetVisible(show, color, pulse);
    }

    private void OnDestroy()
    {
        glow?.Destroy();
        glow = null;
    }

    /// <summary>Editor tool.</summary>
    public void EditorSetup(bool penalties, Transform[] parts)
    {
        missedPenalties = penalties;
        glowParts = parts;
    }
}
