using System.Collections;
using Mirror;
using UnityEngine;

/// <summary>Owns only ghost spawn timing. Building power is independent.</summary>
[RequireComponent(typeof(NetworkIdentity))]
public class GhostManager : NetworkBehaviour, IDevGhostSource
{
    public static GhostManager Instance { get; private set; }

    [Header("Prefab Settings")]
    [Tooltip("Must also be registered in NetworkManager.spawnPrefabs, or clients will never see the ghost.")]
    [SerializeField] private GameObject ghostPrefab;
    private GameObject currentGhostInstance;

    [Header("Spawn Position")]
    [SerializeField] private Transform spawnPoint;

    [Header("Timing Settings")]
    [SerializeField] private float spawnInterval = 60f;
    [UnityEngine.Serialization.FormerlySerializedAs("flickerDuration")]
    [SerializeField] private float arrivalLeadTime = 3f;
    [SerializeField] private float countdownDuration = 10f;
    [SerializeField] private float ghostDuration = 15f;

    private float timer = 0f;
    private bool isGhostActive = false;
    private bool isSequenceRunning = false;
    private bool flickerRunning; // true while the warning flicker may have the lights off
    private Coroutine despawnRoutine;
    // Safety: never wait longer than this for the appear animation before the life time starts.
    private const float MaxAppearWait = 20f;

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
    }

    private void Update()
    {
        // Clients never run the spawn cycle. They receive the ghost from the
        // server.
        if (!HasAuthority) return;
        if (isGhostActive || isSequenceRunning) return;

        timer += Time.deltaTime;

        if (timer >= (spawnInterval - arrivalLeadTime))
            StartCoroutine(GhostArrivalSequence());
    }

    private IEnumerator GhostArrivalSequence()
    {
        isSequenceRunning = true;
        timer = 0f;

        // Warning flicker before the ghost arrives (restored 14 Oct, same timing as
        // the old version: 1.1 s). Driven by BuildingLightManager, so every machine
        // sees it. Only when the lights are ON; if a player switched them off, the
        // building stays dark (Atiroj, 11 Oct: the ghost never turns lights back on).
        BuildingLightManager power = BuildingLightManager.Instance;
        if (power != null && BuildingLightManager.LightsOn)
        {
            flickerRunning = true;
            power.SetLights(false); yield return new WaitForSeconds(0.2f);
            power.SetLights(true);  yield return new WaitForSeconds(0.3f);
            power.SetLights(false); yield return new WaitForSeconds(0.2f);
            power.SetLights(true);  yield return new WaitForSeconds(0.3f);
            power.SetLights(false); yield return new WaitForSeconds(0.1f);
            power.SetLights(true);
            flickerRunning = false;
        }
        else
        {
            // Lights already off (or no power manager): keep the same arrival timing.
            yield return new WaitForSeconds(1.1f);
        }

        yield return new WaitForSeconds(countdownDuration);

        SpawnGhost();
    }

    private void SpawnGhost()
    {
        isGhostActive = true;

        if (ghostPrefab == null || spawnPoint == null)
        {
            Debug.LogWarning("[GhostManager] No ghost prefab or spawn point assigned — nothing spawned.", this);
            StartDespawnTimer();
            return;
        }

        currentGhostInstance = Instantiate(ghostPrefab, spawnPoint.position, spawnPoint.rotation);
        currentGhostInstance.transform.localScale = ghostPrefab.transform.localScale; // Nonnapat: keep the prefab's size

        // The line that makes the ghost real for everyone rather than for one
        // machine. Offline there is no server, so the local Instantiate above
        // is already the whole story.
        if (NetworkServer.active) NetworkServer.Spawn(currentGhostInstance);

        // Arrival flickers are too short to count as a sustained blackout.
        // Relocate seasoning on the authority when the ghost actually appears.
        GhostFavorRecovery.Instance?.ServerOnGhostAppeared();

        StartDespawnTimer();
    }

    /// <summary>
    /// 22 Oct (Mr.k): ghostDuration starts AFTER the appear animation ends, so the
    /// ghost gets its full time to hunt (before, the Entry animation used part of it).
    /// </summary>
    private void StartDespawnTimer()
    {
        StopDespawnTimer();
        despawnRoutine = StartCoroutine(DespawnAfterAppear());
    }

    private void StopDespawnTimer()
    {
        if (despawnRoutine != null) StopCoroutine(despawnRoutine);
        despawnRoutine = null;
    }

    private IEnumerator DespawnAfterAppear()
    {
        yield return null; // let the new ghost run its Start first
        TimedGhost ghost = currentGhostInstance != null ? currentGhostInstance.GetComponentInChildren<TimedGhost>() : null;
        float waited = 0f;
        while (ghost != null && ghost.IsAppearing && waited < MaxAppearWait)
        {
            waited += Time.deltaTime;
            yield return null;
        }
        yield return new WaitForSeconds(ghostDuration);
        despawnRoutine = null;
        DespawnGhost();
    }

    private void DespawnGhost()
    {
        isGhostActive = false;
        isSequenceRunning = false;

        if (currentGhostInstance != null)
        {
            GhostManifestation manifestation = currentGhostInstance.GetComponent<GhostManifestation>();
            if (manifestation != null) manifestation.Despawn();
            else if (NetworkServer.active) NetworkServer.Destroy(currentGhostInstance);
            else Destroy(currentGhostInstance);
        }

    }

    /// <summary>SERVER ONLY. Stops the despawn timer so a jumpscare is not cut short.</summary>
    public void CancelDespawnTimer()
    {
        if (!HasAuthority) return;
        CancelInvoke(nameof(DespawnGhost));
        StopDespawnTimer();
    }

    /// <summary>SERVER ONLY. Clears state after a jumpscare so the next cycle can start.</summary>
    public void ResetManagerAfterJumpscare()
    {
        if (!HasAuthority) return;

        isGhostActive = false;
        isSequenceRunning = false;
        currentGhostInstance = null;

    }


    // ---- Dev cheats (F1, 14 Oct). Server only; the panel exists only in Editor / Development Builds. ----

    public string DevGhostName => "Timed ghost";
    public string DevTriggerText => "Timer reached (lights flicker, 10 s countdown)";

    /// <summary>SERVER. The ghost appears at its spawn point now: no flicker, no countdown.</summary>
    public void ServerDevSummonNow(GameObject requester)
    {
        if (!HasAuthority || isGhostActive) return;
        StopAllCoroutines();
        if (flickerRunning && BuildingLightManager.Instance != null) BuildingLightManager.Instance.SetLights(true);
        flickerRunning = false;
        isSequenceRunning = true; // blocks Update until DespawnGhost resets it
        timer = 0f;
        SpawnGhost();
    }

    /// <summary>SERVER. The spawn timer is reached now: normal arrival (flicker + countdown + spawn).</summary>
    public void ServerDevFireTrigger(GameObject requester)
    {
        if (!HasAuthority || isGhostActive || isSequenceRunning) return;
        timer = spawnInterval; // next Update starts GhostArrivalSequence
    }
}
