# SACC vs SCUM Admin Helper 1.1.3 — feature comparison

Date: 2026-11-11. SAH version observed: **1.1.3** (by "Ares"), installed at `%LOCALAPPDATA%\SAH\`.

## How this was gathered (clean-room)

No decompilation. Observable facts only:

- **Install folder listing + manifest** (`sq.version`): SAH 1.1.3 is now a **Velopack** app on **.NET 10**, with a **WebView2** web UI (`WebView2Loader.dll`), a `Sah.Native.dll` helper, and `libwebpdecoder.dll`. The old 0.4.x was a 126 MB WPF single-file; 1.1.x is ~14 MB because the UI is HTML/JS in WebView2 using the OS Edge runtime.
- **Public feature list** from the official site [scumadminhelper.com](https://scumadminhelper.com) and third-party listings. The site advertises the current build as **v1.1.3 · NEW COMPACT MODE**.

The embedded web UI assets live inside the exe; they were **not** extracted (that would be decompilation). The "UI study" below is from the vendor's own public screenshots/descriptions and is about *functional direction*, not copying their layout — per `CLEANROOM.md`, we never copy SAH's UI layout, strings or images.

## Headline numbers SAH advertises

2,578 game items · 444 fillable containers · 148 admin commands · 1,266 map points · 23 languages.
(SACC ships 2,578 item codes and ~444-equivalent containers already, so the catalog *size* is at parity; the gaps are in metadata and surrounding features.)

---

## Where SACC is at parity or ahead

| Area | SACC | Note |
|---|---|---|
| Command catalog | Generated form + live preview + **confidence levels** | SAH shows an example reply; SACC uniquely flags Documented/Conflicting/Verified. |
| Item/vehicle spawning | Type-ahead, icons, amount-splitting, StackCount/Full/fill-magazines | Parity on mechanics. |
| Filled containers | `#SpawnInventoryFullOf` builder | SAH has a **visual grid auto-pack**; ours is list-based (gap below). |
| Kits/loadouts | Folders, clone, edit, fill magazines | ≈ SAH packages (but items-only; gap below). |
| Interactive map | scum-map.com tiles, layers, players/vehicles/flags, grids/keypads | Parity+; see advantages. |
| **Terrain ground-height solving** | Terrain grid + learned ground + climb-to-ground for NPC/zombie/item spawns | **SACC-unique / more advanced.** SAH spawns at a point but shows no equivalent ground-solving. |
| **Island overlays** | Threat zones, hunting areas, mech patrols, location names, temperature heatmap | **SACC-unique.** |
| **Missions planner** | Phases, trigger/hold radii, prefab scenes, run tally, area cleanup | **SACC-unique** (SAH has teleport routes + area cleanup, not a mission system). |
| **Player tracking** | Timed `#ListPlayers`, tracks.csv, per-player & all CSV export, track-on-map | SACC-unique depth. |
| **WhalleyBot export** | Macros→task, kits→pack, map→coords | SACC-unique. |
| Open source | GPL-3.0, clean-room | SAH is closed source. |

**Protect these when chasing parity** — they're the reasons to run SACC over SAH, and several (terrain solving, missions, tracking, overlays) SAH has no answer to.

---

## What SACC is missing (SAH has it, we don't)

### Tier 1 — architectural gaps (big, change the shape of the app)

1. **Multi-server profiles.** SAH keeps a register *per server* (players, squads, flags, economy, settings) and you switch the active server from a menu. SACC is a single profile. Nearly every SAH feature below is scoped per-server.
2. **ggCon / remote command execution.** SAH can send commands **straight to the server** (no typing into game chat), read the server's live player list, and parse replies (e.g. `#PlayerInfo` auto-fills the player card). This is a fundamentally more reliable delivery path than our SendInput-into-chat. *Requires a ggHost rental*, and SAH keeps keyboard-send as a fallback — so it's additive, not a replacement.
3. **Server configuration editing (local folder or FTP/FTPS).** An entire category SACC lacks: `ServerSettings.ini` editor (search, categories, changed-values), economy/traders editor from the game template, raid times, admins/whitelist/banned/silenced user lists. SAH edits the server's own config files with SCUM's own translated setting texts.

### Tier 2 — feature-depth gaps

4. **Item metadata.** SAH shows weight, size, rarity, **trader prices**, **fame required to buy**, DLC pack info and descriptions. SACC has icons + codes only.
5. **Squads + flag ownership register.** SAH links players↔squads (members, leader) and flags (owner, sector). SACC has players and raw flag markers, no squad/owner model.
6. **Visual container packing.** SAH lays items into the real container grid with rotations, weight-aware, stops at what fits, "sets" or "fill" modes, saved presets. SACC's container builder is text/list-based.
7. **Spawn queue.** A staging area to queue several spawns then fire together. SACC spawns immediately (kits are the nearest thing).
8. **Packages = items + commands + containers in one unit** (name/icon/color/description, reorder, duplicate, import old SAH favorites). SACC kits are items-only (+ loose commands from import).
9. **Multi-player teleport & teleport routes.** SAH teleports several players at once and runs **routes with multiple stops, a countdown and player announcements**. SACC does single teleports (+ Missions dashed route, but not a player-facing routed teleport with countdown).
10. **Moderation result verification.** SAH reads the game's reply to confirm a kick/ban/edit actually worked. SACC sends + logs but doesn't verify most replies.

### Tier 3 — polish & ecosystem

11. **Automatic game-data updates** after each SCUM patch (delta download) + **app self-update** (Velopack). SACC bundles static data; new data needs a new build.
12. **23 languages** (UI + SCUM's own item/setting texts, switchable live). SACC is English-only.
13. **Battlemetrics widgets** on the desktop. Absent in SACC.
14. **Compact mode (the flagship 1.1 feature).** One click shrinks SAH into a slim side panel beside the game (Ctrl+M), map opens beside it, position/size remembered. SACC has always-on-top but no docked compact panel.
15. **Guided tour / onboarding** on first launch. SACC has none.
16. **Personalization depth.** SAH: 2 dark + 2 light palettes, 6 accent colors, animation/shadow toggles, collapsible icon sidebar. SACC now has multiple theme palettes (recent work) but no accent-color axis or motion.

---

## The UI jump

SAH 1.1 was "rewritten from scratch" as a **WebView2 (HTML/CSS/JS) app**: card/grid catalog, theme palettes + accent colors, animations, a collapsible icon sidebar, and the compact docked panel. That's the modern feel you noticed. SACC is WPF with functional tabs.

Realistic options for SACC (we are **not** rewriting to web):

- **Adopt specific patterns in WPF**, designed ourselves (not copied): a **card/grid catalog view** for the Spawner (icon + name + key stats), an **accent-color** axis on top of the theme work just done, and a **compact mode** (a narrow docked window state that shows Spawner/Players/Commands, toggled with a hotkey). These are the highest-impact, most visible wins.
- **Lower priority:** motion/animation polish, collapsible icon sidebar.

Compact mode and a card catalog are the two UI items that would most close the "feels better" gap, and both are achievable in WPF without an architecture change.

---

## Suggested priority if we chase this

1. **Item metadata** (weight/rarity/trader price/fame/DLC/description) — enriches the catalog we already have; data-only, no architecture change. Also unlocks a card view.
2. **Card/grid catalog view + accent colors + compact mode** — the visible UI wins.
3. **Squads + flag-owner model** — natural extension of Players.
4. **Spawn queue + richer packages** — extends kits.
5. **Multi-server profiles** — large; prerequisite for config editing.
6. **Server config (FTP) editing** — large, separate workstream.
7. **ggCon remote execution** — large; gated on ggHost, additive to keyboard send.
8. Localization, Battlemetrics, auto-data-update — ecosystem, lower urgency for a solo/self-host admin.

Items 5–7 are where SAH's paid/hosted ecosystem (ggHost, ggCon, FTP) lives; weigh them against SACC staying a free, local, keyboard-driven tool.
