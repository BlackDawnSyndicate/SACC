# SACC: working notes for Claude

Standalone project: the cyberpunk monorepo's process rules (board, testtube, integrate, build.sh) do **not** apply here. It has its own git repo; the user runs and deploys it themselves.

- **Layout:** `src/Sacc.Core` (pure logic, unit-tested), `src/Sacc.App` (WPF UI and Win32 input), `tests/Sacc.Core.Tests` (xunit), `data/commands.json` (the command catalog, which is data and not code).
- **Build/test:** `dotnet test tests/Sacc.Core.Tests`, `./build.ps1` for a release zip.
- **Clean room:** never decompile or copy from SCUM Admin Helper; see CLEANROOM.md.
- **Testing the UI:** always set `SACC_DATA_DIR` to a scratch folder seeded with `{"settings":{"dryRun":true,"processName":"NoSuchGameProcess"}}`, and launch minimized. The user plays SCUM on this machine. A foreground SACC steals their keystrokes, and their real profile may have Dry run off, which makes Send a live send to their server.
- **Command syntax:** any change to a catalog entry's syntax must keep or adjust its `confidence` honestly. Only the user can verify in-game.
