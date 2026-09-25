using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Reads a rebindable button from a saved control path (GameSettings), e.g.
/// "&lt;Mouse&gt;/leftButton" or "/Keyboard/v". Works for keyboard keys and all
/// mouse buttons (left, right, middle, forward, back = Mouse0..Mouse4).
///
/// Caches the resolved control and re-resolves only when the path changes, so
/// calling it every frame costs nothing.
/// </summary>
public sealed class BoundButton
{
    private string path;
    private ButtonControl control;

    private ButtonControl Resolve(string currentPath)
    {
        if (currentPath != path || (control != null && control.device != null && !control.device.added))
        {
            path = currentPath;
            control = string.IsNullOrEmpty(path) ? null : InputSystem.FindControl(path) as ButtonControl;
        }
        return control;
    }

    public bool IsPressed(string currentPath)
    {
        ButtonControl c = Resolve(currentPath);
        return c != null && c.isPressed;
    }

    public bool WasPressedThisFrame(string currentPath)
    {
        ButtonControl c = Resolve(currentPath);
        return c != null && c.wasPressedThisFrame;
    }

    /// <summary>"Left Button [Mouse]", "V [Keyboard]" — for the Settings screen.</summary>
    public static string DisplayName(string controlPath)
    {
        if (string.IsNullOrEmpty(controlPath)) return "None";
        return InputControlPath.ToHumanReadableString(controlPath);
    }
}
