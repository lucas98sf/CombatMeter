# 0.2.0

- Remove the Emberglass dependency; networking is built into CombatMeter.
- Use directed native game messages with compressed, bounded packet framing. Requests are tied to the connected native user and replies to the current client channel.
- Preserve encounter authorization, request throttling, history, source breakdowns, and server-authoritative damage accounting.
- Resolve source names from native prefab metadata instead of Emberglass's prefab catalog.
- Protocol 4: update client and server together and restart once, including Runtime/Core/Shared.
- Testing-phase release: local-game registration, damage tracking, source replies, and client hot reload verified; broader multiplayer and contest validation continues.
# 0.1.6

Initial public early-access release.

- Server-authoritative player and enemy damage and DPS rankings.
- Native HUD, settings, encounter review, and session history.
- Colored damage-source tooltips and detail windows, with native icons where available.
- Combined numbered target variants, boss target filters, and comparable player/enemy bars.
- Draggable windows, visible scrollbars, hover controls, red buttons, and larger text with a softened dark contour.
- Protocol 3; matching client and server required.
- 43 automated tests pass. Additional multiplayer and contest validation remains ongoing.
