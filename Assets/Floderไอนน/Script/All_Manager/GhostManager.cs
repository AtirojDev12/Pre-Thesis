using System.Collections;
using Mirror;
using UnityEngine;

/// <summary>
/// ตัวจัดการรอบการปล่อยผีและไฟในฉาก
///
/// MULTIPLAYER NOTES
/// -----------------
/// The SERVER owns the whole cycle: the timer, the light flicker, the spawn and
/// the despawn. Clients are told the results.
///
/// Three things had to change:
///
///   1. Instantiate -> NetworkServer.Spawn. A plain Instantiate creates a ghost
///      that exists on one machine only. Five players would be running from
///      nothing while the sixth insists there is a ghost.
///
///   2. The light state became a SyncVar. It used to be a local bool on each
///      machine, so in a match half the team could be standing in the dark and
///      the other half in the light, and TimedGhost's hunting mode would differ
///      per machine.
///
///   3. A static Instance, so TimedGhost stops calling FindObjectOfType twice
///      per jumpscare.
///
/// The ghost prefab MUST be in NetworkManager.spawnPrefabs or clients cannot
/// spawn it and will see nothing.
///
/// OFFLINE still works: with no server running, the same code Instantiates
/// locally, so the enemy test scenes need no host.
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
public class GhostManager : NetworkBehaviour
{
    public static GhostManager Instance { get; private set; }

    [Header("Prefab Settings")]
    [Tooltip("Must also be registered in NetworkManager.spawnPrefabs, or clients will never see the ghost.")]
    [SerializeField] private GameObject ghostPrefab;
    private GameObject currentGhostInstance;

    [Header("Spawn Position")]
    [SerializeField] private Transform spawnPoint;

    [Header("Lights Settings")]
    [SerializeField] private Light[] allLights;

    [Header("Timing Settings")]
    [Tooltip("FALLBACK only. In a match these come from the room's DifficultyProfile (via MatchDirector). Used when there is no MatchDirector, e.g. an enemy test scene.")]
    [SerializeField] private float spawnInterval = 60f;
    [SerializeField] private float flickerDuration = 3f;
    [Tooltip("FALLBACK only — see Spawn Interval.")]
    [SerializeField] private float countdownDuration = 10f;
    [Tooltip("FALLBACK only — see Spawn Interval.")]
    [SerializeField] private float ghostDuration = 15f;

    // What the cycle actually uses. Start as the Inspector fallbacks, replaced
    // once by the difficulty profile when the round is configured. Server only.
    private float activeSpawnInterval;
    private float activeCountdown;
    private float activeGhostDuration;
    private bool difficultyApplied;

    private float timer = 0f;
    private bool isGhostActive = false;
    private bool isSequenceRunning = false;

    /// <summary>
    /// Replicated so every machine agrees whether the building is lit. TimedGhost
    /// hunts differently in the dark, so this disagreeing between clients would
    /// mean the ghost behaves differently on each screen.
    /// </summary>
    [SyncVar(hook = nameof(OnLightsChanged))]
    private bool lightsOn = true;

    public bool areLightsOnCurrently => lightsOn;

    /// <summary>True on the machine allowed to run the cycle: server, or anybody when offline.</summary>
    private bool HasAuthority => NetworkMode.HasServerAuthority(this);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[GhostManager] There is a second GhostManager in this scene — destroying the duplicate.", this);
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        timer = 0f;
        activeSpawnInterval = spawnInterval;
        activeCountdown = countdownDuration;
        activeGhostDuration = ghostDuration;
        ApplyLights(true);

        if (HasAuthority) lightsOn = true;
    }

    private void Update()
    {
        // Clients never run the spawn cycle. They receive the ghost and the
        // light state from the server.
        if (!HasAuthority) return;

        // Polled rather than read once in Start: MatchDirector configures the
        // round in OnStartServer, which can land after our Start. The profile
        // never changes mid-round, so this stops checking once it has it.
        if (!difficultyApplied) TryApplyDifficulty();

        if (isGhostActive || isSequenceRunning) return;

        timer += Time.deltaTime;

        if (timer >= (activeSpawnInterval - flickerDuration))
            StartCoroutine(GhostArrivalSequence());
    }

    private IEnumerator GhostArrivalSequence()
    {
        isSequenceRunning = true;
        timer = 0f;

        // The flicker is driven from the server so everyone sees the same
        // stutter at the same moment — it is a warning, and a warning that
        // arrives on one machine only is worthless.
        SetLights(false); yield return new WaitForSeconds(0.2f);
        SetLights(true); yield return new WaitForSeconds(0.3f);
        SetLights(false); yield return new WaitForSeconds(0.2f);
        SetLights(true); yield return new WaitForSeconds(0.3f);
        SetLights(false); yield return new WaitForSeconds(0.1f);

        SetLights(true);

        yield return new WaitForSeconds(activeCountdown);

        SpawnGhost();
    }

    private void SpawnGhost()
    {
        isGhostActive = true;

        if (ghostPrefab == null || spawnPoint == null)
        {
            Debug.LogWarning("[GhostManager] No ghost prefab or spawn point assigned — nothing spawned.", this);
            Invoke(nameof(DespawnGhost), activeGhostDuration);
            return;
        }

        currentGhostInstance = Instantiate(ghostPrefab, spawnPoint.position, spawnPoint.rotation);

        // The line that makes the ghost real for everyone rather than for one
        // machine. Offline there is no server, so the local Instantiate above
        // is already the whole story.
        if (NetworkServer.active) NetworkServer.Spawn(currentGhostInstance);

        if (currentGhostInstance.TryGetComponent(out TimedGhost ghostScript))
            ghostScript.isSceneLightOn = lightsOn;

        Invoke(nameof(DespawnGhost), activeGhostDuration);
    }

    /// <summary>
    /// SERVER / OFFLINE. Takes the ghost timing from the room's difficulty.
    /// Without a MatchDirector in the scene the Inspector values stay in use.
    /// </summary>
    private void TryApplyDifficulty()
    {
        MatchDirector director = MatchDirector.Instance;
        DifficultyProfile profile = director != null ? director.ActiveProfile : null;
        if (profile == null) return;

        activeSpawnInterval = Mathf.Max(flickerDuration + 1f, profile.ghostSpawnIntervalSeconds);
        activeCountdown = Mathf.Max(0f, profile.ghostWarningSeconds);
        activeGhostDuration = Mathf.Max(1f, profile.ghostActiveSeconds);
        difficultyApplied = true;

        Debug.Log($"[GhostManager] {profile.level}: ghost every {activeSpawnInterval:F0}s, " +
                  $"{activeCountdown:F0}s warning, stays {activeGhostDuration:F0}s.", this);
    }

    private void DespawnGhost()
    {
        isGhostActive = false;
        isSequenceRunning = false;

        if (currentGhostInstance != null)
        {
            if (NetworkServer.active) NetworkServer.Destroy(currentGhostInstance);
            else Destroy(currentGhostInstance);
        }

        SetLights(true);
    }

    /// <summary>SERVER ONLY. Stops the despawn timer so a jumpscare is not cut short.</summary>
    public void CancelDespawnTimer()
    {
        if (!HasAuthority) return;
        CancelInvoke(nameof(DespawnGhost));
    }

    /// <summary>SERVER ONLY. Clears state after a jumpscare so the next cycle can start.</summary>
    public void ResetManagerAfterJumpscare()
    {
        if (!HasAuthority) return;

        isGhostActive = false;
        isSequenceRunning = false;
        currentGhostInstance = null;
        SetLights(true);
    }

    /// <summary>
    /// SERVER ONLY. Call this from a player's [Command] — see LightController.
    /// A client calling it directly changes only its own screen, and TimedGhost
    /// would then hunt differently on that machine than on everyone else's.
    /// </summary>
    public void PlayerToggleLights(bool open)
    {
        if (!HasAuthority)
        {
            Debug.LogWarning("[GhostManager] PlayerToggleLights was called on a client. Route it through a Command — ignored.", this);
            return;
        }

        SetLights(open);
    }

    private void SetLights(bool state)
    {
        lightsOn = state;

        // SyncVar hooks do not fire on the machine that made the change, so the
        // host has to apply its own result directly or the host's lights would
        // never move while every client's did.
        ApplyLights(state);

        if (currentGhostInstance != null && currentGhostInstance.TryGetComponent(out TimedGhost ghost))
            ghost.isSceneLightOn = state;
    }

    private void OnLightsChanged(bool oldValue, bool newValue)
    {
        ApplyLights(newValue);

        if (currentGhostInstance != null && currentGhostInstance.TryGetComponent(out TimedGhost ghost))
            ghost.isSceneLightOn = newValue;
    }

    private void ApplyLights(bool state)
    {
        if (allLights == null) return;

        foreach (Light lightItem in allLights)
            if (lightItem != null) lightItem.enabled = state;
    }
}
