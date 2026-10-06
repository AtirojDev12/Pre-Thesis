# Audio

## MainMenu (configured)

`AudioManager` is created before the first scene and persists across scene changes.
The library is `Assets/Resources/Audio/SoundLibrary.asset`. Do not add another
manager to each scene. A manually placed duplicate destroys itself.

All 26 MainMenu buttons have `UISoundEmitter` with `UI_Click` and `UI_Hover`.
Hover fires on pointer entry; disabled buttons are silent. The menu builder also
adds the component to newly generated buttons. UI uses 2D SFX sources and may
finish playing after a scene transition.

The ESC pause menu also ensures every child button, including inactive settings
pages and leave confirmation, has one `UISoundEmitter` at startup. It uses the
same `UI_Click` sound, with hover disabled on these buttons, without rebuilding
the existing prefab. MainMenu buttons keep `UI_Hover` enabled.

The existing `AdvancedLightLoop` in MainMenu controls its configured light group:

- `LongRun` loops on a separate, scene-owned 2D Ambient source.
- Every blink uses U1, U2, U3 in that order, on pooled 2D SFX sources.
- Spark clips are scheduled 50 ms ahead on the DSP clock. `hitOffsets` compensate
  for the clips' initial silence (approximately 67, 73, 53 ms).
- At each hit the light turns off and the hum's base volume becomes zero. The
  hum keeps advancing silently, then becomes audible when the light turns on.
- Blink duration is 0.1 seconds; the pause between rounds remains 4 seconds.
- Sources stop on scene exit. Re-enabling the component restarts one routine.
- Imported menu clips preload their data to avoid first-use loading delays.

The hit offsets are estimated from waveform onset; they remain editable in the
Inspector for final listening adjustment. Graphics follow the DSP clock at frame
resolution, not audio sample resolution.

## New effects

Add an entry to SoundLibrary with a unique ID, clips, category, base volume and
3D distances. UI uses `AudioManager.Instance.PlayUI(id)`. For scene-owned sounds,
call `Play(id, position, gameObject.scene, clipIndex)`. Passing an invalid scene
is reserved for sounds intended to survive a scene transition.

`PlayClip` is also available for components with explicitly assigned clips.
The pool is capped at 24 voices; when full it replaces the oldest world effect
before replacing UI. Category changes and fades use `SetBaseVolume`, never a
direct write to `AudioSource.volume`.

## Multiplayer

`RoHRoomManager` registers/unregisters the `WorldSoundMessage` receiver as the
client session starts/stops. From an already validated server gameplay action:

```csharp
NetworkAudioRelay.Play("Door_Open", transform.position, clipIndex: 0);
```

Create `Door_Open` in the library first. This is a server-to-client event only;
there is no generic client command to request arbitrary sounds. Existing
interaction Commands remain responsible for ownership, range and rate checks.
The host uses the same receive path as guests, so do not also play locally on
the server. One-shot sounds are not replayed to late joiners. The relay works
offline, too. Messages use reliable delivery to ready connections; spatial
attenuation is local. Distance-based network filtering can be added if needed.

For persistent world loops, attach `NetworkSoundEmitter` to an object with a
NetworkIdentity, set its library ID, and call `SetPlaying` from the server.
The loop follows that object's transform. Its replicated start time and playing
state restore playback for new observers. Do not send an additional loop RPC.

Footsteps keep their existing immediate owner playback and validated Mirror
replication. Voice chat also retains its independent playback pipeline.

## Popcorn zone in Cinema_GamePlay

The existing station bindings supply all audio positions; no additional scene
NetworkIdentity or runtime NetworkBehaviour is added. All seven new library
entries are 3D SFX, audible from 2 to 15 metres, with preloaded clips:

| Interaction | Sound |
| --- | --- |
| Successful bucket/cup pickup | `pickupPOPcorn1` |
| Successful Cheese/BBQ/Paprika/Ghost Flavor selection | `pickUPitem2` |
| Accepted drink hold, including all eight dispenser controls | `ClickDispenserWaterStart`, then looping `Pouring_water` |
| Drink hold release, cancellation or completion | `ClickDispenserWaterEnd` |
| Accepted bucket scoop from tank | Looping `PopCornScoopong` |
| Accepted maker hold | One-shot `PopCornISMaking`, stopped if work is cancelled |

Controls remain hold E. Online water now uses the same server acknowledgement
and timed completion flow as tank/maker work. Container pickup and flavor state
remain owner-driven as before; their sound requests validate range, health,
station, held-item revision and expected resulting item state on the server.
Those sounds are never broadcast directly by an arbitrary client sound ID.

The existing `PopcornNetSync` scene identity replicates active work through a
SyncDictionary keyed by player netId. Each client has separate local emitters
per actor, so cancelling one user cannot silence another. The owner stops audio
immediately on release or when the local hold finishes; server removal stops
other observers and cannot replay the owner's end click. Joining observers
restore the current loop position without replaying the initial click. Server
cleanup removes work on disconnect, death/downing, out-of-range movement,
invalid held items, or a six-second safety timeout. Request/revision checks
and short server cooldowns reject duplicate or rapid sound requests.

Tank refill is an immediate stock operation and does not play the bucket-scoop
sound. Station audio is distinct from the shared 24-voice one-shot pool. MainMenu,
footsteps, voice, order scoring and the authored tank capacity are unchanged.

## Ticket counter clicks

Accepted movie selections use `Ticket_MovieClick` (`Click1.wav`) at the movie
panel. Accepted human/ghost ticket submissions use `Ticket_SubmitClick`
(`Click3.wav`) at the corresponding sell button, for both correct and incorrect
orders. Both are preloaded 3D SFX with 2–15 metre attenuation, sent once through
the authoritative world-sound relay. Invalid requests and duplicate sales are
silent; restoring the shared selection does not replay a click.

## Light switches


`LightSwitchInteractable` plays `LightSwitch_On` / `LightSwitch_Off` after an
accepted interaction changes the light state. They use the existing
`LIghtSwichOn.wav` / `LightSwichOff.wav` clips as preloaded 3D SFX at the switch,
with 2–15 metre attenuation. The authoritative interaction sends one world-sound
event, so the host and guests use the same playback path. Ghost flickers and
initial state restoration do not play switch clicks.

## Room ambience configuration


Add one `AmbientController` to the scene and assign its default clip and zones.
For each room add `AmbientZone` with a trigger BoxCollider, clip, volume and
priority. The controller selects the highest-priority zone containing the local
enabled AudioListener, including when the listener starts inside a room. It
crossfades in unscaled time and falls back to the default outside all zones.
Zone transforms may be rotated/scaled. These components are ready for scene
configuration; no gameplay room layout or ambient clips have been assigned.

## Verification in Unity

1. Open MainMenu, hover and click buttons; verify one sound per event and silence
   on non-interactable buttons. Hovering without leaving must not repeat.
2. Watch several light rounds: U1/U2/U3 align with three off transitions, the
   hum is inaudible while off, and resumes without starting the clip again.
3. Adjust SFX and Ambient separately; they should independently control sparks/
   buttons and lamp hum. Master affects both.
4. Change scenes and return: no old hum/spark survives and only one AudioSystem
   exists. A click triggering a transition may finish.
5. Start a host and a guest; invoke a configured world effect from server
   gameplay. Both hear it once. A client-only call must not broadcast.
6. Test a network loop with a new observer: it starts from its elapsed loop
   position. Stopping on the server stops all observers.
7. Configure two ambient zones, cross their boundary, and verify that remote
   players do not select the local listener's room.

Direct compiler validation does not replace Unity's Mirror weaving or the above
listening/multiplayer checks.
