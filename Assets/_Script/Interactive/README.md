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
- **InteractionOutline.cs / InteractionEdgeGlowFeature.cs** — local hover selection and depth-tested edge lighting. The PC and mobile renderer assets include the feature; it preserves original prop materials and highlights silhouettes plus hard normal creases. See `Tests/InteractionHighlight/README.md` for tuning, limitations, and render checks.
- **ExampleDoor.cs / Lever.cs** — two worked examples of the correct networked pattern.

## Focus logging and interactable inventory (19 Oct 2026)

### Add a custom name to any scene object

Select the object in the Hierarchy, choose **Add Component > Interaction Name Label**,
and edit **Display Name**. The name appears at the fixed HUD position when aiming
at that object's enabled, non-trigger Collider or a child Collider. Add a Box Collider
if the object has none. The collider must be on a layer included in the player's
interaction mask, within interaction range and unobstructed.

Put labels on separate child objects to give parts different names. A label overrides
the scene's popcorn name for that object without changing its existing E action.
Standalone props need no interaction script or NetworkIdentity just to show a name.
An empty name hides the label; disabling/removing the component restores normal naming.
Make permanent edits outside Play mode and save the scene/prefab.

Popcorn stations, placed cups and customers show names in **GAME HUD > Focus Name**.
Open `Assets/Prefab/GAME HUD.prefab` to edit this TMP label's RectTransform, font,
font size and color directly. `CanvasInteract > InteractionPromptUI > Focus Name Text`
is already wired to the label. Runtime code changes only its text and visibility;
the prefab's styling takes priority over the popcorn scene's fallback layout settings.
**GAME HUD > CrossHair > Circle** is a UI Image using the authored circle sprite.
Its RectTransform controls the crosshair size (11 x 11 by default).

Name display applies independently of the global
**Show Names Only** toggle, which defaults to off so other zones retain action prompts.
Scenes without an authored Focus Name reference retain the generated-canvas fallback.

The optional **Show Names Only** toggle extends name-only text to other targets
through the existing prompt UI. Stations, cups,
customers, chores, rule props, ticket counters, switches, warning TVs and inventory
items have readable names; world buttons use their visible label. Other interactables
fall back to their object name with underscores and `(Clone)` removed.
Names also appear for unavailable targets whose colliders are enabled. E input and
edge highlighting still require availability. The popcorn customer's separate hover
submit prompt is suppressed in this mode; order displays and hold progress remain.
Outside the popcorn targets, disable **Show Names Only** to use action prompts.

`PlayerInteractor` has a **Log Focused Interactable** toggle, disabled by default.
In Play mode, filter the Unity Console by `[Interaction Focus]`. Each entry includes
the hierarchy path, component type, current interaction prompt and `CanInteract`
result. Clicking the entry selects the target transform. Entries are emitted only
when the target, prompt or availability changes; looking away emits one clear entry.
Only the local player's interactor logs. Logging also works without a prompt UI.

The focus log can identify an unavailable interactable if its collider is still
enabled. Disabled/inactive colliders, triggers, objects outside the interaction
layers/range, and objects behind the closest blocking collider remain undetectable.
The existing highlight and E input still require `CanInteract()`. Action prompts
also require availability when **Show Names Only** is disabled.
Logging is Console diagnostics; names use the existing on-screen prompt UI.

Project-wide concrete interaction types (some are runtime-created or examples,
so this list does not imply every type is placed in the Cinema scene):

| Component | Targets / actions |
| --- | --- |
| `PopcornStation` | Empty buckets/cups, popcorn tank scooping/refilling, maker, BBQ/Cheese/Paprika seasoning, drink controls, ghost seasoning pickup/return/mixing |
| `PlacedBottleInteractable` | Prepared cups placed at drink stations |
| `PopcornCustomer` | Submit an order to the waiting customer; uses its own UI, so its shared prompt is empty |
| `WorldButtonInteractable` | World-space UI controls, including movie selection and flavor controls |
| `TicketSellButton` | Sell human or ghost tickets |
| `ZoneChoreSpot` | Change film, clean screen rooms, clean toilets |
| `RuleInteractable` | Toilet light, knock on a stall, shadow seat, pick up/throw a black box |
| `LightSwitchInteractable` | Building or zone light switches |
| `WarningTV` | Play/stop warning media |
| `WorldInventoryItem` | Pick up dropped inventory items; item name comes from the catalog |
| `ShopTerminal` / `StorageTerminal` | Open the lobby shop/storage |
| `LobbyBoardRow` / `LobbyBoardButton` | Host kick action and Ready/Start action |
| `ExampleDoor` / `Lever` | Example open/close door and pull/reset lever |

Revival is handled separately in `PlayerInteractor.Revival.cs`, rather than through
`IInteractable`. Task boards and walk-in exit areas also use separate systems and
are not reported by this focus logger unless they gain an interactable component.

Manual verification: look at a station, hold the aim steady (no duplicate entries),
change its state (updated prompt), switch to another object with the same prompt
(new target entry), then look away (one clear entry). Check an unavailable target
whose collider remains enabled, an occluding wall, and a host/remote-client session
to confirm each player logs only their own focus. Toggle logging off in the Inspector.

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
- **Custom hover feedback:** implement `IInteractionHighlight` for prompts or UI. Mark legacy whole-surface tint implementations with `IInteractionSurfaceTint` so `PlayerInteractor` uses edge lighting instead of tinting the prop.
- **Role-gated interactables:** override `CanInteract()` to check the player's assigned map Role — and remember the server re-checks it, so the gate is real, not cosmetic.
