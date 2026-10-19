# Built-in transport: 0.2.0 / protocol 4 (2026-10-19)

- Release and Debug builds pass; 55 automated tests pass. Wire tests cover single-frame requests, fragmented/reordered details, duplicate rejection, connection-channel isolation, direction validation, malformed framing, transfer expiry, reset, decompression limits, oversized packets, and 32-player snapshots/full 128-source details.
- Removed all Emberglass assembly references and plugin dependencies. Bootstrap, isolated-profile setup, and installers no longer download or install it. Existing third-party installs are preserved by installation scripts.
- The isolated server loaded Runtime and Server 0.1.7 with Emberglass absent and reached the native collector tick. A temporary validation plugin created a reserved directed-message event with a native connected-user identity. The permanent ChatMessageSystem prefix delivered it exactly once and destroyed the event before chat routing. Native prefab catalog: 14,481 entries. This synthetic test does not demonstrate delivery over a real connection.
- Evidence: `artifacts/validation/native-transport-20261019/server.log`, `Unity.log`, and temporary probe source. The probe plugin was removed after testing; the isolated process was stopped.
- Default development profile updated with Debug 0.1.7; stable-library receipt refreshed. Its manual Emberglass DLL was renamed to `Emberglass.dll.disabled` after checking installed plugin assembly references. No other installed plugin referenced Emberglass. The file can be restored if another mod needs it.
- Live validation during 0.1.7 development: initial readiness checks missed Client_0 and relied on server-side User.IsConnected. The replacement queries the actual LocalUser/User/NetworkId entity in any created world. Logs confirm protocol 4 registration, accepted policy, a populated encounter packet, and an available source response. The user confirmed tracking works and provided HUD/detail screenshots. Repeated client hot reloads re-registered against the same server session without Emberglass.
- 0.2.0 is a testing-phase release of this implementation. Reconnect, normal-chat compatibility, two-player behavior, contest lifecycle, and large real-game fights still require broader validation. Earlier Emberglass tests do not validate these remaining paths.

## Earlier validation record
# Validation record and release gate

Development build 0.1.6, October 4, 2026. This document distinguishes automated and native probe evidence from interactive game validation. Unchecked items have not been validated and block a production-ready release.

## Verified

- [x] Client and server compile against installed game interop with no warnings/errors, targeting net6.0.
- [x] Workspace-local .NET SDK installed with published SHA-512 verification.
- [x] Dedicated server boots in an isolated profile for `v1.1.13.0-r99712-b17`; BepInEx generates fresh matching server interop.
- [x] Emberglass 0.3.2-pre and CombatMeter Server load; registration/history packet handlers register.
- [x] Collector ticks against the native server clock in the loaded isolated world without update errors. This idle-world smoke test does not validate combat hooks.
- [x] User's ServerLaunchFix profile loads the collector and HUD in the correct processes; HUD attaches to the native canvas in a live local world.
- [x] Live client registration and accepted policy share the server session ID. User reports V Blood encounters starting and ending correctly.
- [x] Native health probe on the matching isolated server: three synthetic -17 health events on a V Blood unit each measured 511.5063 -> 494.5063, counted 17 damage, and reached boss encounter accounting. This validates the native entry point, not real player attacks or attribution.
- [x] Automated accounting/protocol and client presentation suite: 43 cases passed, including 32,000 events across 32 players, enemy/source accounting, solo presentation, filters, protocol 3 validation, target-variant grouping, a shared player/enemy bar scale, and scrollbar geometry/bounds.
- [x] User confirmed damage tracking working in a live local V Blood encounter on 0.1.2; detailed mitigation/overkill/attribution matrix remains unchecked.
- [x] 0.1.3 native enemy probe: a real NPC's resolved hits on a simulated recipient in the meter cache accumulated enemy damage while player and unresolved totals stayed zero. Native name lookup produced `Militia HoundMaster`. The temporary probe did not create fake native player/user entities and was not installed in the user's profile.
- [x] Common DPS denominator; late joining/dead/disconnected roster data remains in aggregates.
- [x] Versioned owner chains, vanished-source cache, index reuse, cycles, and expiration.
- [x] Repeated-event rejection; simultaneous fights; graph merges; resets; quiet-tail exclusion.
- [x] Boss filters; unknown damage; disabled modes; boss/world separation at the core routing boundary.
- [x] Per-user/observer history capped at 20, unaffected by other players' completed fights.
- [x] Duplicate/outdated/foreign-session packet rejection, reconnect snapshots, protocol mismatch, invalid numbers, and unavailable-server state.

## Interactive checks still required

### Damage sources in 0.1.6

Client defaults now match the saved HUD/display setup: visible and locked, scale 1, background opacity 0.9, rounded offsets `(-8, -944)`, enemy rows shown, boss-only filtering, F8, and every mode visible within server policy. Fresh configurations start at layout version 1, so legacy migration cannot unlock the new default. Existing preferences remain authoritative. Buttons use dark red with a brighter selected-history state. Server tracking/diagnostic defaults are unchanged.

Native UI follow-up: the installed `Window_Background` sprite has a 52-pixel black tail below its border. A private cropped sprite removes that tail and adjusts the bottom nine-slice border; the crop is guarded by the validated 241-pixel height and 100-pixel bottom border. The shared sprite/texture are unchanged. Evidence: `artifacts/validation/hud-clipping-20261019/native-window-original.png`. List viewports now use stencil masks in addition to pointer/rectangle clipping; the installed DefaultUI material was checked for stencil/color-mask support. Hovering a list/track disables only previously enabled camera/map/menu-scroll input actions and restores them on leave, disconnect, world change, and client unload. Interactive camera-scroll, last-row clipping, and resulting window edges still need an in-game check.

Modal lists now reserve a 16-pixel gutter for a visible track and draggable thumb when content overflows. Wheel scrolling and track/thumb input share the same clamped offset; Page Up/Down/Home/End work in review/detail windows. Four scrollbar tests cover history, large source lists, fitting/empty lists, and changed content/viewport sizes. Review and source panels use a shared 48-pixel horizontal content inset with titles below the native header ornament and a shorter footer. Actual drag interaction and visual padding still need an interactive check.

The compact client HUD now measures 320×136 with four 24-pixel row slots and adaptive name/value widths. Review/detail contents are inset from the native frame; scroll areas end above the bottom ornament and the selected encounter is highlighted. Client builds and native asset attachment were verified after hot reload. Visual spacing, clipping, hover controls, and scrolling across UI scales still require interactive checks.

Client presentation refinements (2026-10-19) combine terminal numbered target variants (for example Chain Lightning Target 01/02/03) in detail windows and hover tooltips. Four additional tests verify conservation, ranking, stable group colors, boss filters, separate debuffs/summons, and comparable player/enemy bar lengths. Client-only code keeps Core/Shared unchanged for hot reload. Outside left/right clicks dismiss the detail window and retain input focus through button release; dragging from inside the header still works. Visual behavior, outside-click focus, and review return need an interactive check.

The source resolver was exercised on the installed server with converted sword-primary, Chaos Volley projectile, and shared burn-debuff prefabs. Controlled native -17 health operations produced three separate source entries summing to 51 damage, including projectile normalization through an explicit `AbilityOwner` relationship. The recipient was simulated only in the meter's ownership cache. These are synthetic health operations; real player casts, native DealDamage-to-StatChange relationships, and simultaneous merged effects remain to be verified interactively. The temporary probe was installed only in the isolated profile.

Native checks exposed unavailable generic ECS instantiations for metadata reads. The resolver now uses explicit native component types and bounded raw component reads. Prefab names prefer native converted metadata, decoded as bounded UTF-8, with a version-pinned Emberglass catalog fallback behind the transport adapter. Revalidate the fallback and native layouts after a game or dependency update.

Evidence: `artifacts/validation/damage-sources-20261004/server.log`. The final clean probe logs Weapon sword-primary damage, Ability Chaos Volley damage, and Debuff burn damage separately. It also destroys the projectile's ability-group entity and confirms the cached source remains Chaos Volley. Experimental probe errors were corrected before this final run and packaging.

The automated suite has 35 passing cases. The source-load case processes 32,000 events across 32 players and inspects one player: 20 replies over ten simulated seconds, 48,471 JSON bytes before Emberglass framing. Its measured Release accounting time was 50.9 ms; the 68.0 ms detail-query measurement includes JSON serialization and test assertions. This is a managed simulation, not a measured live-network benchmark. Evidence: `artifacts/test-results/source-load.trx`.

- [ ] Player weapon combos, projectile variants, damaging debuffs, procs, summons, and enemy attacks identify the expected sources in real combat.
- [ ] Native merged operations with ambiguous source identity remain Unknown source; source sums match participant totals.
- [ ] Hover updates after 250 ms without taking combat focus; tooltip clamping and scroll clipping work at each screen edge.
- [ ] Click pins the correct encounter/participant during ranking changes; detail dragging, scrolling, boss filtering, Tab/Enter, and Escape work.
- [ ] Localization/icons use client native metadata where available; fallback labels remain readable.
- [ ] Repeat client reloads/world changes without duplicate UI, stale replies, or retained input focus. Measure live two-player delivery and framing overhead.

Core and Shared changed for protocol 3. Update both plugins and refresh the hot-reload profile after closing the game; restart once before resuming the watcher. Open-world tracking remains unchanged.

### Hot reload in 0.1.5

Bloodpebble 1.4.2 automatically unloaded/reloaded the server plugin twice while isolated server PID 46672 stayed running. Packet handlers and callbacks were released and registered again; each collector reload created a new session. A native NPC probe after the first reload recorded three 17-health hits, routed all three, and accumulated enemy totals with zero player/unresolved damage. The permanent Runtime plugin kept native hooks and its injected Unity behaviour alive across both reloads. Evidence: `artifacts/validation/hot-reload-20261004/server.log`.

The setup script was exercised against a workspace-only profile. Client hot reload still requires an interactive check: verify cog/drag input, UI cleanup, reconnection, and history restoration across repeated reloads. Native Runtime, Core, and Shared changes require restarting; they are protected by deployment hash checks. Long-run assembly collection and memory growth have not been measured.

Use the isolated profiles, a fresh validation save, and a second player. Keep server diagnostics enabled during damage checks. Save evidence under `artifacts/validation/`; do not commit saves or game DLLs.

### Compact HUD in 0.1.4

Removed the large HUD title, completion/status text, bottom settings button, enemy heading row, and repeated encounter-duration explanation. A 24-pixel cog button now opens review/settings from the top left; the adjacent compact strip still supports dragging. Native cog/settings sprites are reused when available, with a small UI-geometry cog fallback so missing font glyphs cannot hide the icon. The panel always uses `CombatMeter` as its title and shows only player count and time underneath, avoiding repeated encounter-type labels. Enemy rows retain their gold bars. Actual appearance, clicking the cog, and dragging beside it need a live client check.

### UI and enemy refinement in 0.1.3

The meter and review show damage and DPS together, without a metric toggle. Player shares appear only with at least two players. Enemy damage to players is stored separately in protocol-2 snapshots and rendered below an enemy divider. Boss-target filters affect outgoing player damage only. NPC/projectile origins are cached by entity index and version with 120-second retention, including the boss root for delayed damage after a summon disappears. Enemy names fall back to readable native prefab names.

The review panel now uses the visible HUD parent rather than the native menu parent. Custom button rectangles use mouse press/release matching and keyboard Tab/Enter handling; native onClick listeners are not also registered, avoiding duplicate actions. Scrolling is scoped to our lists and clamps their contents. F8/Escape remain available. Verify actual button clicks, focus restoration, scrolling and keyboard navigation in the next client session; these changes have only been compiled and their data presentation tested automatically.

Evidence: `artifacts/validation/enemy-damage-20261004/server.log`. Ordinary open-world collection is not repaired or enabled by this refinement.

### Damage hook correction in 0.1.2

Live 0.1.1 logs showed a registered subscriber and active boss encounter but `healthHookCalls=0`, `captured=0`, and `routed=0`. The Burst-compiled stat-change job bypassed the IL2CPP health method hook. The server now replaces only that job's Burst runner with a delegate calling the original native non-Burst lambda body. Native merging, ordering, damage modifiers, health writes, and other side effects are preserved.

The health method uses a direct BepInEx native detour with the Windows x64 calling convention. Large by-value structures are passed as their native pointers, and Entity is passed as eight bytes. A Harmony value-type hook crashed the isolated probe and was removed before deployment. The successful direct-detour run stayed alive through damage and native healing. No experimental probe plugin was installed in the user's profile.

Evidence: `artifacts/validation/health-hook-20261004/server.log` and `Unity.log`. This adapter is currently Windows x64 only and requires revalidation against changed game interop. Retest actual player attacks, multi-attacker bursts, DoTs, shields, and overkill, and profile the non-Burst stat-change job during larger fights. Open-world behavior was left unchanged at the user's request.

### Networking first

The first live session did not start Emberglass's handshake. Version 0.3.2-pre triggers it only through a particular coffin-spawn buff. CombatMeter retries the same authenticated handshake from its transport adapter once the actual local character exists. Live registration and accepted policy were subsequently observed. This compatibility workaround uses the beta's protected game-event method and must be reviewed when upgrading Emberglass. It does not change key exchange, trust policy, or packet authentication.

- [ ] Close existing client, start isolated client, connect to 127.0.0.1:29878.
- [x] Server logs client registration; client logs accepted policy with the same session ID.
- [ ] Trigger one encounter and confirm snapshots and completion packets reach the HUD.
- [ ] Disconnect/reconnect during a fight: current totals persist, a fresh snapshot replaces stale state.
- [ ] Restart server: old session/history disappears. Incompatible protocol shows a readable status.
- [ ] Connect to an unmodded server: waiting/unavailable state, no fake totals or chat commands.

### Damage meaning and attribution

- [ ] Direct physical/magic hits, two attackers in one frame, and multi-hit abilities: sum of logged deltas equals health lost before healing.
- [ ] Armor/resistance/weakening: count damage after native mitigation.
- [ ] Shields, blocks, counters: count health damage only; verify fully absorbed hits and encounter activity.
- [ ] Lethal overkill: a target with 12 HP contributes at most 12 damage.
- [ ] DoT after projectile destruction and player-owned summons: correct originating player.
- [ ] Boss-owned summons: separate totals and both filter results; unrelated nearby creatures/PvP excluded.
- [ ] Second player with no CombatMeter client is counted and named correctly.
- [ ] Confirm the health hook fires for all these paths; native Burst/inlining must not bypass it. Confirm health writes are observable before the postfix reads them.

### Native encounter lifecycle

- [ ] Boss starts on engagement, including players initially taking damage; death ends precisely, native return resets, long phases stay in the same attempt.
- [ ] Duel countdown excluded; combat start, winner, draw/cancellation, and post-match restoration handled on both PvE and PvP servers.
- [ ] Castle arena: full roster, native teams, zero-damage/late arrivals/defeated players, completion/cancellation, and observers.
- [ ] Verify native observer buff EntityOwner links to ContestInstance or its Creator; no proximity-based spectators are accepted.
- [ ] Verify native finish/cancel events are visible before restoration. If native cleanup bypasses the hooks or event polling, adjust adapter ordering before release.
- [ ] Ordinary PvP/PvE: unrelated simultaneous fights remain separate; a connecting participant merges them; 10-second quiet tail is excluded from duration.

### UI and load

- [ ] Check HUD below minimap, native fonts/frames/buttons/tooltips, and visually review it at 1080p and another aspect ratio.
- [ ] 32-player roster scrolls fully; history shows current encounter and 20 completed encounters.
- [ ] Drag, scale, opacity, lock, visibility and mode policy behave and persist.
- [ ] Passive HUD/scrolling never triggers gameplay input; review buttons and keyboard navigation work; F8 opens and Escape closes cleanly.
- [ ] Review captures and releases focus/cursor correctly, including disconnect and world reload.
- [ ] World reload destroys UI clones, releases contexts, discards old sessions and stale ownership.
- [ ] Profile 32-player live updates at 4 Hz in-game. Automated aggregation load is not a measurement of Unity rendering, transport latency, or bandwidth.

After each check, record game version, loader/runtime/dependency versions, result, logs/video/health readings, and any adapter changes. Re-run the relevant automated checks after fixes and rebuild packages.



