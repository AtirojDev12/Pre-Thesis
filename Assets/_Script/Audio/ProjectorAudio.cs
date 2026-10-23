using Mirror;
using UnityEngine;

/// <summary>Local 3D playback driven by the film chore's replicated state.</summary>
[DisallowMultipleComponent]
public sealed class ProjectorAudio : MonoBehaviour
{
    [Tooltip("Leave empty for a projector that never needs a film change.")]
    [SerializeField] private ZoneChoreSpot filmChore;
    [SerializeField] private AudioClip runningClip;
    [SerializeField] private AudioClip filmChangeClip;
    [SerializeField, Range(0, 1)] private float runningVolume = 0.85f;
    [SerializeField, Range(0, 1)] private float filmChangeVolume = 0.35f;
    [SerializeField, Min(0.1f)] private float minDistance = 2f;
    [SerializeField, Min(0.2f)] private float maxDistance = 15f;

    private enum Playback { Silent, Running, Changing }
    private Playback playback;
    private double changeStartedAt;
    private AudioSource source;
    private SoundCategoryVolume categoryVolume;
    private static double Now => NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;

    private void LateUpdate()
    {
        // Wait for the chore's initial snapshot. Dedicated servers stay silent.
        bool ready = !NetworkMode.SessionEnding && (NetworkMode.IsOffline || NetworkClient.active)
            && (filmChore == null || NetworkMode.IsOffline || filmChore.isClient);
        bool roundRunning = MatchDirector.Instance == null || MatchDirector.Instance.RoundRunning;
        Playback next = !ready || !roundRunning ? Playback.Silent
            : filmChore == null || !filmChore.Needed ? Playback.Running
            : filmChore.IsWorking ? Playback.Changing : Playback.Silent;
        ApplyPlayback(next);
    }

    private void ApplyPlayback(Playback next)
    {
        double start = next == Playback.Changing ? filmChore.WorkStartedAt : 0;
        // A completed one-shot must not restart while awaiting server completion.
        // A new hold in the same replication interval must restart from its new epoch.
        if (next == playback && (next != Playback.Changing || start == changeStartedAt)) return;
        if (source != null) source.Stop();
        playback = next;
        changeStartedAt = start;
        if (next == Playback.Silent) return;

        AudioClip clip = next == Playback.Running ? runningClip : filmChangeClip;
        if (clip == null) return;
        double elapsed = next == Playback.Changing ? System.Math.Max(0, Now - start) : 0;
        if (next == Playback.Changing && elapsed >= clip.length) return;
        EnsureSource();
        source.clip = clip;
        source.loop = next == Playback.Running;
        source.time = (float)elapsed;
        categoryVolume.SetBaseVolume(next == Playback.Running ? runningVolume : filmChangeVolume);
        source.Play();
    }

    private void EnsureSource()
    {
        if (source != null) return;
        var emitter = new GameObject("Projector Sound");
        emitter.transform.SetParent(transform, false);
        // "Projecter" contains booth walls too. Match the chore builder's
        // smallest-renderer rule so the sound comes from the machine itself.
        if (filmChore != null) emitter.transform.position = filmChore.transform.position;
        else
        {
            float smallest = float.MaxValue;
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                Vector3 size = renderer.bounds.size;
                float volume = size.x * size.y * size.z;
                if (volume <= 0.001f || volume >= smallest) continue;
                smallest = volume;
                emitter.transform.position = renderer.bounds.center;
            }
        }
        source = emitter.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1;
        source.dopplerLevel = 0;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = Mathf.Max(0.1f, minDistance);
        source.maxDistance = Mathf.Max(source.minDistance + 0.1f, maxDistance);
        // Both clips already contain room reverb.
        source.reverbZoneMix = 0;
        categoryVolume = emitter.AddComponent<SoundCategoryVolume>();
        categoryVolume.Category = SoundCategory.Sfx;
    }

    private void OnDisable()
    {
        if (source != null) source.Stop();
        playback = Playback.Silent;
        changeStartedAt = 0;
    }
}
