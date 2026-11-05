using Mirror;
using UnityEngine;

/// <summary>
/// 5 Nov 2026 (Mr.k): the STAFF NPC in the lobby. Press E: it gives you a WORK SHEET
/// (item "worksheet") that explains tonight's job in detail (WorksheetReader).
///   - The sheet takes a real hotbar slot. Keep it in the hotbar to carry it into the
///     match (one match only, then it is gone; never saved).
///   - Already have one: E again = give it back (frees the slot).
///   - Hotbar full: the NPC says so.
///
/// SERVER AUTHORITY: the server checks and adds / removes the item (PlayerInventory).
/// The answer line is shown only to that player (TargetRpc).
/// Placed by Tools > Pre-Thesis > Scenes > Lobby: Add Staff NPC (where the Scene view looks).
/// </summary>
[DisallowMultipleComponent]
public sealed class StaffNpc : InteractableBase, IPromptOffset
{
    private const float ServerRange = 4.5f;

    [Header("Face the nearest player")]
    [Tooltip("How fast the Staff turns to face the nearest player (degrees per second).")]
    [SerializeField, Min(0f)] private float turnSpeed = 270f;

    [Tooltip("Height of the [E] prompt above the Staff's feet (metres).")]
    [SerializeField, Min(0f)] private float promptHeight = 1.75f;

    public Vector3 PromptOffset => Vector3.up * promptHeight;

    // 5 Nov 2026 (Mr.k): the Staff always turns (yaw only) to the nearest player.
    // Local only, on every PC: player positions are already synced, so every PC picks
    // the same player. 0 network traffic. The player list refreshes every 0.5 s (PlayerRegistry).
    private void Update()
    {
        var players = PlayerRegistry.All;
        Vector3 me = transform.position;
        Transform nearest = null;
        float best = float.MaxValue;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerHealth p = players[i];
            if (p == null || !p.isActiveAndEnabled) continue;
            float d = (p.transform.position - me).sqrMagnitude;
            if (d < best) { best = d; nearest = p.transform; }
        }
        if (nearest == null) return;

        Vector3 dir = nearest.position - me;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion want = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * Time.deltaTime);
    }

    public override string GetInteractionPrompt()
    {
        PlayerInventory mine = PlayerInventory.Local;
        bool has = mine != null && mine.CountOf(ItemCatalog.Worksheet) > 0;
        return has ? "[E] Give back the work sheet" : "[E] Take a work sheet";
    }

    public override bool CanInteract() => isActiveAndEnabled && PlayerInventory.InLobby;

    /// <summary>SERVER.</summary>
    protected override void OnInteracted(GameObject interactor)
    {
        if (interactor == null || !PlayerInventory.InLobby) return;
        if ((interactor.transform.position - transform.position).sqrMagnitude > ServerRange * ServerRange) return;
        PlayerInventory inventory = interactor.GetComponent<PlayerInventory>();
        if (inventory == null) return;

        if (inventory.ServerCountOf(ItemCatalog.Worksheet) > 0)
        {
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                InventorySlot s = inventory.GetSlot(i);
                if (!s.IsEmpty && ItemCatalog.IsWorksheet(s.itemId)) { inventory.ServerRemoveAt(i); break; }
            }
            Say(interactor, "Staff: \"Okay, I'll take it back.\"");
            return;
        }

        if (!inventory.HasEmptySlot)
        {
            Say(interactor, "Staff: \"Your hands are full. Free a hotbar slot first.\"");
            return;
        }
        inventory.ServerAddItem(ItemCatalog.Worksheet);
        Say(interactor, "Staff: \"Here's tonight's job. Click to open it and pick a topic. Keep it in your hotbar to take it with you.\"");
    }

    private void Say(GameObject player, string line)
    {
        NetworkIdentity id = player.GetComponent<NetworkIdentity>();
        if (NetworkMode.IsOffline || (id != null && id.isLocalPlayer && id.isClient)) RuleFx.Toast(line, 4f);
        else if (id != null && id.connectionToClient != null) TargetSay(id.connectionToClient, line);
    }

    [TargetRpc]
    private void TargetSay(NetworkConnectionToClient target, string line) => RuleFx.Toast(line, 4f);
}
