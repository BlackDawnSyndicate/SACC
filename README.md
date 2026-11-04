# SACC: SCUM Admin Command Console

A desktop console for SCUM server admins. Pick a command, fill in its fields, and SACC types it into the game's chat for you. It also handles player lists, saved locations, item and vehicle favorites, and multi-step macros.

SACC is a clean-room build. See [CLEANROOM.md](CLEANROOM.md).

## How it sends commands

SACC sends commands the same way you would by hand. It focuses the SCUM window, presses the open-chat key, pastes (or types) the line, and presses Enter. It never touches the game process or its memory.

Safety rails:
- **Dry run** (on by default) logs every command and sends nothing.
- SACC only sends lines that start with `#`, and refuses anything containing a line break. That stops a single field from sending a second, hidden command.
- If the game window loses focus mid-send, SACC stops before pressing Enter.
- Kick, ban and destroy-all commands ask for confirmation.
- **Esc** or **Stop** cancels a running send or macro.

If SCUM runs as administrator, Windows blocks keystrokes from non-elevated programs. The header shows this. Use **Settings → Restart as administrator** when it happens.

## Tabs

| Tab | What it does |
|---|---|
| Console | Searchable command catalog with a generated form, a live preview of the exact line, and a raw command box (Enter sends, Up/Down recalls history). |
| Players | **Online now** (on by default) shows only the players in the last `#ListPlayers` output; its label gives the count and when the list was read. The box beside it filters by character name, Steam name or SteamID64 as you type. **Name** is the in-game character; **Steam name** is the game's "Platform Name". A **Sector** column shows each player's keypad from their last known position. **Tracking** (side panel): tick **Track every** and set the seconds (10 or more, default 60) to read `#ListPlayers` on a timer and record everyone's position in `tracks.csv` in the data folder. Each read brings SCUM to the front, opens chat and types the command, so it can cut across what you're doing and replaces anything half-typed in chat; SACC asks first, goes back to the window you were in, and tracking is off whenever SACC opens. It also stops if a send fails or the game doesn't answer 3 reads in a row. A player who moved under 1 m gets a new point only every 5 minutes. **Show on map** draws the selected player's track, **Export…** saves their track as CSV (time, SteamID, name, X, Y, Z), **Export all…** saves everyone's, and **Clear** deletes them. **Refresh from game** sends `#ListPlayers true` and reads the clipboard. **Paste list** parses output you copied yourself. Per-player buttons: go to, bring, locate, fame, money, mute, kick, ban. |
| Spawner | Item and vehicle spawning. Type any part of a code ("m16 mag") to filter the full list of known codes. Down moves into the list, Enter picks or spawns, double-click spawns. Favorites work the same way (double-click to spawn). Amounts above 10 are sent as several lines of at most 10 (23 → 10, 10, 3; configurable in Settings). This applies to every `#SpawnItem` SACC sends, including the Console form, the raw box and macros. An optional **StackCount** sets rounds in a magazine or units in a stack. **Full** sends a large value (200 by default), which the game caps at the item's maximum. **Fill magazines** (on by default) spawns magazines full unless you type a StackCount. **Search in game** sends `#ListItems <filter>`. The item list is the game's own `#ListItems` output (2,578 codes, shipped in `data/game-items.json`), in the game's spelling, plus older codes from the SAH table, kits and favorites. Codes the game doesn't list are marked *not in #ListItems* and sorted last: they're DLC items, or were renamed or removed, and may not spawn. **Refresh list from game** sends `#ListItems` and reads the result from SCUM's client log (`%LOCALAPPDATA%\SCUM\Saved\Logs\SCUM.log`), since the command doesn't use the clipboard. With Dry run on, it reads the last full list already in that log. **Container** builds a `#SpawnInventoryFullOf` line: a container (e.g. `Hiking_Backpack_01_02`) the game fills to its space and weight limit with stacks of one or more items. Pick the container and items in the Items box (← picked item, + picked item) or type them; save containers you reuse. **→ kit** puts the container into the kit selected on the Kits tab; on the Kits tab, **Add container** adds a saved one (Amount = how many). |
| Kits | Named item loadouts, grouped by folder. **Spawn kit** sends each item; counts above 10 are split into several lines (configurable). **Fill magazines** (on by default) spawns every magazine in a kit with the Full StackCount; the magazine pattern is a setting. **Clone** makes an editable copy of any kit or imported pack in the same folder. Re-importing the original never touches the copy. Items can be edited in place, including a per-item StackCount or Full: type in the last row to add one, or press Delete to remove one. **Copy to macro** turns a kit into an editable macro. **New kit** creates an empty kit in the current folder; rename it and pick or type a folder (`/` for subfolders). The add-item row has the same type-ahead and icons as the Spawner, plus Amount, StackCount and Full. Enter takes the top suggestion, then adds. Adding the same item with the same stack settings increases the existing row's amount. **Remove item** (or Delete) removes the selected row. Kits can also be created by importing (see below) or cloning. |
| Map | The SCUM island map and its points of interest from [scum-map.com](https://scum-map.com), shown with their permission. Tick layers on the left (bunkers, caves, outposts…). **All layers** and each section heading (Buildings, Bunkers…) have their own checkbox, half-ticked when only some of their layers are shown; clicking a half-ticked box shows the rest. On top: **Online players** (green, from the last `#ListPlayers`; **Refresh** re-reads it), **Offline players** at their last known position (grey), **Flags** (red pennants from `#ListFlags`; **Refresh** sends `#ListFlags 1 true`, `#ListFlags 2 true`, … reading each page from the clipboard until no new flags come back; **Paste** adds output you copied yourself, one page at a time), **Vehicles** (blue, from `#ListSpawnedVehicles`; **Refresh** or **Paste**; type in **Car ID** to keep only the cars whose ID starts with it, labelled with their IDs; once a single car is left, the map centres on it), **Player tracks** (where tracked players went, for one player or everyone, over the last N hours; a ring and time where a session starts, a dot and time where it ends, with no line joining separate sessions), and your saved locations. Scroll to zoom and drag to pan; the corner shows the X/Y under the cursor. Right-click a spot (or a marker, to use its exact height) to **Teleport target here**, copy the `#Teleport` command, save it as a location, or open the same spot on scum-map.com. **Grid** overlays: the game's sectors (B2), sectors with keypad lines, keypads (B27, numbered like a numeric keypad: 7-8-9 on top), or the 15×15 "map keypads" (A0 top-left, or inverted). The cursor and clicked spot always show their keypad. **Go to** accepts a sector, keypad, `X Y` coordinates or a player name. |
| Map → GM spawn | Right-click a spot → **GM spawn here** to spawn at that spot. The **GM spawn** sidebar tab sets what each action spawns and how many (up to 50); click a spot and press ▶ to repeat. The actions:<br>• **Animals**: the blueprints the game's own chat autocomplete lists (BP_Wolf, BP_Bear_Mutant…). Older names such as BP_Wolf3 in saved kits and macros are renamed on first start.<br>• **NPC squads** (Drifters/Guards), **zombie groups**: type lists come from the bundled spawn codes.<br>• **Beepers** (suicide-vest zombies), **Razors**, **cargo drops**.<br>• **Vehicles**.<br>• **A kit**: every `#SpawnItem` gets `Location "X Y Z"`.<br>**Ground height:** a spot clicked on open ground has no height. **Learned ground** comes first: every NPC or zombie climb that's accepted right after a refused step pins the ground there to one step (about 3 m), and SACC keeps it in `ground.json` in the data folder. A learned spot within 30 m beats every other source, and the next climb there starts 3 steps below it, so it usually lands in a few steps instead of ten. Repeat climbs narrow it further. The more you spawn, the more of the map is measured; **Forget** clears it. Otherwise SACC borrows a height from the closest enabled source within 300 m (learned spots further than 30 m compete here too):<br>• the nearest scum-map marker (or the clicked marker itself)<br>• your own last known position<br>• the nearest player's last known position<br>• the nearest vehicle (from the **Vehicles** map layer)<br>• the nearest saved location (Locations tab)<br>Otherwise it estimates the height from the 6 nearest scum-map markers at any distance; near our test spots this was within 1–8 m. scum-map heights of exactly 0 (about 220 fishing spots, villages and POIs) are treated as unknown. **Set height** uses one fixed Z instead of all of these.<br>**Find the ground for NPCs and zombies** (on by default): the game refuses `#SpawnArmedNPC` ("Cannot spawn at the specified location.") unless there's ground from about 3 m above to 20–30 m below the Z. It never spawns an NPC underground, and it spawns one in mid-air when Z is high. So SACC sends the squad starting 30 m below the height guess and climbs in 3 m steps. Each step without ground near it is refused and creates nothing, and SACC sees the refusal in SCUM's client log. The first step the game accepts is on the ground, and SACC stops there. If the whole ±30 m is refused, the guess was off, so SACC carries on up to +120 m, then tries −120 m to −30 m. The status line names the height guess and its source. Range, step and wait are profile settings (`gmClimbRange`, `gmClimbStep`, `gmClimbWaitMs`). Zombie groups and beepers are refused the same way, so they climb too; if only part of a group is refused, the later steps send just the refused count. Animals and razors aren't ground-checked by the game (measured: they're accepted 10 m underground), so they use the height as-is. See `spikes/npc-spawn-ground`. **Height offset** (cm) is added to whichever height is used, e.g. 200 to drop things from 2 m up. The right-click menu shows the height and where it came from. The `Location` forms come from WhalleyBot's task docs and are Unverified until tried. |
| Locations | Saved X/Y/Z bookmarks. **Capture my location** saves where you stand: SACC sends a bare `#Location` and reads the reply that SCUM.log echoes from chat. If the log can't be found, it falls back to `#Location <your SteamID64> true` and the clipboard; the game reads a bare `#Location true` as a player named "true"; type a player in Target to capture theirs instead. Teleport a target to a bookmark. |
| Macros | Plain-text scripts: one command per line, `wait 500` to pause, `//` for comments, and `{player}` / `{me}` variables. |
| Log | Every command sent or dry-run, with resend and edit. |
| Settings | Chat key, delays, paste vs type, your SteamID64, catalog editing. |

## The command catalog

Commands live in `data/commands.json`, not in code. Each entry has a confidence level:

- **Documented**: public server-host documentation agrees on the syntax.
- **Conflicting**: public sources disagree (e.g. `#Teleport` argument order, `#ListItems` vs `#ListAssets`).
- **Unverified**: community knowledge with no public source found, including the `true` clipboard flag on `#ListPlayers` and `#Location`.

If the game rejects a command, use **Settings → Edit command catalog**. This copies the catalog to your data folder, where your edits survive updates. Fix the template there, then press **Reload catalog**. Typing `#` in game chat shows the server's own command list.

## WhalleyBot

SACC never connects to WhalleyBot. It copies text in WhalleyBot's own formats for you to paste into its panel:

- **Macros → Copy as WhalleyBot task.** Comments and `#` lines carry over unchanged. Other conversions:
  - `wait 1500` becomes `#taskpause 2` (seconds, rounded up).
  - `{player}` becomes `<argument>`, the SteamID a pack passes when it runs `#ftask taskname [SteamID]`.
  - `{me}` becomes your SteamID64 from Settings.
  - Any other `{x}` becomes a `<x>` task variable, to define with `#var_x`.
  - Spawns over 10 split as usual.
  
  The status bar explains anything that needs a look. WhalleyBot sends a task's in-game lines as one batch, so a pause may not space out game commands.
- **Kits → Copy as WhalleyBot pack.** The kit's spawn lines, split and with magazines filled, for a pack in the Packs tab.
- **Map → right-click → WhalleyBot.** Copies the clicked spot in one of these forms:
  - `X Y Z` for the panel's location fields (taxis, dead drops, world events)
  - `#location "X Y Z"`
  - `#map X Y Z "Event"`
  - `#spawnrazor "X Y Z"`
  - `#spawnarmedNPC … Location "X Y Z"`
  
  **Add spot to coordinate list** collects several spots. **Copy coordinate list** then gives `#var_coords {X Y Z;X Y Z;…}`, and WhalleyBot picks one of those spots each time a task uses `"<coords>"`. A spot clicked on open ground has Z = 0 (or the Map Z setting); right-click a marker to use its height.

The catalog also has commands learned from WhalleyBot's task docs (Unverified until tried from SACC):
- `#SpawnItem … Location <player>` and `Location "X Y Z"`
- `#SpawnZombie`
- `#SpawnArmedNPC`
- `#SpawnRazor`
- `#ScheduleWorldEvent`
- `#DestroyAllItemsWithinRadius` (radius in meters; 25000 covers the map)

## Importing from SCUM Admin Helper

**Kits → Import SAH folder…** reads a whole folder tree, including `.zip` files inside it. Each subfolder becomes a kit group. It handles:

- **Pack exports** (`<Title> - SAH Pack (dd-mm-yyyy).json`) become kits. SAH stores pack items as its own numeric ids; `data/sah-item-ids.json` translates them to game spawn codes, and vehicles, zombies, animals and NPCs map to their full commands. The original id stays on each item, so an id the table can't translate is kept and can be filled in later with **Resolve ids**.
- **Player exports** (`SAH Players (dd-mm-yyyy).json`) are merged into Players. Newer data wins, and the export date is used as "last seen".
- **Text loadouts:** a title line followed by `#spawnitem <code> [n]` lines becomes a kit. Other `#` lines are kept as extra commands.

Re-importing the same file replaces its kits instead of duplicating them. Problems with individual files are written to the log.

## Map data

Map tiles, layers and markers come from **scum-map.com** (operated by NOTIMEO). NOTIMEO gave permission on 2026-10-30 for SACC to display them, provided it shows clear attribution and a link back, which is the "Map © scum-map.com" link on the Map tab. scum-map.com has no public API and no technical support for this use, so SACC is deliberately gentle. It caches marker data for a day (press **Refresh** to update early), downloads each map tile once into `mapcache\` in the data folder, and fetches at most four tiles at a time. If scum-map.com is unreachable, the last cached copy is used.

The map images are WebP. Windows 11 decodes them out of the box. If tiles stay blank, install **WebP Image Extensions** from the Microsoft Store.

## Item icons

The Spawner lists, favorites and kit items show SCUM's own inventory thumbnails, so look-alike items can be told apart. To replace or add one, save an in-game screenshot as `<SpawnCode>.png` (for example `Magazine_M16.png`) in **Settings → Open icons folder**, then press **Reload icons**. Your icons take precedence over the bundled ones. About 35 vehicles and two items ship without an icon.

## Data

Profile, log and your catalog copy live in `%LOCALAPPDATA%\SACC\`. Set `SACC_DATA_DIR` to use a different folder (handy for testing). SACC never reads or writes the old SCUM Admin Helper's files.

## Building

```
./build.ps1                 # tests + framework-dependent single-file exe in dist/
./build.ps1 -SelfContained  # bundles the .NET runtime
```

Requires the .NET 8 SDK. The framework-dependent build needs the .NET 8 Desktop Runtime on the target machine.

## Credits

Lurid Phoenix and Maddog Murphy. SCUM is a trademark of its respective owners; SACC is an unofficial fan tool.
