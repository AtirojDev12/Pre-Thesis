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
        layers.Add(new Layer { zone = zone, source = source, volume = volume });
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
        }
    }

    private void OnDisable()
    {
        foreach (Layer layer in layers)
        {
            layer.source.Stop();
            layer.gain = 0;
            layer.volume.SetBaseVolume(0);
        }
    }
}
