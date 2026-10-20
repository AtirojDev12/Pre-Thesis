# Paper reader checks

Run `./Tests/PaperReader/Run-PaperReaderChecks.ps1` with Unity 6000.5.7f1.
The runner copies/reuses `.utmp/popcorn-regression/Project` so it does not open
or modify the authored project in the Editor. Do not run it concurrently with
other checks that use that same isolated project.

The checks exercise the actual reader components in a headless Unity Editor:
offscreen start, animation midpoint/end, aspect fit, sparse page navigation,
duplicate-open rejection, input lock through exit, cleanup when the player or
paper is disabled, and suppression of same-frame reopen/pause. They also load
the Cinema scene and verify Paper's image, collider and animation defaults.

Results: `.utmp/popcorn-regression/paper-reader-results.txt` and
`paper-reader-unity.log`.

These are programmatic Editor checks. Playtest physical aiming/E interaction,
visual readability at the target resolutions, and two connected players with
independent paper overlays in a live match.
