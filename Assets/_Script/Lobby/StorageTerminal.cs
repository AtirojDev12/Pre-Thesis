using UnityEngine;

/// <summary>
/// 8 Oct (Mr.k). The locker in the lobby STORAGE ROOM. Look at it and press E:
/// opens YOUR OWN storage (StorageUI). One room for everybody, but each player
/// only sees their own items. Same interaction setup as ShopTerminal (local only,
/// no network: the storage itself is server-checked in PlayerInventory).
///
/// Placed by Tools > Pre-Thesis > Scenes > Lobby: Add Spawn Points + Storage Room.
///
/// 24 Nov 2026: the lockers in the Lobby scene are plain models (MemPhis_Logger) without
/// colliders, so the interaction ray went through them: no name, no [E]. If nothing under
/// this object has a solid collider, one Box Collider is added at start that wraps the
/// locker models (the Sign text is left out). Add your own collider to turn this off.
/// </summary>
[DisallowMultipleComponent]
public sealed class StorageTerminal : MonoBehaviour, ILocalInteractable, IInteractionSurfaceTint
{
    [SerializeField] private string prompt = "[E] Storage (your items)";
    [Tooltip("24 Nov: no solid collider under this object = add a Box Collider around the locker models at start.")]
    [SerializeField] private bool addColliderIfMissing = true;

    private Renderer[] renderers;
    private MaterialPropertyBlock highlightBlock;
    private bool highlighted;

    private void Awake()
    {
        if (addColliderIfMissing) EnsureCollider();
    }

    /// <summary>24 Nov: one Box Collider around every model under this object (not the TMP sign).</summary>
    private void EnsureCollider()
    {
        foreach (Collider c in GetComponentsInChildren<Collider>(true))
            if (c != null && !c.isTrigger) return; // already has one (scene-made)

        bool found = false;
        Bounds local = default;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is ParticleSystemRenderer || r.GetComponent<TMPro.TMP_Text>() != null) continue;
            Bounds b = r.bounds;
            Vector3 min = b.min, max = b.max;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                Vector3 p = transform.InverseTransformPoint(corner);
                if (!found) { local = new Bounds(p, Vector3.zero); found = true; }
                else local.Encapsulate(p);
            }
        }
        if (!found) return;

        var box = gameObject.AddComponent<BoxCollider>();
        box.center = local.center;
        box.size = local.size;
    }

    public string GetInteractionPrompt() => prompt;
    public bool CanInteract() => isActiveAndEnabled && !StorageUI.IsOpen;
    public Transform GetTransform() => transform;

    public void Interact(GameObject interactor)
    {
        // Only the player on this PC opens their own shop.
        PlayerHealth local = PlayerHealth.LocalInstance;
        if (local == null || interactor != local.gameObject) return;
        StorageUI.Open();
    }

    public void SetHighlighted(bool on)
    {
        if (on == highlighted) return;
        highlighted = on;
        if (renderers == null) renderers = GetComponentsInChildren<Renderer>();
        if (highlightBlock == null) highlightBlock = new MaterialPropertyBlock();

        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            if (on)
            {
                r.GetPropertyBlock(highlightBlock);
                highlightBlock.SetColor("_BaseColor", new Color(1f, 0.85f, 0.4f));
                highlightBlock.SetColor("_Color", new Color(1f, 0.85f, 0.4f));
                r.SetPropertyBlock(highlightBlock);
            }
            else
            {
                r.SetPropertyBlock(null);
            }
        }
    }

    private void OnDisable() => SetHighlighted(false);
}
