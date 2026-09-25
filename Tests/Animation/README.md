# Player animation build regression

Run `Tests/Animation/Run-AnimationChecks.ps1` with Unity 6000.5.7f1 installed (or pass `-UnityPath`). The runner reuses the ignored `.utmp/customer-build/Project` build cache, copies the current project into it, builds a small Windows test scene containing the actual Player prefab, and launches separate host/client processes over local KCP. Do not run it concurrently with the customer build checks, which use the same isolated cache.

The probe uses keyboard input through the real PlayerMovement component. It checks local movement and bone motion, remote movement animation, stable sprint state between network snapshots, stopping, downing, reviving, and instant death on both machines. Results are written to `AnimationBuild/animation-results-host.txt` and `animation-results-client.txt` inside the isolated project. Build/player logs are in `.utmp/animation-*.log`. Test saves use a separate company/product name.

## Causes and fixes

- Remote sprint previously compared displacement in one rendered frame against a speed threshold. NetworkTransform interpolation can pause or catch up between snapshots, so continuous sprinting repeatedly toggled the Animator back to walking. A two-process build reproduced sprint being active in only 81/121 samples on the host and 79/121 on the joining client. PlayerMovement now sends the owner's walking/sprinting choice when it changes, through an authority-required Command and server SyncVars. Local animation remains immediate; remote and newly spawned observers receive the same locomotion state. Downed/dead state still overrides locomotion.
- The controller only entered Dying when IsDowned was true. PlayerHealth.ServerKill skips downing, sets IsDead, and clears IsDowned, so both local and remote characters stayed outside Dying after an instant kill. The controller now also enters Dying when IsDead is true. Neither transition restarts Dying while already in it; the existing revival transition still requires both flags to be false.

The initial build diagnostics confirmed all four clips were included and moved the humanoid skeleton. No import, Animator hierarchy, GhostManager, or graphics setting changes were needed. These are gameplay-state bugs exposed during built multiplayer play, rather than missing animation assets.

## Sprint grounding

Sampling the authored Running clip showed the skinned mesh dipping roughly 20 cm below the player's capsule bottom. The existing hip stabilization only removed sideways drift. PlayerMovement now lifts the visual hips when the foot/toe support points would sink below the capsule, with an 8 cm shoe allowance measured against this model's retargeted toe-off poses. Feet already above the support plane keep their upward motion. The correction follows the actual Running state (including transitions) and skips downed/dead players. It never moves the physics body or camera.

The two-player build probe also samples the baked mesh after LateUpdate, independently checking that both local and remote running models stay above their capsule floor. Existing running, stopping, downing, revival and death checks remain in the suite.
