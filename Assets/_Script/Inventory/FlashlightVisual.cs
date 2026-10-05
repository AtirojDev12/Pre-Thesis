using UnityEngine;

/// <summary>
/// 5 Oct (Mr.k). The flashlight MODEL: lens glow, and - only on a flashlight lying
/// on the floor - its own small beam. No input, no battery logic.
///
/// Used by the held model (Resources/Items/Flashlight) and the world pickup
/// (Resources/Items/WorldFlashlight). Both prefabs are made by
/// Tools > Pre-Thesis > Build Flashlight Prefabs.
///
/// The beam of a flashlight IN A HAND is drawn by FlashlightController, because it
/// must follow the player's eyes, not the model.
/// </summary>
public sealed class FlashlightVisual : MonoBehaviour
{
    [Tooltip("The glass at the front. Glows while the light is on.")]
    [SerializeField] private Renderer lensRenderer;
    [Tooltip("Where a dropped flashlight's beam comes out (points along +Z). Empty = this object.")]
    [SerializeField] private Transform beamOrigin;

    private MaterialPropertyBlock properties;
    private Light floorBeam;
    private bool wantFloorBeam;

    private static readonly Color BodyColor = new Color(0.1f, 0.1f, 0.11f);
    private static readonly Color LensOff = new Color(0.15f, 0.15f, 0.12f);
    private static readonly Color LensOn = new Color(1f, 0.95f, 0.75f);

    private void Awake()
    {
        properties = new MaterialPropertyBlock();
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r == lensRenderer) continue;
            properties.SetColor("_BaseColor", BodyColor);
            properties.SetColor("_Color", BodyColor);
            r.SetPropertyBlock(properties);
        }
        SetLens(false);
    }

    /// <summary>
    /// Shows the item's state. <paramref name="lyingOnFloor"/>: a world pickup draws its own
    /// beam while it is on (a held one does not, FlashlightController does).
    /// </summary>
    public void Show(InventorySlot item, bool lyingOnFloor)
    {
        bool lit = !item.IsEmpty && item.poweredOn && item.charge > 0f;
        SetLens(lit);

        wantFloorBeam = lyingOnFloor && lit;
        if (!wantFloorBeam)
        {
            if (floorBeam != null) floorBeam.enabled = false;
            return;
        }

        ItemCatalog.ItemInfo info = ItemCatalog.Find(item.itemId);
        if (info == null) return;
        if (floorBeam == null)
        {
            var go = new GameObject("Floor Beam");
            Transform parent = beamOrigin != null ? beamOrigin : transform;
            go.transform.SetParent(parent, false);
            floorBeam = go.AddComponent<Light>();
            floorBeam.type = LightType.Spot;
            floorBeam.shadows = LightShadows.None; // cheap: it is just lying there
        }
        floorBeam.range = info.lightRange;
        floorBeam.spotAngle = info.spotAngle;
        floorBeam.innerSpotAngle = info.spotAngle * 0.6f;
        floorBeam.intensity = info.lightIntensity;
        floorBeam.color = LensOn;
        floorBeam.enabled = true;
    }

    private void SetLens(bool on)
    {
        if (lensRenderer == null) return;
        if (properties == null) properties = new MaterialPropertyBlock();
        Color c = on ? LensOn : LensOff;
        properties.SetColor("_BaseColor", c);
        properties.SetColor("_Color", c);
        properties.SetColor("_EmissionColor", on ? LensOn * 2f : Color.black);
        lensRenderer.SetPropertyBlock(properties);
    }
}
