# SACC changelog

## 0.7.3

A big one: player management now lives on the map, base map layers stack with transparency, and the spawner knows far more about every item.

Added the player list to the Map tab's right bar — search, online/offline toggles, and the selected player highlights on the map.
Added a player details pop-out: double-click a list row or a map dot for every detail (Steam name, SteamID64, fame, account, gold, sector, location, last seen) and every action — go to, bring, locate, use as target, mark/move to dest, fame, money, mute, kick, ban, and per-player track show and export.
Added a Player database window with the full multi-column table (Refresh, Paste, Import SAH, Clear, online-only, search); its selection stays in sync with the bar and the map.
Added stacked map base layers: turn Regular and Tactical on together, fade each with its own transparency, and reorder which sits on top.
Added Temperature as a map layer, so it stacks, fades and reorders with the rest.
Added item metadata to the spawner, kits and favorites — readable names and descriptions, weight, inventory grid size, rarity, trader buy and sell prices, fame to buy, and which DLC an item belongs to, keyed by spawn code. Generated with SCUM Modding Studio and licensed like the icons and terrain grid.
Added a terrain-lift height offset to GM spawning, to raise spawns off the terrain grid's ground.
Changed the minimap to mirror the Map tab's layer stack — the same layers, transparency and order.
Changed the Kits add-item filter to stay put when you switch kits or add and remove items, so you can filter once and apply the same item to many loadouts.
Removed the separate Players tab; its list and actions moved onto the Map.
Removed the separate "Minimap map type" setting, since the layer stack now drives the minimap.
Fixed automatic position copying crouching your character instead of copying — it now holds Ctrl properly around the C press.
