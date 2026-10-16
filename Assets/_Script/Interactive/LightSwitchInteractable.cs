using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>A world switch using the building's existing replicated light state.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
public sealed class LightSwitchInteractable : InteractableBase
{
    [Header("Controlled zone")]
    [Tooltip("Zone ID configured on Building Lights. Empty preserves a legacy whole-building switch.")]
    [SerializeField] private string zoneId = "";
    private bool IsOn => BuildingLightManager.Instance == null ||
        (string.IsNullOrWhiteSpace(zoneId) ? BuildingLightManager.LightsOn : BuildingLightManager.Instance.ZoneLightsOn(zoneId));
    [Header("Switch appearance")]
    [Tooltip("Renderers to turn red when the lights are off. Empty uses this object's children.")]
    [SerializeField] private Renderer[] switchRenderers;
    [SerializeField] private Color lightsOffColor = Color.red;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private readonly List<Surface> surfaces = new List<Surface>();
    private bool? appliedLightsOn;
    private Color appliedOffColor;

    private sealed class Surface
    {
        public Renderer renderer;
        public int materialIndex;
        public MaterialPropertyBlock original;
        public MaterialPropertyBlock red;
    }

    protected override void Awake()
    {
        // Use PlayerInteractor's outline; do not tint the switch on hover.
        base.Awake();
        if (switchRenderers == null || switchRenderers.Length == 0)
            switchRenderers = GetComponentsInChildren<Renderer>(true);

        foreach (Renderer target in switchRenderers)
        {
            if (target == null) continue;
            Material[] materials = target.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null || (!material.HasProperty(BaseColorId) && !material.HasProperty(ColorId))) continue;
                var original = new MaterialPropertyBlock();
                target.GetPropertyBlock(original, i);
                var red = new MaterialPropertyBlock();
                target.GetPropertyBlock(red, i);
                // With no per-material override, preserve the renderer-wide block as well.
                if (red.isEmpty) target.GetPropertyBlock(red);
                surfaces.Add(new Surface { renderer = target, materialIndex = i, original = original, red = red });
            }
        }
    }

    public override bool CanInteract() => isActiveAndEnabled && BuildingLightManager.Instance != null &&
        (string.IsNullOrWhiteSpace(zoneId) || BuildingLightManager.Instance.HasZone(zoneId));

    public override string GetInteractionPrompt() => IsOn
        ? "[E] Turn lights off" : "[E] Turn lights on";

    protected override void OnInteracted(GameObject interactor)
    {
        BuildingLightManager manager = BuildingLightManager.Instance;
        if (manager == null) return;
        bool wasOn = IsOn;
        if (string.IsNullOrWhiteSpace(zoneId)) manager.SetLights(!wasOn);
        else manager.SetZoneLights(zoneId, !wasOn);
        // Only an accepted switch interaction makes a click, never initial state restoration or late joins.
        if (IsOn != wasOn)
            NetworkAudioRelay.Play(IsOn ? "LightSwitch_On" : "LightSwitch_Off", transform.position);
    }

    public override void SetHighlighted(bool highlighted) { }

    private void LateUpdate()
    {
        // Reading the SyncVar also follows keyboard input and late joins.
        bool lightsOn = IsOn;
        if (appliedLightsOn == lightsOn && (lightsOn || appliedOffColor == lightsOffColor)) return;
        foreach (Surface surface in surfaces)
        {
            if (surface.renderer == null) continue;
            if (!lightsOn)
            {
                surface.red.SetColor(BaseColorId, lightsOffColor);
                surface.red.SetColor(ColorId, lightsOffColor);
            }
            surface.renderer.SetPropertyBlock(lightsOn ? surface.original : surface.red, surface.materialIndex);
        }
        appliedLightsOn = lightsOn;
        appliedOffColor = lightsOffColor;
    }

    private void OnDisable()
    {
        foreach (Surface surface in surfaces)
            if (surface.renderer != null)
                surface.renderer.SetPropertyBlock(surface.original, surface.materialIndex);
        appliedLightsOn = null;
    }
}
