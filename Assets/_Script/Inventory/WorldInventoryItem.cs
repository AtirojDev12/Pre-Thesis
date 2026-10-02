using Mirror;
using UnityEngine;

/// <summary>One server-owned physical item. A successful pickup consumes it exactly once.</summary>
[RequireComponent(typeof(Rigidbody), typeof(NetworkIdentity))]
public sealed class WorldInventoryItem : NetworkBehaviour, IInteractable
{
    [SyncVar(hook = nameof(OnItemChanged))] private InventorySlot item;
    [SyncVar(hook = nameof(OnClaimedChanged))] private bool claimed;
    private Rigidbody body;
    private WalkieTalkieVisual visual;
    private bool pickupInProgress;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        visual = GetComponentInChildren<WalkieTalkieVisual>();
    }

    public void Initialize(InventorySlot state)
    {
        item = state;
        UpdateVisual();
    }

    public override void OnStartClient()
    {
        // Host uses the server's dynamic body. Remote clients only interpolate transforms.
        if (!isServer)
        {
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.isKinematic = true;
        }
        UpdateVisual();
        if (claimed) Hide();
    }

    private void OnItemChanged(InventorySlot oldItem, InventorySlot newItem) => UpdateVisual();
    private void OnClaimedChanged(bool oldValue, bool newValue) { if (newValue) Hide(); }
    private void UpdateVisual() { if (visual != null) visual.SetPower(item.poweredOn); }

    public string GetInteractionPrompt()
    {
        // You already carry one: this becomes a SPARE (only to give away).
        if (LocalCarries(item.itemId)) return "[E] Pick up " + ItemCatalog.DisplayName(item.itemId) + " (spare)";
        return "[E] Pick up " + ItemCatalog.DisplayName(item.itemId);
    }

    private static bool LocalCarries(string itemId)
    {
        PlayerInventory local = PlayerInventory.Local;
        if (local == null || string.IsNullOrEmpty(itemId)) return false;
        for (int i = 0; i < local.Count; i++)
            if (local.GetSlot(i).itemId == itemId) return true;
        return false;
    }
    public bool CanInteract() => !claimed && !pickupInProgress && isActiveAndEnabled && !item.IsEmpty;
    public Transform GetTransform() => transform;

    public void Interact(GameObject interactor)
    {
        if (!NetworkMode.HasServerAuthority(this) || !CanInteract() || interactor == null) return;
        PlayerHealth health = interactor.GetComponent<PlayerHealth>();
        PlayerInventory inventory = interactor.GetComponent<PlayerInventory>();
        if (inventory == null || (health != null && (health.IsDead || health.IsDowned))) return;
        if ((interactor.transform.position - transform.position).sqrMagnitude > 4.5f * 4.5f) return;
        // Test line of sight on the server as well as the local interaction ray.
        Vector3 origin = interactor.transform.position + Vector3.up;
        Vector3 offset = transform.position - origin;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, offset.normalized, offset.magnitude,
                     ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(interactor.transform) || hit.transform.IsChildOf(transform)) continue;
            // Player bodies (e.g. the dead owner lying next to the item) do not block a pickup.
            if (hit.collider.GetComponentInParent<PlayerHealth>() != null) continue;
            return;
        }

        // Latch before granting: a second command can never grant this item again.
        pickupInProgress = true;
        if (!inventory.ServerAddItem(item)) { pickupInProgress = false; return; }
        claimed = true;
        Hide();
        if (NetworkServer.active)
        {
            RpcHide();
            NetworkServer.Destroy(gameObject);
        }
        else Destroy(gameObject);
    }

    [ClientRpc] private void RpcHide() => Hide();

    private void Hide()
    {
        // Disable physics, visuals and interaction immediately, before deferred destruction.
        foreach (Collider collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>()) renderer.enabled = false;
        InteractionOutline outline = GetComponent<InteractionOutline>();
        if (outline != null) outline.SetVisible(false);
        body.isKinematic = true;
        body.detectCollisions = false;
        enabled = false;
    }
}
