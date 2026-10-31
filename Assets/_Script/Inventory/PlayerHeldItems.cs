using System;
using Mirror;
using UnityEngine;

[Serializable]
public struct TaskHeldState
{
    public uint revision;
    public bool hasItem, isCup, hasPopcorn, isRefill, ghostMixed;
    public PopcornFlavor flavor;
    // Zero means in hand; otherwise the stable preparation station ID plus one.
    public int placementStation;
    public bool IsReady => hasItem && !isRefill && PopcornRecipe.IsOrder(flavor);
    public bool IsInHand => hasItem && placementStation == 0;
}

/// <summary>Replicates task-item state; radio uses the existing server-owned inventory.</summary>
[RequireComponent(typeof(PlayerInventory))]
public sealed class PlayerHeldItems : NetworkBehaviour
{
    [SyncVar] private TaskHeldState taskItem;
    [Header("Remote chest placement (world metres)")]
    [SerializeField] private Vector3 taskOffset = new Vector3(-.18f, -.1f, .3f);
    [SerializeField] private Vector3 radioOffset = new Vector3(.18f, -.08f, .3f);
    [Tooltip("24 Oct (Mr.k): where other players see your map (only while it is in your hand).")]
    [SerializeField] private Vector3 mapOffset = new Vector3(.14f, -.05f, .32f);
    private uint localRevision;
    private PlayerInventory inventory;
    private PlayerVoice voice;
    private PlayerHealth health;
    private Transform chest;
    private GameObject taskVisual, radioVisual, mapVisual;
    private GameObject partVisual; // 31 Oct: a Missing Parts ghost body part
    private string partVisualId;
    private GameObject renderedPrefab;
    private TaskHeldState renderedState;
    public TaskHeldState TaskItem => taskItem;

    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        voice = GetComponent<PlayerVoice>();
        health = GetComponent<PlayerHealth>();
    }

    public uint Publish(TaskHeldState state)
    {
        if (!NetworkMode.IsLocalController(this)) return 0;
        state.revision = ++localRevision;
        if (NetworkMode.IsOffline) SetTask(state);
        else CmdSetTask(state);
        return state.revision;
    }

    [Command] private void CmdSetTask(TaskHeldState state) => SetTask(state);
    private void SetTask(TaskHeldState state)
    {
        if (state.revision <= taskItem.revision) return;
        if (state.hasItem && health != null && (health.IsDead || health.IsDowned)) return;
        if (state.flavor != PopcornFlavor.None && !PopcornRecipe.IsOrder(state.flavor)) return;
        if (state.IsReady && state.isCup != PopcornRecipe.IsDrink(state.flavor)) return;
        if (state.IsReady && !state.isCup && !state.hasPopcorn) return;
        if ((state.isCup && state.hasPopcorn) || (state.ghostMixed && !state.IsReady)) return;
        if (state.isRefill && (state.isCup || state.hasPopcorn || state.flavor != PopcornFlavor.None || state.ghostMixed)) return;
        if (state.placementStation != 0) return;
        // A hand-state publication cannot discard a bottle left on a machine.
        if (!NetworkMode.IsOffline && taskItem.placementStation > 0 && !state.hasItem &&
            health != null && !health.IsDead && !health.IsDowned) return;
        if (!NetworkMode.IsOffline && state.hasItem && taskItem.placementStation > 0 && !state.isCup) return;
        if (!NetworkMode.IsOffline && state.hasItem && state.isCup)
        {
            // Placement and drink completion are server-owned transitions. Publishing
            // hand visuals cannot bypass the placement step or the server stopwatch.
            if (state.placementStation != taskItem.placementStation) return;
            if (state.IsReady && (!taskItem.IsReady || taskItem.flavor != state.flavor)) return;
            if (taskItem.IsReady && !state.IsReady) return;
        }
        if (!state.hasItem) state = new TaskHeldState { revision = state.revision };
        taskItem = state;
    }

    public bool ServerConsume(uint revision)
    {
        if (!NetworkMode.HasServerAuthority(this) || taskItem.revision != revision || !taskItem.IsReady || !taskItem.IsInHand) return false;
        taskItem = new TaskHeldState { revision = revision };
        return true;
    }

    public bool ServerReleaseEmptyBottle(uint revision)
    {
        if (!isServer || taskItem.revision != revision || !taskItem.IsInHand || !taskItem.isCup || taskItem.IsReady) return false;
        taskItem = new TaskHeldState { revision = revision };
        return true;
    }

    public bool ServerReceiveBottle(PopcornFlavor flavor, uint revision)
    {
        if (!isServer || taskItem.hasItem || taskItem.revision != revision) return false;
        taskItem = new TaskHeldState { revision = revision, hasItem = true, isCup = true, flavor = flavor };
        return true;
    }

    private void LateUpdate()
    {
        if (isServer && health != null && (health.IsDead || health.IsDowned) && taskItem.hasItem)
            taskItem = new TaskHeldState { revision = taskItem.revision };
        if (NetworkMode.IsLocalController(this)) return;
        if (health != null && (health.IsDead || health.IsDowned))
        {
            Clear(ref taskVisual); Clear(ref radioVisual); Clear(ref mapVisual); Clear(ref partVisual); renderedPrefab = null;
            if (isServer && taskItem.hasItem) taskItem = new TaskHeldState { revision = taskItem.revision };
            return;
        }
        PopcornItemHoldingSystem holder = PopcornPreparation.Instance != null ? PopcornPreparation.Instance.Holder : null;
        GameObject prefab = holder != null ? holder.PrefabFor(taskItem) : null;
        if (!taskItem.hasItem || prefab == null) { Clear(ref taskVisual); renderedPrefab = null; }
        else
        {
            if (taskVisual == null || renderedPrefab != prefab)
            {
                Clear(ref taskVisual);
                taskVisual = CreateVisual(prefab, "Remote task item", taskItem.isRefill ? .3f : taskItem.isCup ? .18f : .24f);
                renderedPrefab = prefab;
                if (!taskItem.isCup) PopcornItemHoldingSystem.TintVisual(taskVisual, taskItem.flavor, taskItem.ghostMixed);
            }
            if (!renderedState.Equals(taskItem))
            {
                if (!taskItem.isCup) PopcornItemHoldingSystem.TintVisual(taskVisual, taskItem.flavor, taskItem.ghostMixed);
                renderedState = taskItem;
            }
            var placed = holder != null && taskItem.placementStation > 0 ? PopcornPreparation.Instance.StationAt(taskItem.placementStation - 1) : null;
            if (placed != null && placed.PlacementAnchor != null)
            {
                PopcornItemHoldingSystem.PlaceBottleVisual(taskVisual, placed.PlacementAnchor);
                PlacedBottleInteractable.ConfigureVisual(taskVisual, placed, netId, taskItem.IsReady);
            }
            else
            {
                PlacedBottleInteractable.DisableVisual(taskVisual);
                Place(taskVisual, taskOffset);
            }
        }
        if (!inventory.IsHoldingRadio) Clear(ref radioVisual);
        else
        {
            if (radioVisual == null)
            {
                GameObject radio = Resources.Load<GameObject>("Items/WalkieTalkie");
                if (radio != null) radioVisual = CreateVisual(radio, "Remote walkie-talkie", .24f);
            }
            if (radioVisual != null)
            {
                Place(radioVisual, radioOffset);
                radioVisual.GetComponentInChildren<WalkieTalkieVisual>()?.SetPower(inventory.HeldSlot.poweredOn, voice != null && voice.RadioTransmitting);
            }
        }
        // 24 Oct (Mr.k): other players see the map in your hand. The model is the Map inside WorldMap.
        if (!inventory.IsHoldingMap) Clear(ref mapVisual);
        else
        {
            if (mapVisual == null)
            {
                GameObject world = Resources.Load<GameObject>(ItemCatalog.WorldMapPath);
                if (world != null && world.transform.childCount > 0)
                    mapVisual = CreateVisual(world.transform.GetChild(0).gameObject, "Remote map", .3f);
            }
            if (mapVisual != null) Place(mapVisual, mapOffset);
        }
        // 31 Oct (Mr.k): other players see the ghost part in your hand (model = first child of its world prefab).
        string heldId = inventory.HeldSlot.IsEmpty ? null : inventory.HeldSlot.itemId;
        if (!ItemCatalog.IsGhostPart(heldId)) { Clear(ref partVisual); partVisualId = null; }
        else
        {
            if (partVisual == null || partVisualId != heldId)
            {
                Clear(ref partVisual);
                partVisualId = heldId;
                ItemCatalog.ItemInfo info = ItemCatalog.Find(heldId);
                GameObject world = info != null ? Resources.Load<GameObject>(info.worldPrefabPath) : null;
                if (world != null && world.transform.childCount > 0)
                    partVisual = CreateVisual(world.transform.GetChild(0).gameObject, "Remote ghost part", .3f);
            }
            if (partVisual != null) Place(partVisual, mapOffset);
        }
    }

    private Transform Chest()
    {
        if (chest != null) return chest;
        Animator animator = GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
            chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
        return chest;
    }

    private void Place(GameObject model, Vector3 offset)
    {
        Transform bone = Chest();
        Vector3 centre = bone != null ? bone.position : transform.position + Vector3.up * 1.3f;
        model.transform.position = centre + transform.right * offset.x + Vector3.up * offset.y + transform.forward * offset.z;
        model.transform.rotation = Quaternion.LookRotation(transform.forward, Vector3.up);
    }

    private GameObject CreateVisual(GameObject prefab, string name, float size)
    {
        GameObject anchor = new GameObject(name);
        anchor.transform.SetParent(transform, false);
        GameObject model = Instantiate(prefab, anchor.transform);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        foreach (Transform child in model.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 2;
        foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
        if (DrinkCupVisual.FitInHand(model, anchor.transform.position)) return anchor;
        if (PopcornBucketVisual.FitInHand(model, anchor.transform.position)) return anchor;
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (longest > .001f) model.transform.localScale *= size / longest;
            bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            model.transform.position += anchor.transform.position - bounds.center;
        }
        return anchor;
    }

    private static void Clear(ref GameObject model)
    {
        if (model == null) return;
        model.SetActive(false); Destroy(model); model = null;
    }
    private void OnDisable() { Clear(ref taskVisual); Clear(ref radioVisual); Clear(ref mapVisual); Clear(ref partVisual); renderedPrefab = null; }
}
