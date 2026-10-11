using System.Collections;
using Mirror;
using UnityEngine;

/// <summary>Owns only ghost spawn timing. Building power is independent.</summary>
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

    [Header("Timing Settings")]
    [SerializeField] private float spawnInterval = 60f;
    [UnityEngine.Serialization.FormerlySerializedAs("flickerDuration")]
    [SerializeField] private float arrivalLeadTime = 3f;
    [SerializeField] private float countdownDuration = 10f;
    [SerializeField] private float ghostDuration = 15f;

    private float timer = 0f;
    private bool isGhostActive = false;
    private bool isSequenceRunning = false;

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

        // Preserve the old arrival timing without changing building power.
        yield return new WaitForSeconds(1.1f);

        yield return new WaitForSeconds(countdownDuration);

        SpawnGhost();
    }

    private void SpawnGhost()
    {
        isGhostActive = true;

        if (ghostPrefab == null || spawnPoint == null)
        {
            Debug.LogWarning("[GhostManager] No ghost prefab or spawn point assigned — nothing spawned.", this);
            Invoke(nameof(DespawnGhost), ghostDuration);
            return;
        }

        currentGhostInstance = Instantiate(ghostPrefab, spawnPoint.position, spawnPoint.rotation);
        currentGhostInstance.transform.localScale = ghostPrefab.transform.localScale; // Nonnapat: keep the prefab's size

        // The line that makes the ghost real for everyone rather than for one
        // machine. Offline there is no server, so the local Instantiate above
        // is already the whole story.
        if (NetworkServer.active) NetworkServer.Spawn(currentGhostInstance);

        Invoke(nameof(DespawnGhost), ghostDuration);
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

    }

}
