# Readable papers

`Cinema_GamePlay > Paper` is configured to show the popcorn instruction image
from `Assets/PaperHTP`. Aim at its collider within the player's normal interaction
range and press the interaction key (E by default). The sheet slides up from
below the screen. E or Escape slides it down and returns control.

## Inspector

Select Paper and edit **Readable Paper**:

- **Pages**: drag Texture2D images here in order. Sprite conversion is unnecessary.
  The first page is required; empty later slots are skipped.
- **Interaction Prompt**: text used by the existing interaction prompt system.
- **Maximum Size**: maximum width/height in 1920 x 1080 reference canvas units.
  The image keeps its aspect ratio and shrinks to fit smaller screens.
- **Screen Margin / Center Offset**: space around the sheet and its reading position.
- **Backdrop Color / Sorting Order**: dimming color and overlay draw order.
- **Open Duration / Close Duration**: seconds, using unscaled time; zero is instant.
- **Slide Curve**: normalized progress (0 below screen, 1 reading position).
  The default eases in and out. Add a value above 1 for a small overshoot.
- **Offscreen Padding**: extra distance below the bottom edge.
- **Close Key / Next Page Key / Previous Page Key**: defaults E, RightArrow,
  LeftArrow; Escape always closes. The opening key comes from PlayerInteractor.
- **On Opened / On Closed**: local UnityEvents for sounds or future extensions.
  These events do not replicate quest state across the network.

## Add another paper

1. Add **Readable Paper** to the world object and assign its Pages.
2. Give the object, or a child, an enabled non-trigger Collider. Its layer must
   be included in PlayerInteractor's Interactable Layers.
3. Keep one interaction component per object hierarchy. If a different
   IInteractable is found first, it handles E instead.

The reader creates a screen-space Canvas beneath the interacting local player
on first use and reuses it for subsequent papers. No player prefab changes,
EventSystem, NetworkIdentity or network messages are needed. Movement, camera
look and gameplay interactions are blocked while reading; time keeps running.
Closing keeps those controls blocked until the exit animation finishes.
The HUD returns on close. Disabling/removing the paper, disabling/destroying
the player, death/downing, or a normal scene unload immediately cleans up the
overlay registration. A scene transition that preserves the player and paper
also preserves the reader. Test host and remote client in a live match before
shipping, especially interactions with other custom modal screens.

If instruction text looks blurry, increase the source texture's Max Size or
reduce compression in its import settings. The reader uses the existing image
without modifying its import settings.
