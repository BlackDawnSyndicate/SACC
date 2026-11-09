# `asset heights` — terrain grid producer for SCUM Modding Studio

SACC's **Terrain grid** source (see the main README) reads a `terrain.scgh` height grid. SACC does not read the
game's files; this grid is produced by the free, open-source **[SCUM Modding Studio](https://github.com/ALLAWI32/SCUM-Modding-Studio)**
(MIT), which already decodes SCUM's landscape tiles. `AssetCommands.Heights.cs` here adds the `scumstudio asset heights`
command that walks those decoded heights onto a regular world grid and writes the `.scgh` file SACC reads.

It is kept here so the contribution is durable in SACC's own repo and easy to re-apply or upstream; SACC itself does not
build it.

## Apply it to SCUM Modding Studio

1. Clone the tool: `git clone https://github.com/ALLAWI32/SCUM-Modding-Studio.git`
2. Copy `AssetCommands.Heights.cs` into `src/ScumStudio.Cli/Commands/`.
3. In `src/ScumStudio.Cli/Commands/AssetCommands.cs`, register it in `Build()` next to the other asset subcommands:
   ```csharp
   asset.AddCommand(BuildLandscapeLayers());
   asset.AddCommand(BuildHeights());   // <-- add this line
   ```
4. Build the CLI: `dotnet build src/ScumStudio.Cli` (or `scripts\build.ps1 -Publish` for a self-contained exe).

The command uses only the tool's own public API (`LandscapeExtractor`, `LandscapeSurface.SampleHeight`,
`WorldTileInfo`, `WorldIndex`) and the existing CLI helpers, so it is a candidate for an upstream pull request.

## Run it

```
scumstudio asset heights "D:\SCUM\Content\Paks" --step 400 -o terrain.scgh
```

- `<source>`: the game's `Paks` folder (or a single `.pak`, or a loose `Content` folder).
- `[tile...]`: optional landscape tiles or cell names (`A_0`); none = the whole island.
- `--step`: grid spacing in world centimetres (default 400 = 4 m).
- `-o`: output file (default `heights.scgh`).

Reading the stock paks needs the game's AES key, which SCUM Modding Studio finds and manages (its setup, or the
`SCUMSTUDIO_AES_KEY` environment variable for the CLI). Then load the file in SACC with **GM spawn → Terrain grid → Load…**.

## The `.scgh` format

Little-endian, version 1. The reader is `GroundGrid` in `src/Sacc.Core/GroundGrid.cs`; the two implementations are kept
byte-for-byte identical.

| offset | type | field |
|---|---|---|
| 0 | char[4] | magic `"SCGH"` |
| 4 | u16 | version (1) |
| 6 | u16 | flags (0) |
| 8 | f32 | cell size (world cm between columns/rows) |
| 12 | f32 | origin X (world cm of column 0) |
| 16 | f32 | origin Y (world cm of row 0) |
| 20 | u32 | width (columns, X) |
| 24 | u32 | height (rows, Y) |
| 28 | f32 | zMin cm |
| 32 | f32 | zMax cm |
| 36 | u16 | nodata (`0xFFFF`) |
| 38 | u16 | reserved |
| 40 | u16[width*height] | row-major (`y*width+x`); `0xFFFF` = no terrain, else `zCm = zMin + raw*(zMax-zMin)/65534` |

World cm are UE world coordinates — the same X/Y that `#Location` and `#SpawnItem … Location` use — so SACC looks a
height up with no projection.
