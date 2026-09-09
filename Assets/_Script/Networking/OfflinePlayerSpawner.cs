using Mirror;
using UnityEngine;

/// <summary>
/// Spawns the player prefab ONLY when nothing is hosting -- i.e. someone
/// pressed Play in a sandbox scene like TestWalk with no NetworkManager.
///
/// Why this exists: a real match spawns the player from
/// NetworkManager.playerPrefab when a client connects, so the player must NOT
/// be sitting in the scene already. Leaving one there causes two problems:
///   - Mirror complains the scene object "has no valid sceneId" until the scene
///     is re-saved, every time the prefab's NetworkIdentity changes.
///   - With autoCreatePlayer on, hosting spawns a SECOND player on top of the
///     one already in the scene.
/// But deleting it also breaks solo testing, because then pressing Play gives
/// you a world with nobody in it.
///
/// So: keep the scene free of players, and let this put one in only when there
/// is no server and no client running. In a real match it does nothing at all.
///
/// Setup: put this on an empty GameObject in the sandbox scene, assign the
/// Player prefab, and position the object where the player should appear.
/// </summary>
public class OfflinePlayerSpawner : MonoBehaviour
{
    [Tooltip("The player prefab to drop in when testing offline. Usually the same prefab assigned to NetworkManager's Player Prefab.")]
    [SerializeField] private GameObject playerPrefab;

    [Tooltip("Where to place the player. Leave empty to spawn at this object's own position.")]
    [SerializeField] private Transform spawnPoint;

    [Tooltip("Log a line when a player is spawned, so it is obvious this ran and not the NetworkManager.")]
    [SerializeField] private bool logWhenSpawning = true;

    private void Start()
    {
        if (!NetworkMode.IsOffline)
        {
            // A server or client is running -- the NetworkManager owns player
            // spawning. Touching it here would create a duplicate.
            return;
        }

        if (playerPrefab == null)
        {
            // Fall back to whatever the NetworkManager is configured with, if
            // one happens to be in this scene.
            if (NetworkManager.singleton != null) playerPrefab = NetworkManager.singleton.playerPrefab;

            if (playerPrefab == null)
            {
                Debug.LogWarning(
                    "[OfflinePlayerSpawner] No player prefab assigned, so no player was spawned. " +
                    "Assign one in the Inspector.", this);
                return;
            }
        }

        // Don't add a second player if the scene already has one.
        if (FindFirstObjectByType<PlayerHealth>(FindObjectsInactive.Include) != null)
        {
            if (logWhenSpawning)
                Debug.Log("[OfflinePlayerSpawner] A player already exists in this scene, so none was spawned.", this);
            return;
        }

        Transform point = spawnPoint != null ? spawnPoint : transform;
        GameObject player = Instantiate(playerPrefab, point.position, point.rotation);
        player.name = playerPrefab.name + " (offline test)";

        if (logWhenSpawning)
            Debug.Log($"[OfflinePlayerSpawner] Offline test player spawned at {point.position}.", player);
    }

    private void OnDrawGizmos()
    {
        Transform point = spawnPoint != null ? spawnPoint : transform;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(point.position + Vector3.up, 0.4f);
        Gizmos.DrawLine(point.position, point.position + point.forward);
    }
}
