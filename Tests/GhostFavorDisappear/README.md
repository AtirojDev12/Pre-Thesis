# Ghost Favor disappearance smoke

The Cinema scene assigns `GhostFavorDisappearVfx` on **Popcorn Minigame Systems**.
The graph is `Assets/Resources/GhostFavorDisappear.vfx`. Other maps using the
existing recovery bootstrap automatically load this same graph.

The prop relocates as before. A detached black smoke burst remains at its original
visible centre and finishes in **two seconds total, including its fade**. It does
not follow the item or create colliders. Particle lifetimes are at most two
seconds; the effect object is removed at two seconds. **Playback Speed** scales
this duration; **Size Multiplier** scales the cloud relative to the jar.

The same graph now contains three named groups: **Black smoke**, **Crimson wisps**
and **Crimson embers**. The dark core is surrounded by expanding red smoke, while
small additive red particles flicker and drift upward. All three systems share
the same disappearance event and cleanup. Velocity and acceleration are retimed
with particle lifetimes to preserve their motion in the shorter duration. Total
burst count is 126 particles with three bounded systems.

The authoritative state carries a monotonically increasing event ID and timestamp.
Clients resume recent events at the right age; repeated snapshots and expired
events do not replay. Older snapshots cannot replace a newer event. Returning or
carrying the item does not emit smoke, and session shutdown suppresses new effects.

Both `Assets/VFX/VFX_Preview.unity` and the preview scene in `GhostManifestation`
include the `GhostFavorPreview` prefab and open on its stage. The **Ghost Favor
(2 sec)** button selects it; **Replay disappearance** shows the jar for half a
second, hides it, and plays the same gameplay graph for two seconds. The optional
four-second replay cycle includes the pause between demonstrations. The preview
contains no gameplay recovery or network identity.

Run `Run-GhostFavorSmokeChecks.ps1` with Unity 6000.5.7f1. It copies current assets
into the existing `.utmp/popcorn-regression/Project` test project, checks the real
Cinema graph assignment, then runs a play-mode test using a clone of the actual
seasoning prop. It verifies two-second cleanup, rendered red wisps/embers,
recent/expired/reordered observer states, a real Mirror host, initial and delta
SyncVar deserialization, rejection of client writes, shutdown, and all three
preview stage buttons. Results and seven rendered frames
are written to `.utmp/popcorn-regression/ghost-favor-smoke-*`.
