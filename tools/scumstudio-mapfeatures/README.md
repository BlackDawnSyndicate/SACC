# `asset map-features` — map-overlay producer for SCUM Modding Studio

SACC's **Island overlays** (threat zones, hunting areas on the Map tab) read a `map-features.json`. SACC does not read
the game's files; this file is produced by the free, open-source **[SCUM Modding Studio](https://github.com/ALLAWI32/SCUM-Modding-Studio)**
(MIT), which already decodes SCUM's static level data. `AssetCommands.MapFeatures.cs` here adds the
`scumstudio asset map-features` command that reads the island's spawn-place data and writes the JSON SACC reads.

It is kept here so the contribution is durable in SACC's own repo and easy to re-apply or upstream; SACC itself does not
build it. It is the sibling of `tools/scumstudio-heights/` (the terrain grid producer).

## Apply it to SCUM Modding Studio

1. Clone the tool: `git clone https://github.com/ALLAWI32/SCUM-Modding-Studio.git`
2. Copy `AssetCommands.MapFeatures.cs` into `src/ScumStudio.Cli/Commands/`.
3. In `src/ScumStudio.Cli/Commands/AssetCommands.cs`, register it in `Build()` next to the other asset subcommands:
   ```csharp
   asset.AddCommand(BuildHeights());
   asset.AddCommand(BuildMapFeatures());   // <-- add this line
   ```
4. Build the CLI: `dotnet build src/ScumStudio.Cli`.

The command uses only the tool's own public API (`SpawnPlaces.ReadFrom`, `SpawnPlaces.SettingName`) and the existing CLI
helpers, so it is a candidate for an upstream pull request.

## Run it

```
scumstudio asset map-features "D:\SCUM\Content\Paks" -o map-features.json
```

- `<source>`: the game's `Paks` folder (or a single `.pak`, or a loose `Content` folder).
- `-o`: output file (default `map-features.json`).

Reading the stock paks needs the game's AES key, which SCUM Modding Studio finds and manages (its setup, or the
`SCUMSTUDIO_AES_KEY` environment variable for the CLI). Then drop the file into the SACC data folder (or bundle it at
`data/mapfeatures/map-features.json`). It also prints the distinct zone-settings and biome names it found.

## The `map-features.json` format

Little-endian is irrelevant — it is JSON, version 1. The reader is `MapFeatures` in `src/Sacc.Core/MapFeatures.cs`.

```json
{
  "version": 1,
  "huntingAreas": [ { "x": cm, "y": cm, "r": cm, "biome": "BD_Village" } ],
  "threatZones":  [ { "x": cm, "y": cm, "rx": cm, "ry": cm, "yaw": deg, "settings": "MTZ_Settlement_Large" } ]
}
```

All coordinates are UE world centimetres — the same X/Y that `#Location` uses — so SACC draws them with no projection.
Hunting areas are circles (centre + radius); threat zones are ellipses (centre + semi-axes + yaw). The zone tier comes
from the `settings` prefix: `HTZ_` = High, `MTZ_` = Medium, `LTZ_` = Low.
