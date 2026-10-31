# Cinema traversal verification

Copy `CinemaTraversalChecks.cs` into `Assets/Editor`, open Cinema_GamePlay, allow
Unity to compile, then run **Tools > Pre-Thesis > Player > Verify Cinema Traversal**.
Remove the temporary Editor copy after verification. The report is written to
`.utmp/cinema-walkability-current/report.json`.

The runner uses the production PlayerMovement motor, Player prefab capsule,
Rigidbody settings and friction material in a separate local PhysicsScene. It
copies active static/kinematic solid colliders and their exact transform hierarchy.
Triggers, actors and objects on layers ignored by the player are excluded. Nothing
from the test scene is saved into gameplay. Input is supplied directly to isolate
collision behavior from keyboard focus, stamina and network ownership.

Coverage includes both directions at walking and sprinting speed through adjacent
navigation triangles, via their shared edge, including disconnected floor regions.
Roof polygons above 7.5 m are excluded. Three additional lines cross the staff
doorway at its centre and near both sides. Actual collision surfaces determine
starting poses because the baked navigation heights differ from the scene's floors.
Unusable standing poses are retried crouched and recorded if still invalid.

Separate fixtures cover 0.8/8/18/23/29 cm thresholds, thin trim, crouched traversal,
short staircases, tall walls, low ceilings, 20/30/45-degree crests, downhill ground
following, real ledges, free fall and stopping on a slope. Global gravity stays
unchanged. A map audit is a finite physical route sample, not a guarantee about
every possible position or moving object; remote multiplayer visuals are outside
this runner's coverage.

## Repair

The movement motor uses a footprint support probe, verifies actual floor normals,
and prevents upward carry-over after leaving a slope. Grounded players can step
over low edges up to 30 cm, with head and forward clearance checks. Small adjoining
treads are resolved within the same total height limit. Tall walls remain solid.

Twenty invisible row-ramp BoxColliders in the two cinema rooms have their depth
limited to the ChairBase footprint. They previously protruded into the surrounding
aisle. One ghost-room row ramp also moves 22 cm uphill so its exposed end no longer
blocks the lower tread. Rendered meshes, walls, doors and gameplay triggers are
preserved.

The runner can validate this geometry in an isolated copy using a request file
containing `candidate`. **Apply Validated Cinema Ramp Repair** only accepts a
complete passing candidate report, validates original collider values, and updates
those collider fields on disk and in the open Editor without saving unrelated
unsaved scene edits. A regular verification run must follow application.
