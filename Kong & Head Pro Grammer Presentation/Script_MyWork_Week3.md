# Speaking Script — Personal Work Presentation, Week 3
**Pre-Thesis Demo · Mr. Kongkidakorn Chuayjang**

Total run time: **about 10 minutes** (add 2–3 more if you play the videos).
Lines marked ▸ are stage directions — do not read them aloud.

---

## SLIDE 1 — Title (35 seconds)

Good morning. I'm Kongkidakorn Chuayjang, head programmer on the Pre-Thesis Demo.

The previous presentation covered the whole project. This one covers only my own work.

Before I start, one commitment: **everything I claim in this presentation is traceable to a commit in our repository.** If you want to check any of it, you can.

▸ *Go straight to the next slide. Don't linger here.*

---

## SLIDE 2 — What is mine, and what is not (70 seconds)

▸ *This is the most important slide in the deck. Take your time. Do not rush it.*

There are two programmers on this project, so before I claim anything, I want to draw the line clearly.

▸ *Point at the left column.*

**On the left is work I wrote myself.** The encrypted save system. Room creation and matchmaking. The room browser data layer. The offline-and-online mode switch. Scene persistence, editor tooling, and the persistent HUD.

▸ *Point at the right column — go there next, not to the middle.*

**On the right is Atiroj's work, and I am not claiming any of it.** He designed and wrote the original single-player versions of our gameplay systems. He wrote the downed-and-revive feature — that's roughly a hundred and eighty lines, added a few days ago. He set up the gameplay props in our test scenes.

▸ *Now the middle.*

**The middle column is shared, and I want to be precise about it.** Atiroj designed and wrote these systems — interaction, health and damage, player movement, the camera, the health bar. They were his. What I did was rewrite them to run under server authority so they work for six players instead of one.

So: the feature is his. The networking model it runs on is mine. I'll come back to that in a few slides.

The evidence for this split is simple — my own commits, versus the three merges that brought Atiroj's work into my branch.

---

## SLIDE 3 — Encrypted save system (65 seconds)

My first piece of work, and it is entirely mine.

The save system writes player progression to the machine as AES-256 encrypted JSON. You cannot open it in a text editor and change your currency.

It loads automatically before the first scene runs, so no other system has to ask whether a save file exists — it is simply always there. There is exactly one source of truth in memory, so no component keeps its own copy and drifts out of sync.

Permanent items are capped at one copy each and removed on drop, trade or death. And I built a custom Inspector so we can watch the live save contents during Play, which means progression bugs are visible without attaching a debugger.

▸ *Point at the right-hand card.*

One decision I'd like to defend. The encryption key is not stored as a single string. It's split into four byte arrays and assembled at runtime, so there is no one string sitting in the binary for someone to find.

I want to be honest about what that achieves. Someone determined, with the binary in front of them, still wins — that is true of any save stored on the player's own machine. The achievable goal is narrower: make casual save editing not worth the effort. Casual editing is what actually damages a co-op game's economy.

---

## SLIDE 4 — Room creation and matchmaking (70 seconds)

This is the second piece, also entirely mine.

▸ *Point at the screenshot.*

That is from my own playtest recording. Create Room opens a lobby on Epic's service and starts hosting. Find Room searches and returns every open room.

Three things I'd like to draw out.

**First — a room is a data contract, not a button.** The map, the difficulty, the player limit and the privacy setting live in one validated structure. That means the interface physically cannot create a room that the joining code is unable to read. When we build the real lobby screen, it plugs into this.

**Second — the browser knows things before you join.** Each room advertises its map, its difficulty, whether it's locked, and a live player count like two of six. A player picks a room. They don't gamble on one and find out afterwards.

**Third — passwords are never stored as a room attribute.** I found this by reading Epic's lobby API rather than assuming. An attribute marked private is still readable by anyone who searches for that lobby. So a password stored there is effectively public. We verify it after joining instead. That's a security decision I made from reading the documentation, not from guessing.

---

## SLIDE 5 — Making Atiroj's systems multiplayer-safe (75 seconds)

▸ *Say the first sentence clearly. This is the slide where credit matters most.*

I did not invent these features. Atiroj did. He wrote the interaction and health systems as single-player code, which is the correct way to write them first. My job was the rewrite that makes them survive six players and a dishonest client.

Here is what changed.

**Interaction.** Before, pressing E ran the door's code directly on whichever machine pressed the key. Now the client only asks the server *which* object it wants, and the server re-checks the distance against its own view of the world before opening anything.

**Health and damage.** Before, a collider applied damage locally, so every machine saw a different health value for the same player. Now only the server applies damage, and health syncs outward. Everyone sees the same number.

**Player control.** Before, every copy of the player prefab read the keyboard — so one key press moved all six players at once. Now only the player you own reads your input. The rest are driven by the network.

**Late joiners.** Before, someone joining after a door had opened would see it closed. Now state is re-applied on connect, so the world is consistent for someone arriving mid-run.

▸ *Read the bottom line as written.*

Credit where it is due: the features are Atiroj's. The networking model they now run on is mine.

---

## SLIDE 6 — Letting the team test without hosting a match (60 seconds)

This piece is mine, and it exists to unblock my teammate rather than to add a feature.

Mirror switches off every scene object that carries a network identity, so the server controls when each one appears. That is correct behaviour in a real match.

But it meant that when Atiroj pressed Play in a test scene to check a lever, every prop in the scene vanished. He could not test his own work unless I hosted a match for him.

▸ *Point at the screenshot.*

So I built three things. One offline check that every system consults, so gameplay code is written once and behaves correctly whether or not a server is running. A spawner that places a test player and restores the objects Mirror disabled — and that does nothing at all once a match is running. And persistence helpers so the Epic manager and the HUD survive a scene change instead of being destroyed mid-match.

▸ *Say the honest note. It makes the rest more credible, not less.*

An honest note: this took me three attempts. My first two assumed Mirror disables those objects before Awake runs. It does not — it happens afterwards, so my fix ran too early and silently did nothing. That is the kind of mistake you only find by reading the library instead of trusting your assumption.

---

## SLIDE 7 — Playtest evidence (60 seconds, plus video time)

This is a real two-machine test I ran on the twelfth of September.

▸ *Point left.*

On the left, the scene hierarchy shows two Player Clone objects — the host and the client that joined from the other machine, both in one scene.

▸ *Point centre.*

In the middle, the game view from my client. That is the other player, and in the recording they are moving.

▸ *Point right.*

On the right, the connection log. The client resolved the host's Epic user ID and connected through it. No IP address was involved anywhere.

▸ *If you have time, play the three recordings now — bottom card lists them in order. If you are running short, play only the third one, 15-35-42, which is the join.*

I have three recordings with me: the offline sandbox test, creating a room and starting the host, and the second client finding the room and joining it.

---

## SLIDE 8 — Bugs I found by reading, not guessing (70 seconds)

Five problems I diagnosed. I want to show these because each one had a plausible wrong explanation that would have cost days.

**The build would not compile.** Two Epic SDKs were installed at once, colliding on the same native library. I removed the one we were not using — zero errors, zero warnings.

**A client joined into an empty world.** A server can create a player from a prefab it references directly. A client can only create one that has been registered. Our two machines disagreed about that, and I traced it to a checkout mismatch.

**Props disappeared on Play** — the Awake timing problem I just described.

**Rooms were rejected with an invalid-user error.** Epic's login is asynchronous, so the button could be pressed before login finished and the request went out with no identity.

**Damage did nothing.** The detection relied on a tag that the spawned prefab did not carry — only the old hand-placed player had it. I changed it to detect by component instead.

▸ *Optional closing line if the room is engaged.*

None of these were solved by trying things until something worked. Each one came from opening the library's source and reading what it actually does.

---

## SLIDE 9 — What I am doing next (50 seconds)

Four things.

**The room browser interface.** The search already returns every open room with its map, difficulty, lock state and player count. Nothing draws it on screen yet. That is my next build.

**Move the match into a real scene.** The online test currently runs in the menu scene with a flat plane. Next is a proper level load with players spawning into it.

**Private-room passwords.** The design is settled — verified after joining, never advertised. The implementation is mine.

**Start the 13 Rules system.** This is the design thesis of the game and it is still unstarted. I want it under way before it becomes the thing we ran out of time for.

I'll report against this exact list in next week's presentation.

---

## SLIDE 10 — Everything I have built so far (60 seconds)

To close, my whole contribution to date.

**Written from scratch:** the encrypted save system with auto-load. Permanent items capped and lost on death. The custom Inspector. Room creation on Epic and room search. The room data contract. The offline-and-online mode switch with scene persistence. Editor tooling.

**Rewritten for multiplayer:** interaction, health and damage, player control, camera, late joiners, and the health bar — all moved onto server authority.

**Verified working:** two machines in one session over Epic. Both players spawning, moving, and seeing each other. A room created with its full configuration carried across. Solo sandbox scenes running with no server. And a build with zero errors and zero warnings.

▸ *Point at the numbers, then deliver the last paragraph slowly.*

About one thousand three hundred lines written from scratch, and about one thousand four hundred rewritten for multiplayer.

I'd argue the second number is the harder half. Writing a new system is bounded work. Changing someone else's working code so it behaves identically for one player and correctly for six, without breaking what he built — that is where most of my time actually went.

None of my work is visible in a screenshot of the game. All of it is the reason there is a game to screenshot.

Thank you.

---

# Q&A preparation

**"How much of the gameplay code is really yours?"**
Be precise and don't overclaim: *"The features are Atiroj's. I rewrote about fourteen hundred lines of them to run under server authority. Slide two separates the two."* Then offer to show the repository history.

**"Why does the rewrite count as your work?"**
Because single-player code and networked code solve different problems. A single-player lever just opens. A networked lever has to decide who is allowed to open it, prove the player was close enough, tell five other machines, and be correct for someone who joins afterwards.

**"Isn't client-side encryption pointless?"**
Agree with the premise, then narrow the claim: any save on the player's machine can be broken by someone determined. The goal is to stop casual editing, which is what actually breaks a co-op economy.

**"What have you done in the last week specifically?"**
Got the two-machine session working, fixed the vanishing scene objects, fixed the damage trigger, and removed the duplicate Epic SDK that was blocking builds.

**"What are you worried about?"**
Answer honestly: the 13 Rules system hasn't started, and it is the part this project will be judged on. That's why it's first on my next-week list.

---

# Timing cheat sheet

| Slide | Topic | Target |
|---|---|---|
| 1 | Title | 0:35 |
| 2 | **What is mine / not mine** | 1:10 |
| 3 | Save system | 1:05 |
| 4 | Rooms & matchmaking | 1:10 |
| 5 | The multiplayer rewrite | 1:15 |
| 6 | Offline infrastructure | 1:00 |
| 7 | Playtest evidence | 1:00 (+ video) |
| 8 | Bugs I root-caused | 1:10 |
| 9 | What's next | 0:50 |
| 10 | Full summary | 1:00 |
| | **Total** | **≈ 10:15** |

**If you only have 5 minutes:** keep slides 1, 2, 5, 7, 10. Slide 2 is not optional — it is the slide that protects you from being accused of claiming Atiroj's work.

**Two delivery notes.** Slide 2 and slide 5 are the credibility of the whole presentation; deliver both slowly and say the attribution lines exactly as written. And keep the honest notes on slides 3, 6 and 9 — admitting the three failed attempts and the unstarted rule system makes everything else you claim more believable, not less.
