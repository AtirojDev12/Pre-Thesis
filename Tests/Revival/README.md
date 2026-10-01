# Teammate revival playtest

PlayerInteractor: revive range 2.5 m (root-to-root distance), horizontal facing half-angle 60 degrees, hold duration 5 seconds, prompt size 38 pixels. Hold E by default. The closest downed teammate is selected automatically; selection locks while holding E and releases when E is released.

Run with a host and separate client, then swap roles:

1. Approach a downed teammate from any direction. Within 2.5 m, a fixed bottom-center hint appears without aiming at a collider. Outside range it disappears.
2. Face away: a left/right turn hint appears, E is unavailable, and holding E cannot revive. Turn within 60 degrees of the teammate: E appears. Looking up/down must not change eligibility.
3. Hold E for five seconds: progress fills and the teammate revives. Turn beyond 60 degrees, release E, leave range, open pause, or down/kill the rescuer: progress resets. Turning back requires a fresh five seconds.
4. Put a wall or prop between the bodies: the blocked-path hint appears and revival fails. Check cramped spaces and different downed animations; camera pitch must not change the obstruction check.
5. Down two teammates: the closest is selected. Start holding, then make the other closer: selection must stay locked. If the selected teammate dies or leaves range, no other teammate is revived until E is released and a new attempt starts.
6. Let the downed timer expire during revival. Two rescuers must not combine progress or resurrect a dead player.
7. Check doors, ticket buttons and popcorn still respond to E when a nearby teammate is behind the player or blocked.

The server validates health, range, horizontal facing, body-to-body obstruction, heartbeat and elapsed time. Its progress is authoritative. Verify the facing boundary on a separate client to cover replicated body rotation.
