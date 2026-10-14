using Mirror;
using UnityEngine;

public class TicketGhostSpawner : NetworkBehaviour, IDevGhostSource
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

    // ---- Dev cheats (F1, 14 Oct). Server only. ----

    public string DevGhostName => "Ticket punisher";
    public string DevTriggerText => "1 wrong ticket sale (3 = attack)";

    /// <summary>SERVER. The ticket ghost attacks the player who asked, now.</summary>
    public void ServerDevSummonNow(GameObject requester)
    {
        if (!HasAuthority) return;
        if (currentGhost == null) { CancelInvoke(nameof(SpawnNewGhost)); SpawnNewGhost(); }
        PlayerHealth victim = requester != null ? requester.GetComponent<PlayerHealth>() : null;
        if (currentGhost == null || victim == null) return;
        // A dev summon is aimed at you on purpose, so it works even in god mode
        // (you still take no damage if infinite health is on).
        bool untargetable = victim.DevUntargetable;
        victim.DevUntargetable = false;
        currentGhost.TriggerJumpscare(victim);
        victim.DevUntargetable = untargetable;
    }

    /// <summary>SERVER. Counts as ONE wrong ticket sale by the player who asked.</summary>
    public void ServerDevFireTrigger(GameObject requester)
    {
        if (!HasAuthority || currentGhost == null) return;
        PlayerHealth seller = requester != null ? requester.GetComponent<PlayerHealth>() : null;
        if (seller == null) return;
        bool untargetable = seller.DevUntargetable; // same as above: works in god mode
        seller.DevUntargetable = false;
        currentGhost.RecordWrongSale(seller);
        seller.DevUntargetable = untargetable;
    }
}
