using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.VFX;
using PreThesis.Vfx;

/// <summary>Presentation and movement gate for the LO ghost, on host, clients and offline.</summary>
[DisallowMultipleComponent]
public sealed class GhostManifestation : NetworkBehaviour
{
    [SerializeField] private Material blackWaterMaterial;
    [SerializeField] private VisualEffectAsset splashGraph;
    [SerializeField] private VisualEffectAsset submergeSmokeGraph;
    [SerializeField] private AudioClip spawnClip;
    [SerializeField] private AudioClip[] walkClips;
    [SerializeField, Min(0.1f)] private float stepInterval = 0.65f;
    [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
    [SyncVar] private double spawnedAt;
    // Timestamps precede the bool so the hook reads the matching authoritative time.
    [SyncVar] private double disappearedAt;
    [SyncVar(hook = nameof(OnDisappearingChanged))] private bool disappearing;
    private Animator animator;
    private GhostPoolVisual visual;
    private Collider[] colliders;
    private bool[] enabledColliders;
    private bool started;
    private bool animationStarted;
    private float entryLength;
    private AudioSource footsteps;
    private Vector3 previousPosition;
    private float nextStep;
    private int lastClip = -1;
    private bool spawnFinished;
    private bool despawnStarted;

    public bool BlocksMovement => !spawnFinished || disappearing;

    public override void OnStartServer() => spawnedAt = NetworkTime.time;

    private void Awake()
    {
        visual = gameObject.AddComponent<GhostPoolVisual>();
        visual.Initialize();
        animator = visual.Animator;
        if (animator != null && animator.runtimeAnimatorController != null)
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
                if (clip.name == "Entry") entryLength = clip.length;
        colliders = GetComponentsInChildren<Collider>(true);
        enabledColliders = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++)
        {
            enabledColliders[i] = colliders[i].enabled;
            colliders[i].enabled = false;
        }
        var emitter = new GameObject("Ghost Footsteps");
        emitter.transform.SetParent(transform, false);
        footsteps = ConfigureAudio(emitter);
        previousPosition = transform.position;
    }

    private void Start()
    {
        started = true;
        if (NetworkMode.IsOffline) spawnedAt = Clock;
        float age = Age(spawnedAt);
        SynchronizeAnimator(age);
        if (!disappearing)
        {
            PlayEffect(false, age);
            if (spawnClip != null && CanPresent && age < .4f)
            {
                // Detached so a short ghost lifetime cannot cut off its spawn sound.
                var emitter = new GameObject("Ghost Spawn Sound");
                emitter.transform.position = transform.position;
                ConfigureAudio(emitter).PlayOneShot(spawnClip);
                Destroy(emitter, spawnClip.length + 0.2f);
            }
        }
        else PlayDisappear();
    }

    private bool CanPresent => !NetworkServer.active || NetworkClient.active;
    private double Clock => NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;
    private float Age(double timestamp) => Mathf.Max(0, (float)(Clock - timestamp));

    private void SynchronizeAnimator(float age)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        float animationAge = Mathf.Max(0, age - GhostPoolVisual.GrowSeconds);
        animator.Play(entryLength > 0 && animationAge < entryLength ? "Entry" : "Walk", 0,
            entryLength > 0 && animationAge < entryLength ? animationAge / entryLength : 0);
        animator.Update(0);
        animationStarted = age >= GhostPoolVisual.GrowSeconds;
        animator.speed = animationStarted ? 1 : 0;
    }

    private void LateUpdate()
    {
        if (!started) return;
        if (disappearing) visual.SampleDisappear(Age(disappearedAt));
        else visual.SampleAppear(Age(spawnedAt));
    }

    private void Update()
    {
        if (!started || disappearing) return;
        float age = Age(spawnedAt);
        if (!animationStarted && age >= GhostPoolVisual.GrowSeconds) SynchronizeAnimator(age);
        if (!spawnFinished)
        {
            // Preserve the existing Entry movement gate, including the pool's lead-in.
            spawnFinished = age >= GhostPoolVisual.HiddenAt &&
                (animator == null || animator.runtimeAnimatorController == null ||
                (animator.GetCurrentAnimatorStateInfo(0).IsName("Walk") && !animator.IsInTransition(0)));
            if (spawnFinished)
                for (int i = 0; i < colliders.Length; i++)
                    if (colliders[i] != null) colliders[i].enabled = enabledColliders[i];
        }
        Vector3 delta = transform.position - previousPosition;
        previousPosition = transform.position;
        if (!NetworkMode.HasServerAuthority(this) || BlocksMovement) return;
        delta.y = 0f;
        if (delta.sqrMagnitude < 0.000001f || Time.time < nextStep || walkClips == null || walkClips.Length == 0) return;
        nextStep = Time.time + stepInterval;
        int clip = Random.Range(0, lastClip >= 0 && walkClips.Length > 1 ? walkClips.Length - 1 : walkClips.Length);
        if (lastClip >= 0 && walkClips.Length > 1 && clip >= lastClip) clip++;
        lastClip = clip;
        if (NetworkServer.active) RpcStep(clip);
        else PlayStep(clip);
    }

    [ClientRpc]
    private void RpcStep(int clip) => PlayStep(clip);

    private void PlayStep(int clip)
    {
        if (disappearing || !CanPresent || clip < 0 || clip >= walkClips.Length || walkClips[clip] == null) return;
        footsteps.PlayOneShot(walkClips[clip]);
    }

    public void Despawn()
    {
        if (!NetworkMode.HasServerAuthority(this) || despawnStarted) return;
        despawnStarted = true;
        disappearedAt = Clock;
        disappearing = true;
        GetComponent<GhostWarningSource>()?.SetWarningActive(false);
        if (started) PlayDisappear();
        StartCoroutine(FinishDespawn());
    }

    private IEnumerator FinishDespawn()
    {
        // Keep the identity alive until every observer can see the body sink fully.
        if (!started) yield return null;
        yield return new WaitForSeconds(GhostPoolVisual.GrowSeconds + visual.DisappearTravelSeconds + .2f);
        if (NetworkServer.active) NetworkServer.Destroy(gameObject);
        else Destroy(gameObject);
    }

    private void OnDisappearingChanged(bool oldValue, bool newValue)
    {
        if (newValue && started) PlayDisappear();
    }

    private bool disappearancePlayed;
    private void PlayDisappear()
    {
        if (disappearancePlayed) return;
        disappearancePlayed = true;
        footsteps.Stop();
        visual.SampleAppear(Mathf.Max(0, (float)(disappearedAt - spawnedAt)));
        float entryProgress = entryLength > 0
            ? Mathf.Clamp01(((float)(disappearedAt - spawnedAt) - GhostPoolVisual.GrowSeconds) / entryLength) : 1;
        visual.BeginDisappear(entryProgress);
        visual.SampleDisappear(Age(disappearedAt));
        PlayEffect(true, Age(disappearedAt));
        foreach (Collider collider in colliders) if (collider != null) collider.enabled = false;
        GetComponent<GhostWarningSource>()?.SetWarningActive(false);
    }

    private void PlayEffect(bool submerging, float age)
    {
        float travelSeconds = submerging ? visual.DisappearTravelSeconds : GhostPoolVisual.TravelSeconds;
        if (blackWaterMaterial == null || !CanPresent || age >= GhostPoolEffect.LifetimeFor(travelSeconds)) return;
        Vector3 position = transform.position;
        Vector3 normal = Vector3.up;
        float nearest = float.PositiveInfinity;
        foreach (var hit in Physics.RaycastAll(position + Vector3.up * .5f, Vector3.down, 3,
                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform) || Vector3.Dot(hit.normal, Vector3.up) < .5f || hit.distance >= nearest) continue;
            nearest = hit.distance; position = hit.point; normal = hit.normal;
        }
        var effect = new GameObject(submerging ? "Ghost Black Water - Submerge" : "Ghost Black Water - Emerge");
        effect.transform.SetPositionAndRotation(position,
            Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, transform.eulerAngles.y, 0));
        effect.AddComponent<GhostPoolEffect>().Initialize(blackWaterMaterial, splashGraph, submergeSmokeGraph, age, submerging, travelSeconds);
    }

    private AudioSource ConfigureAudio(GameObject emitter)
    {
        AudioSource source = emitter.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.Stop();
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1f;
        source.maxDistance = 20f;
        source.volume = volume;
        emitter.AddComponent<SoundCategoryVolume>().Category = SoundCategory.Sfx;
        return source;
    }
}
