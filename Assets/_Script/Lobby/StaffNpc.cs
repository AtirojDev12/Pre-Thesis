using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 5 Nov 2026 (Mr.k): the STAFF NPC in the lobby.
///
/// 17 Nov 2026 (Mr.k, Task 8): it hands out the MEMO BOOK (the work sheet, renamed).
///   - It WALKS UP to the nearest player who has no Memo Book and never got the free one,
///     says "Take this Memo Book, it will help you survive." and FOLLOWS them until they
///     press E on it. Then it picks the next such player, or walks back to its spot.
///   - E: never claimed (this save file, this map) = the free Memo Book (into your save + hotbar).
///        Claimed before = "I already gave you this Memo Book" (buy another in the shop, 999).
///        Already carrying one = "You already have your Memo Book."
///   - No more "give it back".
///   Walking needs a NavMesh in the Lobby (Tools > Pre-Thesis > Scenes > Lobby: Bake NavMesh).
///   Without one it walks straight (it may bump into things).
///
/// SERVER AUTHORITY: the server picks who to follow and moves the Staff (position + turn are
/// SyncVars, smoothed on clients). The save is on each player's own PC: the server asks the
/// owner's PC (TargetRpc) to add the book and remember the claim, like a shop purchase.
/// Placed by Tools > Pre-Thesis > Scenes > Lobby: Add Staff NPC (where the Scene view looks).
/// </summary>
[DisallowMultipleComponent]
public sealed class StaffNpc : InteractableBase, IPromptOffset
{
    private const float ServerRange = 4.5f;
    private const string OfferLine = "Take this Memo Book, it will help you survive.";
    /// <summary>17 Nov: old name of a thin walker type (not used any more: the Lobby NavMesh is baked thin instead).</summary>
    public const string AgentTypeName = "Staff (small)";

    [Header("Face the nearest player")]
    [Tooltip("How fast the Staff turns (degrees per second).")]
    [SerializeField, Min(0f)] private float turnSpeed = 270f;

    [Tooltip("Height of the [E] prompt above the Staff's feet (metres).")]
    [SerializeField, Min(0f)] private float promptHeight = 1.75f;

    [Header("Follow players without a Memo Book (17 Nov)")]
    [SerializeField] private bool followPlayers = true;
    [SerializeField, Min(0.5f)] private float walkSpeed = 2.2f;
    [Tooltip("Stops this far from the player it follows (metres).")]
    [SerializeField, Min(0.8f)] private float keepDistance = 1.6f;
    [Tooltip("Height of the speech bubble above the Staff's feet (metres).")]
    [SerializeField, Min(0.5f)] private float bubbleHeight = 2.3f;
    [Tooltip("Size of the speech bubble text (world units).")]
    [SerializeField, Range(0.3f, 3f)] private float bubbleTextSize = 0.85f;

    public Vector3 PromptOffset => Vector3.up * promptHeight;

    // Server writes, everyone reads.
    [SyncVar] private Vector3 syncedPosition;
    [SyncVar] private float syncedYaw;
    [SyncVar] private uint followingId;   // 0 = nobody
    [SyncVar] private bool moved;         // false = still at the scene spot (clients keep the scene transform)

    // Server only.
    private Vector3 home;
    private Quaternion homeRotation;
    private NavMeshAgent agent;
    private float nextPick;
    private float nextSync;
    private GameObject offlineTarget;

    // Local only.
    private TextMeshPro bubble;
    private Transform sign;      // the big "STAFF" sign: hidden while the bubble shows (they overlapped)
    private bool signLooked;

    private static string MemoId => ItemCatalog.MemoBookId(RoomConfig.DemoMapID);

    protected override void Awake()
    {
        base.Awake();
        home = transform.position;
        homeRotation = transform.rotation;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        TryMakeAgent();
    }

    private void Start()
    {
        if (NetworkMode.IsOffline) TryMakeAgent();
    }

    private void TryMakeAgent()
    {
        if (agent != null || !followPlayers) return;
        int agentType = StaffAgentTypeId();
        var filter = new NavMeshQueryFilter { agentTypeID = agentType, areaMask = NavMesh.AllAreas };
        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, filter)) return; // no NavMesh: walk straight
        agent = gameObject.AddComponent<NavMeshAgent>();
        agent.agentTypeID = agentType;
        agent.speed = walkSpeed;
        agent.angularSpeed = turnSpeed;
        agent.stoppingDistance = keepDistance;
        agent.updateRotation = false;
        agent.radius = 0.3f;
        agent.height = 1.8f;
        agent.Warp(hit.position);
    }

    /// <summary>Humanoid (0). The Lobby NavMesh is baked for Humanoid with a thin radius (fits through doorways).</summary>
    private static int StaffAgentTypeId() => 0;

    private void Update()
    {
        if (NetworkMode.HasServerAuthority(this)) ServerTick();
        else ClientSmooth();
        UpdateBubble();
    }

    // ---- Server: who to follow, and walking -------------------------------------------------

    private void ServerTick()
    {
        PlayerHealth target = followPlayers && PlayerInventory.InLobby ? ServerTarget() : null;
        Vector3 goal = target != null ? target.transform.position : home;
        float stopAt = target != null ? keepDistance : 0.15f;

        Vector3 flat = goal - transform.position;
        flat.y = 0f;
        bool far = flat.magnitude > stopAt + 0.05f;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.stoppingDistance = stopAt;
            if (far) agent.SetDestination(goal);
            else if (agent.hasPath) agent.ResetPath();
        }
        else if (far)
        {
            Vector3 stop = new Vector3(goal.x, transform.position.y, goal.z) - flat.normalized * stopAt;
            transform.position = Vector3.MoveTowards(transform.position, stop, walkSpeed * Time.deltaTime);
        }

        // Face the followed player, else the nearest player, else the way it walks / its spot.
        Transform look = target != null ? target.transform : NearestPlayer();
        Quaternion want = homeRotation;
        if (look != null)
        {
            Vector3 dir = look.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f) want = Quaternion.LookRotation(dir, Vector3.up);
        }
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * Time.deltaTime);

        if (Time.time >= nextSync)
        {
            nextSync = Time.time + 0.1f;
            if (!moved && (transform.position - home).sqrMagnitude > 0.0004f) moved = true;
            syncedPosition = transform.position;
            syncedYaw = transform.eulerAngles.y;
        }
    }

    /// <summary>SERVER. Keep the current player until they have / got the book; else the nearest one who needs it.</summary>
    private PlayerHealth ServerTarget()
    {
        PlayerHealth current = FollowedBody();
        if (current != null && NeedsBook(current)) return current;
        if (Time.time < nextPick) return null;
        nextPick = Time.time + 0.5f;

        PlayerHealth best = null;
        float bestSqr = float.MaxValue;
        foreach (PlayerHealth p in PlayerRegistry.All)
        {
            if (p == null || !NeedsBook(p)) continue;
            float d = (p.transform.position - transform.position).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = p; }
        }
        uint id = best != null ? IdOf(best.gameObject) : 0u;
        if (followingId != id) followingId = id;
        offlineTarget = NetworkMode.IsOffline && best != null ? best.gameObject : null;
        return best;
    }

    private bool NeedsBook(PlayerHealth p)
    {
        if (p == null || !p.isActiveAndEnabled || p.IsDead) return false;
        PlayerInventory inventory = p.GetComponent<PlayerInventory>();
        return inventory != null && !inventory.MemoClaimed && !inventory.ServerHasMemoBook(MemoId);
    }

    private PlayerHealth FollowedBody()
    {
        if (followingId == 0) return null;
        if (NetworkMode.IsOffline) return offlineTarget != null ? offlineTarget.GetComponent<PlayerHealth>() : null;
        NetworkIdentity identity;
        if (NetworkServer.active ? NetworkServer.spawned.TryGetValue(followingId, out identity) : NetworkClient.spawned.TryGetValue(followingId, out identity))
            return identity != null ? identity.GetComponent<PlayerHealth>() : null;
        return null;
    }

    private Transform NearestPlayer()
    {
        var players = PlayerRegistry.All;
        Transform nearest = null;
        float best = float.MaxValue;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerHealth p = players[i];
            if (p == null || !p.isActiveAndEnabled) continue;
            float d = (p.transform.position - transform.position).sqrMagnitude;
            if (d < best) { best = d; nearest = p.transform; }
        }
        return nearest;
    }

    // ---- Clients ----------------------------------------------------------------------------

    private void ClientSmooth()
    {
        if (!moved) return; // never moved: keep the scene transform
        transform.position = Vector3.Lerp(transform.position, syncedPosition, 1f - Mathf.Exp(-10f * Time.deltaTime));
        Quaternion want = Quaternion.Euler(0f, syncedYaw, 0f);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * Time.deltaTime);
    }

    /// <summary>Every machine: the speech bubble while it follows someone (everyone sees it).</summary>
    private void UpdateBubble()
    {
        bool show = followingId != 0 && PlayerInventory.InLobby;
        // 17 Nov fix: the big "STAFF / Take your Memo Book" sign and the bubble overlapped (unreadable).
        // The sign shows only while the Staff stands still at its spot and says nothing.
        if (!signLooked)
        {
            signLooked = true;
            sign = transform.Find("Sign");
        }
        if (sign != null)
        {
            bool signOn = !show && (!moved || (transform.position - home).sqrMagnitude < 0.25f);
            if (sign.gameObject.activeSelf != signOn) sign.gameObject.SetActive(signOn);
        }
        if (bubble == null)
        {
            if (!show) return;
            var go = new GameObject("Staff Speech Bubble");
            go.transform.SetParent(transform, false);
            bubble = go.AddComponent<TextMeshPro>();
            // A dark band behind the words so they read on any wall.
            bubble.text = "<mark=#000000C8 padding=\"8,8,4,4\">Staff: \"" + OfferLine + "\"</mark>";
            bubble.richText = true;
            bubble.fontSize = bubbleTextSize;
            bubble.alignment = TextAlignmentOptions.Center;
            bubble.color = new Color(1f, 0.95f, 0.75f);
            bubble.textWrappingMode = TextWrappingModes.Normal;
            bubble.rectTransform.sizeDelta = new Vector2(1.9f, 0.6f);
        }
        if (bubble.gameObject.activeSelf != show) bubble.gameObject.SetActive(show);
        if (!show) return;
        bubble.transform.position = transform.position + Vector3.up * bubbleHeight;
        Camera cam = RuleFx.LocalCamera();
        if (cam != null) bubble.transform.rotation = Quaternion.LookRotation(bubble.transform.position - cam.transform.position, Vector3.up);
    }

    // ---- Interaction --------------------------------------------------------------------------

    public override string GetInteractionPrompt()
    {
        PlayerInventory mine = PlayerInventory.Local;
        if (mine == null) return "[E] Talk to the Staff";
        bool has = mine.CountOf(MemoId) > 0 || mine.IsStored(MemoId);
        return has || mine.MemoClaimed ? "[E] Talk to the Staff" : "[E] Take the Memo Book";
    }

    public override bool CanInteract() => isActiveAndEnabled && PlayerInventory.InLobby;

    /// <summary>SERVER.</summary>
    protected override void OnInteracted(GameObject interactor)
    {
        if (interactor == null || !PlayerInventory.InLobby) return;
        if ((interactor.transform.position - transform.position).sqrMagnitude > ServerRange * ServerRange) return;
        PlayerInventory inventory = interactor.GetComponent<PlayerInventory>();
        if (inventory == null) return;

        if (inventory.ServerHasMemoBook(MemoId))
        {
            Say(interactor, "Staff: \"You already have your Memo Book. Keep it in your hotbar to take it with you.\"");
            return;
        }
        if (inventory.MemoClaimed)
        {
            Say(interactor, "Staff: \"I already gave you this Memo Book. You can buy another one in the shop (999).\"");
            return;
        }

        // The free one: the owner's PC writes it into the save (claim + owned) and adds it, like a purchase.
        inventory.ServerSetMemoClaimed(true);
        if (followingId == IdOf(interactor)) followingId = 0;
        NetworkIdentity id = interactor.GetComponent<NetworkIdentity>();
        if (NetworkMode.IsOffline || (id != null && id.isLocalPlayer && id.isClient)) LocalReceiveBook();
        else if (id != null && id.connectionToClient != null) TargetReceiveBook(id.connectionToClient);
    }

    [TargetRpc]
    private void TargetReceiveBook(NetworkConnectionToClient target) => LocalReceiveBook();

    /// <summary>OWNER'S PC: own the book, remember the claim, put it in the hotbar.</summary>
    private static void LocalReceiveBook()
    {
        SaveManager.ClaimMemoBook(RoomConfig.DemoMapID, MemoId);
        if (PlayerInventory.Local != null) PlayerInventory.Local.RequestAddOwned(MemoId);
        RuleFx.Toast("Staff: \"" + OfferLine + "\" (Memo Book added. Select it and press the read key to open it.)", 5f);
    }

    private void Say(GameObject player, string line)
    {
        NetworkIdentity id = player.GetComponent<NetworkIdentity>();
        if (NetworkMode.IsOffline || (id != null && id.isLocalPlayer && id.isClient)) RuleFx.Toast(line, 4f);
        else if (id != null && id.connectionToClient != null) TargetSay(id.connectionToClient, line);
    }

    [TargetRpc]
    private void TargetSay(NetworkConnectionToClient target, string line) => RuleFx.Toast(line, 4f);

    private static uint IdOf(GameObject player)
    {
        if (NetworkMode.IsOffline) return 1u;
        NetworkIdentity identity = player != null ? player.GetComponent<NetworkIdentity>() : null;
        return identity != null ? identity.netId : 0u;
    }
}
