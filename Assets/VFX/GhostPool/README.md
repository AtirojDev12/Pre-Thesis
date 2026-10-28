# Black water ghost preview

Unity 6000.5.7f1 / URP 17.5.0 / Visual Effect Graph 17.5.0.

Open `BlackWater_Preview.unity`, then enter Play mode. Left: pool spreads, the original Ghost Walk Anim model emerges and water splashes. Right: smoke, pool, splash and sinking ghost. Both pools linger and fade. The Replay button restarts the thirteen-second cycle.

The two editable native VFX Graph assets are in `Graphs/`: `Ghost_BlackWater_Splash.vfx` and `Ghost_Submerge_Smoke.vfx`. Both contain Spawn, Initialize, Update and Output contexts derived from the installed Unity Simple Burst template, with authored textures, size/color curves, velocity, lifetime and force settings. They use GPU simulation, not Shuriken Particle Systems.

The pool is a separate URP material with an irregular edge, glossy lighting, radial ripple normals and fade controls. It is not a fluid simulation. The preview samples the original Walk animation and moves only a presentation holder.

The gameplay `Ghost Walk Anim.prefab` now assigns this water material and both VFX Graph assets to `GhostManifestation`. Its local visual pivot emerges after the 0.9-second pool growth and sinks while playing the original Entry clip from its last frame back to its first. The original Entry/Walk controller remains the movement gate. The server keeps the ghost identity until the reversed Entry clip and the 0.9-second pool lead-in finish, plus a 0.2-second delivery margin; collision and attacks stop immediately. The detached pool holds at full opacity for four seconds after the emergence/submergence motion finishes, fades over 1.6 seconds, and cleans up after fading independently of the destroyed ghost. Network timestamps allow a late observer to join the current phase without restarting expired effects. Audio and the network/navigation root remain on the original object.

`GhostPoolVisual` preserves the model's animation binding paths under a separate presentation Animator. `GhostPoolEffect` projects the quad and VFX into the ground plane chosen by `GhostManifestation`, keeping effects out of the ghost's child hierarchy and creating no gameplay colliders.

Editor menu: Tools > Pre-Thesis > VFX > Open Black Water Preview.
Build Black Water Preview authors a fresh preview only and refuses to overwrite an existing one.

## Resolve atmosphere — first visual pass

The existing pool, splash graph, submerge smoke graph and body motion remain the base.
`GhostPoolAtmosphere` adds 72 procedural smoke billboards (144 triangles per pool),
which curl against one another and gather into an ambiguous upright silhouette.
This silhouette is a visual approximation, not sampling of the ghost mesh. The
shared water shader now supports inward ripples, a still surface at full Resolve,
and frost along the inside of the pool edge. No reflection of the ghost is added.

`GhostPoolEffect` drives this local presentation from the existing emergence and
submergence times. Additional smoke fades within .8 seconds after body travel;
the pool keeps its original hold/fade timing. There are no new gameplay,
networking, model, animator or player dependencies. The graphs are unchanged.

In Play mode in the existing Black Water preview (also available from the Ghost
black water stage in the combined VFX preview), toggle **Resolve smoke + inward
ripples + frost** to compare with the old look. Select **Pause and inspect
Resolve** and drag the slider from 0 to 1 to inspect smoke and water independently
of the body animation. Uncheck it to resume. This is a visual inspection control,
not a new gameplay mechanic or a banishment trigger.

Shader defaults preserve old materials until the local effect explicitly sets
`_Horror = 1`. `_Resolve` controls formation, `_Phase` supplies time, and `_Mist`
is reserved for the generated smoke material. Dynamic meshes and materials are
owned by the effect and released when it is destroyed.

The gameplay and preview share surface size 3.55 by 3.25 (about 15% wider than the first version). The pool stays fully visible for four seconds after the body motion finishes, then fades over 1.6 seconds. Submergence uses the original Entry clip duration; preview cycles last at least thirteen seconds. If expiry interrupts an unfinished Entry, reversal begins at that animation phase instead of jumping to the end pose.
