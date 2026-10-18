using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One ordered PA playlist, played together by all four lobby speakers.</summary>
[DisallowMultipleComponent]
public sealed class LobbyAnnouncements : MonoBehaviour
{
    [SerializeField] private AudioClip[] clips;
    [SerializeField] private Transform[] speakers;
    [SerializeField] private AudioClip interludeClip;
    [SerializeField, Min(0f)] private float gapSeconds = 20f;
    [SerializeField, Range(0f, 1f)] private float volume = .08f;
    [SerializeField, Range(0f, 1f)] private float interludeVolume = .04f;
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
                double end = PlayClip(clip, false, clip.length, volume);
                yield return new WaitUntil(() => AudioSettings.dspTime >= end);

                // Fill every gap, including the last-to-first transition, with the PA loop.
                float gap = Mathf.Max(0f, gapSeconds);
                if (gap <= 0f) continue;
                if (interludeClip != null)
                {
                    end = PlayClip(interludeClip, true, gap, interludeVolume);
                    yield return new WaitUntil(() => AudioSettings.dspTime >= end);
                }
                else
                {
                    yield return new WaitForSecondsRealtime(gap);
                }
            }
            if (!foundClip) yield break;
        }
    }

    private double PlayClip(AudioClip clip, bool loop, float duration, float baseVolume)
    {
        // Share the audio clock so all four speakers start and stop together.
        double start = AudioSettings.dspTime + .05;
        double end = start + duration;
        foreach (AudioSource source in sources)
        {
            if (source == null) continue;
            source.Stop();
            source.clip = clip;
            source.loop = loop;
            source.time = 0f;
            source.GetComponent<SoundCategoryVolume>().SetBaseVolume(baseVolume);
            source.PlayScheduled(start);
            source.SetScheduledEndTime(end);
        }
        return end;
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
