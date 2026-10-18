# Black water ghost preview

Unity 6000.5.7f1 / URP 17.5.0 / Visual Effect Graph 17.5.0.

Open `BlackWater_Preview.unity`, then enter Play mode. Left: pool spreads, the original Ghost Walk Anim model emerges and water splashes. Right: smoke, pool, splash and sinking ghost. Both pools linger and fade. The Replay button restarts the thirteen-second cycle.

The two editable native VFX Graph assets are in `Graphs/`: `Ghost_BlackWater_Splash.vfx` and `Ghost_Submerge_Smoke.vfx`. Both contain Spawn, Initialize, Update and Output contexts derived from the installed Unity Simple Burst template, with authored textures, size/color curves, velocity, lifetime and force settings. They use GPU simulation, not Shuriken Particle Systems.

The pool is a separate URP material with an irregular edge, glossy lighting, radial ripple normals and fade controls. It is not a fluid simulation. The preview samples the original Walk animation and moves only a presentation holder.

The gameplay `Ghost Walk Anim.prefab` now assigns this water material and both VFX Graph assets to `GhostManifestation`. Its local visual pivot emerges after the 0.9-second pool growth and sinks while playing the original Entry clip from its last frame back to its first. The original Entry/Walk controller remains the movement gate. The server keeps the ghost identity until the reversed Entry clip and the 0.9-second pool lead-in finish, plus a 0.2-second delivery margin; collision and attacks stop immediately. The detached pool holds at full opacity for four seconds after the emergence/submergence motion finishes, fades over 1.6 seconds, and cleans up after fading independently of the destroyed ghost. Network timestamps allow a late observer to join the current phase without restarting expired effects. Audio and the network/navigation root remain on the original object.

`GhostPoolVisual` preserves the model's animation binding paths under a separate presentation Animator. `GhostPoolEffect` projects the quad and VFX into the ground plane chosen by `GhostManifestation`, keeping effects out of the ghost's child hierarchy and creating no gameplay colliders.

Editor menu: Tools > Pre-Thesis > VFX > Open Black Water Preview.
Build Black Water Preview authors a fresh preview only and refuses to overwrite an existing one.

The gameplay and preview share surface size 3.55 by 3.25 (about 15% wider than the first version). The pool stays fully visible for four seconds after the body motion finishes, then fades over 1.6 seconds. Submergence uses the original Entry clip duration; preview cycles last at least thirteen seconds. If expiry interrupts an unfinished Entry, reversal begins at that animation phase instead of jumping to the end pose.
