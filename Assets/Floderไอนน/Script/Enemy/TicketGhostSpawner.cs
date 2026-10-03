using Mirror;
using UnityEngine;

public class TicketGhostSpawner : NetworkBehaviour
{
    [Header("Ghost Setup")]
    [SerializeField] private GameObject ticketGhostPrefab;
    [SerializeField] private TicketMinigame minigame;
    [Header("Spawn Position")]
    [SerializeField] private Transform spawnPoint;
    [Header("Respawn Delay")]
    [SerializeField, Min(0)] private float respawnDelay = 2f;

    private TicketPunisherGhost currentGhost;
    private bool subscribed;
    private bool HasAuthority => NetworkMode.HasServerAuthority(this);

    public override void OnStartServer() => BeginSpawning();

    private void Start()
    {
        if (NetworkMode.IsOffline) BeginSpawning();
    }

    private void BeginSpawning()
    {
        if (!HasAuthority || subscribed) return;
        if (minigame == null)
        {
            Debug.LogError("[Ticket Ghost] Assign the ticket minigame in this scene.", this);
            return;
        }
        minigame.SaleResolved += OnSaleResolved;
        subscribed = true;
        SpawnNewGhost();
    }

    private void OnSaleResolved(PlayerHealth seller, bool correct)
    {
        if (HasAuthority && !correct && currentGhost != null) currentGhost.RecordWrongSale(seller);
    }

    public void SpawnNewGhost()
    {
        if (!HasAuthority || currentGhost != null || ticketGhostPrefab == null) return;
        Transform point = spawnPoint != null ? spawnPoint : transform;
        GameObject instance = Instantiate(ticketGhostPrefab, point.position, point.rotation);
        currentGhost = instance.GetComponent<TicketPunisherGhost>();
        if (currentGhost == null)
        {
            Debug.LogError("[Ticket Ghost] Prefab requires TicketPunisherGhost.", this);
            Destroy(instance);
            return;
        }
        currentGhost.Initialize(this);
        if (NetworkServer.active) NetworkServer.Spawn(instance);
    }

    public void NotifyGhostDestroyed(TicketPunisherGhost ghost)
    {
        if (!HasAuthority || currentGhost != ghost) return;
        currentGhost = null;
        Invoke(nameof(SpawnNewGhost), respawnDelay);
    }

    public override void OnStopServer() => StopSpawning();
    private void OnDestroy() => StopSpawning();

    private void StopSpawning()
    {
        CancelInvoke();
        if (subscribed && minigame != null) minigame.SaleResolved -= OnSaleResolved;
        subscribed = false;
    }
}
