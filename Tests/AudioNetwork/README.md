# Audio network checks

Run `Run-AudioNetworkChecks.ps1` with Unity 6000.5.7f1. The runner copies the
project to `.utmp/audio-network/Project` and never launches the user's checkout.

Checks cover switch/ticket clicks and both order-result sound IDs, Mirror message serialization
and reliable batching, exactly one host delivery, ready guest delivery, no
delivery to loading clients, no replay on joining, rejected client-only sends,
session shutdown, receiver registration/cleanup, local-only pause/settings
clicks, and hover disabled only in the in-game menu. Order-result clips must resolve
from the sound library as spatial SFX with a 1 metre minimum and 5 metre maximum range.
These relay checks cover ticket order audio; popcorn order results use the scene
identity's reliable ClientRpc after the seller's feedback TargetRpc.

Host delivery uses Mirror's local connection. Guest delivery uses capture
connections and real Mirror serialized packets. This is not a separate remote
game process and does not test audible playback, latency or room acoustics.
Listen once in a host/guest play session before release.
