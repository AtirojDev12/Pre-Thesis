using UnityEngine;

/// <summary>
/// Small screens that open over the 3D view and need the mouse: the lobby
/// panel (Tab) and the shop. While one is open the Esc menu must not open,
/// and Esc closes the panel instead. Each panel calls Opened / Closed.
/// </summary>
public static class OverlayPanels
{
    private static int openCount;
    private static int lastClosedFrame = -1;

    public static bool AnyOpen => openCount > 0;

    /// <summary>True while a panel is open, and in the frame one closed (that Esc was the panel's).</summary>
    public static bool BlocksEscape => openCount > 0 || lastClosedFrame == Time.frameCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        openCount = 0;
        lastClosedFrame = -1;
    }

    public static void Opened() => openCount++;

    public static void Closed()
    {
        openCount = Mathf.Max(0, openCount - 1);
        lastClosedFrame = Time.frameCount;
    }

    /// <summary>Gives the mouse to the panel and freezes the player (or gives it back).</summary>
    public static void SetMouseForUi(bool ui)
    {
        GameplayInput.Blocked = ui;
        Cursor.lockState = ui ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = ui;
    }
}
