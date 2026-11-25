using System;
using Mirror;
using UnityEngine;

/// <summary>
/// 25 Nov 2026 (Mr.k): a SLIDING door (every door that is not an exit).
///   - Look at a door leaf and press E: it slides open. Press E again: it slides shut.
///   - Double doors: each half slides to its own side. Handles move with their half.
///   - Server decides (SyncVar), every PC plays the same slide. Late joiners see the right state.
/// Made by Tools > Pre-Thesis > One Step > Sliding Doors: it puts the door model under this object
/// (placed at the doorway, so the E range check is fair) and fills the parts list.
/// Designers: change Open Offset per part (world metres, as set by the tool) and Slide Seconds freely.
/// 25 Nov: LOCKED doors (tick "Locked"): E only rattles it (every player sees the tug), it never opens.
/// The one who pressed gets the "locked" line. Code can unlock it later (ServerSetLocked).
/// </summary>
[DisallowMultipleComponent]
public sealed class SlidingDoor : InteractableBase
{
    [Serializable]
    public sealed class Part
    {
        [Tooltip("A door leaf or a handle.")]
        public Transform part;
        [Tooltip("Info only: its local position when shut. The game takes where the part stands in the scene.")]
        public Vector3 closedLocal;
        [Tooltip("How far it moves when open, in world metres (direction + distance).")]
        public Vector3 openOffset;
    }

    [SerializeField] private Part[] parts = new Part[0];
    [SerializeField, Min(0.05f)] private float slideSeconds = 0.6f;
    [SerializeField] private string openPrompt = "Press E: open";
    [SerializeField] private string closePrompt = "Press E: close";
    [Tooltip("Seconds between two presses (stops double toggles).")]
    [SerializeField, Min(0f)] private float pressCooldown = 0.35f;

    [Header("Locked (25 Nov)")]
    [Tooltip("On = it never opens. E plays a rattle (tries to open, cannot).")]
    [SyncVar, SerializeField] private bool locked;
    [SerializeField] private string lockedPrompt = "Press E: open";
    [SerializeField] private string lockedLine = "It's locked.";
    [Tooltip("How far the door tugs open before it stops (metres).")]
    [SerializeField, Min(0f)] private float rattleDistance = 0.035f;
    [SerializeField, Min(0.1f)] private float rattleSeconds = 0.55f;

    [SyncVar(hook = nameof(OnOpenChanged))] private bool isOpen;
    private float rattleUntil = -1f;

    private float open01;      // 0 = shut, 1 = open (local, animated)
    private float target01;
    private double nextPress;
    private Vector3[] openLocal;

    public bool IsOpen => isOpen;

    protected override void Awake()
    {
        base.Awake();
        CacheOpenPositions();
        Apply(0f);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // Joined late: jump straight to the real state.
        target01 = open01 = isOpen ? 1f : 0f;
        Apply(open01);
    }

    public bool Locked => locked;

    public override string GetInteractionPrompt() => locked ? lockedPrompt : isOpen ? closePrompt : openPrompt;

    /// <summary>SERVER (InteractableBase.Interact).</summary>
    protected override void OnInteracted(GameObject interactor)
    {
        double now = NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;
        if (now < nextPress) return;
        nextPress = now + pressCooldown;
        if (locked)
        {
            // 25 Nov: locked: everyone sees the rattle, only the one who pressed reads the line.
            if (NetworkMode.IsOffline) { PlayRattle(); RuleFx.Toast(lockedLine, 1.5f); return; }
            RpcRattle();
            if (interactor != null && interactor.TryGetComponent(out NetworkIdentity who) && who.connectionToClient != null)
                TargetLocked(who.connectionToClient);
            return;
        }
        isOpen = !isOpen;
        target01 = isOpen ? 1f : 0f; // Mirror runs no hook on the machine that set the SyncVar
    }

    /// <summary>SERVER. Open or shut from code (e.g. a rule or an NPC later).</summary>
    public void ServerSetOpen(bool open)
    {
        if (!NetworkMode.HasServerAuthority(this) || isOpen == open) return;
        isOpen = open;
        target01 = open ? 1f : 0f;
    }

    /// <summary>SERVER. Lock or unlock from code (a key, a rule...). Unlocking does not open it.</summary>
    public void ServerSetLocked(bool value)
    {
        if (!NetworkMode.HasServerAuthority(this)) return;
        locked = value;
        if (value && isOpen) ServerSetOpen(false);
    }

    [ClientRpc]
    private void RpcRattle() => PlayRattle();

    [TargetRpc]
    private void TargetLocked(NetworkConnectionToClient target) => RuleFx.Toast(lockedLine, 1.5f);

    private void PlayRattle() => rattleUntil = Time.time + rattleSeconds;

    private void OnOpenChanged(bool oldValue, bool newValue) => target01 = newValue ? 1f : 0f;

    private void Update()
    {
        if (rattleUntil > 0f) { Rattle(); return; }
        if (Mathf.Approximately(open01, target01)) return;
        open01 = Mathf.MoveTowards(open01, target01, Time.deltaTime / slideSeconds);
        Apply(open01);
    }

    /// <summary>25 Nov: three quick tugs toward open, each smaller, then back shut.</summary>
    private void Rattle()
    {
        float left = rattleUntil - Time.time;
        float k = 0f;
        if (left <= 0f) rattleUntil = -1f;
        else
        {
            float t = 1f - left / rattleSeconds;                  // 0 -> 1
            k = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3f)) * (1f - t * 0.6f);
        }
        for (int i = 0; i < parts.Length; i++)
        {
            Part p = parts[i];
            if (p == null || p.part == null) continue;
            Vector3 dir = openLocal[i] - p.closedLocal;
            float len = dir.magnitude;
            Vector3 tug = len > 0.0001f ? dir / len * Mathf.Min(rattleDistance, len) * k : Vector3.zero;
            p.part.localPosition = Vector3.LerpUnclamped(p.closedLocal, openLocal[i], open01 * open01 * (3f - 2f * open01)) + tug;
        }
    }

    private void CacheOpenPositions()
    {
        openLocal = new Vector3[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            Part p = parts[i];
            if (p == null || p.part == null) continue;
            // 25 Nov fix: SHUT = where the part stands in the scene now. Before, the position saved by
            // the setup tool won, so a door you slid shut by hand in the editor started open in game.
            p.closedLocal = p.part.localPosition;
            Transform parent = p.part.parent;
            Vector3 local = parent != null ? parent.InverseTransformVector(p.openOffset) : p.openOffset;
            openLocal[i] = p.closedLocal + local;
        }
    }

    private void Apply(float t)
    {
        float eased = t * t * (3f - 2f * t); // smooth start and stop
        for (int i = 0; i < parts.Length; i++)
        {
            Part p = parts[i];
            if (p == null || p.part == null) continue;
            p.part.localPosition = Vector3.LerpUnclamped(p.closedLocal, openLocal[i], eased);
        }
    }

#if UNITY_EDITOR
    /// <summary>Editor tool.</summary>
    public void EditorSetup(Part[] newParts) => parts = newParts ?? new Part[0];
    public Part[] EditorParts => parts;
#endif
}
