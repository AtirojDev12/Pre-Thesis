# Interaction System

A small, extensible framework so players see what they can interact with, and so
you can keep adding new interactable types without touching the core
detection/UI code.

**This system is server-authoritative.** It has to be — this is a 1–6 player
co-op game, so a door someone opens must be open for the whole party, and a
one-time pickup must be gone for everyone. Read "How the networking works"
below before adding a new interactable.

## Files

- **IInteractable.cs** — the core interface. Everything else depends only on this. Its doc comments state which methods run on the client and which run on the server.
- **InteractableBase.cs** — optional base `NetworkBehaviour` with prompt text, highlight, and replicated one-time-use support built in.
- **PlayerInteractor.cs** — put on the player. Raycasts locally for the prompt, then routes the actual interaction to the server as a `[Command]`.
- **InteractionPromptUI.cs** — world-space "Press E..." text that follows the current target and faces the camera. Purely local.
- **ExampleDoor.cs / Lever.cs** — two worked examples of the correct networked pattern.

## How the networking works

| What | Where it runs | Why |
|---|---|---|
| Raycast, highlight, prompt text | Local client only | Instant, never waits on the network. Each player highlights what *they* are looking at. |
| Key press → `CmdInteract` | Client → Server | The client only says *which* object it wants. |
| Range + `CanInteract()` re-check | Server | A modified client must not be able to interact from across the map. |
| `Interact()` / `OnInteracted()` | Server only | The single place state may change. |
| Resulting state (door open, lever pulled) | `[SyncVar]` → all clients | So everyone sees the same world. |

Two Mirror details that will bite you if you forget them:

1. **A SyncVar hook does not fire on the machine that made the change.** The
   server changes the value, so the server must apply its own visual result
   directly. Both examples do this — see the `ApplyOpenState(isOpen)` call
   inside `OnInteracted`.
2. **Late joiners need `OnStartClient`.** A player joining mid-match receives the
   current SyncVar values at spawn but no hook call, so re-apply the visual
   state there or doors will look shut when they aren't.

## Setup

1. Add `PlayerInteractor.cs` to your player GameObject (the one with the
   `NetworkIdentity`).
2. Create a **World Space Canvas** with a `TextMeshProUGUI` child for the prompt
   and add `InteractionPromptUI.cs` to it. You can leave `PlayerInteractor`'s
   `Prompt UI` slot empty — it finds one on the player prefab first, then falls
   back to one in the scene.
3. On any object you want interactable, add a script inheriting
   `InteractableBase`. That gives it a `NetworkIdentity` automatically, which it
   **must** have for the interaction to replicate.
4. Give it a `Collider` (a trigger is fine), and put it on a dedicated
   **Interactable layer**, then set that layer in `PlayerInteractor`'s
   `Interactable Layers`. Leaving the mask as *Everything* lets the ray hit the
   player's own collider and silently block interaction.

## Adding new interactable types later

Copy this shape. The three things that make it multiplayer-correct are marked:

```csharp
public class PressurePlate : InteractableBase
{
    [SerializeField] private string prompt = "Press E to step on plate";

    // 1. shared state lives in a SyncVar
    [SyncVar(hook = nameof(OnPressedChanged))]
    private bool isPressed;

    public override string GetInteractionPrompt() => prompt;

    // 2. state changes on the SERVER (InteractableBase guarantees this)
    protected override void OnInteracted(GameObject interactor)
    {
        isPressed = true;
        ApplyPressed(isPressed);   // hook won't fire on the change side
    }

    private void OnPressedChanged(bool oldValue, bool newValue) => ApplyPressed(newValue);

    private void ApplyPressed(bool pressed) { /* animation, sound, open a gate... */ }

    // 3. late joiners get the current state
    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyPressed(isPressed);
    }
}
```

You never need to modify `PlayerInteractor` or `InteractionPromptUI` — they only
know about the `IInteractable` interface.

### Testing without a NetworkManager

These scripts detect when no Mirror server *and* no Mirror client is running
(see `_Script/Networking/NetworkMode.cs`) and fall back to running the
authoritative path directly. That keeps scratch scenes like `TestWalk` usable
for solo prototyping. It is a convenience for testing only — the moment a real
match is running, every check falls back to Mirror's normal `isServer` /
`isLocalPlayer` rules.

Prototyping solo is fine, but test with two clients before calling a feature
done: almost every multiplayer bug in this system class is invisible with one
player.

### Common extensions you might want next
- **Held-interaction (progress bar):** track key-held duration in `PlayerInteractor` and only send the `[Command]` once the threshold is reached. Keep the progress bar local.
- **Icons instead of/alongside text:** add `GetInteractionIcon()` to `IInteractable` and an `Image` to `InteractionPromptUI`.
- **Outline shader highlight:** swap `SetHighlighted`'s property-block tint for an outline material pass. Keep using a `MaterialPropertyBlock` rather than `renderer.material`, which instantiates a copy of the material per object.
- **Role-gated interactables:** override `CanInteract()` to check the player's assigned map Role — and remember the server re-checks it, so the gate is real, not cosmetic.
