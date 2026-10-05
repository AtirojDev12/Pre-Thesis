using Mirror;
using UnityEngine;

/// <summary>Attach to a networked world object for a persistent, server-owned loop.</summary>
[RequireComponent(typeof(NetworkIdentity)), DisallowMultipleComponent]
public sealed class NetworkSoundEmitter : NetworkBehaviour
{
    [SerializeField] private string soundId;
    [SyncVar(hook = nameof(OnStartedAtChanged))] private double startedAt;
    [SyncVar(hook = nameof(OnPlayingChanged))] private bool playing;
    private AudioSource source;

    public override void OnStartClient() => Apply();
    private void OnPlayingChanged(bool previous, bool current) => Apply();
    // A stop/start inside one replication interval may leave playing == true
    // on a remote client. The new epoch must still restart its loop.
    private void OnStartedAtChanged(double previous, double current)
    {
        if (playing) Apply(true);
    }

    public void SetPlaying(bool value)
    {
        if (!NetworkMode.HasServerAuthority(this) || playing == value) return;
        if (value) startedAt = NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;
        playing = value;
        if (NetworkMode.IsOffline || isClient) Apply();
    }

    private void Apply(bool restart = false)
    {
        if (Application.isBatchMode) return;
        if (!playing) { if (source != null) source.Stop(); return; }
        if (!restart && source != null && source.isPlaying) return;
        var entry = AudioManager.Instance?.Find(soundId);
        if (entry == null || entry.clips == null || entry.clips.Length == 0 || entry.clips[0] == null) return;
        if (source == null)
        {
            var emitter = new GameObject("Network Sound Loop");
            emitter.transform.SetParent(transform, false);
            source = emitter.AddComponent<AudioSource>();
            source.playOnAwake = false;
            emitter.AddComponent<SoundCategoryVolume>();
        }
        source.clip = entry.clips[0];
        source.loop = true;
        source.spatialBlend = entry.spatial ? 1 : 0;
        source.dopplerLevel = 0;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = Mathf.Max(0.1f, entry.minDistance);
        source.maxDistance = Mathf.Max(source.minDistance + 0.1f, entry.maxDistance);
        var volume = source.GetComponent<SoundCategoryVolume>();
        volume.Category = entry.category;
        volume.SetBaseVolume(entry.volume);
        double now = NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;
        if (source.clip.length > 0) source.time = (float)(System.Math.Max(0, now - startedAt) % source.clip.length);
        source.Play();
    }

    private void OnDisable() { if (source != null) source.Stop(); }
    private void OnEnable() { if (NetworkMode.IsOffline || isClient) Apply(); }
}
