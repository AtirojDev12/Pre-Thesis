# Quest guarantees and choice customers

The old 30% unfinished-quest selection bias has been removed. Ordinary customers
use normal random menus/movies and customer types. Normal food customers retain
the 30% two-item probability; the second item remains random.

Each counter owns a `QuestOrderDirector`, used only by the server or offline
authority. Five successfully completed customers with no relevant task progress
force the next customer's first item/movie and type to match an unfinished task.
The task waiting longest for an opportunity takes priority, including unseen tasks.
An ordinary random matching order also records an opportunity. Completed tasks
are excluded using difficulty-adjusted targets. Chores do not affect sales stages.
The food and ticket counters count independently. A relevant accepted item resets
the drought immediately, including the first delivery of a two-item order. Two
items still count as one completed customer. Wrong submissions do not increment
either guarantee; a wrong guaranteed order spends its opportunity and resets the
quest drought. The guarantee repeats until completion; it never awards progress.

Food customers can also let the player choose any one prepared item. Human choice
customers require no ghost mix; ghost choice customers require ghost mix. Credit
uses the actual submitted flavor and customer type, with existing score/damage.
The atomic replicated order contains `chooseAny`; choice customers always request
one item. The display explicitly says `YOUR CHOICE: ANY 1 PREPARED ITEM`.

Combined choice probabilities (human/ghost equally likely outside a quest guarantee):

| Unfinished food task lines | Combined chance | Human / ghost |
| --- | --- | --- |
| More than one, none, or no board | 2% | 1% / 1% |
| One, with more than one sale remaining | 10% | 5% / 5% |
| One, with one sale remaining | 30% | 15% / 15% |

The final-item stage starts a separate counter: after ten successful normal
customers in that stage, the next is a choice customer. Entering/exiting the
stage clears that counter; a choice appearance resets it even if served wrongly.
The customer that enters the stage is not counted retroactively. A simultaneous
quest guarantee selects the matching customer type but leaves the item to the
player. After all food tasks finish, the base 2% choice chance remains.

No choice customer variant has been added to tickets. Their quest guarantee
matches both movie and customer type. Tasks for unavailable menu/movie choices
are excluded from generation rather than creating impossible orders.

## Verification

Run `Tests/Orders/Run-QuestOrderChecks.ps1` (optionally pass `-UnityPath`).
It copies the project to the ignored `.utmp/quest-orders/Project` directory.

Copy `QuestOrderChecks.cs` into `Assets/Editor` in an isolated copy of the Unity
project. Run Unity in batch mode with `-executeMethod QuestOrderChecks.Run`.
Unity must have access to its licensing service. The runner exits with a nonzero code on failure and
writes `quest-order-results.txt` in the isolated project.

The runner covers 40,000-sample distributions, removal of the old bias, all choice
recipes and seasoning restrictions, Mirror order serialization, fifth/sixth and
tenth/eleventh boundaries, partial deliveries, wrong submissions, repeated
guarantees, oldest-opportunity selection, difficulty targets, wildcard tasks,
completed tasks, no boards and independent ticket selection.
`QuestCustomerPlayChecks.Run` additionally exercises choice submissions through
the real authoritative serving resolver, duplicate requests, actual-flavor
credit, real sixth-customer spawning and two-item customer counting in Cinema.

Validated with Unity 6000.5.7f1: 95 quest checks, 20 focused serving checks and
229 existing ticket regression checks passed. The broader popcorn regression
run stopped at its simulated E/bucket pickup check before reaching serving;
that suite is not a passing result for this change.

## Separate-process network checks

Run `Tests/Orders/Run-QuestNetworkChecks.ps1`. It builds an isolated development
player, starts a host and a separate KCP client on loopback port 17996, then
starts a second client during a ghost choice order. The test manager uses the
real game's player and registered spawn prefabs. Each role writes its own
results and log in a timestamped run directory. The scripted fixture sets
deterministic orders, but all serving requests, held-item publications, order
snapshots, damage, score and task progress travel through real Mirror packets.

Validated against the pulled Cinema scene: 15 host, 43 remote-client and 6
late-observer checks passed. These cover human/ghost choice instructions and
matching, actual-flavor credit, wrong seasoning and damage, duplicate request
rejection, partial orders, late-join choice and task snapshots, later partial
deliveries, five real irrelevant remote sales and the sixth guaranteed spawn.
The remote client cannot replace orders or advance its own guarantee counter.
The harness fails on runtime exceptions and unregistered spawn errors.

This is three independent game processes on one computer. Internet routing,
latency/packet-loss simulation, and full interactive item preparation remain
outside this focused serving test. A client-loading null identity in
`ProjectorAudio` discovered during this run was fixed; its separate 36-check
regression suite also passed.
