using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One ordered PA playlist, played together by all four lobby speakers.</summary>
[DisallowMultipleComponent]
public sealed class LobbyAnnouncements : MonoBehaviour
{
    [SerializeField] private AudioClip[] clips;
    [SerializeField] private Transform[] speakers;
    [SerializeField, Min(0f)] private float gapSeconds = 5f;
    [SerializeField, Range(0f, 1f)] private float volume = .12f;
    [SerializeField, Min(.1f)] private float minDistance = 2f;
    [SerializeField, Min(1f)] private float maxDistance = 24f;
    private readonly List<AudioSource> sources = new List<AudioSource>();
    private Coroutine playlist;

    private void Awake()
    {
        if (speakers == null) return;
        foreach (Transform speaker in speakers)
        {
            if (speaker == null) continue;
            var emitter = new GameObject("Lobby Announcement Audio");
            emitter.transform.SetParent(speaker, false);
            var source = emitter.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.Stop();
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.spread = 75f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = Mathf.Max(.1f, minDistance);
            source.maxDistance = Mathf.Max(source.minDistance + .1f, maxDistance);
            source.volume = volume;
            source.priority = 180;
            emitter.AddComponent<SoundCategoryVolume>().Category = SoundCategory.Ambient;
            sources.Add(source);
        }
    }

    private void OnEnable() => playlist = StartCoroutine(PlayAnnouncements());

    private IEnumerator PlayAnnouncements()
    {
        if (clips == null || clips.Length == 0 || sources.Count == 0) yield break;
        while (true)
        {
            bool foundClip = false;
            foreach (AudioClip clip in clips)
            {
                if (clip == null) continue;
                foundClip = true;
                // All emitters use the same audio-clock start, avoiding four delayed echoes.
                double start = AudioSettings.dspTime + .05;
                foreach (AudioSource source in sources)
                {
                    if (source == null) continue;
                    source.clip = clip;
                    source.time = 0f;
                    source.PlayScheduled(start);
                }
                // The gap is after the complete clip, including the last-to-first transition.
                yield return new WaitForSecondsRealtime(clip.length + Mathf.Max(0f, gapSeconds));
            }
            if (!foundClip) yield break;
        }
    }

    private void OnDisable()
    {
        if (playlist != null) StopCoroutine(playlist);
        playlist = null;
        foreach (AudioSource source in sources)
            if (source != null) source.Stop();
    }

    private void OnDestroy()
    {
        foreach (AudioSource source in sources)
            if (source != null) Destroy(source.gameObject);
    }
}
