# Popcorn orders and held items

Run `Run-PopcornChecks.ps1 -UnityPath <Unity.exe>` in PowerShell. It opens an isolated project under `.utmp/popcorn-regression`, using the authored scene and prefabs. Results and UI captures are written there; the user's scene is not edited.

Orders retain the seven products, flavor tasks and Ghost Flavor requirements. Each customer independently has a 70% chance of one product and a 30% chance of two. Any pair is allowed. Duplicate products display ×2 and quantity progress. Correct deliveries earn existing task and score credit per item and update the same NPC. A wrong prepared delivery consumes the item and ends the order; existing ghost punishment and earlier earned credit remain.

The suite checks preparation, stock, recovery, 20,000 generated orders, all 49 ordered pairs, duplicates, partial completion, wrong delivery, stale/replayed requests, Mirror snapshots, real host delivery acknowledgements, and task/refill/radio chest visuals. Radio pickup and throwing have a separate suite in `Tests/Throwing`.

Current limits: the player still prepares one task item at a time; a radio can be displayed alongside it. Existing preparation remains owner-driven, with recipe consistency and delivery outcomes checked by the server. This is not a fully server-owned preparation inventory. Network tests use a local Mirror host and serialized observer snapshots; a two-machine EOS session remains a manual check. All builds must use the same Player prefab and network scripts.

Station audio checks cover server-approved scoop and water work, active sound
snapshots for joining observers, cancellation/completion cleanup, invalid cup
requests and all seven spatial SFX library bindings. Water completion now waits
for server validation just like tank/maker work. The stock fixtures use the
scene's configured capacity rather than assuming 20, and reacquire the host
camera after replacing the offline player. Batch checks do not verify audible
output or perceived timing; listen in Play Mode and with a second client.
