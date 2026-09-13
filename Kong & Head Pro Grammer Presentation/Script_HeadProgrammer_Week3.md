# Speaking Script — Head Programmer Presentation, Week 3
**Pre-Thesis Demo · Presented by Mr. Kongkidakorn Chuayjang**

Total run time: **about 10 minutes.** Target times are per slide.
Lines marked ▸ are stage directions — do not read them aloud.

---

## SLIDE 1 — Title (40 seconds)

Good morning. I'm Kongkidakorn Chuayjang, head programmer on the Pre-Thesis Demo.

This is the Week 3 programming report. From now on I'll give one of these every week, so you can see the project move rather than only seeing it at the end.

The project is a one-to-six-player online co-op horror game, built in Unity 6.5, using Mirror for networking and Epic Online Services for matchmaking.

The headline for this week is short: **the game is now playable online between two machines.** That was the biggest technical risk in the whole project, and it is now behind us. I'll show you the proof in a few slides.

▸ *Pause here. Let the headline land before moving on.*

---

## SLIDE 2 — Scope (55 seconds)

Before the progress, here is what the demo actually has to prove. Three things.

**One — online co-op for one to six players.** One person hosts, up to five join. They find each other through Epic Online Services, so no one types an IP address and no one touches their router. For a horror game this matters: if joining your friends is difficult, nobody plays it twice.

**Two — the 13 Rules of Horror.** A rule system that changes what is safe to do during a run. This is the design thesis. It is the reason this project exists and not just another co-op game.

**Three — persistent progression.** Equipment and currency survive between runs, stored encrypted on the player's own machine.

▸ *Point at the line at the bottom.*

One thing I want to say clearly: the demo ships with **one map**, and that is a decision, not something we failed to finish. Multiple maps and the map pipeline are thesis work. We chose depth on one map over breadth across several.

---

## SLIDE 3 — Team (55 seconds)

There are two programmers on this project.

I'm the head programmer. I own the networking architecture, room creation and matchmaking, the save system, the offline-and-online infrastructure, and integration.

Atiroj Kemwanicha is the sub programmer, on gameplay. He owns player movement and camera, the interaction system — doors, levers, props — the health and damage model, and the downed-and-revive system.

▸ *Point at the panel at the bottom. This is the part advisors usually ask about.*

The important part is where the two meet. Atiroj writes each feature as single-player code first. Then I rewrite it to run under server authority so it works for six players.

I want to be clear that this is a **planned step in our process, not a repair**. Writing a feature single-player first is the fast way to get it right. Making it network-safe is a different job, and it is my job.

---

## SLIDE 4 — This week the game went online (50 seconds)

Three things landed this week.

**Rooms.** A host can create a room on Epic's service, and a completely separate machine can find it and join it.

**Presence.** Those two machines now share one world. Both players exist on both clients, and they can see each other move.

**Sandbox.** Solo test scenes work again without a server running, so Atiroj is not blocked on my networking to test his own features.

▸ *Point at the card at the bottom.*

What this means for the project: the networking risk is retired. This is the part that could have failed late and taken the whole demo down with it. Everything left is content and feature work, on top of a connection we have now seen work.

The next three slides are the evidence for these three claims, in order.

---

## SLIDE 5 — Evidence: rooms on Epic (70 seconds)

▸ *These are screenshots from a real playtest recording, not mockups. Say that.*

On the left is our test menu. Two buttons. Create Room opens a lobby on Epic's service and then starts the Mirror host. Find Room searches Epic and returns every open room.

On the right is the Unity console during that run. You can see the host opening a room and then the server starting and listening.

▸ *Point at the green monospace text.*

Underneath, this is the room configuration being carried into the lobby — the map, the difficulty, and the player limit. That is not decoration. Those values are published as attributes on the Epic lobby, which is what lets the room browser show you a room's map, difficulty and player count **before** you commit to joining it.

One design decision worth flagging. Passwords are deliberately **not** stored as lobby attributes. I read Epic's lobby documentation and found that an attribute marked private is still readable by anyone who searches for the lobby. So a password stored there would be public. We verify the password after joining instead.

---

## SLIDE 6 — Evidence: two machines, one world (70 seconds)

This is the result I most wanted to show you.

▸ *Point left.*

On the left is the scene hierarchy on the host machine. You can see **two** Player Clone objects. One is the host. One is the client that joined from the other machine. Both are alive in the same scene.

▸ *Point centre.*

In the middle is the game view. That capsule is the other player, and in the recording it is moving in real time.

▸ *Point right.*

On the right is the client's log. "Connection established." The client got there by resolving the host's Epic product user ID — not an IP address. That is the whole point of using Epic: two machines on different networks find each other through Epic's service.

I'll be honest about last week. This exact test failed then. The client connected, but no player objects appeared on its screen. The cause turned out to be a prefab registration mismatch between the two machines' copies of the project. The server can create a player from a prefab it holds directly; a client can only create one that has been registered. Once both machines were on the same commit, it worked.

---

## SLIDE 7 — Evidence: solo testing works again (65 seconds)

This one is less exciting to look at, but it unblocked the whole team.

Mirror deliberately switches off every object in a scene that carries a network identity. It does that so the server decides when each object appears. In a real match that is exactly right.

But in a sandbox test scene there is no server. So when Atiroj pressed Play to test a lever, every interactable object in the scene simply disappeared. He could not test his own work without me hosting a match for him.

▸ *Point at the hierarchy screenshot.*

The fix is a dedicated offline mode. When there is no server and no client running, a spawner places a test player and switches those scene objects back on. This screenshot is Play mode — you can see the props are still there.

I'll be honest that this took me three attempts. My first two assumed Mirror disables those objects before Awake runs. It does not — it happens after. So my fix ran too early and did nothing at all. The working version stops guessing at Unity's execution order and watches across several frames instead.

And in a real match this code does nothing. It is gated on there being no server and no client.

---

## SLIDE 8 — Problems that were not obvious (75 seconds)

Four problems from this period that were worth the time.

**One. The build would not compile.** We had two different Epic SDKs installed at the same time, colliding on the same native library. About a thousand warnings. Removing the one we were not using took the build to zero errors.

**Two. The client saw an empty world** — that is the one I just described.

**Three. Props vanished on Play.** Mirror disables scene objects after Awake, so an Awake-time fix cannot see them.

**Four. Room creation was rejected** with an "invalid user" error, seemingly at random. Epic's login is asynchronous. If you click Create Room before login finishes, the request goes out with no identity attached. It's now gated on login state.

▸ *Point at the line at the bottom. This is the point of the slide.*

The common thread matters more than the individual bugs. Every one of these was found by **reading the third-party library's own source code**, not by guessing and trying things. Each one had an obvious-looking wrong explanation that would have cost days.

---

## SLIDE 9 — Next week's plan (55 seconds)

For me: build the room browser interface on top of the search that already works. Move the match out of the test menu and into a real level scene. Implement the private-room password exchange. And start the 13 Rules system.

For Atiroj: extend interaction to cover the equipment the demo needs, tune the downed-and-revive timings against how it actually feels to play, and author the props and triggers for the demo map.

▸ *Point at the card at the bottom. Say this part directly — do not soften it.*

I want to flag one thing honestly. The 13 Rules of Horror is the design thesis of this project and it is still at zero lines of code. We have spent this time building infrastructure, and that infrastructure is now finished. So next week is when we stop building plumbing and start building the game the project is actually about.

---

## SLIDE 10 — Everything built so far (70 seconds)

To close, here is the whole project to date, in three groups.

**Foundations.** An encrypted local save system that loads automatically before the first scene. Permanent items capped at one copy and lost on death. A custom Inspector so we can watch save data live while playing. Editor tooling that checks our network setup for us.

**Networking.** Mirror and Epic Online Services integrated and building clean. Create a room, find rooms, join by Epic user ID with no IP and no port forwarding. A two-machine session verified working. And the offline mode so solo testing still works.

**Gameplay.** First-person movement and camera. Interaction with doors, levers and props. Health and damage. Downed and revive with a synced timer. And all of it rewritten to run under server authority.

▸ *Point at the numbers.*

Twenty-one scripts, about two thousand eight hundred lines of C sharp, five scenes, and a build with zero errors and zero warnings.

One month in, we have a working online session, a server-authoritative gameplay layer, an encrypted save system, and a clean build. What is left is the 13 Rules system, the demo map, and the room browser interface.

Thank you. I'm happy to take questions.

---

# Q&A preparation

**"Why Epic Online Services instead of Steam?"**
Epic is free, has no storefront requirement for development, and gives us lobbies and peer connections in one service. Our architecture also keeps Steam crossplay possible later — the transport layer is swappable.

**"Two thousand eight hundred lines seems small for a month."**
Line count is a poor measure for networking work. The difficulty is in the twenty lines that are correct rather than the two hundred that are subtly wrong. Four of the problems on slide eight each took longer to diagnose than to fix.

**"Show me the game."**
I'd be straight about this: right now there is a menu, a flat test plane and two capsules. There is no map and no rule system yet. That is exactly why next week is the 13 Rules and the demo level.

**"What happens if a player cheats?"**
The server decides everything that matters. A client can only ask to interact with an object; the server re-checks the distance against its own view of the world before allowing it. Damage is applied only on the server. A modified client can lie about what it wants, not about what happened.

**"What's the biggest risk left?"**
Time, not technology. The 13 Rules system has not started, and it is the part the whole project is judged on.

---

# Timing cheat sheet

| Slide | Topic | Target |
|---|---|---|
| 1 | Title | 0:40 |
| 2 | Scope | 0:55 |
| 3 | Team | 0:55 |
| 4 | Milestones | 0:50 |
| 5 | Evidence — rooms | 1:10 |
| 6 | Evidence — two clients | 1:10 |
| 7 | Evidence — offline | 1:05 |
| 8 | Hard problems | 1:15 |
| 9 | Next week | 0:55 |
| 10 | Full summary | 1:10 |
| | **Total** | **≈ 10:00** |

**If you only have 5 minutes:** keep slides 1, 4, 6, 9, 10. Skip 2, 3, 5, 7, 8 — but still say the one-line version of slide 8: *"Four hard bugs, all found by reading the library source instead of guessing."*
