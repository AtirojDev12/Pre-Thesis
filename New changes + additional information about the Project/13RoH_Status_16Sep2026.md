# 13RoH — Status, 16 Sep 2026

Verified by scanning the project, not from memory.

---

# PART 1 — What you need to do

## 1.1 Code — one line, and it blocks everything

- [ ] **Add `MatchState.SetPendingConfig(config);` to `LobbyController.CreateRoom`**
  Put it after `Validate()` passes, before `CreateLobby`.

  There is currently no reference to `MatchState` anywhere in `LobbyController.cs`. Until this line exists, the host's difficulty choice never survives the scene load into GamePlay, and **every match runs Normal** no matter what was picked in the lobby. Every difficulty asset, every tuning decision from today, is invisible without it.

  This is the single highest-value thing on the whole list. It is one line.

## 1.2 Difficulty assets — `Assets/Dificult/`

A new field appeared today that your three assets do not have values for yet.

- [ ] **`flatExtraTasksPerZone`** — suggested Easy `0`, Normal `1`, Hard `2`
  Tasks added to every zone on that tier regardless of team size. It is the only task lever that reaches a solo player, and the only way Hard rooms are bigger rather than just more dangerous.

- [ ] **Consider `extraTasksPerAdditionalPlayer` 4 → 2** on all three
  At 4, a full lobby generates 20 random extras and the map can only hold 24 (6 zones × cap 4). Five of six zones end up loaded near the cap, so "which room is the heavy one" becomes "all of them are." Lowering to 2 keeps rooms a sane size *and* restores the inequality that makes the surprise work. `OnValidate` will now warn you when this goes flat.

- [ ] Confirm the rest reads as intended:

| | Zones | Round | Drain | Random ghosts |
|---|---|---|---|---|
| Easy | 3 of 6 | 10 min | 0.5 | 2 |
| Normal | 4 of 6 | 15 min | 1.0 | 3 |
| Hard | 5 of 6 | 20 min | 2.0 | 4 |

## 1.3 Ghost roster — `Assets/Dificult/GhostRoster_Cinema.asset`

- [x] Created, `mapID` = `demo_map_01` — correct
- [ ] `guaranteedGhosts` — still empty (blocked: no ghost prefabs exist)
- [ ] `randomGhostPool` — still empty (same block)

## 1.4 GamePlay scene — currently 8 KB: one camera, one light

- [ ] Empty GameObject + `MatchDirector` + `NetworkIdentity`; drag in all three profiles and the ghost roster
- [ ] Six `NetworkStartPosition` components
- [ ] `OfflinePlayerSpawner` — without it, MatchDirector's NetworkIdentity stays disabled after scene load when there is no host
- [ ] Three `ExitPoint` objects — ประตูหน้า / ประตูหลัง / ทางหนีไฟ
  - capacity **2** each
  - `AudioSource` with **Spatial Blend = 1** (fully 3D)
  - the **same clip** a zone plays when its task completes
- [ ] Greybox the cinema — **9 zones, every one of them has rules**:
  - **Quest + Rule (6):** จุดจำหน่ายตั๋ว & จุดขายเครื่องดื่มและขนม (one zone), ห้องแม่บ้าน, ห้องฉายหนัง, จุดตรวจตั๋ว, ห้อง Staff, ห้องไฟ
  - **Rule only (3):** ทางเดิน, ห้องน้ำ, ห้องนั่งชมภาพยนตร์
  - plus the three exits

## 1.5 Configuration

- [ ] `NetworkManager.spawnPrefabs` is still `[]`. Anything spawned at runtime that is not the player prefab will not appear on clients.

## 1.6 Tell Atiroj

- [ ] I added `public void ServerKill(string reason)` to `PlayerHealth.cs`. Additive only — nothing existing changed. It routes through the existing private `ServerDie()` so death consequences are identical however you died.
  You need it for two rules: a player still inside at 07:00, and the Jump Scare instant-death rule. `TakeDamage` can only knock someone down.

## 1.7 Systems with nothing behind them

Every one of these is a hook that exists and compiles, with no caller:

| System | Hook waiting for it | Owner |
|---|---|---|
| Quest / Task | `ServerReportTaskCompleted` + `ServerReportZoneCompleted` | ? |
| Zone rules (the 13) | — nothing written at all | ? |
| Ghost prefabs + spawner | `BuildGhostRoster()` | ? |
| Ritual (Method 2) | `ServerReportGhostBanished()` — must check `RitualPermitted` first | ? |
| Sanity | `SanityDrainMultiplier` | ? |
| End-of-round payout | `PlayerOutcome` + `TasksCompletedBy()` | ? |
| Room browser UI | — | ? |
| Voice chat | — | **You** |

---

# PART 2 — What to discuss with the team

## 2.1 Blocking — code cannot proceed without these

**A. Zone task lists.** No number anywhere means anything until each of the six quest zones has its actual task list written down. Every estimate in today's discussion rested on a guess of "about 4–5 tasks per zone." Design needs to write the real lists.

**B. Are the random extras spread across all six zones, or only the ones the team engages?**
I assumed **all six at round start**. That means some extras land in rooms nobody enters, so the real workload varies run to run. The alternative is more consistent but requires the game to know what "chosen" means, which is murky. This decides how the quest system builds a round.

**C. Does a partly-finished zone count for anything?**
Implemented as **no** — 4 of 7 tasks in ห้องไฟ is worth zero toward survival, though those 4 tasks still pay money. That makes abandoning a bloated room a real decision: sunk cost vs. cut losses with three minutes left. Confirm this is what you want.

**D. Owners for every system in the table in 1.7.**

## 2.2 Consequences of today's rules that the team may not have realised

**E. Missing the minimum is total party death with zero payout.**
The gates need both 06:00 *and* the minimum. The ritual needs the minimum too. So a team that falls short cannot leave, cannot banish, and everyone dies at 07:00 — and death means no money and lost items. One zone short = the entire run is worth nothing to all six players.

That is a coherent, brutal design and it is what makes the minimum frightening. But make sure the team has heard it out loud before a playtest, not after.

**F. The sealed-in team has 150 seconds of nothing.**
They are dead, they know it, and mechanically there is nothing to do but wait. If dawn is the punishment it probably wants teeth — e.g. the ghosts turn aggressive at 06:00 for a team that failed, so it arrives as a hunt rather than a countdown.

**G. Early deaths make the round harder in a way nobody can see.**
The zone requirement is flat (good — it no longer scales with headcount), but the *extras* are calculated once at round start from the player count. If four of six die at minute three, the surviving two still face zones inflated for a six-player team. Leave it, or recompute? Recomputing rewards losing people.

**H. Anyone still inside at 07:00 dies.**
Implemented. Confirm — the alternative is losing items but surviving.

## 2.3 Balance — needs playtests, but decide the direction now

**I. Money per task vs. the risk of staying inside.**
This alone decides whether the time after the minimum is real greed or an obvious hide-and-wait. No amount of tuning elsewhere fixes it.

**J. Ritual bonus size.** Too large and every team rituals; too small and nobody does.

**K. Solo Hard.** `flatExtraTasksPerZone` is a *solo-weighted* lever — every +1 costs a solo player 5 tasks (one per required zone) but costs a six-player team the same 5 split six ways. Hard's solo/group gap widens fast. First thing to check in a solo playtest.

**L. Can a team tell which room is heavy before committing?**
On Hard you skip exactly one zone of six, so "which one do we abandon" is the whole strategy — but only if scouting is cheap enough to be worth doing. That is a level-layout question for whoever greyboxes the cinema, not a number in an asset.

## 2.4 Documentation gaps

**M. The 13 rules are still unwritten.** All nine zones need them. It is the first pillar and the title of the game.

**N. The Game Play Loop section in the GDD is still an empty heading.**

**O. The GDD does not describe today's decisions.** The zone-based minimum, the both-conditions gate, the ritual requiring the minimum, and the escape window all need writing up before the next panel.

---

# Reference — the rules as they now stand in code

- Night is always **00:00 → 06:00**, six in-game hours, every map, every difficulty
- Round length sets the clock speed: 10 / 15 / 20 min → 100 / 150 / 200 s per in-game hour
- A downed player survives **1 in-game hour** — same at every tier
- The escape window is **1 in-game hour**, deliberately identical to the downed timer
- Exits open at 06:00 **and only** at 06:00
- Crossing a gate needs 06:00 **and** the zone minimum
- Method 2 (ritual) needs the zone minimum too — otherwise players skip the tasks, hide, and win on the ghost
- Zones = survival. Tasks = money, counted **per player**
- Three exits, two people each, six players max — zero slack, the team must split
- No HUD shows which exits are full; a door plays the zone-completion sound when it fills
- The round ends the moment no living player is left inside
