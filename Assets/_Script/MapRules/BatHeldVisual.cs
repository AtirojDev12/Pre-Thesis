using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 23 Nov 2026 (Mr.k): the Bat in your own hand (first person) and its swing on Left Click.
/// LOCAL ONLY and cosmetic: the hit itself is checked by MapWalker (Map Rule 1).
/// Model: Resources/Items/Bat (made by Tools > Pre-Thesis > One Step > Map Rules).
/// </summary>
public sealed class BatHeldVisual : MonoBehaviour
{
    private static readonly Vector3 HeldPosition = new Vector3(0.32f, -0.32f, 0.55f);
    private static readonly Vector3 RestAngles = new Vector3(-35f, -10f, 10f);
    private static readonly Vector3 SwingAngles = new Vector3(45f, -45f, 10f);
    private const float SwingSeconds = 0.28f;

    private static BatHeldVisual instance;
    private GameObject model;
    private Camera ownCamera;
    private float swingStart = -99f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null || Application.isBatchMode) return;
        var go = new GameObject("[Bat Held Visual]");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<BatHeldVisual>();
    }

    private void Update()
    {
        PlayerInventory inventory = PlayerInventory.Local;
        PlayerHealth me = PlayerHealth.LocalInstance;
        bool show = inventory != null && me != null && !me.IsDead && inventory.HeldSlot.itemId == ItemCatalog.Bat;
        if (show && model == null) Build(inventory);
        if (model == null) return;
        if (model.activeSelf != show) model.SetActive(show);
        if (!show) return;

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && !GameplayInput.Blocked && Cursor.lockState == CursorLockMode.Locked)
            swingStart = Time.time;
        float t = (Time.time - swingStart) / SwingSeconds;
        float k = t < 0f || t > 1f ? 0f : Mathf.Sin(t * Mathf.PI);
        model.transform.localRotation = Quaternion.Euler(Vector3.Lerp(RestAngles, SwingAngles, k));
    }

    private void Build(PlayerInventory inventory)
    {
        if (ownCamera == null || !ownCamera.isActiveAndEnabled) ownCamera = inventory.GetComponentInChildren<Camera>();
        ItemCatalog.ItemInfo info = ItemCatalog.Find(ItemCatalog.Bat);
        GameObject prefab = info != null ? ItemCatalog.LoadPrefab(info.heldPrefabPath, null) : null;
        if (ownCamera == null || prefab == null) return;
        model = Instantiate(prefab, ownCamera.transform, false);
        model.name = "Held Bat";
        model.transform.localPosition = HeldPosition;
        model.transform.localRotation = Quaternion.Euler(RestAngles);
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        foreach (Collider c in model.GetComponentsInChildren<Collider>()) Destroy(c);
    }
}
