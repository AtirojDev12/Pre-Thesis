using System.Collections.Generic;
using UnityEngine;

/// <summary>Local 3D playback of server-approved station work, isolated per actor.</summary>
public sealed class PopcornStationAudio : MonoBehaviour
{
    private PopcornPreparation preparation;
    private readonly Dictionary<uint, Voice> voices = new Dictionary<uint, Voice>();
    private readonly Dictionary<uint, int> suppressed = new Dictionary<uint, int>();
    private sealed class Voice
    {
        public PopcornSoundAction action;
        public PopcornStation station;
        public AudioSource source;
        public AudioSource click;
    }
    public void Configure(PopcornPreparation owner) => preparation = owner;
    private void Start() => PopcornNetSync.Instance?.RestoreStationSounds();

    public void Begin(uint actor, PopcornSoundAction action, bool initialSnapshot = false)
    {
        if (Application.isBatchMode || !isActiveAndEnabled || preparation == null ||
            !preparation.isActiveAndEnabled || AudioManager.Instance == null) return;
        if (suppressed.TryGetValue(actor, out int cancelled) && action.request <= cancelled) return;
        if (voices.TryGetValue(actor, out var previous))
        {
            if (previous.action.request == action.request) return;
            End(actor, previous.action.request);
        }
        var station = preparation.StationAt(action.station);
        if (station == null) return;
        string id = station.Kind == PopcornStationKind.Water ? "Popcorn_WaterPour" :
            station.Kind == PopcornStationKind.Tank ? "Popcorn_Scoop" : "Popcorn_Making";
        var entry = AudioManager.Instance.Find(id);
        if (entry == null || entry.clips == null || entry.clips.Length == 0 || entry.clips[0] == null) return;
        var voice = new Voice { action = action, station = station, source = CreateSource(station, entry, "Station Action Sound") };
        voices[actor] = voice;
        double elapsed = initialSnapshot ? System.Math.Max(0, Mirror.NetworkTime.time - action.started) : 0;
        double playAt = AudioSettings.dspTime + 0.05;
        if (station.Kind == PopcornStationKind.Water)
        {
            var start = AudioManager.Instance.Find("Popcorn_WaterStart");
            if (start != null && start.clips != null && start.clips.Length > 0 && start.clips[0] != null)
            {
                double clickLength = start.clips[0].length;
                if (!initialSnapshot)
                {
                    voice.click = CreateSource(station, start, "Dispenser Start Click");
                    voice.click.PlayScheduled(playAt);
                }
                playAt += System.Math.Max(0, clickLength - elapsed);
                elapsed = System.Math.Max(0, elapsed - clickLength);
            }
        }
        voice.source.loop = station.Kind != PopcornStationKind.Maker;
        if (voice.source.loop && voice.source.clip.length > 0)
            voice.source.time = (float)(elapsed % voice.source.clip.length);
        else if (elapsed >= voice.source.clip.length) return;
        else voice.source.time = (float)elapsed;
        voice.source.PlayScheduled(playAt);
    }

    private static AudioSource CreateSource(PopcornStation station, SoundLibrary.Entry entry, string name)
    {
        var emitter = new GameObject(name);
        emitter.transform.SetParent(station.transform, false);
        // Avoid the authored object's scale affecting the emitter position.
        emitter.transform.position = station.transform.position;
        var source = emitter.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.clip = entry.clips[0];
        source.spatialBlend = 1;
        source.dopplerLevel = 0;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = Mathf.Max(0.1f, entry.minDistance);
        source.maxDistance = Mathf.Max(source.minDistance + 0.1f, entry.maxDistance);
        var volume = emitter.AddComponent<SoundCategoryVolume>();
        volume.Category = entry.category;
        volume.SetBaseVolume(entry.volume);
        return source;
    }

    public void End(uint actor, int request, bool playEnd = true)
    {
        if (!voices.TryGetValue(actor, out var voice) || voice.action.request != request) return;
        voices.Remove(actor);
        if (voice.source != null) { voice.source.Stop(); Destroy(voice.source.gameObject); }
        if (voice.click != null) { voice.click.Stop(); Destroy(voice.click.gameObject); }
        if (playEnd && isActiveAndEnabled && voice.station != null && voice.station.Kind == PopcornStationKind.Water)
            AudioManager.Instance?.Play("Popcorn_WaterEnd", voice.station.transform.position, gameObject.scene);
    }

    public void StopLocal(int request)
    {
        uint actor = NetworkMode.IsOffline ? 0 : PlayerHealth.LocalInstance != null ? PlayerHealth.LocalInstance.netId : 0;
        suppressed[actor] = request;
        End(actor, request);
    }

    public void OneShot(PopcornStation station, uint revision)
    {
        if (NetworkMode.IsOffline)
        {
            string id = station.Kind == PopcornStationKind.Bucket || station.Kind == PopcornStationKind.Cup
                ? "Popcorn_Pickup" : "Popcorn_Flavor";
            AudioManager.Instance?.Play(id, station.transform.position, gameObject.scene);
        }
        else PopcornNetSync.Instance?.RequestStationOneShot(station.Id, revision);
    }

    public void StopAll()
    {
        foreach (var voice in voices.Values)
        {
            if (voice.source != null) { voice.source.Stop(); Destroy(voice.source.gameObject); }
            if (voice.click != null) { voice.click.Stop(); Destroy(voice.click.gameObject); }
        }
        voices.Clear();
        suppressed.Clear();
    }
    private void OnDisable() => StopAll();
}
