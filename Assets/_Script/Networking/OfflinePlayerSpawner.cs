using System.Collections;
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

    [Header("Offline scene objects")]
    [Tooltip("Re-enable scene objects that Mirror force-disabled. Leave on for sandbox scenes; it does nothing once anything is hosting.")]
    [SerializeField] private bool activateSceneNetworkObjects = true;

    [Tooltip("How many frames to keep watching for Mirror switching scene objects off. It does this while the scene loads, so a handful of frames is plenty.")]
    [SerializeField] private int framesToWatchForDisable = 10;

    private void Start()
    {
        if (!NetworkMode.IsOffline)
        {
            // A server or client is running -- the NetworkManager owns player
            // spawning and scene object spawning. Touching either here would
            // create duplicates and fight Mirror.
            return;
        }

        if (activateSceneNetworkObjects) StartCoroutine(KeepSceneObjectsEnabled());

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

        // Don't add a second player if the scene already has one. FindAny rather
        // than FindFirst: the "First" variant is deprecated in Unity 6.5 for
        // depending on instance-ID ordering, and this is only a null check.
        if (FindAnyObjectByType<PlayerHealth>(FindObjectsInactive.Include) != null)
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

    /// <summary>
    /// Mirror's NetworkScenePostProcess force-disables EVERY scene object that
    /// carries a NetworkIdentity, so Start/Update don't run until the server
    /// deliberately spawns them. They are normally switched back on by
    /// NetworkServer.SpawnObjects() -- which never happens in a scene with no
    /// NetworkManager, so levers and doors simply vanish on Play.
    ///
    /// This watches for a few frames rather than doing one pass, because that
    /// disable does NOT land before Awake: an Awake-time pass finds everything
    /// still active and skips it. Watching removes the need to guess Unity's
    /// exact ordering, and it stops on its own once the window closes.
    /// </summary>
    private IEnumerator KeepSceneObjectsEnabled()
    {
        int totalReEnabled = 0;
        int lastSeen = 0;

        for (int frame = 0; frame < Mathf.Max(1, framesToWatchForDisable); frame++)
        {
            totalReEnabled += ReEnableDisabledSceneIdentities(out lastSeen);
            yield return null;
        }

        if (logWhenSpawning)
            Debug.Log($"[OfflinePlayerSpawner] Offline mode: {lastSeen} scene NetworkIdentity object(s) present, re-enabled {totalReEnabled}.", this);
    }

    /// <summary>
    /// Switches on any scene object Mirror disabled. Returns how many were
    /// changed this pass; <paramref name="seen"/> reports how many scene
    /// NetworkIdentities exist at all, which distinguishes "nothing to do" from
    /// "found nothing to look at".
    /// </summary>
    private int ReEnableDisabledSceneIdentities(out int seen)
    {
        seen = 0;
        int reEnabled = 0;

        // Resources.FindObjectsOfTypeAll, not FindObjectsByType: this is the
        // dependable way to reach components sitting on INACTIVE GameObjects,
        // which is exactly what we are hunting for. It also returns prefab
        // assets and editor-internal objects, so both are filtered out.
        foreach (NetworkIdentity identity in Resources.FindObjectsOfTypeAll<NetworkIdentity>())
        {
            GameObject go = identity.gameObject;

            if (!go.scene.IsValid()) continue;             // prefab asset, not in the scene
            if (go.hideFlags != HideFlags.None) continue;  // editor-internal object

            seen++;
            if (go.activeSelf) continue;

            go.SetActive(true);
            reEnabled++;
        }

        return reEnabled;
    }

    private void OnDrawGizmos()
    {
        Transform point = spawnPoint != null ? spawnPoint : transform;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(point.position + Vector3.up, 0.4f);
        Gizmos.DrawLine(point.position, point.position + point.forward);
    }
}
