# Popcorn and drinks — Cinema_GamePlay

## Player loop

### Focus names on the player HUD

To edit in the scene: open `Assets/Scenes/Map/Cinema_GamePlay.unity`, select
**Popcorn Minigame Systems** in the Hierarchy, and expand **Focus Names** under
**Focus names on player HUD** on `PopcornMinigameBootstrap`.
Flavor/station names are serialized on this scene component. Both machines
share the name for each drink. Negative offset Y moves the text below the centre.
Use a TMP font containing Thai glyphs when entering Thai names.
Edits preview immediately while aiming in Play mode; make permanent edits outside
Play mode and save the scene. An empty name hides that target's label.

For the shipped **GAME HUD**, edit the actual **Focus Name** TMP child in
`Assets/Prefab/GAME HUD.prefab` to change position, box size, font, size and color.
These prefab values are preserved at runtime. The scene component's layout/font
fields apply only to fallback HUDs without a wired Focus Name text reference.

Point the existing interaction ray at a popcorn-zone target within its normal
range to show its name at a fixed position 65 reference pixels below the crosshair.
The label belongs to the local HUD, changes when the focused target changes, and
hides when looking away, opening an input-blocking menu, losing the local player,
or leaving interaction range. It shows only a name, without E instructions or
recipe steps. Station interaction, serving, hold progress and order displays remain.

| Target | HUD name |
| --- | --- |
| Cheese / BBQ / Papica stations | Cheese Flavor / BBQ Flavor / Paprika Flavor |
| GhostFavor, including when displaced | Ghost Flavor |
| BucketSpwan / PaperBottleSpwan | Popcorn Bucket / Drink Cup |
| PopCornTank / Popcorn_Maker | Popcorn Tank / Popcorn Maker |
| Water / Fanta / orange juice / Pepsi controls on both machines | Water / Fanta / Orange Juice / Pepsi Dispenser |
| Placed cup | Drink Cup |
| Waiting popcorn customer | Customer |

Names use the configured station kind/flavor, rather than authored scene spelling.
The usual collider, occlusion and local-player checks still apply. A blocked action
such as full hands does not hide a station's name. Other zones retain their original
action prompts by default. This adds no navigation markers or quest tracking.

Drink visuals use `EmptyCup` before filling and the same `FullCup` for water,
Pepsi, Fanta and orange juice. Ghost seasoning replaces it with `FullCupGhost`. Cup materials keep
their authored colors; flavor and ghost seasoning remain separate order data.
`DrinkCupVisual` measures the cup body separately from the lid/straw, keeping it
22.4 cm tall in local/remote hands and on machine trays. The dispenser targets
the measured rim; splashes fit inside its opening and falling droplets stop at
cup height. Cup profiles store model-local base, rim and body measurements.

Popcorn uses `PopcornBucket_Empty`, then `NotastePopcorn` after scooping, then
`BBQPopcorn`, `CheesePopcorn` or `PapricaPopcorn` after seasoning. Ghost seasoning
uses `GhostFaverPopcorn` while preserving the ordered base flavor.
`PopcornBucketVisual` keeps the bucket body 24 cm tall, excluding the pile above
the rim. Local and observer visuals share the same prefab selection and sizing;
Mirror continues to replicate recipe state rather than spawning networked props.

1. Check the **Cashier UI Anchor** display for the customer type and order.
   The display stays on **WAITING FOR CUSTOMER...** while the customer walks in;
   the order appears only when that customer's local visual reaches the counter.
   Replicated order updates and partial deliveries preserve this arrival check.
2. Aim at **BucketSpwan** and press **E** for an empty popcorn bucket, or **PaperBottleSpwan** for an empty cup.
3. For popcorn, hold **E** at **PopCornTank** for **3 seconds** to receive plain **HeldPopcorn**, then press **E** at **Cheese**, **BBQ**, or **Papica** (Paprika) to add a flavor. An empty bucket cannot be flavored or served. For drinks, hold **E** at the matching dispenser control for **3 seconds**. Releasing E, looking away, opening a menu, walking out of range, or an obstruction cancels preparation and resets progress.
4. For a **ghost customer**, press **E** at **GhostFavor** after filling. This adds ghost seasoning without changing the base popcorn flavor or water order. Human orders require no ghost seasoning.
5. Aim at the waiting customer and press **E** to serve.

**R** discards the held container/item so the player can recover from choosing the wrong one. One item can be held at a time. Empty containers cannot be served, mixed, or silently replaced. Filled items cannot be refilled. Held props attach to the local player's hand view and cannot block interaction rays.

## Tank refill

The tank starts with **20 servings**. Each finished scoop consumes one serving. A world-space canvas attached directly to **PopCornTank** shows remaining servings and a supply bar, facing the local camera. Stock is not displayed on the player HUD. With empty hands, hold **E** at the authored **Popcorn_Maker** for **3 seconds** to receive the exact **NotastePopcorn** prefab. Carry it to **PopCornTank** and press **E** to add **10 servings**, capped at **20**. Topping up a partly full tank consumes the batch and discards overflow. A full tank rejects refilling and leaves the batch in hand for later. Empty tanks reject scooping; buckets, unflavored popcorn, and refill batches cannot be served. Capacity and servings per batch can be changed on the bootstrap in the Inspector.

## Blackout recovery

`GhostManager` notifies `GhostFavorRecovery` when the timed ghost actually spawns, including developer summons and arrivals while the lights are on. Warning flickers alone do not relocate the seasoning: each dark pulse is shorter than the 0.5-second blackout threshold. A separate sustained blackout of at least 0.5 seconds also triggers recovery. The bootstrap's optional **Ghost Favor Relocation Points** list supplies destinations chosen by the server. Leaving it empty uses the ticket booth's customer counter point.

Any player can press **E** to pick up the displaced seasoning. Carry it within 1.8 metres of its original spot to restore its exact position and rotation automatically. **R** drops it for another player; death, being downed, or disconnecting also drops it from its last carried position. The server (or offline game) simulates gravity and floor collisions, and replicates the falling/resting pose to clients. Picking it up stops physics. Its colliders are disabled while carried. Existing containers remain held, but preparation is paused during recovery. Seasoning cannot be applied until the object is back home, and later blackouts leave an unfinished recovery alone.

`PopcornNetSync` replicates displacement, carrier and pose, including to late joiners. Ghost arrival, pickup and drop publish their state immediately; clients follow that shared pose and never simulate drop physics or choose relocation points. Pickup/drop/mix requests validate the requesting player and range on the server. Each flavor's hover prompt always includes its name, even with empty hands or an already-filled container.

## Results

Both human and ghost customers can order Cheese, BBQ, Paprika, Water, Pepsi, Fanta, or Orange Juice. Correct orders still award one point; wrong human orders award nothing; wrong ghost orders still apply the configured damage (10 by default). A submitted filled item is consumed and the customer leaves after either outcome. The existing zone target, task rewards, and match win/loss flow are unchanged.

`PopcornRecipe` shares recipe matching between local play and `PopcornNetSync`. The server still resolves score and punishment. Drink/flavor inventory and hand visuals remain local as in the original prototype. Shared tank stock is server authoritative: begin/complete/cancel requests validate the sender, station, range, player health, and three-second duration. A server acknowledgement starts the local timer so latency cannot shorten an honest hold. Successful maker completion grants that connection a single refill token; refilling or discarding consumes it. Stock and capacity are SyncVars, including initial snapshots for joining clients. Repeated requests cannot double-spend servings or batches. The existing serving resolver continues to trust the submitted local flavor; remote held-prop replication is unchanged.

## Scene setup

The **Popcorn Minigame Systems** component in `Cinema_GamePlay` explicitly references all sixteen station objects, including the existing **Popcorn_Maker** prefab instance, **PopCornTank**, and eight drink controls. The original scene calls the second orange control **Grange2.3**; its direct reference maps to Orange Juice. No authored scene positions, rotations, scales, names, or materials are changed.

| Controls | Drink | Held prefab |
| --- | --- | --- |
| Blue1.1 / Blue2.1 | Water | PapperBotteWater |
| Green1.2 / Green2.2 | Fanta | PapperBotteFanta |
| Orange1.3 / Grange2.3 | Orange Juice | PapperBotteOrangeJuice |
| Red1.4 / Red2.4 | Pepsi | PapperBottePepsi |

The prefabs live in `Assets/Prefab/Zone1Prefab`. Adding ghost seasoning to **any** drink swaps its hand visual to **PapperBotteWaterGhost**. The chosen drink type is preserved for order matching, along with the ghost-seasoning flag and HUD label. Station objects need enabled, non-trigger colliders. Existing scene names and materials are preserved.

The Cashier UI Anchor shows customer type, base item, and ghost requirement on separate lines. The Popcorn Maker UI Anchor now displays instructions. Instant Make buttons are retired. Preparation has a high-contrast progress bar, percentage, countdown, and cancellation feedback. Hovering a world interactable adds a white silhouette outline without changing its materials.

Other scenes using `PopcornMinigameBootstrap` must assign the new station and container fields before using this loop. The older `Z1_Gameplay` layout predates these authored stations; the updated playable layout and regression target are **Cinema_GamePlay**.

## Verification

Run `Tests/Popcorn/Run-PopcornChecks.ps1 -UnityPath '<Unity 6000.5.7f1 executable>'` in PowerShell. It copies the project into ignored `.utmp/popcorn-regression`, then checks the Cinema scene in an isolated editor. Tests cover all sixteen station bindings/colliders, tank stock and top-up limits, exact refill/drink prefab references, all seven NPC orders, E pickup, correct container restrictions, three-second preparation, release/aim/occlusion cancellation, progress reset, ghost recipes, authored drink visuals, score/damage, repeat submissions, outlines, and local-player cleanup. Recovery checks cover flicker filtering, blackout relocation, unavailable seasoning, competing pickups, transfer to another player, exact return, real Mirror host pickup/drop commands, carrier despawn, and serialization of a carried snapshot. Supply checks also exercise real Mirror host commands and targeted inventory responses, server timing/range/cancellation checks, repeated-completion/refill rejection, and stock/capacity snapshot serialization. A separate remote-client playtest remains a manual check.

For a final manual playtest, walk through every station in the authored layout at the target display resolution, then test a host and a remote client serving the same queue. Adjust the scene UI anchors and held prop offsets in the Inspector if needed for the preferred camera framing.
