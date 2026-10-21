# Quest-aware customer orders

The default is a 30% chance to select a menu/customer pair that advances an
unfinished task, with 70% ordinary random selection. Completed tasks are excluded
using tonight's difficulty-adjusted targets. Matching pairs are equally likely;
overlapping task lines do not add extra weight to the same pair.

Popcorn and drinks bias only the first item; the second item stays random and
the two-item probability remains unchanged. Tickets bias the movie and customer
together. When no eligible tasks exist, ordinary selection is preserved.
`unfinishedQuestChance` on PopcornNetSync and TicketMinigame is adjustable in the
Inspector. The no-sync food fallback uses the same 30% default.

For example, one remaining food quest for a specific menu and ghost customer
changes that pair's first-item probability from 1/14 to 0.30 + 0.70/14 = 35%.
This is a mixture of selection methods, not a 30 percentage-point increase for
each unfinished quest.

## Verification

Copy `QuestOrderChecks.cs` into `Assets/Editor` in an isolated copy of the Unity
project. Run Unity in batch mode with `-executeMethod QuestOrderChecks.Run`.
Use a project path without dot-prefixed directory names. Unity must have access
to its licensing service. The runner exits with a nonzero code on failure and
writes `quest-order-results.txt` in the isolated project.

Verified on Unity 6000.5.7f1: all 13 checks passed, including 40,000-sample
distribution checks, customer restrictions, difficulty targets, completed tasks,
ticket selection, wildcard flavors, no boards, and two-item frequency.
The specific ghost/menu pair occurred in 35.14% of the 30%-bias sample.
