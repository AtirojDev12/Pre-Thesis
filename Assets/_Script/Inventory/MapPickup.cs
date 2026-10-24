using Mirror;
using UnityEngine;

/// <summary>
/// 24 Oct 2026 (Mr.k): the map box. Press E at it = get the Map item in your hotbar.
///
/// Rules (server decides):
///   - 1 map per player per match. Dropping it does not give you a new one from the box
///     (pick yours up again). A friend's dropped map can still be picked up.
///   - Needs a free hotbar slot. Dead / downed players cannot take one.
///   - The map is match only: never sold, never saved (ItemCatalog.Map).
///
/// SETUP: select the box in the scene, then Tools > Pre-Thesis > Scenes > Map: Make Selected the Map Box
/// (or Add Component > Map Pickup). The box needs a Collider on a layer the player can interact with.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))] // E is sent to the server through the box's NetworkIdentity
public sealed class MapPickup : InteractableBase
{
    [Header("Prompts ([E] becomes the player's own key)")]
    [SerializeField] private string takePrompt = "[E] To get the map";
    [SerializeField] private string havePrompt = "You already have a map";
    [SerializeField] private string takenPrompt = "You already took a map";
    [SerializeField] private string fullPrompt = "Hotbar full";

    // Players (body netId) who took a map from this box. Synced so each client's prompt is right.
    private readonly SyncList<uint> takenBy = new SyncList<uint>();
    private bool offlineTaken; // offline test scenes: one local player

    protected override void Awake()
    {
        base.Awake();
        // Same as PlayerInventory: allow writes in the project's offline test mode.
        System.Func<bool> mirrorCanWrite = takenBy.IsWritable;
        takenBy.IsWritable = () => NetworkMode.IsOffline || mirrorCanWrite();
    }

    public override string GetInteractionPrompt()
    {
        PlayerInventory local = PlayerInventory.Local;
        if (local == null) return takePrompt;
        if (local.CountOf(ItemCatalog.Map) > 0) return havePrompt;
        if (HasTaken(local)) return takenPrompt;
        if (!local.HasEmptySlot) return fullPrompt;
        return takePrompt;
    }

    private bool HasTaken(PlayerInventory player)
    {
        if (NetworkMode.IsOffline) return offlineTaken;
        return player != null && takenBy.Contains(player.netId);
    }

    /// <summary>SERVER (InteractableBase routes the E press here).</summary>
    protected override void OnInteracted(GameObject interactor)
    {
        if (interactor == null) return;
        PlayerInventory inventory = interactor.GetComponent<PlayerInventory>();
        PlayerHealth health = interactor.GetComponent<PlayerHealth>();
        if (inventory == null || (health != null && (health.IsDead || health.IsDowned))) return;
        if (inventory.CountOf(ItemCatalog.Map) > 0 || HasTaken(inventory)) return;
        if (!inventory.ServerAddItem(ItemCatalog.Map)) return; // hotbar full

        if (NetworkMode.IsOffline) offlineTaken = true;
        else takenBy.Add(inventory.netId);
    }
}
