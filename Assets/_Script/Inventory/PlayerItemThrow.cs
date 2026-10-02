using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Owner reads Q and draws charge; server transfers inventory and simulates the throw.
/// Also (2 Oct): when the player DIES (not downed), the server drops every hotbar
/// item on the floor where they fell, so teammates can pick them up (E).
/// </summary>
[RequireComponent(typeof(PlayerInventory))]
public sealed class PlayerItemThrow : NetworkBehaviour
{
    public const string WorldPrefabPath = "Items/WorldWalkieTalkie";
    [SerializeField] private float tapDuration = 0.2f;
    [SerializeField] private float fullChargeDuration = 1.5f;
    [SerializeField] private float maximumLaunchSpeed = 9f;
    [Range(0.1f, 1f)] [SerializeField] private float movementMultiplier = 0.5f;
    private PlayerInventory inventory;
    private PlayerHealth health;
    private Transform aim;
    private ThrowChargeHUD hud;
    private bool localCharging;
    private int localSlot;
    private string localItem;
    private float localStarted;
    private bool focused = true;
    private bool serverCharging;
    private int serverSlot;
    private string serverItem;
    private double serverStarted;
    private double lastHeartbeat;
    private float nextHeartbeat;
    public bool IsCharging => localCharging;
    public float MovementMultiplier => localCharging ? movementMultiplier : 1f;
    private double Now => NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;

    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        health = GetComponent<PlayerHealth>();
        Camera camera = GetComponentInChildren<Camera>(true);
        aim = camera != null ? camera.transform : transform;
        if (health != null && health.OnDeath != null) health.OnDeath.AddListener(OnDied);
    }

    private bool CanAct => health == null || (!health.IsDead && !health.IsDowned);
    private bool CanUseInput => focused && CanAct && !GameplayInput.Blocked && Cursor.lockState == CursorLockMode.Locked;

    private void Update()
    {
        if (NetworkMode.HasServerAuthority(this) && serverCharging &&
            (!CanAct || inventory.SelectedSlot != serverSlot || inventory.HeldSlot.itemId != serverItem || Now - lastHeartbeat > 1.5))
            serverCharging = false;
        if (!NetworkMode.IsLocalController(this)) return;
        UpdateLocalInput(CanUseInput);
    }

    private void UpdateLocalInput(bool canUseInput)
    {
        if (localCharging && (!canUseInput || inventory.SelectedSlot != localSlot || inventory.HeldSlot.itemId != localItem))
            CancelLocal();
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) { if (localCharging) CancelLocal(); return; }
        if (!localCharging && canUseInput && keyboard.qKey.wasPressedThisFrame)
        {
            ItemCatalog.ItemInfo info = ItemCatalog.Find(inventory.HeldSlot.itemId);
            if (info == null || !info.throwable) return;
            localCharging = true;
            localSlot = inventory.SelectedSlot;
            localItem = inventory.HeldSlot.itemId;
            localStarted = Time.unscaledTime;
            nextHeartbeat = Time.unscaledTime + 0.25f;
            if (NetworkMode.IsOffline) BeginCharge(localSlot, localItem);
            else CmdBeginCharge(localSlot, localItem);
        }
        if (!localCharging) return;
        float elapsed = Time.unscaledTime - localStarted;
        if (elapsed > tapDuration)
        {
            if (hud == null) hud = ThrowChargeHUD.Create();
            hud.Show(ItemCatalog.DisplayName(localItem), Mathf.Clamp01((elapsed - tapDuration) / (fullChargeDuration - tapDuration)));
        }
        if (keyboard.qKey.wasReleasedThisFrame)
        {
            Vector3 direction = aim.forward;
            bool tap = elapsed <= tapDuration;
            FinishLocal();
            if (NetworkMode.IsOffline) ReleaseCharge(direction, tap);
            else CmdReleaseCharge(direction, tap);
        }
        else if (!keyboard.qKey.isPressed) CancelLocal();
        else if (Time.unscaledTime >= nextHeartbeat)
        {
            nextHeartbeat = Time.unscaledTime + 0.25f;
            if (NetworkMode.IsOffline) Heartbeat();
            else CmdHeartbeat();
        }
    }

    [Command] private void CmdBeginCharge(int slot, string id) => BeginCharge(slot, id);
    [Command] private void CmdHeartbeat() => Heartbeat();
    [Command] private void CmdCancelCharge() => serverCharging = false;
    [Command] private void CmdReleaseCharge(Vector3 direction, bool tap) => ReleaseCharge(direction, tap);

    private void BeginCharge(int slot, string id)
    {
        serverCharging = false;
        ItemCatalog.ItemInfo info = ItemCatalog.Find(id);
        if (!CanAct || inventory.SelectedSlot != slot || inventory.HeldSlot.itemId != id || info == null || !info.throwable) return;
        serverSlot = slot;
        serverItem = id;
        serverStarted = lastHeartbeat = Now;
        serverCharging = true;
    }

    private void Heartbeat() { if (serverCharging) lastHeartbeat = Now; }

    private void ReleaseCharge(Vector3 direction, bool tap)
    {
        bool valid = serverCharging && CanAct && inventory.SelectedSlot == serverSlot && inventory.HeldSlot.itemId == serverItem
            && Now - lastHeartbeat <= 1.5;
        serverCharging = false;
        if (!valid || !Finite(direction.x) || !Finite(direction.y) || !Finite(direction.z) || direction.sqrMagnitude < 0.01f) return;
        ItemCatalog.ItemInfo info = ItemCatalog.Find(serverItem);
        if (info == null || !info.throwable || string.IsNullOrEmpty(info.worldPrefabPath)) return;
        GameObject prefab = Resources.Load<GameObject>(info.worldPrefabPath);
        if (prefab == null) { Debug.LogError("Missing world item prefab: " + info.worldPrefabPath, this); return; }
        direction.Normalize();
        float elapsed = Mathf.Max(0f, (float)(Now - serverStarted));
        float charge = tap ? 0f : Mathf.Clamp01((elapsed - tapDuration) / (fullChargeDuration - tapDuration));
        Vector3 forward = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = transform.forward;
        Vector3 origin = transform.position + Vector3.up;
        Vector3 position = origin + forward * 0.7f;
        // Never spawn through a wall or inside another body. Keep the held item if blocked.
        foreach (RaycastHit hit in Physics.SphereCastAll(origin, 0.19f, forward, 0.7f, ~0, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(transform)) return;
        foreach (Collider collider in Physics.OverlapSphere(position, 0.19f, ~0, QueryTriggerInteraction.Ignore))
            if (!collider.transform.IsChildOf(transform)) return;
        InventorySlot state = inventory.HeldSlot;
        GameObject world = Instantiate(prefab, position, Quaternion.LookRotation(forward));
        WorldInventoryItem pickup = world.GetComponent<WorldInventoryItem>();
        Rigidbody body = world.GetComponent<Rigidbody>();
        if (pickup == null || body == null) { Destroy(world); return; }
        pickup.Initialize(state);
        if (inventory.ServerRemoveAt(serverSlot) == null) { Destroy(world); return; }
        if (!NetworkMode.IsOffline) NetworkServer.Spawn(world);
        body.isKinematic = false;
        body.linearVelocity = tap || charge <= 0f ? forward * 0.35f
            : (direction + Vector3.up * 0.25f).normalized * Mathf.Lerp(2f, maximumLaunchSpeed, charge);
        body.angularVelocity = tap ? Vector3.zero : new Vector3(2f, 3f, 1f);
    }

    // ---- Death drop (2 Oct) ---------------------------------------------------

    private bool droppedOnDeath;

    public override void OnStartServer() => droppedOnDeath = false;

    /// <summary>OnDeath runs on every machine; only the server drops the items, once.</summary>
    private void OnDied()
    {
        if (droppedOnDeath || !NetworkMode.HasServerAuthority(this)) return;
        droppedOnDeath = true;
        serverCharging = false;
        ServerDropAll();
    }

    /// <summary>
    /// SERVER (3 Oct, bug #1). This player left a running match: drop their items
    /// where they stood, like a death, so teammates can still pick them up.
    /// Called by RoHRoomManager before the body is destroyed.
    /// </summary>
    public void ServerDropOnLeave()
    {
        if (droppedOnDeath || !NetworkMode.HasServerAuthority(this)) return;
        droppedOnDeath = true;
        serverCharging = false;
        ServerDropAll();
    }

    /// <summary>
    /// SERVER. Every item in the hotbar becomes a WorldInventoryItem around the body
    /// (same pickup as a thrown item). Items without a world prefab are simply lost.
    /// </summary>
    private void ServerDropAll()
    {
        Vector3 basePosition = transform.position + Vector3.up * 0.6f;
        int dropped = 0;
        for (int i = 0; i < PlayerInventory.SlotCount; i++)
        {
            InventorySlot state = inventory.GetSlot(i);
            if (state.IsEmpty) continue;
            ItemCatalog.ItemInfo info = ItemCatalog.Find(state.itemId);
            GameObject prefab = info != null && !string.IsNullOrEmpty(info.worldPrefabPath)
                ? Resources.Load<GameObject>(info.worldPrefabPath) : null;
            if (inventory.ServerRemoveAt(i) == null) continue;
            if (prefab == null) continue;

            // Around the body, OUTSIDE its capsule (radius 0.5), or the dead body
            // blocks the pickup ray. Pulled in if a wall is closer.
            float angle = dropped * 137.5f * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 offset = direction * DropDistance(basePosition, direction, 0.85f);
            GameObject world = Instantiate(prefab, basePosition + offset, Quaternion.Euler(0f, dropped * 70f, 0f));
            WorldInventoryItem pickup = world.GetComponent<WorldInventoryItem>();
            Rigidbody body = world.GetComponent<Rigidbody>();
            if (pickup == null || body == null) { Destroy(world); continue; }
            pickup.Initialize(state);
            if (!NetworkMode.IsOffline) NetworkServer.Spawn(world);
            body.isKinematic = false;
            body.linearVelocity = offset * 2f + Vector3.up * 1f;
            dropped++;
        }
        if (dropped > 0) Debug.Log($"[PlayerItemThrow] {name}: dropped {dropped} item(s).", this);
    }

    /// <summary>How far out an item can go before a wall (never inside the body: min 0.55 m).</summary>
    private float DropDistance(Vector3 origin, Vector3 direction, float wanted)
    {
        float distance = wanted;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, direction, wanted + 0.15f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform)) continue;
            distance = Mathf.Min(distance, hit.distance - 0.15f);
        }
        return Mathf.Max(0.55f, distance);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private void FinishLocal() { localCharging = false; if (hud != null) hud.Hide(); }
    private void CancelLocal()
    {
        FinishLocal();
        if (NetworkMode.IsOffline) serverCharging = false;
        else if (isLocalPlayer && NetworkClient.active) CmdCancelCharge();
    }
    private void OnApplicationFocus(bool value) { focused = value; if (!value && localCharging) CancelLocal(); }
    private void OnDisable() { if (localCharging) CancelLocal(); serverCharging = false; }
    private void OnDestroy() { if (hud != null) Destroy(hud.gameObject); }
}
