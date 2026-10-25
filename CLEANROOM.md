# Clean-room provenance

SACC replaces the third-party "SCUM Admin Helper" (SAH, closed source, v0.4.0.5) installed at `%LOCALAPPDATA%\SCUM_Admin_Helper`.

## What was examined (behaviour only)

- **The install folder listing.** SAH is a ~126 MB single-file .NET Framework app with an Inno Setup uninstaller.
- **Its `user.config` setting *names*.** These show the feature areas: map display options, UI position, player list, "run as admin", favorites. No values were carried over. The file holds a licence email and key plus real player SteamIDs, and none of that is copied, stored or referenced anywhere in SACC.
- **Its log.** It records start-up details and a failing update check (the update endpoint now returns HTML instead of XML).

## What was NOT done

- No decompiling, disassembling or resource extraction of `SCUM Admin Helper.exe`.
- No copying of its UI layout, strings, images, map assets or data files.

## Where SACC's knowledge comes from

- The SCUM admin command syntax comes from public server-host docs (G-Portal, GameServerCheck, WinterNode) and the SCUM community wiki. Where they disagree, the catalog marks the entry **Conflicting**.
- The keyboard input approach (SendInput with scan codes, clipboard paste) is standard Win32 practice.
- Every line of SACC was written fresh for this project.

## Deliberately deferred

SAH has an interactive map. SACC v0.1 has none, because the game's map imagery is copyrighted art owned by the game's developer and publisher, and we won't bundle it. A later version could take a user-supplied map image and calibrate it with two known coordinates.
