using Mirror;
using UnityEngine;

/// <summary>
/// 17 Nov 2026 (Mr.k, Task 7): the two SYRINGES.
///
///   Syringe in hand + HOLD the use key (default Left Click) 2 s:
///     - looking at a DOWNED friend close by = gets them up,
///     - else = heals yourself.
///   Paid syringe: heals 75% of the MISSING health (not rounded). Free syringe: +15.
///   Getting a friend up gives them the same heal (from 0), at least 1 health.
///   All numbers are in GearTuning (Tools > Pre-Thesis > Designer Settings > Walkies + Syringes).
///
/// SERVER AUTHORITY: the owner only shows the bar and asks. The server checks that the
/// syringe is still in the hand, that the hold really lasted (start time on the server),
/// that the friend is still downed and close, then uses up ONE syringe and heals.
/// Added by PlayerInventory.Awake (the Player prefab needs no edit). A MonoBehaviour on purpose:
/// a NetworkBehaviour added at runtime has no network slot, so the Commands go through
/// PlayerInventory (CmdSyringeStart / CmdSyringeFinish).
/// </summary>
[DisallowMultipleComponent]
public sealed class SyringeController : MonoBehaviour
{
    private static readonly Vector3 HeldPosition = new Vector3(0.24f, -0.26f, 0.45f);

    private PlayerInventory inventory;
    private PlayerHealth health;
    private Camera ownCamera;

    // Owner.
    private float holdTimer;
    private PlayerHealth holdTarget;   // null = self
    private bool holding;
    private GameObject heldModel;
    private string heldModelFor;

    // Server.
    private double serverStart = -1;
    private uint serverTarget;
    private string serverItem;

    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        health = GetComponent<PlayerHealth>();
    }

    private static double Now => NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;
    private static GearTuning.SyringeUse Use => GearTuning.Current.syringeUse;

    // ---- Owner --------------------------------------------------------------------------

    private void Update()
    {
        if (inventory == null || !NetworkMode.IsLocalController(inventory)) return;
        UpdateHeldModel();

        bool canAct = inventory.IsHoldingSyringe && !GameplayInput.Blocked && Cursor.lockState == CursorLockMode.Locked
            && health != null && !health.IsDead && !health.IsDowned;
        bool pressed = canAct && Keybinds.IsPressed(GameAction.UseSyringe);
        if (!pressed)
        {
            if (holding) StopHold();
            return;
        }

        PlayerHealth target = DownedFriendInFront();
        if (!holding || target != holdTarget)
        {
            if (target == null && !NeedsHealing())
            {
                if (!holding) RuleFx.Toast("You are not hurt. Look at a downed friend to use it on them.", 2f);
                if (holding) StopHold();
                holding = true; // stay quiet until the key is released
                holdTarget = null;
                holdTimer = -1f;
                return;
            }
            holding = true;
            holdTarget = target;
            holdTimer = 0f;
            SendStart(target);
        }
        if (holdTimer < 0f) return; // "not hurt" press: wait for release

        holdTimer += Time.deltaTime;
        float need = Mathf.Max(0.1f, Use.holdSeconds);
        ChoreProgressHUD.ShowText(this, holdTarget != null ? "Using the syringe on " + holdTarget.name + "..." : "Using the syringe...", holdTimer / need);
        if (holdTimer < need) return;

        PlayerHealth done = holdTarget;
        StopHold();
        holding = true;  // one use per press
        holdTimer = -1f;
        inventory.RequestSyringeFinish(done);
    }

    private void StopHold()
    {
        holding = false;
        holdTarget = null;
        holdTimer = 0f;
        ChoreProgressHUD.Hide(this);
    }

    private bool NeedsHealing() => health != null && health.CurrentHealth < health.MaxHealth - 0.01f;

    private void SendStart(PlayerHealth target) => inventory.RequestSyringeStart(target);

    /// <summary>The downed player under the crosshair, close enough, else null.</summary>
    private PlayerHealth DownedFriendInFront()
    {
        if (ownCamera == null) ownCamera = GetComponentInChildren<Camera>();
        if (ownCamera == null) return null;
        float range = Use.reviveRange;
        Ray ray = new Ray(ownCamera.transform.position, ownCamera.transform.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, range + 0.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        PlayerHealth best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            PlayerHealth other = hits[i].collider.GetComponentInParent<PlayerHealth>();
            if (other == null || other == health || !other.IsDowned || other.IsDead || hits[i].distance >= bestDistance) continue;
            bestDistance = hits[i].distance;
            best = other;
        }
        if (best != null) return best;

        // A body lying flat is hard to hit with a ray: also take the nearest downed friend in front.
        float bestSqr = range * range;
        foreach (PlayerHealth other in PlayerRegistry.All)
        {
            if (other == null || other == health || !other.IsDowned || other.IsDead) continue;
            Vector3 to = other.transform.position - transform.position;
            if (to.sqrMagnitude > bestSqr) continue;
            Vector3 flat = Vector3.ProjectOnPlane(to, Vector3.up);
            Vector3 look = Vector3.ProjectOnPlane(ownCamera.transform.forward, Vector3.up);
            if (flat.sqrMagnitude > 0.01f && Vector3.Angle(look, flat) > 45f) continue;
            bestSqr = to.sqrMagnitude;
            best = other;
        }
        return best;
    }

    private void UpdateHeldModel()
    {
        bool show = inventory.IsHoldingSyringe && health != null && !health.IsDead;
        string id = show ? inventory.HeldSlot.itemId : null;
        if (show && (heldModel == null || heldModelFor != id))
        {
            if (heldModel != null) Destroy(heldModel);
            heldModel = null;
            heldModelFor = id;
            if (ownCamera == null) ownCamera = GetComponentInChildren<Camera>();
            ItemCatalog.ItemInfo info = ItemCatalog.Find(id);
            GameObject prefab = info != null ? ItemCatalog.LoadPrefab(info.heldPrefabPath, null) : null;
            if (ownCamera != null && prefab != null)
            {
                heldModel = Instantiate(prefab, ownCamera.transform, false);
                heldModel.name = "Held Syringe";
                heldModel.transform.localPosition = HeldPosition;
                heldModel.transform.localRotation = Quaternion.Euler(-20f, -25f, 0f);
                foreach (Renderer r in heldModel.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                foreach (Collider c in heldModel.GetComponentsInChildren<Collider>()) Destroy(c);
            }
        }
        if (heldModel != null && heldModel.activeSelf != show) heldModel.SetActive(show);
    }

    private void OnDestroy()
    {
        if (heldModel != null) Destroy(heldModel);
        ChoreProgressHUD.Hide(this);
    }

    // ---- Server -------------------------------------------------------------------------

    /// <summary>SERVER (through PlayerInventory). The owner started holding the use key.</summary>
    public void ServerStart(GameObject target)
    {
        if (!NetworkMode.HasServerAuthority(inventory) || !inventory.IsHoldingSyringe) { serverStart = -1; return; }
        serverStart = Now;
        serverItem = inventory.HeldSlot.itemId;
        serverTarget = TargetKey(target);
    }

    /// <summary>Who a use is aimed at: 0 = yourself, else the friend's netId (1 offline).</summary>
    private static uint TargetKey(GameObject target)
    {
        if (target == null) return 0u;
        if (NetworkMode.IsOffline) return 1u;
        return target.TryGetComponent(out NetworkIdentity id) ? id.netId : 1u;
    }

    /// <summary>SERVER (through PlayerInventory). The owner held long enough.</summary>
    public void ServerFinish(GameObject targetObject)
    {
        if (!NetworkMode.HasServerAuthority(inventory) || serverStart < 0) return;
        double started = serverStart;
        serverStart = -1;
        if (health == null || health.IsDead || health.IsDowned) return;
        if (!inventory.IsHoldingSyringe || inventory.HeldSlot.itemId != serverItem) return;
        // The hold must really have lasted (a little slack for lag).
        if (Now - started < Mathf.Max(0.1f, Use.holdSeconds) * 0.8f) return;

        PlayerHealth target = targetObject != null ? targetObject.GetComponent<PlayerHealth>() : null;
        if (TargetKey(target != null ? target.gameObject : null) != serverTarget) return; // aimed at someone else when the hold started
        ItemCatalog.ItemInfo info = ItemCatalog.Find(serverItem);
        if (info == null || !info.isSyringe) return;

        if (target != null && target != health)
        {
            if (!target.IsDowned || target.IsDead) return;
            if ((target.transform.position - transform.position).sqrMagnitude > (Use.reviveRange + 1.5f) * (Use.reviveRange + 1.5f)) return;
            float revived = Mathf.Max(1f, target.MaxHealth * info.healPartOfMissing + info.healFlat);
            if (!inventory.ServerTakeOne(inventory.SelectedSlot)) return;
            target.Revive(revived);
            Debug.Log($"[Syringe] {name} got {target.name} up with a {info.displayName} ({revived:0.#} health).", this);
            return;
        }

        float missing = health.MaxHealth - health.CurrentHealth;
        if (missing <= 0.01f) return;
        // Sanity BREAKING blocks healing (PlayerHealth.Heal): do not waste the syringe.
        if (TryGetComponent(out PlayerSanity sanity) && sanity.BlocksHealing) return;
        float heal = missing * info.healPartOfMissing + info.healFlat; // not rounded (Mr.k #54)
        if (heal <= 0f) return;
        if (!inventory.ServerTakeOne(inventory.SelectedSlot)) return;
        health.Heal(heal);
        Debug.Log($"[Syringe] {name} used a {info.displayName}: +{heal:0.##} health.", this);
    }
}
