# Ghost Flavor blood-trail checks

Run `./Tests/GhostFavorTrail/Run-GhostFavorTrailChecks.ps1` with Unity 6000.5.7f1.
The script refreshes/reuses `.utmp/popcorn-regression/Project`, never the open
checkout, and runs `GhostFavorTrailChecks.Run` with graphics enabled.

It checks complete NavMesh paths around walls, invalid destinations, floor-height
limits, existing relocation-list selection, repeated ghost arrivals, offline
carrying with netId zero, settling/re-routing after a drop, return cleanup,
late-join Mirror serialization, bounded mark/mesh pooling, and reachability and
mark placement in the authored Cinema scene. Rendering checks compare resting
blood against the automatic sweep and verify opaque geometry hides the effect.

Results: `.utmp/popcorn-regression/ghost-trail-results.txt` and
`ghost-trail-unity.log`. Preview images use the `ghost-trail-*.png` prefix there.
This does not replace host/remote-client playtesting, checking dynamic doors and
stairs, or measuring CPU/GPU frame time on the target device.
