using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Player-owned L key input, routed to independent building power on the server.</summary>
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
        if (BuildingLightManager.Instance == null)
        {
            Debug.LogWarning("[LightController] No BuildingLightManager in this scene — nothing owns the lights.", this);
            return;
        }

        if (NetworkMode.IsOffline)
        {
            BuildingLightManager.Instance.SetLights(!BuildingLightManager.LightsOn);
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
        if (BuildingLightManager.Instance == null) return;
        BuildingLightManager.Instance.SetLights(!BuildingLightManager.LightsOn);
    }

    /// <summary>
    /// SERVER ONLY. For rules and scripted events — "turn off the lights and go
    /// to the Staff room" is a Ticket Zone rule that needs this.
    /// </summary>
    public static void ServerSetLights(bool on)
    {
        if (BuildingLightManager.Instance == null) return;
        BuildingLightManager.Instance.SetLights(on);
    }
}
