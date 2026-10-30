# Clean-room provenance

SACC replaces the third-party "SCUM Admin Helper" (SAH, closed source, v0.4.0.5) installed at `%LOCALAPPDATA%\SCUM_Admin_Helper`.

## What was examined (behaviour only)

- **The install folder listing.** SAH is a ~126 MB single-file .NET Framework app with an Inno Setup uninstaller.
- **Its `user.config` setting *names*.** These show the feature areas: map display options, UI position, player list, "run as admin", favorites. No values were carried over. The file holds a licence email and key plus real player SteamIDs, and none of that is copied, stored or referenced anywhere in SACC.
- **Its log.** It records start-up details and a failing update check (the update endpoint now returns HTML instead of XML).

## Two-room decompilation (2026-10-25, after v0.1)

At the user's request, SAH was decompiled using a two-room process:

- **Dirty room.** A separate agent decompiled a copy of the exe with ILSpy, working only in `D:\Projects\Claude\modding\_sah_dirtyroom\`, outside this repo. It found .NET Framework WPF, Costura-embedded libraries and ConfuserEx obfuscation. It did not run the exe, did not read `user.config`, and did not analyse the licence subsystem.
- **The wall.** The only thing that crossed over is that agent's `SPEC.md`: a plain-English behaviour spec of game-facing facts. It covers command strings, SCUM file paths and formats, constants, key sequences and public hostnames, and contains no code.
- **Clean room.** SACC's authors read only `SPEC.md`, never the decompiled output. Anything SACC adopts from it is reimplemented from that description, and the commit that adopts it says so.

### Interoperability table: `data/sah-item-ids.json`

SAH's exported packs refer to items only by SAH's internal numeric ids. To import the user's own exports, the dirty room extracted a single two-column table: SAH id → game spawn code, or the full spawn command for vehicles, zombies, animals and NPCs. SAH's display names, categories and icons were excluded. The spawn codes are the game's own asset names.

The dirty room checked the table against the user's independently written `#spawnitem` text loadouts: 12 of 16 kits that exist in both match exactly. The table is used to read SAH files. Since 0.2.4 (at the user's request), its spawn codes, and nothing else, also feed the Spawner's type-ahead list of known item and vehicle codes. Those codes are the game's own asset names. No SAH display names, categories or icons are used.

### Item icons: `data/icons/`

The icons are SCUM's own inventory thumbnails, which SAH ships as plain, unencrypted PNG resources. Admins need the real in-game art to tell apart variants with near-identical names, so the user decided (2026-10-26) that SACC uses it, as SCUM's wiki and other admin tools do. SACC is a free, non-commercial tool that only works with SCUM. The dirty room extracted only the per-item icons, named by spawn code: 2,386 of 2,388 item codes and 19 of 54 vehicles. They were downscaled to at most 64 px for bundling; full-size originals stay in the dirty room. No SAH UI art, branding or map tiles were taken. SCUM's encrypted game files were never decrypted.

Icons in the user's data folder `icons\<SpawnCode>.png` override the bundled ones, so admins can replace or add any icon with their own in-game screenshots.

### Behaviour adopted from the spec

Kit counts above 10 are split into lines of at most 10, as SAH does. SACC's version is independently written and its chunk size is configurable.

## What was NOT done

- No decompiled code, structure or identifiers copied into SACC.
- No copying of its UI layout, strings, images, map assets, map tiles, or bundled data tables (item/vehicle display lists, icons), apart from the id → spawn-code interoperability table and the item icons described above.

## Where SACC's knowledge comes from

- The SCUM admin command syntax comes from public server-host docs (G-Portal, GameServerCheck, WinterNode) and the SCUM community wiki. Where they disagree, the catalog marks the entry **Conflicting**.
- The keyboard input approach (SendInput with scan codes, clipboard paste) is standard Win32 practice.
- Every line of SACC was written fresh for this project.

## Map (0.3.0)

SACC's Map tab shows scum-map.com's tiles, layer categories and markers. scum-map.com's terms (§7) forbid reuse without permission, so the user asked first. On 2026-10-30, NOTIMEO (Patrick Concord, Developer Outreach) replied that they have no concern with SACC displaying their map layers, with clear attribution and a link back to their site. They offer no API and no technical help.

How SACC uses their site:
- **Requests:** the same three public GraphQL queries their own map page sends (template, categories, markers), plus their CDN tile URLs.
- **Fetched live, not bundled:** nothing from scum-map.com is in the repo or the release zip. Data is cached per user, refreshed at most daily; tiles are cached once.
- **Coordinates:** the map↔world conversion comes from the template's own bounding box, checked against their Copy TP output (`#Teleport -523800 238199.9531 0` at map x 240, y 80).
- **Attribution:** "Map © scum-map.com", linking to their site, is always visible on the map.

None of this came from SAH. SAH's map tiles were never extracted or used.
