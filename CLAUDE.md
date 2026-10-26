# SACC: working notes for Claude

Standalone project: the cyberpunk monorepo's process rules (board, testtube, integrate, build.sh) do **not** apply here. It has its own git repo; the user runs and deploys it themselves.

- **Layout:** `src/Sacc.Core` (pure logic, unit-tested), `src/Sacc.App` (WPF UI and Win32 input), `tests/Sacc.Core.Tests` (xunit), `data/commands.json` (the command catalog, which is data and not code).
- **Build/test:** `dotnet test tests/Sacc.Core.Tests`, `./build.ps1` for a release zip.
- **Clean room:** never decompile or copy from SCUM Admin Helper; see CLEANROOM.md.
- **Testing the UI:** always set `SACC_DATA_DIR` to a scratch folder seeded with `{"settings":{"dryRun":true,"processName":"NoSuchGameProcess"}}`, and launch minimized. The user plays SCUM on this machine. A foreground SACC steals their keystrokes, and their real profile may have Dry run off, which makes Send a live send to their server.
- **Encoding:** never round-trip source files through Windows PowerShell 5.1's `Get-Content`/`Set-Content`. It reads UTF-8 without a BOM as ANSI and corrupts non-ASCII text (an em dash became "â€”" in 0.2.5). Use the Edit tool.
- **Debug vs release:** `build.ps1` publishes Release into `dist/`; it does not rebuild `bin/Debug`. Smoke-test the `dist/` exe, or run `dotnet build` first.
- **Command syntax:** any change to a catalog entry's syntax must keep or adjust its `confidence` honestly. Only the user can verify in-game.
