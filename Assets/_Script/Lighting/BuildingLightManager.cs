using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>Owns building power independently of any ghost or switch.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
public sealed class BuildingLightManager : NetworkBehaviour
{
    public static BuildingLightManager Instance { get; private set; }
    public static bool LightsOn => Instance == null || Instance.lightsOn;

    [SyncVar(hook = nameof(OnLightsChanged))]
    [SerializeField] private bool lightsOn = true;

    private readonly Dictionary<Light, bool> lamps = new Dictionary<Light, bool>();
    private readonly List<Light> found = new List<Light>();
    private readonly List<Light> removed = new List<Light>();
    private float nextScan;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("Only one BuildingLightManager may be active in a gameplay scene.", this);
            enabled = false;
            return;
        }
        Instance = this;
    }

    private void Start() => RefreshLights();
    public override void OnStartClient() => RefreshLights();

    private void Update()
    {
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 0.5f;
        RefreshLights();
    }

    /// <summary>Server/offline only. Returns true only when power changed.</summary>
    public bool SetLights(bool on)
    {
        if (!NetworkMode.HasServerAuthority(this) || lightsOn == on) return false;
        lightsOn = on;
        RefreshLights();
        return true;
    }

    private void OnLightsChanged(bool previous, bool current) => RefreshLights();

    /// <summary>Also discovers disabled lamps and lamps added after scene load.</summary>
    public void RefreshLights()
    {
        if (Instance != this) return;
        removed.Clear();
        foreach (var entry in lamps)
        {
            if (IsBuildingLight(entry.Key)) continue;
            if (entry.Key != null) entry.Key.enabled = entry.Value;
            removed.Add(entry.Key);
        }
        foreach (Light lamp in removed) lamps.Remove(lamp);

        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            found.Clear();
            root.GetComponentsInChildren(true, found);
            foreach (Light lamp in found)
            {
                if (!IsBuildingLight(lamp)) continue;
                if (!lamps.ContainsKey(lamp)) lamps.Add(lamp, lamp.enabled);
                lamp.enabled = lightsOn && lamps[lamp];
            }
        }
    }

    private bool IsBuildingLight(Light lamp)
    {
        return lamp != null && lamp.gameObject.scene == gameObject.scene
            && lamp.GetComponentInParent<IndependentLight>(true) == null
            && lamp.GetComponentInParent<Camera>(true) == null
            && lamp.GetComponentInParent<PlayerInteractor>(true) == null
            && lamp.GetComponentInParent<FlashlightController>(true) == null
            && lamp.GetComponentInParent<FlashlightVisual>(true) == null;
    }

    private void OnDestroy()
    {
        foreach (var entry in lamps)
            if (entry.Key != null) entry.Key.enabled = entry.Value;
        if (Instance == this) Instance = null;
    }
}
