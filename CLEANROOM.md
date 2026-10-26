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

The dirty room checked the table against the user's independently written `#spawnitem` text loadouts: 12 of 16 kits that exist in both match exactly. The table is used only to read SAH files. It is not a browsable item catalog in SACC.

### Behaviour adopted from the spec

Kit counts above 10 are split into lines of at most 10, as SAH does. SACC's version is independently written and its chunk size is configurable.

## What was NOT done

- No decompiled code, structure or identifiers copied into SACC.
- No copying of its UI layout, strings, images, map assets, map tiles, or bundled data tables (item/vehicle display lists, icons), apart from the id → spawn-code interoperability table above.

## Where SACC's knowledge comes from

- The SCUM admin command syntax comes from public server-host docs (G-Portal, GameServerCheck, WinterNode) and the SCUM community wiki. Where they disagree, the catalog marks the entry **Conflicting**.
- The keyboard input approach (SendInput with scan codes, clipboard paste) is standard Win32 practice.
- Every line of SACC was written fresh for this project.

## Deliberately deferred

SAH has an interactive map. SACC v0.1 has none, because the game's map imagery is copyrighted art owned by the game's developer and publisher, and we won't bundle it. A later version could take a user-supplied map image and calibrate it with two known coordinates.
