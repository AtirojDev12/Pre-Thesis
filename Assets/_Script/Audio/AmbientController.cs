using System.Collections.Generic;
using UnityEngine;

/// <summary>Scene-owned room tones; only the local enabled listener selects a zone.</summary>
/// <remarks>
/// 11 Nov 2026: zones that use the SAME loop clip now share one AudioSource, so walking
/// from one of them into another never restarts the clip; only the volume glides to the
/// new zone's value. Zones present in the scene but missing from <see cref="zones"/> are
/// picked up automatically at Start.
/// Crossfades are driven by POSITION: each zone fades over its blendDistance outside its
/// box, zones of equal priority share the mix, higher priority zones take their share
/// first, and different clips are mixed with an equal-power curve so there is no dip or
/// jump in loudness while walking through a doorway.
/// </remarks>
[DisallowMultipleComponent]
public sealed class AmbientController : MonoBehaviour
{
    [SerializeField] private AudioClip defaultAmbient;
    [SerializeField, Range(0, 1)] private float defaultVolume = 0.4f;
    [SerializeField, Min(0.01f)] private float fadeSeconds = 1;
    [SerializeField] private AmbientZone[] zones;
    private AudioListener listener;
    private readonly List<AmbientZone> activeZones = new List<AmbientZone>();
    private readonly List<Loop> loops = new List<Loop>();
    private readonly List<Occasional> occasionals = new List<Occasional>();

    /// <summary>One looping source per distinct clip, shared by every zone using that clip.</summary>
    private sealed class Loop
    {
        public AudioClip clip;
        public AudioSource source;
        public SoundCategoryVolume volume;
        public float gain;
        public float share;        // this frame: summed mix weight of zones using this clip
        public float shareVolume;  // this frame: sum of weight * zone volume
    }

    private readonly Dictionary<AmbientZone, Loop> zoneLoops = new Dictionary<AmbientZone, Loop>();
    private readonly Dictionary<AmbientZone, float> zoneShares = new Dictionary<AmbientZone, float>();
    private Loop defaultLoop;

    /// <summary>Per-zone random event layer (kept per zone: each zone has its own timer/settings).</summary>
    private sealed class Occasional
    {
        public AmbientZone zone;
        public AudioSource source;
        public SoundCategoryVolume volume;
        public float gain;
        public float wait;
        public int extraPlays;
        public bool active;
    }

    private void Start()
    {
        if (Application.isBatchMode) { enabled = false; return; }
        if (zones != null)
            foreach (var zone in zones) Register(zone);
        foreach (var zone in FindObjectsByType<AmbientZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (zone.gameObject.scene == gameObject.scene) Register(zone);
        defaultLoop = LoopFor(defaultAmbient, "Default Ambient");
        // Highest priority first; Update relies on this order.
        activeZones.Sort((a, b) => b.priority.CompareTo(a.priority));
    }

    private void Register(AmbientZone zone)
    {
        if (zone == null || activeZones.Contains(zone)) return;
        activeZones.Add(zone);
        Loop loop = LoopFor(zone.clip, zone.clip != null ? zone.clip.name + " Ambient" : null);
        if (loop != null) zoneLoops[zone] = loop;
        var emitter = new GameObject(zone.name + " Occasional");
        emitter.transform.SetParent(transform, false);
        var source = emitter.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0;
        var volume = emitter.AddComponent<SoundCategoryVolume>();
        volume.Category = SoundCategory.Ambient;
        volume.SetBaseVolume(0);
        // A separate source prevents the occasional sound from interrupting the room tone.
        occasionals.Add(new Occasional { zone = zone, source = source, volume = volume });
    }

    private Loop LoopFor(AudioClip clip, string emitterName)
    {
        if (clip == null) return null;
        foreach (Loop existing in loops)
            if (existing.clip == clip) return existing;
        var emitter = new GameObject(emitterName);
        emitter.transform.SetParent(transform, false);
        var source = emitter.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0;
        source.clip = clip;
        var volume = emitter.AddComponent<SoundCategoryVolume>();
        volume.Category = SoundCategory.Ambient;
        volume.SetBaseVolume(0);
        var loop = new Loop { clip = clip, source = source, volume = volume };
        loops.Add(loop);
        return loop;
    }

    private void Update()
    {
        if (listener == null || !listener.isActiveAndEnabled)
        {
            listener = null;
            foreach (var candidate in FindObjectsByType<AudioListener>())
                if (candidate.isActiveAndEnabled) { listener = candidate; break; }
        }
        foreach (Loop loop in loops) { loop.share = 0; loop.shareVolume = 0; }
        zoneShares.Clear();
        AmbientZone selected = null;
        float selectedShare = 0;
        if (listener != null)
        {
            Vector3 position = listener.transform.position;
            float remaining = 1;
            // Walk priority groups from high to low. Inside a group the weights are
            // normalised so overlapping blends of equal priority split the mix fairly.
            for (int i = 0; i < activeZones.Count && remaining > 0.0001f;)
            {
                int priority = activeZones[i].priority;
                int end = i;
                float sum = 0;
                while (end < activeZones.Count && activeZones[end].priority == priority)
                {
                    AmbientZone zone = activeZones[end];
                    if (zone != null && zone.clip != null && zoneLoops.ContainsKey(zone))
                    {
                        float w = zone.Weight(position);
                        if (w > 0) { zoneShares[zone] = w; sum += w; }
                    }
                    end++;
                }
                if (sum > 0)
                {
                    float scale = remaining / Mathf.Max(1, sum);
                    float used = 0;
                    for (int j = i; j < end; j++)
                    {
                        AmbientZone zone = activeZones[j];
                        if (zone == null || !zoneShares.TryGetValue(zone, out float w)) continue;
                        float share = w * scale;
                        zoneShares[zone] = share;
                        Loop loop = zoneLoops[zone];
                        loop.share += share;
                        loop.shareVolume += share * zone.volume;
                        used += share;
                        if (share > selectedShare) { selectedShare = share; selected = zone; }
                    }
                    remaining -= used;
                }
                i = end;
            }
            if (defaultLoop != null && remaining > 0)
            {
                defaultLoop.share += remaining;
                defaultLoop.shareVolume += remaining * defaultVolume;
            }
        }

        // Equal-power mix between different clips; the gain is still rate-limited by
        // fadeSeconds so teleports / respawns never pop.
        float step = Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds);
        foreach (Loop loop in loops)
        {
            float target = loop.share > 0 ? Mathf.Sqrt(Mathf.Clamp01(loop.share)) * (loop.shareVolume / loop.share) : 0;
            loop.gain = Mathf.MoveTowards(loop.gain, target, step);
            loop.volume.SetBaseVolume(loop.gain);
            if (loop.gain > 0 && !loop.source.isPlaying) loop.source.Play();
            else if (loop.gain <= 0 && loop.source.isPlaying) loop.source.Stop();
        }
        foreach (Occasional occasional in occasionals)
            UpdateOccasional(occasional, listener != null && occasional.zone == selected);
    }

    private static float Delay(Vector2 range)
    {
        float min = Mathf.Max(0.1f, range.x);
        return Random.Range(min, Mathf.Max(min, range.y));
    }

    private void UpdateOccasional(Occasional layer, bool active)
    {
        var zone = layer.zone;
        if (zone == null) return;
        if (active && !layer.active)
        {
            layer.wait = Delay(zone.quietSeconds);
            layer.extraPlays = 0;
        }
        layer.active = active;
        float target = active && zone.occasionalClip != null ? zone.occasionalVolume : 0;
        layer.gain = Mathf.MoveTowards(layer.gain, target,
            Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
        layer.volume.SetBaseVolume(layer.gain);
        if (!active || zone.occasionalClip == null)
        {
            if (layer.gain <= 0) layer.source.Stop();
            return;
        }
        // Count only silence, so even a long clip cannot overlap itself.
        if (layer.source.isPlaying) return;
        layer.wait -= Time.unscaledDeltaTime;
        if (layer.wait > 0) return;
        layer.source.clip = zone.occasionalClip;
        layer.source.Play();
        if (layer.extraPlays > 0) layer.extraPlays--;
        else if (Random.value < zone.burstChance)
        {
            int min = Mathf.Max(0, zone.burstExtraPlays.x);
            layer.extraPlays = Random.Range(min, Mathf.Max(min, zone.burstExtraPlays.y) + 1);
        }
        layer.wait = Delay(layer.extraPlays > 0 ? zone.burstGapSeconds : zone.quietSeconds);
    }

    private void OnDisable()
    {
        foreach (Loop loop in loops)
        {
            loop.source.Stop();
            loop.gain = 0;
            loop.volume.SetBaseVolume(0);
        }
        foreach (Occasional layer in occasionals)
        {
            layer.active = false;
            layer.gain = 0;
            layer.source.Stop();
            layer.volume.SetBaseVolume(0);
        }
    }
}
