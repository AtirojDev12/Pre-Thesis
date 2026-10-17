# Ghost manifestation checks

Copy `GhostManifestationChecks.cs` into `Assets/Editor` in an isolated copy of the
current Unity project. Run Unity in batch mode with
`-executeMethod GhostManifestationChecks.Run` (without `-quit`). The check enters
Play Mode in an empty scene, builds a temporary NavMesh, and exits with status 0
on success. Results are written to `ghost-manifestation-results.txt` at the project root.

Checks cover effect/audio references, the complete Entry-to-Walk transition,
no translation or footsteps during Entry, movement and footstep playback after
Entry, and disappearance smoke surviving destruction of the ghost. Multiplayer
host/client listening and visual alignment still need a manual play session.

The batch editor on this machine compiled and resolved the assets, but its game
clock stayed at zero, so the lifecycle check timed out before Entry advanced.
Run from a normal isolated editor through **Tools > Pre-Thesis > Tests > Ghost
Manifestation** to check playback. This test closes that isolated editor when it
finishes; do not run it in an editor with unsaved work.
