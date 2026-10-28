# Resolve atmosphere visual checks

Use an isolated Unity 6000.5.7f1 project with URP 17.5 and VFX Graph installed and
a URP renderer assigned. Copy `Assets/VFX/GhostPool/Scripts/` and
`Assets/VFX/GhostPool/BlackWater.shader` into that project. Copy
`GhostPoolResolveChecks.cs` into its `Assets/Editor/` folder.

Run Unity with `-batchmode -force-d3d11 -projectPath <isolated-project>
-executeMethod GhostPoolResolveChecks.Run -logFile <log-path>`. Do not add
`-nographics` because the checks compile and render the shader. The check exits
the isolated editor itself, so do not run it in an editor with unsaved work.

The checks cover appear/submerge Resolve endpoints, zero travel duration,
shader compilation, collider isolation, the smoke geometry budget, and vertical
volume. They render four formation stages to
`captures/ghost-pool-resolve/resolve-*.png` with results in `results.txt`.
These images test the local effect without requiring a character model.

For the existing character and GPU effects, open the working project's
`Assets/VFX/GhostPool/BlackWater_Preview.unity`, enter Play mode and compare
the atmosphere toggle on/off. Check replay, pause, Resolve slider, resume,
both body motions, and the original pool hold/fade. Gameplay timing, multiplayer
transport and a literal ghost reflection are outside these visual checks.
