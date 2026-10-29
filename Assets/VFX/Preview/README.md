# Food preparation VFX

## Item drop impact

Both `VFX_Preview.unity` scenes also contain an **Item drop (0.5 sec)** stage. Select it using the upper-right button in Play mode, or **Show Item Drop Impact** on the stage switcher's Inspector before Play. The stage drops the actual world flashlight onto a dry tiled floor using the gameplay collision callback. **Replay physical drop** repeats the drop; **Large item (2.5x physical size)** compares the automatic footprint scaling. The exact-time slider and **Freeze impact** inspect the shared effect independently. Strength, size and dust sliders apply to this inspection; physical drops use the world item's Inspector settings.

The gameplay prefab is `Assets/Resources/ItemImpact/ItemImpact.prefab`. Its three materials share the lit, transparent, non-emissive `PreThesis/VFX/Item Impact` shader. Smoke now consists of **12 overlapping 3D density volumes**: one central cloud, eight outward puffs and three higher billows. Each puff uses a box solely to bound a 12-step ray integral through an ellipsoid with evolving 3D noise; the box surface is invisible. Smoke has thickness and changes silhouette across viewpoints, with density-derived lighting normals. Scene depth clips the ray integral against the item and floor when available. No textures or camera-facing smoke cards are used. The footprint retains the 1.65 multiplier and automatic per-item scaling. Five very faint streaks (.02–.12 s) and a subdued ground ring (.03–.27 s) remain as secondary accents. Smoke begins at impact and clears by .5 s. The ring uses subtle scene-color refraction when an opaque texture is available, with a plain translucent fallback otherwise. The PC pipeline already supplies scene depth and an opaque texture.

The preview's **Front view / Side view / Above view** buttons make it easy to inspect the volume from different directions. Use **Freeze impact** around .18 s to compare the smoke's height, overlap and outline.

Every `WorldInventoryItem` uses its collider bounds centre projected onto the actual contact plane, so the burst originates beneath the middle of the item. The footprint automatically grows by the square root of the item's largest bounds dimension relative to a .2 m reference, clamped to 1–2 times the per-item size setting. Offline play displays locally; Mirror sends the resulting centre, scale and intensity to observers, including the host once. Remote kinematic items never generate their own collision effect. Effects remain at the impact position while the item moves, and do not change item physics, lighting or the ground. Each world item's **Ground impact VFX** fields control minimum/full impact speed, size, dust and allowed surface layers. The default suppresses normal contact speeds below .65 m/s, caps full strength at 4.5 m/s, ignores walls/players/other items, excludes Unity's Water layer, and spaces impacts by .10 s. Assign any additional water layers outside the surface mask; water splashes are a separate effect. Up to 48 transient instances are allowed simultaneously.

Validation and rendered timeline frames are described in `Tests/ItemImpact/README.md`.

Open either `Assets/VFX/VFX_Preview.unity` or `Assets/VFX/GhostManifestation/VFX_Preview.unity`, or use **Tools > Pre-Thesis > VFX > Open Food Preview**. Both scenes open with the Ghost Favor stage visible. Press Play to see the seasoning jar disappear in red-black smoke and flickering embers. The effect lasts **two seconds including fade**. Replay shows the jar for half a second before disappearance; automatic replay includes a pause between demonstrations. The graph is `Assets/Resources/GhostFavorDisappear.vfx`, shared with gameplay.

In Play mode, the upper-right **Popcorn + Drink**, **Ghost black water**, and **Ghost Favor (2 sec)** buttons switch stages. In edit mode, select **VFX Preview Stage Switcher** in the Hierarchy and use its Inspector buttons. Press Play to animate the selected stage. The black-water stage uses the editable VFX Graph assets and material in `Assets/VFX/GhostPool`; the previous smoke stage remains inactive in the scene. Food particles also animate in Scene view before entering Play mode when that stage is selected; disable **Preview In Edit Mode** on Food Preview Controls to pause this inspection preview.

The two stations play for three seconds and repeat every six seconds. **Prepare both** restarts them. **Cancel drink now** clears the stream and every droplet immediately and turns off automatic replay so the stopped state can be inspected. **Stop all** clears both stations. Toggle **Auto replay** to resume cycling.

## Separate effect folders

- `Assets/VFX/PopcornMachine`: prefab, controller, steam/kernel/powder materials and kernel mesh.
- `Assets/VFX/DrinkDispenser`: prefab, controller and water materials.
- `Assets/VFX/Preview`: scene controls, authoring tool and display materials.
- `Assets/VFX/Shared`: the small URP particle shader used by both effects.

Preview machine, cup and bucket geometry lives in the scene only. Effect prefabs contain just the effects, start idle, and do not emit automatically when added to a gameplay scene.

## Popcorn machine

Use `PopcornMachine/Prefabs/PopcornMachine_VFX.prefab` at a uniform scale of 1. Steam and kernel emitters sit at local `(-0.3, 1.03, 0)` above the hot tray. The seasoning emitter sits at `(0.65, 1.65, 0)` over the bucket landing surface at approximately `(0.65, 0.86, 0)`. Move these child transforms to match the actual prop.

Call `BeginCooking()` while heating/popping and `BeginSeasoning()` while adding flavor, or `BeginPreparation()` to show all three layers together as in the preview. Call `Cancel()` or `Complete()` to clear every layer. Disabling the prefab also clears it.

`Cinema_GamePlay` contains a steam-only instance parented to the refill `Popcorn_Maker`, with its emitter centered on the machine mesh. Its `makerStation` reference follows active refill preparation: steam plays during the three-second hold and clears on completion or cancellation. Other clients see server-approved maker actions through the existing station action state, including actions already in progress when they join. Kernel pops and seasoning are removed from this scene instance. The completed refill uses `NotastePopcorn` and retains the refill-batch rules for topping up `PopCornTank`.

Gameplay steam renders at sorting order 1, after the machine's transparent order-0 material. Using the same order lets the rising plume's bounds reverse distance sorting, so the machine hides the smoke even while the emitter is still running. The popcorn regression checks render the actual player-side viewpoint before and after this correction at 0.3, 1.3 and 2.3 seconds.

## Drink dispenser

Use `DrinkDispenser/Prefabs/DrinkDispenser_VFX.prefab` at a uniform scale of 1. Set `nozzle` and `cupRim` in local coordinates, and move `03_FallingDroplets` to the nozzle and `04_CupRimSplashes` to the cup rim. The preview uses `(0, 1.7, 0)` and `(0, 0.78, 0)` respectively.

Call `BeginDispensing()` when a preparation action starts. Call `Cancel()` in the same cancellation callback as the hold action; it synchronously hides both stream renderers and stops particle systems with `StopEmittingAndClear`. Call `Complete()` when filling finishes. Disabling the prefab also clears it. Gameplay and network events are not modified by this preview-only addition.

## Authoring validation

`FoodVfxAuthoring.Build` is an explicit batch authoring entry point intended for a fresh isolated project. It refuses to overwrite an existing food preview. It creates the assets, checks rising steam, small kernel hops, downward seasoning/water motion, immediate cancellation, completion, disable cleanup, restart and serialized scene bindings. It also renders preparing and cancelled preview images. It never runs automatically on import.
