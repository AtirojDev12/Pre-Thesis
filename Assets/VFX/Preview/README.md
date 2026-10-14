# Food preparation VFX

Open either `Assets/VFX/VFX_Preview.unity` or `Assets/VFX/GhostManifestation/VFX_Preview.unity`, or use **Tools > Pre-Thesis > VFX > Open Food Preview**. Both scenes contain the food and ghost stages and open with popcorn and drink visible. Food particles also animate in Scene view before entering Play mode; disable **Preview In Edit Mode** on Food Preview Controls to pause this inspection preview.

In Play mode, the upper-right **Popcorn + Drink** and **Ghost effects** buttons switch stages. In edit mode, select **VFX Preview Stage Switcher** in the Hierarchy and use its Inspector buttons.

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

## Drink dispenser

Use `DrinkDispenser/Prefabs/DrinkDispenser_VFX.prefab` at a uniform scale of 1. Set `nozzle` and `cupRim` in local coordinates, and move `03_FallingDroplets` to the nozzle and `04_CupRimSplashes` to the cup rim. The preview uses `(0, 1.7, 0)` and `(0, 0.78, 0)` respectively.

Call `BeginDispensing()` when a preparation action starts. Call `Cancel()` in the same cancellation callback as the hold action; it synchronously hides both stream renderers and stops particle systems with `StopEmittingAndClear`. Call `Complete()` when filling finishes. Disabling the prefab also clears it. Gameplay and network events are not modified by this preview-only addition.

## Authoring validation

`FoodVfxAuthoring.Build` is an explicit batch authoring entry point intended for a fresh isolated project. It refuses to overwrite an existing food preview. It creates the assets, checks rising steam, small kernel hops, downward seasoning/water motion, immediate cancellation, completion, disable cleanup, restart and serialized scene bindings. It also renders preparing and cancelled preview images. It never runs automatically on import.
