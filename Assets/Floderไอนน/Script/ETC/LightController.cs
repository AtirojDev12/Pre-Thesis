using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ปุ่ม L สำหรับเปิด-ปิดไฟ
///
/// PUT THIS ON THE PLAYER PREFAB, NOT IN THE SCENE.
///
/// Why it moved: this reads the keyboard, and in a match every machine has a
/// keyboard. A scene copy runs Update on all six clients, so one person
/// pressing L used to flip the lights on their screen only — and because
/// TimedGhost hunts by movement in the dark and by sight in the light, the
/// ghost would then be running a different mode on every machine.
///
/// Now: only the local player reads input, the press travels to the server as a
/// Command, and GhostManager flips its replicated light state for everybody.
///
/// The lights themselves are configured ONCE on GhostManager. This component no
/// longer owns a light array — two lists of lights is two answers to the same
/// question, and they drift.
/// </summary>
public class LightController : NetworkBehaviour
{
    [Header("Input")]
    [Tooltip("Key that toggles the lights. Only the local player's press counts.")]
    [SerializeField] private Key toggleKey = Key.L;

    private void Update()
    {
        // Offline sandbox has no local-player concept, so let the keyboard work.
        // In a match, only the player that belongs to THIS machine may read it,
        // or one keypress would fire once per player copy on this client.
        if (!NetworkMode.IsOffline && !isLocalPlayer) return;

        if (Keyboard.current == null) return;
        if (!Keyboard.current[toggleKey].wasPressedThisFrame) return;

        RequestToggle();
    }

    private void RequestToggle()
    {
        if (GhostManager.Instance == null)
        {
            Debug.LogWarning("[LightController] No GhostManager in this scene — nothing owns the lights.", this);
            return;
        }

        if (NetworkMode.IsOffline)
        {
            GhostManager.Instance.PlayerToggleLights(!GhostManager.Instance.areLightsOnCurrently);
            return;
        }

        CmdToggleLights();
    }

    /// <summary>
    /// The switch press, sent to the server. The server decides — a client that
    /// could set the light state directly could also sit in permanent daylight
    /// and never be hunted in the dark.
    /// </summary>
    [Command]
    private void CmdToggleLights()
    {
        if (GhostManager.Instance == null) return;
        GhostManager.Instance.PlayerToggleLights(!GhostManager.Instance.areLightsOnCurrently);
    }

    /// <summary>
    /// SERVER ONLY. For rules and scripted events — "turn off the lights and go
    /// to the Staff room" is a Ticket Zone rule that needs this.
    /// </summary>
    public static void ServerSetLights(bool on)
    {
        if (GhostManager.Instance == null) return;
        GhostManager.Instance.PlayerToggleLights(on);
    }
}
