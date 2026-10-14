# Reusing ItemHoldingSystem

`ItemHoldingSystem` is a single task-item slot, independent of recipes, player input, HUDs, and networking. It owns the item's visual, hand/placed location, placement anchor, presentation prefab, action locks, and local revision.

`PopcornItemHoldingSystem` is the existing popcorn adapter. It supplies the recipes, player lifecycle, HUD, bottle interaction, and `TaskHeldState` publication. The popcorn bootstrap creates this adapter, which automatically adds the shared component through `RequireComponent`. Existing scene anchors and the Mirror message/state layout remain unchanged.

## A new minigame

Add `ItemHoldingSystem` to the minigame's item-holder object and bind it to that player's hand or camera Transform. Keep preparation state and rules in the new minigame's own adapter.

```csharp
holding.ConfigureHandAnchor(playerHand, handPosition, handRotation);
var product = new HeldItemDefinition("coffee.empty-cup", emptyCupPrefab, .2f);

// Player pickup: reserves this one-item slot.
holding.TryPickUp(product);

// Approved placement: anchor is the item's bottom centre.
holding.TryPlace(machineAnchor, machineId);

// After successful preparation: stays on the same machine.
holding.TryReplaceVisual(preparedCupPrefab);

// Separate player interaction: bring the prepared visual back to the hand.
holding.TryRetrieve();

// Player discard: succeeds only in hand and while unlocked.
holding.TryDiscardInHand();
```

`HeldItemDefinition.id` identifies the product independently of its presentation. `CurrentPrefab` identifies the current visual, including a prepared replacement. `placedSize` sets the longest world-space dimension when placed; zero preserves the prefab's world scale. The component handles pivot alignment and nonuniform machine scale. Hand visuals retain the prefab scale and use the configured position and rotation.

Subscribe to `Changed` to update the minigame's UI. Subscribe to `ItemCleared` to release its item-specific resources. Read `HasItem`, `IsInHand`, `IsPlaced`, `Location`, `PlacementAnchor`, and `PlacementId` to decide which interactions to offer. Preparation readiness and pickup ownership are minigame rules, not holding rules.

## Actions and acknowledgements

`SetTransferPending(true)` locks pickup, placement, retrieval, visual replacement, discard, and submission while the minigame awaits an authoritative transfer result. Clear this lock before applying an approved transition; a rejected result should only unlock the slot.

For delivery, call `TryBeginSubmission(out uint token)`. The slot stays locked until `FinishSubmission(token, consumed)` receives the matching token. A stale token is rejected. A rejected delivery (`consumed: false`) unlocks the same item; a successful delivery clears it.

`Clear()` is unconditional authority/lifecycle cleanup. It is intentionally distinct from player discard: use it for death, despawn, scene teardown, or an approved server consume. Disabling the component cleans up the slot and its visual, including detached placed visuals.

## Multiplayer

The component is a presentation and local slot implementation, not a network authority. A new multiplayer adapter must validate ownership, reach, machine occupancy, preparation timing, and request identity on the server before applying a transition. Replicate stable product/machine IDs and minigame payload rather than Unity instance IDs or Transforms. Restore location and prepared visuals for joining observers, and release machine occupancy on owner cleanup.

`Revision` is a local operation token; it does not replace a minigame's network revision or request IDs. The popcorn adapter continues using its existing Mirror validation and `TaskHeldState` synchronization.
