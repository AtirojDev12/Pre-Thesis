# Popcorn and drinks — Cinema_GamePlay

## Player loop

1. Check the **Cashier UI Anchor** display for the customer type and order.
2. Aim at **BucketSpwan** and press **E** for an empty popcorn bucket, or **PaperBottleSpwan** for an empty cup.
3. Hold **E** while aiming at **Cheese**, **BBQ**, or **Papica** (Paprika) to scoop popcorn, or either water dispenser nozzle (**Cube (4)** / **Cube (10)**) to fill water. Both take **3 seconds**. Releasing E, looking away, walking out of range, or an obstruction cancels preparation and resets progress.
4. For a **ghost customer**, press **E** at **GhostFavor** after filling. This adds ghost seasoning without changing the base popcorn flavor or water order. Human orders require no ghost seasoning.
5. Aim at the waiting customer and press **E** to serve.

**R** discards the held container/item so the player can recover from choosing the wrong one. One item can be held at a time. Empty containers cannot be served, mixed, or silently replaced. Filled items cannot be refilled. Held props attach to the local player's hand view and cannot block interaction rays.

## Blackout recovery

`GhostFavorRecovery` observes `GhostManager.areLightsOnCurrently` without modifying the ghost system. Darkness lasting at least 0.5 seconds teleports **GhostFavor** to the ticket booth's customer counter point; short warning flickers do not count. The bootstrap's optional **Ghost Favor Relocation Points** list supports future map anchors, chosen randomly by the server. Leaving it empty uses the ticket booth.

Any player can press **E** to pick up the displaced seasoning. Carry it within 1.8 metres of its original spot to restore its exact position and rotation automatically. **R** drops it for another player; death, being downed, or disconnecting also drops it from its last carried position. The server (or offline game) simulates gravity and floor collisions, and replicates the falling/resting pose to clients. Picking it up stops physics. Its colliders are disabled while carried. Existing containers remain held, but preparation is paused during recovery. Seasoning cannot be applied until the object is back home, and later blackouts leave an unfinished recovery alone.

`PopcornNetSync` replicates displacement, carrier and pose, including to late joiners. Pickup/drop/mix requests validate the requesting player and range on the server. Each flavor's hover prompt always includes its name, even with empty hands or an already-filled container.

## Results

Both human and ghost customers can order Cheese, BBQ, Paprika, or water. Correct orders still award one point; wrong human orders award nothing; wrong ghost orders still apply the configured damage (10 by default). A submitted filled item is consumed and the customer leaves after either outcome. The existing zone target, task rewards, and match win/loss flow are unchanged.

`PopcornRecipe` shares recipe matching between local play and `PopcornNetSync`. The server still resolves score and punishment. Container preparation remains per-client inventory, as in the original prototype; this change does not introduce server-validated inventory or remote held-prop replication.

## Scene setup

The **Popcorn Minigame Systems** component in `Cinema_GamePlay` explicitly references all eight station objects and the authored empty bucket, empty cup, water, and ghost-water prefabs. This avoids accidentally binding other objects named Cube (4). Station objects need enabled, non-trigger colliders. Existing scene names and materials are preserved.

The Cashier UI Anchor shows customer type, base item, and ghost requirement on separate lines. The Popcorn Maker UI Anchor now displays instructions. Instant Make buttons are retired. Preparation has a high-contrast progress bar, percentage, countdown, and cancellation feedback. Hovering a world interactable adds a white silhouette outline without changing its materials.

Other scenes using `PopcornMinigameBootstrap` must assign the new station and container fields before using this loop. The older `Z1_Gameplay` layout predates these authored stations; the updated playable layout and regression target are **Cinema_GamePlay**.

## Verification

Run `Tests/Popcorn/Run-PopcornChecks.ps1 -UnityPath '<Unity 6000.5.7f1 executable>'` in PowerShell. It copies the project into ignored `.utmp/popcorn-regression`, then checks the Cinema scene in an isolated editor. Tests cover station bindings/colliders, E pickup, correct container restrictions, three-second preparation, release/aim/occlusion cancellation, progress reset, ghost recipes, authored drink visuals, score/damage, repeat submissions, outlines, and local-player cleanup. Recovery checks cover flicker filtering, blackout relocation, unavailable seasoning, competing pickups, transfer to another player, exact return, real Mirror host pickup/drop commands, carrier despawn, and serialization of a carried snapshot. A separate remote-client playtest remains a manual check.

For a final manual playtest, walk through every station in the authored layout at the target display resolution, then test a host and a remote client serving the same queue. Adjust the scene UI anchors and held prop offsets in the Inspector if needed for the preferred camera framing.
