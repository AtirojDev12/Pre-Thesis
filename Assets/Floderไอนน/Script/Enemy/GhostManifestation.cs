using System.Collections;
using Mirror;
using UnityEngine;

/// <summary>Presentation and movement gate for the LO ghost, on host, clients and offline.</summary>
[DisallowMultipleComponent]
public sealed class GhostManifestation : NetworkBehaviour
{
    [SerializeField] private GameObject appearPrefab;
    [SerializeField] private GameObject disappearPrefab;
    [SerializeField] private AudioClip spawnClip;
    [SerializeField] private AudioClip[] walkClips;
    [SerializeField, Min(0.1f)] private float stepInterval = 0.65f;
    [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
    [SyncVar(hook = nameof(OnDisappearingChanged))] private bool disappearing;
    [SyncVar] private double spawnedAt;
    private Animator animator;
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
        animator = GetComponent<Animator>();
        var emitter = new GameObject("Ghost Footsteps");
        emitter.transform.SetParent(transform, false);
        footsteps = ConfigureAudio(emitter);
        previousPosition = transform.position;
    }

    private void Start()
    {
        // Keep evaluating Entry even when nobody has the ghost on screen.
        if (animator != null)
        {
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            // A joining observer starts at the same point in Entry as the server.
            if (!NetworkMode.IsOffline && animator.runtimeAnimatorController != null)
            {
                foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
                {
                    if (clip.name != "Entry" || clip.length <= 0f) continue;
                    float age = Mathf.Max(0f, (float)(NetworkTime.time - spawnedAt));
                    animator.Play(age >= clip.length ? "Walk" : "Entry", 0,
                        age >= clip.length ? 0f : age / clip.length);
                    animator.Update(0f);
                    break;
                }
            }
        }
        if (!disappearing)
        {
            PlayEffect(appearPrefab);
            if (spawnClip != null && CanPresent)
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

    private void Update()
    {
        if (!spawnFinished)
        {
            // The controller leaves Entry only at normalized time 1.
            spawnFinished = animator == null || animator.runtimeAnimatorController == null ||
                (animator.GetCurrentAnimatorStateInfo(0).IsName("Walk") && !animator.IsInTransition(0));
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
        disappearing = true;
        GetComponent<GhostWarningSource>()?.SetWarningActive(false);
        if (NetworkMode.IsOffline) PlayDisappear();
        StartCoroutine(FinishDespawn());
    }

    private IEnumerator FinishDespawn()
    {
        // Let observers receive the disappearing state before removing the identity.
        yield return new WaitForSeconds(0.3f);
        if (NetworkServer.active) NetworkServer.Destroy(gameObject);
        else Destroy(gameObject);
    }

    private void OnDisappearingChanged(bool oldValue, bool newValue)
    {
        if (newValue) PlayDisappear();
    }

    private bool disappearancePlayed;
    private void PlayDisappear()
    {
        if (disappearancePlayed) return;
        disappearancePlayed = true;
        footsteps.Stop();
        PlayEffect(disappearPrefab);
        foreach (Renderer visual in GetComponentsInChildren<Renderer>()) visual.enabled = false;
        foreach (Collider collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
        GetComponent<GhostWarningSource>()?.SetWarningActive(false);
    }

    private void PlayEffect(GameObject prefab)
    {
        if (prefab == null || !CanPresent) return;
        // Independent world-space object: smoke survives the network ghost's removal.
        GameObject effect = Instantiate(prefab, transform.position, transform.rotation);
        foreach (ParticleSystem particles in effect.GetComponentsInChildren<ParticleSystem>()) particles.Play();
        Destroy(effect, 5f);
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
