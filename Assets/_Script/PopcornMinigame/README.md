# Popcorn Minigame (Z1_Gameplay)

## Playtest controls

- Move/look with the existing player controls.
- Aim at a flavor, the Make button, or the waiting customer and press **E**.
- With the cursor locked, left-click activates the machine button under the crosshair.
- Alternatively, press **Tab** to release the mouse and click the world-space machine buttons. Press **Tab** again to resume FPS look.
- Both mouse and E interactions require a clear path within the player's interaction range.
- The selected flavor keeps a gold outline, including while hovering **Make** and after making popcorn. Selecting another flavor moves the outline.
- Aim at a waiting human or ghost within the player's interaction range (3m by default). **[E] Submit order** appears above that customer; it disappears when looking away, moving out of range, or after submission.

## Current rules

- Human customers request either Cheese or BBQ.
- Ghost customers request Ghost Flavor.
- Select a flavor, then press **Make** to spawn the popcorn prefab in the player's left-hand view and update the HUD. Selecting a flavor alone does not make an item.
- Making another portion replaces the held item. Submitting consumes both its inventory entry and visible prefab.
- A correct exact match awards one point.
- A wrong human order awards no point and deals no damage.
- A wrong ghost order deals 10 damage.
- Every submitted item is consumed and the customer leaves after either result.

The `CounterSlot` owns occupancy separately from `PopcornCustomer`, so more slots or a queue can be added later. `PopcornFlavor` is currently a single enum value; it can be replaced by a set/recipe when flavor mixing is introduced.

## Adjusting placement in Z1_Gameplay

Expand **Popcorn Minigame Systems** in the Hierarchy and move/rotate these child objects with the normal Unity transform tools:

- **Player Spawn Point** — offline player's starting position and facing.
- **Cashier UI Anchor** — order/score display position and facing.
- **Popcorn Maker UI Anchor** — flavor/Make controls position and facing.
- **Customer Spawn Point** — beginning of the customer route.
- **Customer Wait Point** — occupied position at the counter.
- **Customer Exit Point** — destination after serving.

Colored scene gizmos preview both UI rectangles and the customer route. These are placement markers only and are not visible during gameplay.

The **Held Popcorn Prefab**, **Held Popcorn Position**, and **Held Popcorn Rotation** fields on **Popcorn Minigame Systems** control the camera-mounted bucket. `Assets/Prefab/HeldPopcorn.prefab` provides the default model; it can be replaced with an art prefab. Its colliders are disabled while held so it cannot block customer interaction. A failed creation preserves the previous held item and selection.

## Regression playtest

1. Press Make without selecting a flavor: no item should appear.
2. Select each flavor using E and mouse clicks. Look away and hover Make; the selected flavor should retain its gold outline.
3. Make each flavor: exactly one bucket should appear at the lower left and the HUD should show the same flavor. Make again to check replacement.
4. Aim at a waiting human and ghost. Check the prompt hides outside 3m, behind an obstacle, and when looking away.
5. Submit empty-handed: the current order should remain active. Submit a matching flavor: the bucket disappears, score increases once, the customer leaves, and the next order arrives.
6. Submit the wrong flavor to a human (no score or damage) and a ghost (no score, 10 damage). Both consume the item and advance the customer queue.

This scene remains a local/offline prototype; its customers, selection, and inventory are not network replicated.

## Automated scene checks

Run `Tests/Popcorn/Run-PopcornChecks.ps1 -UnityPath '<path to Unity.exe>'` from PowerShell with the project's Unity version installed. The runner copies the project into ignored `.utmp/popcorn-regression`, opens the actual Z1 scene there, and drives virtual mouse/keyboard device state through the UI input module and player interactor. It checks raycast targeting, locked/unlocked clicks, held items, selection, range/occlusion, customer prompts, scoring, and the next customer cycle. It freezes locomotion and places the player at standing height for deterministic targeting. Results and the Unity log remain in that isolated directory.
