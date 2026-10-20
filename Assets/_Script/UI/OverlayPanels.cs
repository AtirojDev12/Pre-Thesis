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
        if (!ui) CursorRelock.Schedule();
    }

    /// <summary>
    /// 20 Oct (Mr.k) bug: closing a panel with Esc left the mouse cursor on screen.
    /// The Unity Editor unlocks the cursor on every Esc press, AFTER our lock in that
    /// same frame. So lock again for a few frames, if nothing else wants the mouse.
    /// </summary>
    private sealed class CursorRelock : MonoBehaviour
    {
        private static CursorRelock instance;
        private int framesLeft;

        public static void Schedule()
        {
            if (instance == null)
            {
                var go = new GameObject("Cursor Relock") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                instance = go.AddComponent<CursorRelock>();
            }
            instance.framesLeft = 5;
        }

        private void Update()
        {
            if (framesLeft <= 0) return;
            framesLeft--;
            bool somethingWantsMouse = GameplayInput.Blocked || AnyOpen || PauseMenuController.IsOpen || MatchResultsUI.IsShowing;
            bool walking = PlayerHealth.LocalInstance != null || SpectatorSession.Active;
            if (somethingWantsMouse || !walking) { framesLeft = 0; return; }
            if (Cursor.lockState != CursorLockMode.Locked) Cursor.lockState = CursorLockMode.Locked;
            if (Cursor.visible) Cursor.visible = false;
        }
    }
}
