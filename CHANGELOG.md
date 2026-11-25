# SACC changelog

Player-facing history of SACC — the SCUM Admin Command Console. Newest first.

Versions up to 0.4.41 are reconstructed from each release's own notes. The 0.4.42–0.7.3 run was recorded as one batch, so a few middle versions are grouped as closely as the record allows.

## 0.7.3

A big one: player management now lives on the map, base map layers stack with transparency, and the spawner knows far more about every item.

Fixed automatic position copying crouching your character instead of copying — it now holds Ctrl properly around the C press.

## 0.7.2

Removed the separate "Minimap map type" setting — the minimap now follows the main map's layer stack instead.

## 0.7.1

Changed the minimap to mirror the Map tab's base-layer stack: the same layers, transparency and order, temperature included.

## 0.7.0

Added stacked map base layers — turn Regular and Tactical on together, fade each with its own transparency, and reorder which sits on top.
Added Temperature as a map layer, so it stacks, fades and reorders with the rest instead of being a standalone toggle.

## 0.6.7

Changed the Kits add-item filter to stay put when you switch kits or add and remove items, so you can filter once and apply the same item to many loadouts.

## 0.4.42 – 0.6.6

The player list and item data got a big overhaul across these releases.

Added the player list to the Map tab's right bar — search, online/offline toggles, and the selected player highlights on the map.
Added a player details pop-out: double-click a list row or a map dot for every detail (Steam name, SteamID64, fame, account, gold, sector, location, last seen) and every action — go to, bring, locate, use as target, mark/move to dest, fame, money, mute, kick, ban, and per-player track show and export.
Added a Player database window with the full multi-column table (Refresh, Paste, Import SAH, Clear, online-only, search), its selection synced with the bar and the map.
Added item metadata to the spawner, kits and favorites — readable names and descriptions, weight, inventory grid size, rarity, trader buy and sell prices, fame to buy, and which DLC an item belongs to, keyed by spawn code.
Added a terrain-lift height offset to GM spawning, to raise spawns off the terrain grid's ground.
Changed the map's teleport target to the one selected player, so right-clicking a spot reads "Teleport &lt;name&gt; here" and moves that player.
Removed the separate Players tab; its list and actions moved onto the Map.

## 0.4.41

Added an automatic Ctrl+C timer so the position minimap refreshes where you are without you pressing it (only while SCUM is focused).

## 0.4.40

Added a map type for the minimap, independent of the Map tab (later folded into the shared layer stack in 0.7.x).

## 0.4.39

Changed the minimap so its map fills the window at any size and rotation.

## 0.4.38

Fixed map labels on the minimap so they stay upright when the minimap is rotated.

## 0.4.37

Added minimap rotation to your direction of travel, with a North arrow, and made the minimap mirror the main map's layers.

## 0.4.36

Added a position minimap overlay driven by SCUM's Ctrl+C coordinate copies, and themed the map's right-click menu.

## 0.4.35

Changed the Spawner categories: added Alcohol and Vehicle Parts and shrank the catch-all Other.

## 0.4.34

Fixed colour cans showing under Food, and themed checkbox text.

## 0.4.33

Added Favorites as its own Spawner category with a toggle button, and themed the pop-up windows.

## 0.4.32

Added click-to-select in the Spawner and a "To spawn" basket for queueing several items.

## 0.4.31

Added Spawner sub-tabs and category filters, and themed the buttons.

## 0.4.22 – 0.4.30

A run of theming and layout work: colour schemes applied across comboboxes, tabs, lists, scrollbars, tab backgrounds and inner content, a two-column Settings layout, and a general GUI best-practice pass.

Fixed colour schemes not applying, a Settings colour-scheme row overlap, and invisible header checkbox labels.

## 0.4.21

Added dark colour schemes and themed text fields.

## 0.4.20

Added colour schemes.

## 0.4.19

Added an optional DespawnLifetime on GM and catalog spawns, so spawned things can clean themselves up after a time.

## 0.4.18

Added a Temperature heatmap overlay on the map, estimated from terrain altitude.

## 0.4.17

Added mech patrol paths and location names to the map's island overlays.

## 0.4.16

Added a terrain height grid and island map overlays (threat zones, hunting areas), so GM spawns can land on real ground.

## 0.4.14

Added the Missions tab — a second map for planning multi-phase events with spawn pins, trigger and hold radii, reusable prefab scenes, a run sequencer and cleanup. SACC's code is now licensed under GPL-3.0.

## 0.4.13

Added player tracking: read `#ListPlayers` on a timer, record everyone's positions, draw their trajectories on the map, and export the tracks as CSV.

## 0.4.12

Added learned ground (SACC remembers measured ground heights to make later GM spawns land first try) and capture-your-location from SCUM's log.

## 0.4.10

Added filled containers to kits.

## 0.4.9

Added a Spawner container builder for `#SpawnInventoryFullOf` — pick a container and items and the game fills it to its limit.

## 0.4.8

Changed ground-finding so zombie groups and beepers climb to the ground like NPC squads do.

## 0.4.7

Fixed reading `#ListSpawnedVehicles` to match the exact layout the game prints.

## 0.4.6

Changed the GM height guess to be more accurate, widened the ground-climb range, and added a car-ID filter to the Vehicles layer.

## 0.4.5

Added ground-finding for GM NPC squads, so `#SpawnArmedNPC` lands on the surface instead of being refused.

## 0.4.4

Added the game's own item list (from `#ListItems`), a Vehicles map layer, real animal blueprint names, and capture-my-location.

## 0.4.3

Fixed the Players list to read the real `#ListPlayers` layout.

## 0.4.2

Added an All-layers checkbox and per-section checkboxes to the Map's layer panel.

## 0.4.1

Added paged `#ListFlags` reading and more GM height-source options.

## 0.4.0

Added the GM spawn panel and WhalleyBot export (copy spots and scripts in WhalleyBot's own formats).

## 0.3.3

Changed large spawns to split into lines of at most 10, and made magazines spawn full by default.

## 0.3.2

Added online-player and flag map layers, and player list filters.

## 0.3.1

Added sector and keypad grid overlays to the map.

## 0.3.0

Added the Map tab, with the SCUM island map and its points of interest from scum-map.com (shown with permission).

## 0.2.6

Added item icons, type-ahead item search, Fill-magazines, and creating kits from scratch.

## 0.2.3

Changed command sending to focus the game reliably and batch the chat input, and added a per-item StackCount.

## 0.2.0

Added named item kits (loadouts) and importing from SCUM Admin Helper.

## 0.1.0

First release. A desktop console for SCUM server admins: a searchable command catalog with a generated form, a live preview of the exact line, and a dry-run safety mode that types commands into the game's chat for you.
