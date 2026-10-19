# CombatMeter

A native damage and DPS meter for V Rising, inspired by Details! for WoW. Compare players and enemies, inspect damage sources, and review recent encounters without using chat commands.

**Early access, version 0.1.7.** Built and locally tested with V Rising **1.1.13.0-r99712-b17** on Windows x64. V Blood damage tracking and the HUD have been tested in a local game. Duels, castle arenas, ordinary PvP, multiplayer edge cases, and large fights still need broader testing. Open-world tracking is experimental and disabled by default.

## Features

- Live player and enemy damage rankings, showing total damage and whole-encounter DPS together.
- Colored damage-source breakdowns for weapon attacks, abilities, debuffs, procs, and summons, with native names and icons where available.
- Hover a participant for a quick breakdown; click for a draggable, scrollable detail window.
- Current encounter and the latest 20 completed encounters, retained for the server session.
- Boss-only and boss-plus-summons filters.
- Movable, scalable HUD with opacity, position locking, and mode visibility settings.
- Server-authoritative tracking includes participants who do not have the client mod.

## Required setup

**Both the server collector and the client HUD are included. The server must run CombatMeter to provide statistics. Installing only on your client cannot track an unmodded server.**

BepInEx is installed automatically by your mod manager. CombatMeter includes its own networking; Emberglass is no longer required. No extra port or server address is needed.

### Local private game / host and play

1. Install CombatMeter in your Thunderstore profile.
2. Install [Mythic's ServerLaunchFix](https://thunderstore.io/c/v-rising/p/Mythic/ServerLaunchFix/).
3. Restart V Rising using **Start modded**, then open your private game.


ServerLaunchFix lets the bundled local server use the same plugin folder. You do not need a separate manual CombatMeter installation under `BepInEx_Server`. Bloodpebble is optional for development and is not required to play.

### Dedicated server

Install BepInEx and this package on the server. Install the same versions on clients that want the HUD. For manual installation, copy the archive's `BepInEx` folder into the game/server root. Keep all five CombatMeter DLLs together in `BepInEx/plugins/CombatMeter/`.

The client and server plugins have process filters, so each loads only in its intended process. ServerLaunchFix is only needed for local hosting. Players without the client mod are still counted by the server collector.

## Controls

- **F8** opens settings and encounter review; **Escape** closes the current panel.
- Hover the meter to reveal the **cog** and **padlock** buttons. Unlock the meter, then drag its title strip to move it.
- Hover a player or enemy row for its top damage sources; click for the full breakdown. Click outside the detail window to dismiss it.
- Scroll over lists or drag their scrollbars. Scrolling a meter list temporarily blocks game zoom.
- Review supports **Tab / Shift+Tab**, **Enter**, and **Page Up / Page Down / Home / End**.

## Configuration

Configuration files are created after first launch:

- `BepInEx/config/io.combatmeter.server.cfg`: enabled encounter modes and combat timeout. Bosses, arenas, duels, and ordinary PvP are enabled by default; open-world PvE is disabled. Ordinary fights end after 10 seconds without qualifying combat. Restart the server after changing its policy.
- `BepInEx/config/io.combatmeter.client.cfg`: display preferences. The default HUD is locked near the bottom-right corner, with scale 1, background opacity 0.9, enemy rows visible, and boss-only targets. Settings can also be changed in-game. Client visibility settings cannot enable tracking disabled by the server.

Damage is measured as actual health lost after mitigation, excluding overkill. DPS uses the same whole-encounter duration for every participant. Damage whose source cannot be identified stays in **Unknown source**. History is cleared when the server session restarts.

## Upgrading from 0.1.6

Close the game and server, update both sides to 0.1.7, then restart. Runtime/Core/Shared also change, so hot reload alone cannot perform this upgrade. Emberglass may be removed if no other installed mod requires it; CombatMeter does not uninstall other mods.

## Compatibility and troubleshooting

- CombatMeter 0.1.7 uses **protocol 4**. Update server and client together.
- The health hook targets the game build listed above; later game updates may require a CombatMeter update.
- If the HUD has no statistics, verify CombatMeter Server loaded in the server log. The client plugin alone cannot collect server combat data.
- Remove older manual CombatMeter copies before installing through a mod manager. Development installations under `BepInEx/BloodpebblePlugins/CombatMeter` must also be removed to avoid duplicate loading. Close the game before replacing DLLs.
- Healing, shielding, and CC statistics are not included.

## Credits

[BepInEx](https://github.com/BepInEx/BepInEx), [ServerLaunchFix](https://thunderstore.io/c/v-rising/p/Mythic/ServerLaunchFix/) provide the loader and local-server integration. [Eclipse](https://github.com/mfoltz/Eclipse) demonstrated native HUD integration techniques.
