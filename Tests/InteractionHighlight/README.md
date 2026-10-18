# Hover edge lighting

`InteractionOutline` registers the current local selection. `InteractionEdgeGlowFeature`
records its visible mesh renderers into a depth-tested normal/mask buffer, then blends
white edges and a restrained halo over the final camera image. Both PC_Renderer
and Mobile_Renderer have the feature enabled. No extra components need to be added to
existing interactable prefabs; PlayerInteractor still creates the selection component.

Only silhouette boundaries and sufficiently large changes in authored vertex normals
light up. Coplanar triangulation is invisible, and smooth curved surfaces mostly retain
just their silhouette. Hard internal creases require hard/split normals in the model.
Original materials and property blocks are preserved. IInteractionSurfaceTint marks
legacy whole-surface tint callbacks that PlayerInteractor now bypasses; other hover
callbacks (customer prompts and lobby UI) still run.

Tune the renderer feature's Edge Width (1.5 pixels), Glow Width (4 pixels), Glow Opacity
(0.22), Edge Color, and Crease Threshold (0.18). The overlay runs after the retro filter
so pixelation/vignetting do not erase thin lines. The width is measured in mask pixels;
a reduced render scale changes its apparent final size.

A soft band sweeps upward in world height over the selected model's bounds in 1.25
seconds, then pauses for 0.5 seconds. Sweep Width defaults to 0.18 of the object's
height. Base Brightness keeps the edges visible at 0.5 between sweeps. Both the band
and resting edges use the same Edge Color; only opacity/brightness changes. Selecting
a new target restarts the sweep. No light is added across flat face interiors.

The selection pass supports MeshRenderer and SkinnedMeshRenderer surfaces, including
meshes without CPU read access. Cached capture materials copy URP _BaseMap/_BaseColor
(or legacy _MainTex/_Color), UV transforms, alpha cutoff and culling. This includes the
Popcorn Maker's transparent material without modifying its glass or original textures.
Transparent alpha below 0.05 is discarded; cutouts use their authored cutoff. Custom
procedural opacity shaders require a matching capture shader. A separate selection
depth buffer hides rear faces, and sampled scene depth hides the effect behind opaque
walls. A narrow halo extends a few pixels around visible boundaries. This feature
requires Render Graph, matching the existing retro filter. The depth input is requested
consistently even without a selection so hover cannot toggle the scene's retro filter.

## Automated rendering checks

Run with the matching Unity 6000.5 editor:

```powershell
./Tests/InteractionHighlight/Run-EdgeGlowChecks.ps1 -UnityPath 'C:/InstallUnity/6000.5.7f1/Editor/Unity.exe'
```

The script creates an isolated project under `.utmp/edge-glow`, using locally cached
URP packages. It compiles the implementation/shaders and renders a cube to check outer
edges, internal creases, preserved materials/interiors, hover exit, camera ownership,
disable cleanup, wall occlusion, MSAA, upward band movement and a consistent white hue.
It also imports the actual Popcorn Maker prefab/material/textures to verify its hover
and exit states, and checks fully transparent pixels. Results, before/hover PNGs, and the Unity log
are saved there. This does not replace checking the authored scene's varied props and
split-screen/camera configurations in the game.
