# CombatMeter

[Thunderstore release](https://thunderstore.io/c/v-rising/p/BiribaMods/CombatMeter/) · [Installation and player guide](thunderstore/README.md) · [Source and issues](https://github.com/lucas98sf/CombatMeter)

**Development status:** Thunderstore currently provides 0.1.6. This repository includes unreleased 0.1.7 work to replace Emberglass with built-in networking. Development changes are not a new public release; see the player guide and validation record for their current status.

A V Rising server collector and client HUD for damage and whole-encounter DPS. Version 0.1.7 is a Windows x64 development build. Player damage in V Blood fights has been confirmed in the local game; enemy damage accounting has been checked with native synthetic events. Detailed damage validation, native contest lifecycle, and the revised UI still require the interactive checks in [VALIDATION.md](docs/VALIDATION.md).

## Install

1. Install **BepInExPack_V_Rising 1.733.2** on the server and each client that wants the HUD.
2. CombatMeter includes its native transport. Emberglass is not required; no extra port or server address is needed.
3. For a separately managed dedicated server, extract `CombatMeter-Server-0.1.7.zip` in the server root and `CombatMeter-Client-0.1.7.zip` in the client root. For a local private/host-and-play game through Thunderstore, install [ServerLaunchFix](https://thunderstore.io/c/v-rising/p/Mythic/ServerLaunchFix/) and put BOTH plugins plus Runtime, Core, and Shared in the profile's `BepInEx/plugins/CombatMeter` directory. ServerLaunchFix makes the bundled server load the same plugin directory. `BepInEx_Server` holds separate server state; it does not require another manual plugin installation in this setup. Keep Runtime, Core, and Shared DLLs beside the plugins and use the same CombatMeter version on both sides.
4. Restart the server and client. Players without the client plugin are still included by the server collector; they do not receive a HUD. The server plugin is required for authoritative totals.

Built against `v1.1.13.0-r99712-b17`, Unity 2022.3.58f1, and BepInEx's .NET 6 loader. See [versions.json](docs/versions.json) for provenance and validation status. The plugins have process filters: the collector loads in `VRisingServer.exe`, and the HUD loads in `VRising.exe`. The shared-folder ServerLaunchFix route has been used successfully in a local V Blood encounter. [Bloodpebble](https://github.com/cheesasaurus/Bloodpebble) is optional for development hot reloading; it does not replace ServerLaunchFix or the server collector. Bloodpebble server hot reload has been validated in an isolated process. Use the one-time setup and watcher in [DEVELOPMENT.md](docs/DEVELOPMENT.md); client HUD reload still needs an interactive check.

## Use and configure

The compact HUD is 320×136 at scale 1, with four visible rows and scrolling for the remaining roster. Existing saved position, scale, and locking preferences are preserved.

Encounter history, review rows, and damage sources show a draggable scrollbar when their lists overflow. Use the mouse wheel over a list or its scrollbar; in review/detail windows, Page Up/Down and Home/End scroll the hovered list (or encounter history/damage sources by default).

Hovering a meter scroll area temporarily blocks the game's camera/map zoom and menu-scroll actions. They are restored when the pointer leaves or the client unloads; our lists continue to read the mouse wheel directly.

Hover over a player or enemy row for 250 ms to see its top five damage sources. Click the row for the full scrollable breakdown, including damage and share of that participant's damage. Numbered target variants of the same source are combined in both views; damaging debuffs and different summons stay separate. Sources have consistent colors and use native ability, debuff, or passive icons where available. Participant rows still show total damage and DPS; player and monster bars share one damage scale. Drag the detail window's title strip to move it; Escape or a click outside closes it and returns to review when opened there. Outside clicks are consumed through release to avoid combat actions or activating a review row underneath. The window stays on the selected participant and encounter when rankings change. Sources also remain available for retained completed encounters.

The DPS list background defaults to 90% opacity. Hover over the DPS window to reveal its settings cog on the left and position-lock toggle on the right. The padlock shows whether dragging is locked; both icons disappear when you leave the window. The review panel uses the game's native window background and header. Its **BG opacity** buttons adjust only the DPS list background, including full transparency at 0%; text, damage bars, icons, and menus keep their normal opacity. The client config uses `Display.BackgroundOpacity` (0–1); the old whole-meter `Display.Opacity` value is no longer used.

Direct abilities, weapon attacks, damaging debuffs, procs, and summon attacks are accounted separately where native metadata identifies them. Unidentified effects stay in `Unknown source`; additional effects beyond 128 named sources go into `Other sources`. Native localized names and icons are preferred, with readable prefab names as fallback. Source details update twice per second only while inspected; ordinary roster snapshots do not contain source lists. This is protocol **4**, requiring matching server and client versions.

The default meter sits near the bottom-right corner at offsets `(-8, -944)` and scale 1, with its position locked. Click the padlock to unlock it, then drag its compact top strip to move it; the position is saved on release. **F8** or the small **cog button** at the top left opens the CombatMeter settings/review panel; **Escape** closes it. The HUD shows encounter name and time without a large title or completion label. Every row shows total damage and DPS together. Player percentages appear only when two or more players participate. Scroll to see every participant. Team numbers and bar colors identify native contest teams. Unresolved player damage is shown separately and remains part of the damage-share denominator.

Gold enemy bars follow the player bars, without a section heading. They show health damage dealt by bosses, boss summons, and monsters to players in recorded encounters. Enemy damage does not affect player totals or shares, and uses the same whole-encounter duration for DPS. Enemy names use the native name or a readable prefab name. Ordinary monster encounters still require open-world tracking to be enabled on the server.

Select current or completed encounters, hide/show enemies, or switch between boss-only and boss-plus-summons player damage. Lock the position in settings to disable header dragging. Display settings include visibility, scale, opacity, offsets, position locking, and mode visibility. The review panel takes gameplay focus; the passive HUD does not. Clicking buttons, scrolling our lists, and header dragging take temporary focus to avoid gameplay actions through the HUD. **Tab / Shift+Tab** moves between available review buttons; **Enter** activates the selected button. Version 0.1.7 uses protocol 4: update both server and client together.

Configuration is generated on first load:

- Server: `BepInEx/config/io.combatmeter.server.cfg`. Bosses, arenas, duels, and ordinary PvP are enabled; open-world PvE is disabled. `CombatTimeoutSeconds=10` controls ordinary fights. Restart after changing server tracking policy.
- Client: `BepInEx/config/io.combatmeter.client.cfg`. Defaults: visible and locked, scale 1, background opacity 0.9, offsets `(-8, -944)`, enemies shown, boss-only targets, F8, and all mode visibility switches on. Existing saved preferences take precedence. Client preferences cannot enable modes disabled by the server.
- Diagnostic server option: `Diagnostics.LogResolvedDamage=true` logs per-operation before/after health and the native change value. Enable only while verifying actual combat; compare these records with observed health.

History is session-only: each participant and native arena observer retains their latest 20 completed encounters. Restarting or reloading the server world starts a new session.

## Develop locally

For the public upload artifact and release workflow, see [PUBLISHING.md](docs/PUBLISHING.md). Thunderstore metadata and the public installation guide are maintained separately in `thunderstore/`.

For Debug builds, matching PDB deployment, VS Code attachment, and log interpretation, see [DEVELOPMENT.md](docs/DEVELOPMENT.md).

To install an existing build into your local Thunderstore `Default` profile with ServerLaunchFix already installed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-thunderstore.ps1
```

Use `-ProfilePath` for another profile, or `-WhatIf` to preview. This installs both plugins and their shared libraries into the shared client plugin folder. It backs up replaced DLLs under `artifacts/install-backups/`, verifies installed hashes, and leaves existing mods/configuration/saves alone. Restart the game through Thunderstore afterward. It does not enable hot reload or stop a running game.

After the first game launch, `-EnableOpenWorld` enables open-world tracking and client visibility in existing configs. `-EnableDiagnostics` enables resolved-health and periodic collector counters for testing. Config changes are backed up with the installation receipt. Disable `LogResolvedDamage` after verification.

If the game has locked the installed DLLs, `scripts/queue-thunderstore-update.ps1` starts a hidden installer that waits up to 20 minutes for this profile's client and local server to exit normally. Use its optional tracking/diagnostic flags explicitly when needed. It writes progress under `artifacts/install-queue/`, and never closes the game. Wait for `Installation verified` in that log before relaunching. On timeout, no installation changes are made.

`scripts/restart-thunderstore.ps1` requests normal shutdown of the client using the selected profile, waits for its local server to exit, and relaunches with the same Doorstop profile arguments as Thunderstore's Modded launch. It never force-kills either process. After launch, select your private game and press F8; the script does not click through game menus. Use `-WhatIf` to preview.

Use PowerShell from this directory. Scripts can be invoked with process-local execution policy when required:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/bootstrap.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/setup-profile.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/deploy-profile.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/start-profile.ps1 -Mode server
```

The bootstrap installs SDK 10.0.401 in `.tools/dotnet`, verifies its SHA-512, and downloads loader dependencies. Plugins still target **net6.0**; the test runner uses the workspace SDK runtime. No global SDK install is required.

`setup-profile.ps1` accepts `-GameDirectory` and `-ReferenceProfile`. It creates `.profiles/client` and `.profiles/server`, copies executable/loader files, and junctions the installed static game data. Saves, logs, configuration, and plugins are isolated. It can seed matching interop from an existing Thunderstore profile; boot both development profiles before the first build if no generated references exist. BepInEx checks game assembly hashes and regenerates references on game changes. Regeneration requires Steam/game files and network access for Unity base libraries.

Close your existing client before running `start-profile.ps1 -Mode client`. Connect to **127.0.0.1:29878**, using the isolated server and `CombatMeterValidation` save. `stop-profile.ps1 -Mode server` stops only the recorded development process after verifying its executable path. It does not stop other V Rising processes. `package.ps1` creates separate archives under `artifacts/`.

## Implementation

| Project | Responsibility |
| --- | --- |
| CombatMeter.Shared | Versioned registration, policy, snapshots, completion, and history request packets |
| CombatMeter.Core | Encounter graph, ownership cache, aggregation, history, packet acceptance, ranking |
| CombatMeter.Runtime | Permanent native health/input hooks and Unity frame bridge |
| CombatMeter.Server | Reloadable ECS encounter collector and Emberglass transport |
| CombatMeter.Client | Emberglass transport, native canvases, fonts/sprites/tooltips, HUD and settings |
| CombatMeter.Tests | Accounting, attribution, grouping, protocol, reconnect, and synthetic load checks |

The collector surrounds native `StatChangeSystem.ApplyHealthChangeToEntity` and measures target health before/after each operation. It excludes healing and overkill; absorbed/blocked damage contributes zero actual health damage. The meaning and invocation of this native hook need validation in combat, including Burst/inlining and merged native changes. No client supplies damage measurements.

Versioned entity ownership chains are cached when owned entities spawn and when damage resolves. Boss minions also use native minion spawn relationships. Boss fights follow native combat/return/death state and survive phase pauses. Contests use native active IDs, roster/team buffers, active timing, finish/cancel events, and observer buffs tied to the owning contest. Ordinary encounters merge only through connected entities or participants, close after the quiet timeout, and use the last combat timestamp as their endpoint.

Emberglass is contained in transport adapters. Registered players receive authoritative totals every 250 ms; a reconnect registers again and receives history plus fresh active snapshots. Session IDs and per-encounter sequence numbers reject stale, duplicate, and foreign-session packets. DPS always divides by the whole fight duration, including late joins, downtime, and deaths.

Healing, shielding, CC, and a detailed combat log are deferred until damage capture is verified.

## References

- [V Rising mod development](https://wiki.vrisingmods.com/dev/)
- [Emberglass typed networking](https://github.com/mfoltz/Emberglass/blob/v0.3.2-pre/docs/networking-examples.md)
- [Eclipse native canvas approach](https://github.com/mfoltz/Eclipse/blob/main/Services/CanvasService.cs)
- [Official 1.1 contest behavior](https://blog.stunlock.com/v-rising-1-1-patch-notes/)



