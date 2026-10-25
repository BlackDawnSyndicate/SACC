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
| Players | **Refresh from game** sends `#ListPlayers true` and reads the clipboard. **Paste list** parses output you copied yourself. Per-player buttons: go to, bring, locate, fame, money, mute, kick, ban. |
| Spawner | Item and vehicle spawning with favorites (double-click to spawn). **Search in game** sends `#ListItems <filter>`. |
| Locations | Saved X/Y/Z bookmarks. Capture a player's location, or teleport a target to a bookmark. |
| Macros | Plain-text scripts: one command per line, `wait 500` to pause, `//` for comments, and `{player}` / `{me}` variables. |
| Log | Every command sent or dry-run, with resend and edit. |
| Settings | Chat key, delays, paste vs type, your SteamID64, catalog editing. |

## The command catalog

Commands live in `data/commands.json`, not in code. Each entry has a confidence level:

- **Documented**: public server-host documentation agrees on the syntax.
- **Conflicting**: public sources disagree (e.g. `#Teleport` argument order, `#ListItems` vs `#ListAssets`).
- **Unverified**: community knowledge with no public source found, including the `true` clipboard flag on `#ListPlayers` and `#Location`.

If the game rejects a command, use **Settings → Edit command catalog**. This copies the catalog to your data folder, where your edits survive updates. Fix the template there, then press **Reload catalog**. Typing `#` in game chat shows the server's own command list.

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
