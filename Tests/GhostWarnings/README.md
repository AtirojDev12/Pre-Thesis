# Ghost warning checks and tuning

The shipped player prefab has `PlayerGhostWarningController` and `GhostWarningEffect`
on **CameraPivot**. Select that object in the prefab or scene instance to tune cooldown,
duration, attack time, FOV reduction, darkness, border width and softness. The overlay
is created only when the owning player receives a warning; it has no raycast target
and does not alter shared renderer assets. FirstPersonCamera owns the final FOV and
subtracts the warning from its smoothed walking/sprinting base without accumulating it.

Moving `Test_Ghost` uses Proximity (enter 5 m, exit 6 m). Stationary `TicketPunish`
uses Event: each non-final wrong sale requests a warning for the seller only. The
final wrong sale plays the existing jumpscare instead. Both ghosts turn their warning
gate off on attack. Jumpscare display suppresses local warnings for its duration.

## Adding another ghost

Add GhostWarningSource to the networked ghost prefab **before spawning it**, on the
NetworkIdentity object, and keep identical prefabs on the server and clients. Never
add a NetworkBehaviour dynamically after Mirror has spawned an object.

- None: no warning.
- Proximity: checks the local player's body position against the replicated ghost
  position. It rearms only outside Exit Radius; selected gizmos show both radii.
- DangerZone: assign a dedicated enabled trigger collider, preferably a BoxCollider,
  SphereCollider or CapsuleCollider. Zone Exit Margin provides hysteresis outside
  the collider. Use a child zone in a spawned prefab; a scene instance may reference
  a scene zone. Do not reuse a collider that AI disables.
- Event: server-side AI calls `source.WarnPlayer(affectedPlayerHealth)` for each new
  danger event. It uses TargetRpc, never a broadcast. No historical event is replayed
  to a late joiner. The recipient must observe the source network object.

AI may call `source.SetWarningActive(false/true)` on the server (or offline). This
gate is synced; reactivation inside range counts as a new danger. Category, radii,
priority and zone are authored prefab/scene settings, not client-controlled commands.
Call WarnPlayer explicitly for each affected player if an event threatens several.

Warnings are consumed during cooldown, playback, and suppression rather than queued.
Priority chooses between same-frame candidates, then distance breaks ties. There is
one pulse per player; extra sources never add darkness/FOV or restart it. Dead/downed
players, spectators, disabled cameras, remote player copies and ending sessions show
no warning. Disabling/despawning the active source cancels its pulse.

## Automated checks

`GhostWarningRegression.Run` is an Editor batch entry point. Copy it to Assets/Editor
of an **isolated temporary project** with the game scripts, their assembly/dependency
files and the three modified prefabs. Use Unity 6000.5.7f1 and the project's packages.
Do not run it in the working project: it creates/destroys probe objects and changes
Mirror ownership/session flags for checks.

Run Unity with `-batchmode -nographics -projectPath <isolated-project>
-executeMethod GhostWarningRegression.Run -logFile <log-path>`. It writes
`warning-results.txt` and exits nonzero on failure. Checks cover prefab wiring, Mirror
RPC/SyncVar generation, range hysteresis, no periodic retrigger, overlapping sources,
FOV composition, jumpscare suppression, source removal, zones, events and ownership.
These are Editor component checks; network ownership is simulated. They do not replace
a real host/client transport test or visual play-through.

## Manual multiplayer check

Use two separate builds: put A inside 5 m of the moving ghost and B outside 6 m.
Only A should see a one-second border/FOV pulse. Keep A still near the ghost past
three seconds: no repeat. Move A beyond 6 m and back: another pulse. Repeat while
sprinting, with two ghosts nearby, and with host/client roles reversed. A wrong
ticket sale should warn only its seller; the final mistake should show only the
existing jumpscare. Verify death, spectator mode, source despawn and leaving a room
clear the warning. Tune the border visually in the actual game scene.
