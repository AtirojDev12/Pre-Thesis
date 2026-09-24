# Cinema gameplay migration

Scene: `Assets/Scenes/Map/Cinema_GamePlay.unity` (the saved filename uses `GamePlay`).
The original `Z1_EnemyTest` scene remains available.

- Ticket controls: `Ticket Sell/Cube/Cube (11)` (human) and `Cube (14)` (ghost).
- Movie selection: `Ticket Sell/Cube (1)/Movie Selection UI Anchor`.
- Popcorn order display: `Moniter/Cube (2)/Cashier UI Anchor`.
- Flavor/Make panel: `PopcornTasteSelect/Popcorn Maker UI Anchor`.
- Both customer spawn anchors belong to `Ticket n Popcorn Zone/Entrance/Door`.
- The exit anchors belong to `Ticket n Popcorn Zone/Entrance (1)/Door`, separated horizontally by 2.5m.
- Spawn/exit anchors use the door's horizontal position and a capsule-centre height of 1m. Customers leave after their order is resolved.
- Ticket and popcorn approach/departure waypoint arrays are editable in the Inspector. Route gizmos show each segment.
- The ticket booth's staff door is open so the operator area is reachable.

The scene carries one HUD, event system, offline player spawner, task timer, popcorn/ticket network state, and ghost manager.
The existing player prefab provides movement, stamina, health, camera, inventory interaction, replicated movement, and the L-key light command.
The obsolete scene-level LightController was removed to avoid duplicate input. GhostManager references all Cinema lights and the Cinema spawn point; navigation was rebuilt for this layout.

Press Play in Cinema for offline testing. For online play, start from MainMenu: its persistent NetworkManager/EOS/lobby setup owns the connection and now loads Cinema_GamePlay.
Cinema is enabled in build settings; the ghost prefab remains registered on NetworkManager. Six scene spawn points support the configured six-player limit.
Do not add another EOS or NetworkManager instance to Cinema: the connected instance persists from MainMenu.

`Run-CinemaChecks.ps1` runs an isolated offline test and a local KCP host test. It checks scene references, navigation/access, interaction rays, minigame serving/exit routes, player spawning, ghost spawning, and replicated light control.
The host test validates Mirror gameplay without logging into EOS; it does not verify an internet session or a separate remote client.

## Exported customer regression

`Run-CustomerBuildChecks.ps1` builds an isolated Windows player, then runs host and offline checks. The host starts before loading Cinema, so ticket UI initialization runs while the scene network identity is still inactive—the ordering that previously threw in `TicketMinigame.VisibleState` in exported builds. Both queues must naturally reach their waiting positions. The test also verifies that both customer renderers use the included URP material and samples rendered pixels to detect invisible or magenta bodies. Test files, logs, and screenshots are written under `.utmp/customer-build`; the normal game build is not replaced.
