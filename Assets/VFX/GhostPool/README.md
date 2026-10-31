# Black water ghost preview

Unity 6000.5.7f1 / URP 17.5.0 / Visual Effect Graph 17.5.0.

Open `BlackWater_Preview.unity`, then enter Play mode. Left: pool spreads, the original Ghost Walk Anim model emerges and water splashes. Right: pool, splash and sinking ghost. Both pools linger, soak away, then emit low evaporation smoke. The Replay button restarts the thirteen-second cycle.

The two editable native VFX Graph assets are in `Graphs/`: `Ghost_BlackWater_Splash.vfx` and `Ghost_Submerge_Smoke.vfx`. Both contain Spawn, Initialize, Update and Output contexts derived from the installed Unity Simple Burst template, with authored textures, size/color curves, velocity, lifetime and force settings. They use GPU simulation, not Shuriken Particle Systems.

The pool is a separate URP material with an irregular edge, glossy lighting, radial ripple normals and fade controls. It is not a fluid simulation. The preview samples the original Walk animation and moves only a presentation holder.

The gameplay `Ghost Walk Anim.prefab` now assigns this water material and both VFX Graph assets to `GhostManifestation`. Its local visual pivot emerges after the 0.9-second pool growth and sinks while playing the original Entry clip from its last frame back to its first. The original Entry/Walk controller remains the movement gate. The server keeps the ghost identity until the reversed Entry clip and the 0.9-second pool lead-in finish, plus a 0.2-second delivery margin; collision and attacks stop immediately. The detached pool holds at full opacity for four seconds after the emergence/submergence motion finishes, fades over 1.6 seconds, and cleans up after fading independently of the destroyed ghost. Network timestamps allow a late observer to join the current phase without restarting expired effects. Audio and the network/navigation root remain on the original object.

`GhostPoolVisual` preserves the model's animation binding paths under a separate presentation Animator. `GhostPoolEffect` projects the quad and VFX into the ground plane chosen by `GhostManifestation`, keeping effects out of the ghost's child hierarchy and creating no gameplay colliders.

Editor menu: Tools > Pre-Thesis > VFX > Open Black Water Preview.
Build Black Water Preview authors a fresh preview only and refuses to overwrite an existing one.

## Water timing and legacy atmosphere

The pool holds for four seconds after body travel, then soaks away over 1.6 seconds.
Only after the water is fully gone does low vapor rise from the former pool, for
up to 2.5 seconds. Both emergence and submergence use this sequence. The detached
presentation stays alive until the vapor tail finishes; ghost movement and
network identity destruction keep their existing timing.

The old procedural smoke silhouette in `GhostPoolAtmosphere` is retained for
standalone inspection tools, but gameplay and the current preview do not create
it. Resolve still controls the water's inward ripples. White edge frost is removed.

## Opening water eruption

The combined `Assets/VFX/VFX_Preview.unity` now opens on **Ghost black water**.
Play the scene to watch the opening pressure splash at 0.18 seconds and the
existing body-breach splash at 0.9 seconds. The shared `GhostPoolSurface` replaces
the flat quad mesh with a 64-by-64 grid. The water shader displaces it into an
uneven low crown and central upwelling, then settles into small outward ripples.
This is an authored water effect, not a fluid simulation. The same presentation
runs in gameplay and the standalone black-water preview.

The editable `Ghost_BlackWater_Splash.vfx` now emits 210 upward-biased droplets
with gravity, lower drag, larger droplets, and a 512-particle capacity. The opening
and breach use separate VisualEffect instances; replay resets the opening burst.

Preview controls:
- **Water eruption + pressure splash**: compare the displaced water with the flat surface.
- **Water height**: adjust surface displacement from 0 to 2; it does not change droplet velocity.
- **Water only**: hide bodies and smoke to inspect the water.
- **Inspect water + soaking** and **Water time**: scrub the whole effect, including disappearance, from 0 to at least 14 seconds.
  Droplets pause in this mode; their trajectories are inspected with normal Replay.

`Tests/GhostPool/GhostWaterEruptionChecks.cs` can be copied into `Assets/Editor`
in a verification project. Use its menu **Tools > Pre-Thesis > VFX > Verify Water
Eruption** or batch execute method `GhostWaterEruptionChecks.Run` with a GPU.
It checks shader import, the combined preview, geometry, replay and live VFX
particles, and writes captures under `captures/water-evaporation`.

The pool now has a soft wet edge and slightly translucent core (`_Opacity = .94`,
`_EdgeSoftness = .18`). White frost has been removed entirely. Centre sampling
avoids undefined `atan2(0,0)`, and wave troughs are clamped above the floor so
the centre stays filled. After the existing four-second hold, `_Absorb` drives
uneven pore-like wet patches over 1.6 seconds. The footprint stays at its full
size while the water soaks away; it no longer shrinks as a fading image.
The coverage check renders the centre and absorption phases against a contrasting
background, verifies continuous centre coverage, progressive drying and no final
residue, and captures the actual preview near the disappearance phase.

## Evaporation smoke

`GhostPoolSurface` owns a local `Ghost_Submerge_Smoke.vfx` instance per pool.
Despite its legacy filename, this graph now serves as evaporation: particles
spawn from a flattened area 0.06 metres above the water surface, not from the
body at 0.65 metres. `EvaporationStartFor(travel)` is pool growth + body travel +
four-second hold + 1.6-second absorption. Replay resets the vapor, late observers
seek to the matching smoke age, and the effect cleans up after its 2.5-second tail.
The original preview smoke object is retained as an asset reference and stays
inactive. Water only hides the vapor as well as bodies.
