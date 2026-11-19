using Mirror;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Detached local presentation of the server's seasoning disappearance event.
/// 19 Nov 2026: also plays the SoundLibrary sound "GhostFavor_Disappear" (เสียงรสชาติผีหาย.wav)
/// once, at the popcorn station, on every machine. Late joiners do not hear an old one.
/// </summary>
public sealed class GhostFavorDisappearVfx : MonoBehaviour
{
    [SerializeField] private VisualEffectAsset smokeGraph;
    [Tooltip("Playback speed scales the graph's total two-second duration.")]
    [Range(.5f, 2f)] [SerializeField] private float playbackSpeed = 1f;
    [Tooltip("Smoke diameter relative to the seasoning jar's visible size.")]
    [Range(.5f, 2f)] [SerializeField] private float sizeMultiplier = 1f;
    private GhostFavorRecovery recovery;
    private uint observedId;
    private Vector3 centreOffset;
    private float scale;
    private GameObject activeSmoke;
    public const float Lifetime = 2f;
    public const string DisappearSoundId = "GhostFavor_Disappear";
    // 19 Nov: a disappearance older than this (network delay / late join) plays no sound.
    private const float SoundMaxAge = 0.75f;

    public void Configure(GhostFavorRecovery owner)
    {
        recovery = owner;
        if (smokeGraph == null) smokeGraph = Resources.Load<VisualEffectAsset>("GhostFavorDisappear");
        var visuals = owner.ItemTransform.GetComponentsInChildren<Renderer>();
        Bounds bounds = new Bounds(owner.HomePosition, Vector3.one * .25f);
        bool found = false;
        foreach (var visual in visuals)
        {
            if (!visual.enabled) continue;
            if (!found) { bounds = visual.bounds; found = true; }
            else bounds.Encapsulate(visual.bounds);
        }
        centreOffset = bounds.center - owner.HomePosition;
        scale = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, .15f);
    }

    public void Observe(GhostFavorState state)
    {
        if (!Application.isPlaying || !isActiveAndEnabled || NetworkMode.SessionEnding) return;
        if (state.disappearanceId == 0 || (observedId != 0 &&
            unchecked((int)(state.disappearanceId - observedId)) <= 0)) return;
        observedId = state.disappearanceId;
        double now = NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;
        float realAge = Mathf.Max(0, (float)(now - state.disappearedAt));
        float age = realAge * playbackSpeed;
        if (realAge < SoundMaxAge) PlayDisappearSound();
        // Joining after the fade must not replay a stale disappearance.
        if (age >= Lifetime || recovery == null || smokeGraph == null ||
            SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        ClearSmoke();
        activeSmoke = new GameObject("Ghost Favor - lingering disappearance smoke");
        // Deliberately not parented to the item or player.
        activeSmoke.transform.position = recovery.HomePosition + centreOffset;
        activeSmoke.transform.localScale = Vector3.one * (scale * sizeMultiplier);
        var effect = activeSmoke.AddComponent<VisualEffect>();
        effect.initialEventName = "Idle";
        effect.visualEffectAsset = smokeGraph;
        effect.playRate = playbackSpeed;
        effect.startSeed = state.disappearanceId;
        effect.resetSeedOnPlay = false;
        effect.Play();
        if (age > 0) effect.Simulate(1f / 60, (uint)Mathf.FloorToInt(age * 60));
        Destroy(activeSmoke, (Lifetime - age) / playbackSpeed);
    }

    /// <summary>
    /// 19 Nov: the ghost flavor disappears -> "GhostFavor_Disappear" from SoundLibrary, 3D at the station.
    /// PlayClip (not Play) so a ghost event does not raise the local player's noise bar.
    /// </summary>
    private void PlayDisappearSound()
    {
        AudioManager audio = AudioManager.Instance;
        SoundLibrary.Entry entry = audio != null ? audio.Find(DisappearSoundId) : null;
        if (entry == null || entry.clips == null || entry.clips.Length == 0 || recovery == null) return;
        AudioClip clip = entry.clips[Random.Range(0, entry.clips.Length)];
        audio.PlayClip(clip, entry.category, entry.volume, recovery.HomePosition + centreOffset,
            entry.spatial, entry.minDistance, entry.maxDistance, gameObject.scene);
    }

    private void ClearSmoke()
    {
        if (activeSmoke != null) Destroy(activeSmoke);
        activeSmoke = null;
    }

    private void OnDisable() => ClearSmoke();
}
