# Ticket counter

Open `Assets/Scenes/Z1_EnemyTest.unity` and select `TicketSell`.
First select a movie on the panel attached to the inside of `TicketSell/Cube (1)` (aim and press E, or click with the unlocked cursor).
Then aim at `Cube (11)` and press E for a human ticket; aim at `Cube (14)` and press E for a ghost ticket.
Each human or ghost randomly requests a movie from the Inspector's Movies list. Their preference appears in the request bubble only at the counter.
The selected movie is shared at the counter and cleared after every sale. Both movie and human/ghost ticket type must match to earn points.
Only a customer waiting at the counter accepts a sale. Correct tickets add points to the ticket display.
An incorrect movie or ticket type costs nothing for humans; for ghosts it damages the serving player once through PlayerHealth (including its usual invincibility/downed rules).
Every resolved customer leaves, and another arrives after the configured delay.

The Ticket Minigame component exposes speed, spawn delay, ghost probability, points, damage, speech and bubble offset.
Edit the Movies list on this component to replace the default Movie 1 / Movie 2 / Movie 3 titles.
Move or rotate `Cube (1)/Movie Selection UI Anchor` to reposition the wall panel.
Under `Ticket Customer Route`, move Spawn, Approach, Counter, Departure and Exit in the Scene view.
Route positions are the capsule centre (normally Y = 1 on a floor at Y = 0).
Rotate Counter to change the waiting customer's facing direction. Add transforms to Approach Path / Departure Path for more bends.
Select TicketSell to see route gizmos. Move/rotate Ticket Score Anchor to place the score display.

Ticket Network State must stay on its own child object; Mirror disables scene identities in offline mode.
Online customers and points are server-controlled. Commands validate the sending player, distance, waiting state and customer round.
Ticket points are independent of the popcorn score and do not change the existing zone quest target.

Run `Tests/Tickets/Run-TicketChecks.ps1` with the project's Unity version. Checks run in an isolated copy under `.utmp`.
