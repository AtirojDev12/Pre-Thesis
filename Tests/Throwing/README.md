# Walkie-talkie throw and pickup

Only the selected walkie-talkie is throwable in this version. Popcorn, drinks,
refill supplies and Ghost Flavor retain their existing task behavior. Inventory
still allows one copy of each item type per player.

Tap Q (up to 0.2 seconds) to drop. Hold Q to charge, reaching maximum at 1.5
seconds. The left-side bar stays full until release. Charging halves walking
speed and disables sprint. Menus, focus loss, slot changes, down/death and
missing input cancel charging. No throw animation is used.

Look at the world radio and press E to pick it up. The server inserts its full
state into the first empty slot, then immediately disables the collider,
renderers and interaction and despawns the world object. Full inventory,
duplicate radio, obstruction, range and health failures leave it available.
The claim guard prevents a second grant even before destruction completes.
The on/off setting survives dropping and pickup. World radios do not play voice.
Dropping does not change permanent save ownership.

Run automated checks in a separate project:

```powershell
./Tests/Throwing/Run-ThrowChecks.ps1 -UnityPath 'C:/InstallUnity/6000.5.7f1/Editor/Unity.exe'
```

The checks cover physical prefabs, network asset IDs, state preservation,
competing pickups, full/duplicate inventory, blocked throws/pickups, charge
clamping, repeated release/loadout, actual Q input, cancellation, UI visibility,
and a real Mirror host/client lifecycle. The runner renders a UI preview.

For a separate-machine EOS playtest, buy a radio, start a match, then:

1. Drop and charge-throw from both host and remote player; verify shared motion.
2. Have a teammate without a radio pick it up. Confirm the hotbar gains exactly
   one radio and the world radio and prompt disappear on both machines.
3. Compete for one radio and repeat E rapidly. Only one player receives it.
4. Attempt pickup while already carrying a radio or with all slots full; the
   radio must remain in the world for another player.
5. Toggle radio power off, throw, pick up and verify it remains off. Radio
   transmission must stop when the item leaves the selected slot.
6. Charge while walking/sprinting, pause, change slots, lose window focus, get
   downed, or disconnect. Charging must cancel without an unintended throw.
7. Attempt throws beside walls and pickups through obstacles. A rejected throw
   keeps the inventory item; a rejected pickup keeps the world item.

Player movement follows the existing client-authoritative movement system.
World item physics, charge timing, inventory transfer and pickup are controlled
by the server. Automated host tests do not replace separate-machine EOS tests.
