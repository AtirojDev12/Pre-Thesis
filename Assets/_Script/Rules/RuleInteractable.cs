using System;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 16 Oct 2026: one "press E" or "hold E" thing that a zone rule uses
/// (toilet light switch, knock on a stall, sit on the shadow, pick up / throw the
/// black box). The rule script listens to <see cref="ServerUsed"/>.
///
/// Its collider sits on the mockup child (prefab), so swapping the prefab keeps it
/// working. The collider is solid (the interaction ray ignores triggers) but never
/// blocks your own body (RuleFx.KeepOutOfMyWay).
///
/// SERVER AUTHORITY: the server decides if it can be used, runs the hold timer and
/// checks range + alive. The holder's PC only says "I let go of E".
/// </summary>
[DisallowMultipleComponent]
public sealed class RuleInteractable : InteractableBase
{
    [Header("Use")]
    [Tooltip("Shown after [E] / [Hold E], e.g. \"Knock\".")]
    [SerializeField] private string actionText = "Use";
    [Tooltip("0 = press E once. More = hold E this many seconds.")]
    [SerializeField, Min(0f)] private float holdSeconds;
    [Tooltip("Only while usable: these objects are shown (empty = always shown).")]
    [SerializeField] private GameObject[] showWhenUsable;

    private const float ServerRange = 4.5f;
    private const Key HoldKey = Key.E;

    [SyncVar(hook = nameof(OnUsableChanged))] private bool usable = true;
    [SyncVar(hook = nameof(OnLabelChanged))] private string label = "";
    [SyncVar] private uint workerId;
    [SyncVar] private double workStart;

    /// <summary>SERVER. Somebody pressed / finished holding E. Argument = their body.</summary>
    public event Action<GameObject> ServerUsed;
    /// <summary>SERVER. Optional extra check (e.g. "only the box carrier may throw").</summary>
    public Func<GameObject, bool> ServerCanUse;

    private GameObject offlineWorker;
    private Collider[] colliders;
    private string prompt;
    private bool stopSent;
    private bool collidersDirty = true;

    public bool Usable => usable;
    public string DisplayName
    {
        get
        {
            if (GetComponent<ToiletLight>() != null) return "Light Switch";
            switch (actionText)
            {
                case "Knock": return "Stall Door";
                case "Sit on the shadow": return "Shadow Seat";
                case "Pick up black box": return "Black Box";
                case "Throw the black box in": return "Black Box Drop Point";
                default: return name.Replace("(Clone)", "").Replace('_', ' ').Trim();
            }
        }
    }

    private static double Now => NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;

    protected override void Awake()
    {
        base.Awake();
        colliders = GetComponentsInChildren<Collider>(true);
        RebuildPrompt();
        ApplyUsable(usable);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        RebuildPrompt();
        ApplyUsable(usable);
    }

    // ---- Server API -------------------------------------------------------------------

    /// <summary>SERVER. Turn on / off (off hides the "show when usable" objects and the collider).</summary>
    public void ServerSetUsable(bool value)
    {
        if (!NetworkMode.HasServerAuthority(this) || usable == value) return;
        usable = value;
        if (!value) StopWork();
        ApplyUsable(value); // hooks do not run on the machine that set the value
    }

    /// <summary>SERVER. Change the words after [E] (e.g. "Turn light on" / "Turn light off").</summary>
    public void ServerSetLabel(string text)
    {
        if (!NetworkMode.HasServerAuthority(this) || label == text) return;
        label = text;
        RebuildPrompt();
    }

    // ---- Interaction ------------------------------------------------------------------

    public override string GetInteractionPrompt() => prompt;

    public override bool CanInteract() => usable && (workerId == 0 || IsLocalWorker) && RoundRunning;

    protected override void OnInteracted(GameObject interactor)
    {
        if (!usable || interactor == null || !CanUse(interactor)) return;
        if (holdSeconds <= 0f)
        {
            ServerUsed?.Invoke(interactor);
            return;
        }
        uint id = IdOf(interactor);
        if (workerId != 0 && workerId != id) return;
        offlineWorker = NetworkMode.IsOffline ? interactor : null;
        workerId = id;
        workStart = Now;
    }

    private bool CanUse(GameObject player)
    {
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        if (health != null && (health.IsDead || health.IsDowned)) return false;
        if ((player.transform.position - transform.position).sqrMagnitude > ServerRange * ServerRange) return false;
        return ServerCanUse == null || ServerCanUse(player);
    }

    private void Update()
    {
        if (NetworkMode.HasServerAuthority(this) && workerId != 0) ServerTickHold();
        LocalTick();
    }

    private void ServerTickHold()
    {
        GameObject worker = WorkerObject();
        if (!usable || worker == null || !CanUse(worker)) { StopWork(); return; }
        if (Now - workStart < holdSeconds) return;
        StopWork();
        ServerUsed?.Invoke(worker);
    }

    private void StopWork()
    {
        workerId = 0;
        offlineWorker = null;
    }

    private GameObject WorkerObject()
    {
        if (NetworkMode.IsOffline) return offlineWorker;
        return NetworkServer.spawned.TryGetValue(workerId, out NetworkIdentity identity) && identity != null
            ? identity.gameObject : null;
    }

    private static uint IdOf(GameObject player)
    {
        if (NetworkMode.IsOffline) return 1u;
        NetworkIdentity identity = player.GetComponent<NetworkIdentity>();
        return identity != null ? identity.netId : 0u;
    }

    [Command(requiresAuthority = false)]
    private void CmdStopHold(NetworkConnectionToClient sender = null)
    {
        if (sender != null && sender.identity != null && sender.identity.netId == workerId) StopWork();
    }

    // ---- Local ---------------------------------------------------------------------------

    private bool IsLocalWorker
    {
        get
        {
            if (workerId == 0) return false;
            PlayerHealth me = PlayerHealth.LocalInstance;
            if (me == null) return false;
            return NetworkMode.IsOffline ? workerId == 1u : workerId == me.netId;
        }
    }

    private void LocalTick()
    {
        if (collidersDirty && PlayerHealth.LocalInstance != null)
        {
            collidersDirty = false;
            RuleFx.KeepOutOfMyWay(colliders);
        }

        if (holdSeconds <= 0f || !IsLocalWorker)
        {
            stopSent = false;
            ChoreProgressHUD.Hide(this);
            return;
        }
        ChoreProgressHUD.Show(this, CurrentAction, (float)((Now - workStart) / holdSeconds));
        bool holding = Keyboard.current != null && Keyboard.current[HoldKey].isPressed && !GameplayInput.Blocked;
        if (holding || stopSent) return;
        stopSent = true;
        if (NetworkMode.IsOffline) StopWork();
        else CmdStopHold();
    }

    private void OnDisable() => ChoreProgressHUD.Hide(this);

    private string CurrentAction => string.IsNullOrEmpty(label) ? actionText : label;

    private void RebuildPrompt() => prompt = (holdSeconds > 0f ? "[Hold E] " : "[E] ") + CurrentAction;

    private void OnLabelChanged(string oldValue, string newValue) => RebuildPrompt();
    private void OnUsableChanged(bool oldValue, bool newValue) => ApplyUsable(newValue);

    private void ApplyUsable(bool on)
    {
        if (showWhenUsable != null)
            foreach (GameObject go in showWhenUsable)
                if (go != null && go.activeSelf != on) go.SetActive(on);
        if (colliders != null)
            foreach (Collider c in colliders)
                if (c != null) c.enabled = on;
        collidersDirty = true; // Unity forgets ignore pairs on disabled colliders
    }

    /// <summary>LOCAL. The rule moved this object (e.g. to another stall): re-apply "do not block me".</summary>
    public void MarkMoved() => collidersDirty = true;

    private static bool RoundRunning => MatchDirector.Instance == null || MatchDirector.Instance.RoundRunning;

    /// <summary>Editor tool: set the words and hold time.</summary>
    public void Configure(string action, float hold, GameObject[] showWhen)
    {
        actionText = action;
        holdSeconds = hold;
        showWhenUsable = showWhen;
        RebuildPrompt();
    }
}
