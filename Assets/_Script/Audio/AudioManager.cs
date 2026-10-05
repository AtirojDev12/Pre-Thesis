using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>One local playback service. UI survives scene changes; world sounds do not.</summary>
public sealed class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }
    [SerializeField] private SoundLibrary library;
    private const int PoolSize = 24;
    private readonly List<Voice> voices = new List<Voice>();
    private readonly Dictionary<string, double> nextUI = new Dictionary<string, double>();
    private sealed class Voice
    {
        public AudioSource source;
        public SoundCategoryVolume volume;
        public Scene scene;
        public double started;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Application.isBatchMode) return;
        if (Instance == null) new GameObject("AudioSystem").AddComponent<AudioManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        if (library == null) library = Resources.Load<SoundLibrary>("Audio/SoundLibrary");
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        if (Instance == this) Instance = null;
    }

    public SoundLibrary.Entry Find(string id) => library != null ? library.Find(id) : null;

    public void PlayUI(string id)
    {
        var entry = Find(id);
        if (entry == null) return;
        if (nextUI.TryGetValue(id, out double next) && AudioSettings.dspTime < next) return;
        nextUI[id] = AudioSettings.dspTime + entry.cooldown;
        Play(id, Vector3.zero, default, 0, true);
    }

    public void Play(string id, Vector3 position, Scene scene, int clipIndex = 0, bool force2D = false)
    {
        var entry = Find(id);
        if (entry == null || entry.clips == null || clipIndex < 0 || clipIndex >= entry.clips.Length) return;
        PlayClip(entry.clips[clipIndex], entry.category, entry.volume, position,
            entry.spatial && !force2D, entry.minDistance, entry.maxDistance, scene);
    }

    public void PlayClip(AudioClip clip, SoundCategory category, float volume, Vector3 position,
        bool spatial, float minDistance, float maxDistance, Scene scene, double scheduledStart = -1)
    {
        if (clip == null || Application.isBatchMode) return;
        Voice voice = voices.Find(v => !v.source.isPlaying && AudioSettings.dspTime >= v.started);
        if (voice == null && voices.Count < PoolSize)
        {
            var emitter = new GameObject("Pooled Sound");
            emitter.transform.SetParent(transform);
            var source = emitter.AddComponent<AudioSource>();
            source.playOnAwake = false;
            voice = new Voice { source = source, volume = emitter.AddComponent<SoundCategoryVolume>() };
            voices.Add(voice);
        }
        // Preserve UI clicks when world effects exhaust the pool.
        if (voice == null)
        {
            foreach (Voice candidate in voices)
                if (candidate.scene.IsValid() && (voice == null || candidate.started < voice.started)) voice = candidate;
            if (voice == null) return;
        }
        voice.source.Stop();
        voice.scene = scene;
        voice.started = System.Math.Max(AudioSettings.dspTime, scheduledStart);
        voice.source.transform.position = position;
        voice.source.clip = clip;
        voice.source.loop = false;
        voice.source.pitch = 1;
        voice.source.spatialBlend = spatial ? 1 : 0;
        voice.source.dopplerLevel = 0;
        voice.source.rolloffMode = AudioRolloffMode.Linear;
        voice.source.minDistance = Mathf.Max(0.1f, minDistance);
        voice.source.maxDistance = Mathf.Max(voice.source.minDistance + 0.1f, maxDistance);
        voice.volume.Category = category;
        voice.volume.SetBaseVolume(volume);
        if (scheduledStart > AudioSettings.dspTime) voice.source.PlayScheduled(scheduledStart);
        else voice.source.Play();
    }

    private void OnSceneUnloaded(Scene scene)
    {
        foreach (Voice voice in voices)
            if (voice.scene == scene)
            {
                voice.source.Stop();
                voice.started = 0;
            }
    }
}
