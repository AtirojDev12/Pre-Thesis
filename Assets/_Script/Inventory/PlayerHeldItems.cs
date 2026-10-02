using System;
using Mirror;
using UnityEngine;

[Serializable]
public struct TaskHeldState
{
    public uint revision;
    public bool hasItem, isCup, hasPopcorn, isRefill, ghostMixed;
    public PopcornFlavor flavor;
    public bool IsReady => hasItem && !isRefill && PopcornRecipe.IsOrder(flavor);
}

/// <summary>Replicates task-item state; radio uses the existing server-owned inventory.</summary>
[RequireComponent(typeof(PlayerInventory))]
public sealed class PlayerHeldItems : NetworkBehaviour
{
    [SyncVar] private TaskHeldState taskItem;
    [Header("Remote chest placement (world metres)")]
    [SerializeField] private Vector3 taskOffset = new Vector3(-.18f, -.1f, .3f);
    [SerializeField] private Vector3 radioOffset = new Vector3(.18f, -.08f, .3f);
    private uint localRevision;
    private PlayerInventory inventory;
    private PlayerVoice voice;
    private PlayerHealth health;
    private Transform chest;
    private GameObject taskVisual, radioVisual;
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
        if (!state.hasItem) state = new TaskHeldState { revision = state.revision };
        taskItem = state;
    }

    public bool ServerConsume(uint revision)
    {
        if (!NetworkMode.HasServerAuthority(this) || taskItem.revision != revision || !taskItem.IsReady) return false;
        taskItem = new TaskHeldState { revision = revision };
        return true;
    }

    private void LateUpdate()
    {
        if (NetworkMode.IsLocalController(this)) return;
        if (health != null && (health.IsDead || health.IsDowned))
        {
            Clear(ref taskVisual); Clear(ref radioVisual); renderedPrefab = null;
            if (isServer && taskItem.hasItem) taskItem = new TaskHeldState { revision = taskItem.revision };
            return;
        }
        ItemHoldingSystem holder = PopcornPreparation.Instance != null ? PopcornPreparation.Instance.Holder : null;
        GameObject prefab = holder != null ? holder.PrefabFor(taskItem) : null;
        if (!taskItem.hasItem || prefab == null) { Clear(ref taskVisual); renderedPrefab = null; }
        else
        {
            if (taskVisual == null || renderedPrefab != prefab)
            {
                Clear(ref taskVisual);
                taskVisual = CreateVisual(prefab, "Remote task item", taskItem.isRefill ? .3f : taskItem.isCup ? .18f : .24f);
                renderedPrefab = prefab;
                ItemHoldingSystem.TintVisual(taskVisual, taskItem.flavor, taskItem.ghostMixed);
            }
            if (!renderedState.Equals(taskItem))
            {
                ItemHoldingSystem.TintVisual(taskVisual, taskItem.flavor, taskItem.ghostMixed);
                renderedState = taskItem;
            }
            Place(taskVisual, taskOffset);
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
    private void OnDisable() { Clear(ref taskVisual); Clear(ref radioVisual); renderedPrefab = null; }
}
