# Teammate revival playtest

The existing PlayerInteractor carries the revival implementation; no new player prefab component is needed. Tune Teammate revival on PlayerInteractor: marker distance 18 m, revive range 2.5 m, hold duration 5 seconds, prompt size 38 pixels. Hold the configured interaction key (E by default).

Run with a host and a separate client, then swap their roles:

1. Down a teammate using server-side damage. Look directly at the character. Beyond 18 m there should be no marker; approaching should smoothly fade and enlarge the circle. At 2.5 m it should show E anchored near the animated hips.
2. Look away or place a wall between the players: the marker should disappear. A healthy or dead player must have no marker.
3. Hold E in range for five seconds. The progress bar should fill, the teammate should stand and regain movement with 30% maximum health, and their DOWNED panel should close.
4. Release E, look away, open the pause menu, move out of range, or down/kill the rescuer before completion. Progress should reset (network heartbeat timeout is at most 0.4 seconds for interrupted requests). Restarting must require a fresh five seconds.
5. Let the downed timer expire while reviving. Revival must fail once the teammate dies. Two rescuers must not combine progress or resurrect a dead teammate.
6. Check ordinary doors, ticket buttons, and popcorn interactions still respond to E without a downed target in view.

Server validation checks both players' health, range, occlusion, continuous heartbeat and elapsed time. The server owns progress; clients cannot submit completion. The existing player downed countdown remains unchanged.
