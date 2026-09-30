# Player animation build regression

Run `Tests/Animation/Run-AnimationChecks.ps1` with Unity 6000.5.7f1 installed (or pass `-UnityPath`). The runner reuses the ignored `.utmp/customer-build/Project` build cache, copies the current project into it, builds a small Windows test scene containing the actual Player prefab, and launches separate host/client processes over local KCP. Do not run it concurrently with the customer build checks, which use the same isolated cache.

The probe uses keyboard input through the real PlayerMovement component. It checks local movement and bone motion, remote movement animation, stable sprint state between network snapshots, stopping, downing, reviving, and instant death on both machines. Results are written to `AnimationBuild/animation-results-host.txt` and `animation-results-client.txt` inside the isolated project. Build/player logs are in `.utmp/animation-*.log`. Test saves use a separate company/product name.

## Footstep audio

`PlayerFootsteps` is attached to the shared Player prefab with all four carpet clips.
The owner detects each sole's transition from lifted to planted after animation/IK
and visual grounding. `FootTouchedGround(int)` is a UnityEvent (0 left, 1 right).
Contact hysteresis prevents repeated events while a foot remains planted; idle,
blocked movement, airborne feet and incapacitated players do not produce steps.
No fixed timer or imported clip events are needed, including for procedural strafing.

An ownership-required Mirror Command asks the server to select a clip excluding the
previous index. A reliable ClientRpc sends that same foot/clip pair to all observers,
including the owner, with no additional local playback on the host. Network latency
therefore also delays the owner's sound. Offline test scenes play directly.
Each foot has a runtime 3D AudioSource, linear attenuation from 1 to 15 metres,
zero volume beyond 15 metres, no Doppler, and SoundCategoryVolume set to SFX.
Tune volume/range/contact thresholds on PlayerFootsteps in the prefab. Carpet is
currently used for all surfaces. PlayerNoise's existing walking/running levels remain
the gameplay noise input; receiving another player's sound does not add local noise.

The animation probe checks actual walk/run contact playback on host and client,
non-repeating choices, SFX routing, distance settings, and idle/downed/dead silence.
The runner compares the entire foot/clip sequence across both processes. To use a
separate cache, pass `-TestProject '.utmp/footstep-build/Project'`. Logs still share
the animation log names, so run one animation test at a time.

## Causes and fixes

- Remote sprint previously compared displacement in one rendered frame against a speed threshold. NetworkTransform interpolation can pause or catch up between snapshots, so continuous sprinting repeatedly toggled the Animator back to walking. A two-process build reproduced sprint being active in only 81/121 samples on the host and 79/121 on the joining client. PlayerMovement now sends the owner's walking/sprinting choice when it changes, through an authority-required Command and server SyncVars. Local animation remains immediate; remote and newly spawned observers receive the same locomotion state. Downed/dead state still overrides locomotion.
- The controller only entered Dying when IsDowned was true. PlayerHealth.ServerKill skips downing, sets IsDead, and clears IsDowned, so both local and remote characters stayed outside Dying after an instant kill. The controller now also enters Dying when IsDead is true. Neither transition restarts Dying while already in it; the existing revival transition still requires both flags to be false.

The initial build diagnostics confirmed all four clips were included and moved the humanoid skeleton. No import, Animator hierarchy, GhostManager, or graphics setting changes were needed. These are gameplay-state bugs exposed during built multiplayer play, rather than missing animation assets.

## Directional locomotion and grounding

Backward and strafe movement now use a procedural humanoid IK gait over the existing Walking clip. Each foot alternates between a planted support phase and a lifted swing toward the movement direction. Gait phase advances with actual planar travel, and direction/IK weights blend over the animation damping interval. The project has no authored backward/strafe clips; the forward clip is neither reversed nor used unchanged for the legs when backing up. The visual model's offset below the Animator root is explicitly converted when positioning IK goals.

Opposing keys cancel, diagonals remain normalized, and camera pitch does not affect the movement plane. Backpedalling uses 55% of forward walking speed and sideways movement 80%, configurable on PlayerMovement. Sprint is allowed only with forward input. Direction is replicated along with the existing moving/sprinting state; death/downing still take priority.

Ground probes ignore triggers and the player's own colliders. The dynamic Rigidbody uses velocity and continuous collision detection, with a frictionless player material to avoid contact friction changing the intended stride speed. Nearby descending ground is followed while gravity handles falls. Remote bodies remain kinematic and follow NetworkTransform.

Visual grounding now handles idle/walk/run and their transitions. It caches the shoe vertices once and uses their skin weights to track the actual soles against the support surface beneath each foot, with 3 mm clearance. Y Bot's Read/Write import setting is enabled for this small runtime contact cache. No full mesh is baked each frame in gameplay. Walking can be corrected upward or downward; running retains its flight phase. Downed/dead poses are excluded, and visual correction never moves the collider or camera. Models without readable mesh data fall back to approximate foot-bound points.

The two-player build probe uses a real floor and gravity. It independently bakes the mesh after LateUpdate and compares against the physical floor (not the capsule bottom, which can still be settling after spawn). It checks backward foot planting/lift, A/D/S/diagonal/opposing inputs, direction replication, speed, visible grounding, thin-wall sprint collision, stopping, downing, revival, and instant death. Pose-review images `direction-0.png` through `direction-4.png` are saved beside the results. Uneven terrain and full gameplay scenes still warrant a manual play-through when tuning these values for a different model or map.
