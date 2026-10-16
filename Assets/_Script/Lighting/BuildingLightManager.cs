using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>Owns building power independently of any ghost or switch.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
public sealed class BuildingLightManager : NetworkBehaviour
{
    [System.Serializable]
    public sealed class LightZone
    {
        [Tooltip("Unique ID. Use the same ID on each switch controlling this zone.")]
        public string zoneId;
        [Tooltip("All eligible lights below these scene objects belong to this zone.")]
        public Transform[] lightRoots = new Transform[0];
        [Tooltip("Optional individual lights outside the roots.")]
        public Light[] additionalLights = new Light[0];
        public bool initiallyOn = true;
        [Header("Detection area (world metres)")]
        [Tooltip("Centre of the room used by ghost vision. This does not move the lights.")]
        public Vector3 areaCenter;
        [Tooltip("Room width, height and depth. Zero disables this area's position lookup.")]
        public Vector3 areaSize;

        public bool ContainsPosition(Vector3 position) => areaSize.x > 0 && areaSize.y > 0 && areaSize.z > 0
            && new Bounds(areaCenter, areaSize).Contains(position);

        public bool Contains(Light lamp)
        {
            if (additionalLights != null)
                foreach (Light extra in additionalLights) if (extra == lamp) return true;
            if (lightRoots != null)
                foreach (Transform root in lightRoots)
                    if (root != null && lamp.transform.IsChildOf(root)) return true;
            return false;
        }
    }

    [Header("Zone lighting")]
    [Tooltip("Add zones here, then assign their ID to the corresponding world switches. Ungrouped lights only follow building power.")]
    [SerializeField] private LightZone[] zones = new LightZone[0];
    public IReadOnlyList<LightZone> Zones => zones;
    public readonly SyncDictionary<string, bool> zoneStates = new SyncDictionary<string, bool>();
    // Mirror's SyncObjects reject writes without an active server/client.
    // Keep solo scene previews separate from the server-owned replicated state.
    private readonly Dictionary<string, bool> offlineZoneStates = new Dictionary<string, bool>();

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
        zoneStates.OnChange += OnZoneChanged;
    }

    private void OnZoneChanged(SyncDictionary<string, bool>.Operation operation, string key, bool value) => RefreshLights();

    public bool HasZone(string zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId) || zones == null) return false;
        foreach (LightZone zone in zones)
            if (zone != null && zone.zoneId == zoneId) return true;
        return false;
    }

    public bool ZoneLightsOn(string zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId) || zones == null) return true;
        if (NetworkMode.IsOffline)
        {
            if (offlineZoneStates.TryGetValue(zoneId, out bool offlineOn)) return offlineOn;
        }
        else if (zoneStates.TryGetValue(zoneId, out bool on)) return on;
        foreach (LightZone zone in zones)
            if (zone != null && zone.zoneId == zoneId) return zone.initiallyOn;
        return true;
    }

    public bool SetZoneLights(string zoneId, bool on)
    {
        if (!NetworkMode.HasServerAuthority(this) || !HasZone(zoneId) || ZoneLightsOn(zoneId) == on) return false;
        if (NetworkMode.IsOffline) offlineZoneStates[zoneId] = on;
        else zoneStates[zoneId] = on;
        RefreshLights();
        return true;
    }

    private bool ZoneAllows(Light lamp)
    {
        if (zones == null) return true;
        // Overlapping groups are allowed: any zone that is off cuts power to the shared lamp.
        foreach (LightZone zone in zones)
            if (zone != null && !string.IsNullOrWhiteSpace(zone.zoneId) && zone.Contains(lamp) && !ZoneLightsOn(zone.zoneId)) return false;
        return true;
    }

    /// <summary>Effective power at a player's position. Smaller rooms take precedence in overlaps.</summary>
    public bool LightsOnAt(Vector3 position)
    {
        if (!lightsOn) return false;
        LightZone selected = null;
        float smallest = float.PositiveInfinity;
        if (zones != null)
            foreach (LightZone zone in zones)
            {
                if (zone == null || string.IsNullOrWhiteSpace(zone.zoneId) || !zone.ContainsPosition(position)) continue;
                float volume = zone.areaSize.x * zone.areaSize.y * zone.areaSize.z;
                if (volume >= smallest) continue;
                selected = zone;
                smallest = volume;
            }
        return selected == null || ZoneLightsOn(selected.zoneId);
    }

    private void OnDrawGizmosSelected()
    {
        if (zones == null) return;
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
        foreach (LightZone zone in zones)
            if (zone != null && zone.areaSize.x > 0 && zone.areaSize.y > 0 && zone.areaSize.z > 0)
                Gizmos.DrawWireCube(zone.areaCenter, zone.areaSize);
    }

    private void Start() => RefreshLights();
    public override void OnStartServer()
    {
        base.OnStartServer();
        offlineZoneStates.Clear();
        RefreshLights();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        offlineZoneStates.Clear();
        RefreshLights();
    }

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
                lamp.enabled = lightsOn && ZoneAllows(lamp) && lamps[lamp];
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
        zoneStates.OnChange -= OnZoneChanged;
        foreach (var entry in lamps)
            if (entry.Key != null) entry.Key.enabled = entry.Value;
        if (Instance == this) Instance = null;
    }
}
