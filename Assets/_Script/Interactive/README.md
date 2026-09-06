# Interaction System

A small, extensible framework so players see what they can interact with,
and so you can keep adding new interactable types without touching the
core detection/UI code.

## Files

- **IInteractable.cs** — the core interface. Everything else depends only on this.
- **InteractableBase.cs** — optional base MonoBehaviour with prompt text, highlight color, and one-time-use support built in.
- **PlayerInteractor.cs** — put on the player. Finds the closest valid interactable in range (and optionally in front of the player), highlights it, shows the prompt, and calls `Interact()` on key press.
- **InteractionPromptUI.cs** — world-space "Press E..." text that follows the current target and faces the camera.
- **ExampleDoor.cs** — a minimal example of a real interactable, showing the extension pattern.

## Setup

1. Add `PlayerInteractor.cs` to your player GameObject.
2. Create a **World Space Canvas** with a `TextMeshProUGUI` child for the prompt. Add `InteractionPromptUI.cs` to the canvas, assign the text field, and drag the canvas into `PlayerInteractor`'s `Prompt UI` slot.
3. On any object you want interactable, add a script that inherits `InteractableBase` (see `ExampleDoor.cs`), or implement `IInteractable` directly if you need full custom behaviour (e.g. it's not a MonoBehaviour, or you don't want highlight/one-time-use logic).
4. Make sure the object has a `Collider` (can be a trigger) so `PlayerInteractor`'s overlap check finds it, and set its layer to match `Interactable Layers` on the `PlayerInteractor`.

## Adding new interactable types later

This is the main thing you asked for — here's the pattern to keep using:

```csharp
public class Lever : InteractableBase
{
    protected override void Awake()
    {
        base.Awake();
        interactionPrompt = "Press E to pull lever";
    }

    protected override void OnInteracted(GameObject interactor)
    {
        // your logic here
    }
}
```

You never need to modify `PlayerInteractor` or `InteractionPromptUI` — they
only know about the `IInteractable` interface, so any new class that
implements it (directly or via `InteractableBase`) is automatically
detected, highlighted, and prompted.

### Common extensions you might want next
- **Held-interaction (progress bar):** track `Input.GetKey` duration in `PlayerInteractor` instead of `GetKeyDown`, and call `Interact()` only once a threshold is reached. Expose progress via an event for a UI radial bar.
- **Icons instead of/alongside text:** add a `Sprite` field to `IInteractable` (e.g. `GetInteractionIcon()`) and an `Image` component in `InteractionPromptUI`.
- **Outline shader highlight:** swap `SetHighlighted`'s color-swap for enabling an outline material/shader pass.
- **Multiple simultaneous interactables (UI list):** instead of tracking one `_currentTarget`, collect all valid ones each frame and feed a list to the UI.
- **Inventory/quest gating:** override `CanInteract()` in a derived class to check player state.
