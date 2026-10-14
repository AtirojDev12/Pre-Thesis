using System;
using UnityEngine;

public enum HeldItemLocation { None, Hand, Placed }

/// <summary>Presentation data. Recipes, readiness and server authorization belong to the minigame.</summary>
[Serializable]
public sealed class HeldItemDefinition
{
    public string id;
    public GameObject prefab;
    [Min(0f)] public float placedSize = .224f;

    public HeldItemDefinition(string id, GameObject prefab, float placedSize = .224f)
    {
        this.id = id;
        this.prefab = prefab;
        this.placedSize = placedSize;
    }
}

/// <summary>Reusable single task-item slot. Contains no input, recipe, HUD or networking rules.</summary>
[DisallowMultipleComponent]
public sealed class ItemHoldingSystem : MonoBehaviour
{
    [SerializeField] private Transform handAnchor;
    [SerializeField] private Vector3 handPosition;
    [SerializeField] private Vector3 handRotation;
    public HeldItemDefinition Definition { get; private set; }
    public GameObject CurrentPrefab { get; private set; }
    public GameObject Visual { get; private set; }
    public HeldItemLocation Location { get; private set; }
    public Transform PlacementAnchor { get; private set; }
    public int PlacementId { get; private set; }
    public uint Revision { get; private set; }
    public bool HasItem => Location != HeldItemLocation.None;
    public bool IsInHand => Location == HeldItemLocation.Hand;
    public bool IsPlaced => Location == HeldItemLocation.Placed;
    public bool SubmissionPending { get; private set; }
    public bool TransferPending { get; private set; }
    public bool IsLocked => SubmissionPending || TransferPending;
    public event Action Changed;
    public event Action<HeldItemDefinition> ItemCleared;

    public void ConfigureHandAnchor(Transform anchor, Vector3 position, Vector3 rotation)
    {
        handAnchor = anchor;
        handPosition = position;
        handRotation = rotation;
    }

    public bool TryPickUp(HeldItemDefinition definition)
    {
        if (HasItem || IsLocked || definition == null || string.IsNullOrWhiteSpace(definition.id) || definition.prefab == null || handAnchor == null) return false;
        GameObject visual = CreateVisual(definition.prefab, false);
        Definition = definition;
        CurrentPrefab = definition.prefab;
        Visual = visual;
        Location = HeldItemLocation.Hand;
        CommitChange();
        return true;
    }

    public bool TryReplaceVisual(GameObject prefab)
    {
        if (!HasItem || IsLocked || prefab == null || (IsInHand && handAnchor == null) || (IsPlaced && PlacementAnchor == null)) return false;
        GameObject replacement = CreateVisual(prefab, IsPlaced);
        ClearVisual();
        Visual = replacement;
        CurrentPrefab = prefab;
        CommitChange();
        return true;
    }

    public bool TryPlace(Transform anchor, int placementId = 0)
    {
        if (!IsInHand || IsLocked || Visual == null || anchor == null || placementId < 0) return false;
        PlacementAnchor = anchor;
        PlacementId = placementId;
        Location = HeldItemLocation.Placed;
        PlaceVisualAtAnchor(Visual, anchor, Definition.placedSize);
        CommitChange();
        return true;
    }

    public bool TryRetrieve()
    {
        if (!IsPlaced || IsLocked || CurrentPrefab == null || handAnchor == null) return false;
        GameObject replacement = CreateVisual(CurrentPrefab, false);
        ClearVisual();
        Visual = replacement;
        PlacementAnchor = null;
        PlacementId = 0;
        Location = HeldItemLocation.Hand;
        CommitChange();
        return true;
    }

    public bool TryDiscardInHand()
    {
        if (!IsInHand || IsLocked) return false;
        Clear();
        return true;
    }

    public void SetTransferPending(bool pending)
    {
        if (TransferPending == pending) return;
        TransferPending = pending;
        Changed?.Invoke();
    }

    public bool TryBeginSubmission(out uint revision)
    {
        revision = Revision;
        if (!IsInHand || IsLocked) return false;
        SubmissionPending = true;
        Changed?.Invoke();
        return true;
    }

    public bool FinishSubmission(uint revision, bool consumed)
    {
        if (!SubmissionPending || Revision != revision) return false;
        SubmissionPending = false;
        if (consumed) Clear();
        else Changed?.Invoke();
        return true;
    }

    /// <summary>Unconditional lifecycle/authority cleanup; player discard should use TryDiscardInHand.</summary>
    public void Clear()
    {
        if (!HasItem && Visual == null && !IsLocked) return;
        HeldItemDefinition previous = Definition;
        ClearVisual();
        Definition = null;
        CurrentPrefab = null;
        PlacementAnchor = null;
        PlacementId = 0;
        Location = HeldItemLocation.None;
        SubmissionPending = false;
        TransferPending = false;
        CommitChange();
        if (previous != null) ItemCleared?.Invoke(previous);
    }

    private GameObject CreateVisual(GameObject prefab, bool placed)
    {
        GameObject visual = Instantiate(prefab, placed ? null : handAnchor, false);
        visual.name = "Held " + prefab.name;
        visual.transform.localPosition = handPosition;
        visual.transform.localRotation = Quaternion.Euler(handRotation);
        foreach (Transform child in visual.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 2;
        foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Rigidbody body in visual.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
        if (placed) PlaceVisualAtAnchor(visual, PlacementAnchor, Definition.placedSize);
        return visual;
    }

    // Anchors mark the bottom centre, independent of prefab pivot or parent scale.
    public static void PlaceVisualAtAnchor(GameObject visual, Transform anchor, float size)
    {
        if (visual == null || anchor == null) return;
        visual.transform.SetParent(null, true);
        visual.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
        float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (size > 0f && longest > .001f) visual.transform.localScale *= size / longest;
        bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
        visual.transform.position += anchor.position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
    }

    private void CommitChange() { Revision++; Changed?.Invoke(); }
    private void ClearVisual()
    {
        if (Visual == null) return;
        Visual.SetActive(false);
        Destroy(Visual);
        Visual = null;
    }
    private void LateUpdate()
    {
        if (!IsPlaced) return;
        if (PlacementAnchor == null) { Clear(); return; }
        PlaceVisualAtAnchor(Visual, PlacementAnchor, Definition.placedSize);
    }
    private void OnDisable() => Clear();
    private void OnDestroy() => ClearVisual();
}
