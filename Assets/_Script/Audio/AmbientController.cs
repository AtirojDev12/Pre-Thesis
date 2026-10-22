using System.Collections.Generic;
using UnityEngine;

/// <summary>Scene-owned room tones; only the local enabled listener selects a zone.</summary>
[DisallowMultipleComponent]
public sealed class AmbientController : MonoBehaviour
{
    [SerializeField] private AudioClip defaultAmbient;
    [SerializeField, Range(0, 1)] private float defaultVolume = 0.4f;
    [SerializeField, Min(0.01f)] private float fadeSeconds = 1;
    [SerializeField] private AmbientZone[] zones;
    private AudioListener listener;
    private readonly List<Layer> layers = new List<Layer>();
    private sealed class Layer
    {
        public AmbientZone zone;
        public AudioSource source;
        public SoundCategoryVolume volume;
        public float gain;
        public AudioSource occasional;
        public SoundCategoryVolume occasionalVolume;
        public float occasionalGain;
        public float wait;
        public int extraPlays;
        public bool active;
    }

    private void Start()
    {
        if (Application.isBatchMode) { enabled = false; return; }
        AddLayer(null, defaultAmbient);
        if (zones != null)
            foreach (var zone in zones) if (zone != null) AddLayer(zone, zone.clip);
    }

    private void AddLayer(AmbientZone zone, AudioClip clip)
    {
        if (clip == null) return;
        var emitter = new GameObject(zone != null ? zone.name + " Ambient" : "Default Ambient");
        emitter.transform.SetParent(transform, false);
        var source = emitter.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0;
        source.clip = clip;
        var volume = emitter.AddComponent<SoundCategoryVolume>();
        volume.Category = SoundCategory.Ambient;
        volume.SetBaseVolume(0);
        var layer = new Layer { zone = zone, source = source, volume = volume };
        // A separate source prevents the occasional sound from interrupting the room tone.
        if (zone != null)
        {
            var eventEmitter = new GameObject(zone.name + " Occasional");
            eventEmitter.transform.SetParent(transform, false);
            layer.occasional = eventEmitter.AddComponent<AudioSource>();
            layer.occasional.playOnAwake = false;
            layer.occasional.spatialBlend = 0;
            layer.occasionalVolume = eventEmitter.AddComponent<SoundCategoryVolume>();
            layer.occasionalVolume.Category = SoundCategory.Ambient;
            layer.occasionalVolume.SetBaseVolume(0);
        }
        layers.Add(layer);
    }

    private void Update()
    {
        if (listener == null || !listener.isActiveAndEnabled)
        {
            listener = null;
            foreach (var candidate in FindObjectsByType<AudioListener>())
                if (candidate.isActiveAndEnabled) { listener = candidate; break; }
        }
        AmbientZone selected = null;
        if (listener != null && zones != null)
            foreach (var zone in zones)
                if (zone != null && zone.clip != null && zone.Contains(listener.transform.position) &&
                    (selected == null || zone.priority > selected.priority)) selected = zone;
        foreach (Layer layer in layers)
        {
            float target = listener != null && layer.zone == selected ?
                (layer.zone != null ? layer.zone.volume : defaultVolume) : 0;
            layer.gain = Mathf.MoveTowards(layer.gain, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
            layer.volume.SetBaseVolume(layer.gain);
            if (layer.gain > 0 && !layer.source.isPlaying) layer.source.Play();
            else if (layer.gain <= 0 && layer.source.isPlaying) layer.source.Stop();
            UpdateOccasional(layer, listener != null && layer.zone == selected);
        }
    }

    private static float Delay(Vector2 range)
    {
        float min = Mathf.Max(0.1f, range.x);
        return Random.Range(min, Mathf.Max(min, range.y));
    }

    private void UpdateOccasional(Layer layer, bool active)
    {
        if (layer.occasional == null) return;
        var zone = layer.zone;
        if (active && !layer.active)
        {
            layer.wait = Delay(zone.quietSeconds);
            layer.extraPlays = 0;
        }
        layer.active = active;
        float target = active && zone.occasionalClip != null ? zone.occasionalVolume : 0;
        layer.occasionalGain = Mathf.MoveTowards(layer.occasionalGain, target,
            Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
        layer.occasionalVolume.SetBaseVolume(layer.occasionalGain);
        if (!active || zone.occasionalClip == null)
        {
            if (layer.occasionalGain <= 0) layer.occasional.Stop();
            return;
        }
        // Count only silence, so even a long clip cannot overlap itself.
        if (layer.occasional.isPlaying) return;
        layer.wait -= Time.unscaledDeltaTime;
        if (layer.wait > 0) return;
        layer.occasional.clip = zone.occasionalClip;
        layer.occasional.Play();
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
        foreach (Layer layer in layers)
        {
            layer.source.Stop();
            layer.gain = 0;
            layer.volume.SetBaseVolume(0);
            layer.active = false;
            layer.occasionalGain = 0;
            if (layer.occasional != null)
            {
                layer.occasional.Stop();
                layer.occasionalVolume.SetBaseVolume(0);
            }
        }
    }
}
