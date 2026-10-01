using System;
using Mirror;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The owner detects rendered sole contacts after PlayerMovement's IK/grounding.
/// The server chooses one clip per contact; every observer plays that same choice.
/// Remote animation never generates a second set of steps.
/// </summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(PlayerMovement))]
[DisallowMultipleComponent]
public sealed class PlayerFootsteps : NetworkBehaviour
{
    [SerializeField] private AudioClip[] carpetClips;
    [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
    [SerializeField, Min(0.1f)] private float minDistance = 1f;
    [SerializeField, Min(1f)] private float maxDistance = 15f;
    [SerializeField, Min(0f)] private float contactHeight = 0.012f;
    [SerializeField, Min(0f)] private float releaseHeight = 0.035f;
    [Tooltip("Owner's foot contact event: 0 = left, 1 = right.")]
    public UnityEvent<int> FootTouchedGround = new UnityEvent<int>();

    /// <summary>Playback notification on each client: foot index, shared clip index.</summary>
    public event Action<int, int> FootstepPlayed;
    private PlayerMovement movement;
    private PlayerHealth health;
    private Rigidbody body;
    private readonly bool[] planted = new bool[2];
    private readonly AudioSource[] sources = new AudioSource[2];
    private bool initialized;
    private int lastClip = -1;
    private readonly double[] nextServerStep = new double[2];
    private readonly float[] nextLocalStep = new float[2];

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        health = GetComponent<PlayerHealth>();
        body = GetComponent<Rigidbody>();
        for (int foot = 0; foot < 2; foot++)
        {
            var emitter = new GameObject(foot == 0 ? "Left Footstep" : "Right Footstep");
            emitter.transform.SetParent(transform, false);
            var source = emitter.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear; // Exactly zero at/beyond maxDistance.
            source.minDistance = Mathf.Max(0.1f, minDistance);
            source.maxDistance = Mathf.Max(source.minDistance + 0.1f, maxDistance);
            source.volume = volume;
            emitter.AddComponent<SoundCategoryVolume>().Category = SoundCategory.Sfx;
            sources[foot] = source;
        }
    }

    private void LateUpdate()
    {
        // Stop standing steps when crouching, including on remote observers.
        if (movement.IsCrouching)
            foreach (AudioSource source in sources)
                if (source != null && source.isPlaying) source.Stop();
        if (!NetworkMode.IsLocalController(this)) return;
        bool moving = !movement.IsCrouching && !GameplayInput.Blocked && !Incapacitated && movement.CurrentMovementSpeed > 0.05f &&
            body != null && new Vector2(body.linearVelocity.x, body.linearVelocity.z).sqrMagnitude > 0.01f;
        for (int foot = 0; foot < 2; foot++)
        {
            bool supported = movement.TryGetFootGround(foot == 0, out _, out float gap);
            bool contact = supported && gap <= (planted[foot] ? Mathf.Max(contactHeight, releaseHeight) : contactHeight);
            bool landed = initialized && contact && !planted[foot];
            planted[foot] = contact;
            if (!landed || !moving || Time.time < nextLocalStep[foot]) continue;
            // Each clip plants a given foot once per cycle (1.03 s walk,
            // 0.67 s strafe run). This also rejects threshold bounce.
            nextLocalStep[foot] = Time.time + (movement.IsSprinting ? 0.28f : 0.42f);
            FootTouchedGround.Invoke(foot);
            if (NetworkMode.IsOffline) ChooseAndPlay(foot);
            else if (isLocalPlayer && NetworkClient.ready) CmdFootContact(foot);
        }
        initialized = true;
    }

    private bool Incapacitated => health != null && (health.IsDead || health.IsDowned);

    [Command]
    private void CmdFootContact(int foot)
    {
        // Mirror enforces ownership. Reject invalid/spammed contacts and dead players.
        if (foot < 0 || foot > 1 || movement.IsCrouching || Incapacitated || NetworkTime.time < nextServerStep[foot]) return;
        nextServerStep[foot] = NetworkTime.time + (movement.IsSprinting ? 0.25 : 0.38);
        ChooseAndPlay(foot);
    }

    private void ChooseAndPlay(int foot)
    {
        if (carpetClips == null || carpetClips.Length == 0) return;
        int count = carpetClips.Length;
        int clip = UnityEngine.Random.Range(0, lastClip >= 0 && count > 1 ? count - 1 : count);
        if (count > 1 && lastClip >= 0 && clip >= lastClip) clip++;
        lastClip = clip;
        if (NetworkMode.IsOffline) PlayFootstep(foot, clip);
        else RpcFootstep(foot, clip);
    }

    [ClientRpc]
    private void RpcFootstep(int foot, int clip) => PlayFootstep(foot, clip);

    private void PlayFootstep(int foot, int clip)
    {
        if (movement.IsCrouching || Incapacitated) return;
        if (foot < 0 || foot >= sources.Length || carpetClips == null || clip < 0 || clip >= carpetClips.Length || carpetClips[clip] == null) return;
        // Use this client's interpolated player location, so sound follows the visible body.
        movement.TryGetFootGround(foot == 0, out Vector3 position, out _);
        sources[foot].transform.position = position;
        sources[foot].PlayOneShot(carpetClips[clip]);
        FootstepPlayed?.Invoke(foot, clip);
    }
}
