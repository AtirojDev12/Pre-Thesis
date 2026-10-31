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
    private FlashlightVisual flashlight; // 5 Oct: a dropped flashlight that is on keeps shining
    private bool pickupInProgress;
    [Header("Ground impact VFX")]
    [SerializeField, Min(.1f)] private float minimumImpactSpeed = .65f;
    [SerializeField, Min(1f)] private float fullImpactSpeed = 4.5f;
    [SerializeField, Range(.25f, 3f)] private float impactSize = 1f;
    [SerializeField, Range(0f, 2f)] private float impactDust = 1f;
    [Tooltip("Exclude water layers here; water needs its own splash effect.")]
    [SerializeField] private LayerMask impactSurfaces = ~(1 << 4); // Unity's Water layer
    private float nextImpactTime;
    private Collider[] impactColliders;

    private void OnCollisionEnter(Collision collision)
    {
        if (!NetworkMode.HasServerAuthority(this) || claimed || Time.time < nextImpactTime ||
            (impactSurfaces.value & (1 << collision.collider.gameObject.layer)) == 0 ||
            collision.collider.GetComponentInParent<PlayerHealth>() != null ||
            collision.collider.GetComponentInParent<WorldInventoryItem>() != null) return;
        // The server supplies the actual contact, also for kinematic remote copies.
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            if (Vector3.Dot(contact.normal, Vector3.up) < .35f) continue;
            float speed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
            if (speed < minimumImpactSpeed) continue;
            float power = Mathf.Lerp(.15f, 1f, Mathf.InverseLerp(minimumImpactSpeed, Mathf.Max(minimumImpactSpeed + .1f, fullImpactSpeed), speed));
            nextImpactTime = Time.time + .10f;
            GetImpactFootprint(contact, out Vector3 center, out float footprintScale);
            if (NetworkMode.IsOffline) PlayImpact(center, contact.normal, power, footprintScale, impactDust);
            else RpcImpact(center, contact.normal, power, footprintScale, impactDust);
            break;
        }
    }

    private void GetImpactFootprint(ContactPoint contact, out Vector3 center, out float scale)
    {
        Bounds bounds = new Bounds(body.worldCenterOfMass, Vector3.zero);
        bool found = false;
        foreach (var collider in impactColliders)
        {
            if (collider == null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy) continue;
            if (!found) { bounds = collider.bounds; found = true; }
            else bounds.Encapsulate(collider.bounds);
        }
        // Centre the burst under the item, while keeping it on the actual supporting plane.
        center = bounds.center - contact.normal * Vector3.Dot(bounds.center - contact.point, contact.normal);
        float longestSide = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        scale = impactSize * Mathf.Clamp(Mathf.Sqrt(longestSide / .2f), 1f, 2f);
    }

    [ClientRpc]
    private void RpcImpact(Vector3 point, Vector3 normal, float power, float scale, float dust) => PlayImpact(point, normal, power, scale, dust);

    private void PlayImpact(Vector3 point, Vector3 normal, float power, float scale, float dust) =>
        PreThesis.Vfx.ItemImpactVfx.Spawn(point, normal, power, scale, dust);

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        impactColliders = GetComponentsInChildren<Collider>(true);
        visual = GetComponentInChildren<WalkieTalkieVisual>();
        flashlight = GetComponentInChildren<FlashlightVisual>();
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
    private void UpdateVisual()
    {
        if (visual != null) visual.SetPower(item.poweredOn);
        if (flashlight != null) flashlight.Show(item, true);
    }

    public string DisplayName => ItemCatalog.DisplayName(item.itemId);
    /// <summary>31 Oct: the item ID (MissingPartsGhost removes its parts from the map).</summary>
    public string ItemId => item.itemId;

    public string GetInteractionPrompt()
    {
        // You already carry one: this becomes a SPARE (only to give away).
        string label = ItemCatalog.DisplayName(item.itemId) + (item.Units > 1 ? " x" + item.Units : "");
        // 5 Oct: stackable (Battery) joins your stack; says so when it is full.
        if (ItemCatalog.MaxStack(item.itemId) > 1)
            return PlayerInventory.Local != null && !PlayerInventory.Local.CanTake(item.itemId)
                ? label + " (you carry the max)" : "[E] Pick up " + label;
        if (LocalCarries(item.itemId)) return "[E] Pick up " + label + " (spare)";
        return "[E] Pick up " + label;
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

        // 5 Oct: a stack (Batteries) may only partly fit: take what fits, leave the rest here.
        if (ItemCatalog.MaxStack(item.itemId) > 1)
        {
            int taken = inventory.ServerAddStack(item);
            if (taken <= 0) return;
            if (taken < item.Units)
            {
                InventorySlot rest = item;
                rest.count = item.Units - taken;
                item = rest; // SyncVar: everyone sees the smaller pile
                return;
            }
            pickupInProgress = true;
        }
        else
        {
            // Latch before granting: a second command can never grant this item again.
            pickupInProgress = true;
            if (!inventory.ServerAddItem(item)) { pickupInProgress = false; return; }
        }
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
        if (flashlight != null) flashlight.Show(InventorySlot.Empty, false);
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
