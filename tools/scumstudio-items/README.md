# `asset items` — item-metadata producer for SCUM Modding Studio

SACC's **item metadata** (the Spawner details panel: friendly name, description, weight, inventory size, trader buy/sell
price and fame-to-buy) reads a `game-item-meta.json`. SACC does not read the game's files; this file is produced by the
free, open-source **[SCUM Modding Studio](https://github.com/ALLAWI32/SCUM-Modding-Studio)** (MIT), which already decodes
SCUM's cooked item assets and economy table. `AssetCommands.Items.cs` here adds the `scumstudio asset items` command that
reads them and writes the JSON SACC reads.

It is kept here so the contribution is durable in SACC's own repo and easy to re-apply or upstream; SACC itself does not
build it. It is the sibling of `tools/scumstudio-heights/` (terrain grid) and `tools/scumstudio-mapfeatures/` (map overlays).

## Apply it to SCUM Modding Studio

1. Clone the tool: `git clone https://github.com/ALLAWI32/SCUM-Modding-Studio.git`
2. Copy `AssetCommands.Items.cs` into `src/ScumStudio.Cli/Commands/` (it is another part of the existing `AssetCommands` partial class).
3. In `src/ScumStudio.Cli/Commands/AssetCommands.cs`, register it in `Build()` next to the other asset subcommands:
   ```csharp
   asset.AddCommand(BuildLandscapeLayers());
   asset.AddCommand(BuildItems());   // <-- add this line
   ```
4. Build the CLI: `dotnet build src/ScumStudio.Cli`.

The command uses only the tool's own public API — `ModdableAssets.Find`, `AssetEditing.ReadValues`,
`DataTableEdits.TradeableTable`, and the `Tunable`/`ModdableAsset` types — plus the existing `AssetCommands` CLI helpers
(`SourceArgument`, `AesOption`, `OpenCatalog`, `RunGuarded`), so it is a candidate for an upstream pull request.

## Run it

```
scumstudio asset items "D:\SCUM\Content\Paks" -o game-item-meta.json
```

- `<source>`: the game's `Paks` folder (or a single `.pak`, or a loose `Content` folder).
- `--kind` / `-k`: only one kind (`Item`, `Weapon`, `Magazine`, `Attachment`, `Ammo`, `Projectile`, `Vehicle`); default = every kind.
- `-o`: output file (default `game-item-meta.json`).
- `--raw`: also write `game-item-meta.raw.json` — every item's full property bag and its trade row (for mapping, see below).

Reading the stock paks needs the game's AES key, which SCUM Modding Studio finds and manages (its setup, or the
`SCUMSTUDIO_AES_KEY` environment variable for the CLI). Then drop the file into the SACC data folder (or bundle it at
`data/game-item-meta.json`), and SACC's Spawner shows an item's stats when you select it.

## Where the data comes from, and the mapping

SCUM stores the game designers' values as **tagged properties**, so the property/member names are the game's, not this
tool's:

- **Per item** — name, description, weight, inventory size, rarity — from the item's package and its `_ES` entity setup
  (`AssetEditing.ReadValues`, which merges values inherited from `ItemEntitySetup`).
- **Trade numbers** — buy price, sell price, fame-to-buy — once from the economy DataTable
  `/Game/ConZ_Files/Economy/Table_TradeableDesc`, whose rows are named after the item class (`Weapon_AK47_C`), joined to
  each item by its spawn code.

The fields are matched by the candidate property-name lists near the top of `AssetCommands.Items.cs`. Against the current
retail build these members resolve as: weight `Weight` (stored in **grams**, divided by 1000 to kilograms); inventory
size `GridInventoryColumnSpan` / `GridInventoryRowSpan` (whole cells); rarity `_rarity` (an `EItemRarity::…` enum, the
prefix stripped); and, in the trade table, buy `BasePurchasePrice`, sell `BaseSalePrice`, fame `RequiredFamePoints`. These
are the reliable output.

Name and description (`Caption` / `Description`) are **best-effort**: for many items they are localized string-table
references (`[…:KEY]`) rather than readable text, which are skipped — SACC then falls back to a friendly name derived from
the spawn code. Fields that genuinely do not exist on an item are omitted too: stackable ammo carries no grid span or
rarity, and non-tradeable items have no trade row (so no price or fame). On a full retail run (2848 items) that yields
roughly: weight 2728, size 1953, rarity 1141, trader buy/sell/fame 2272, readable name 1622.

The command is **self-revealing** so the mapping is correctable without the game files to hand: every run prints the
distinct item-attribute names and trade-table member names it saw, and the mapped-field coverage counts. If a field you
want comes out empty, run once with `--raw`, read the real property name out of `game-item-meta.raw.json`, add it to the
matching `*Candidates` list in `AssetCommands.Items.cs`, and re-run. No field is ever guessed into the output — an
unmapped field is simply omitted, and SACC shows a dash.

## The `game-item-meta.json` format

JSON, keyed by spawn code. The reader is `ItemMetaTable` in `src/Sacc.Core/ItemMeta.cs`; unknown fields are ignored and
any omitted field reads as "no value".

```json
{
  "source": "SCUM Modding Studio: Paks",
  "readAt": "2026-11-12T00:00:00.0000000Z",
  "items": {
    "Weapon_AK47": {
      "name": "AK-47",
      "weightKg": 3.5,
      "width": 7,
      "height": 3,
      "rarity": "VeryRare",
      "traderBuy": 11200,
      "traderSell": 2333,
      "fameToBuy": 150
    }
  }
}
```

The item key is exactly the code `#SpawnItem` takes. `width`/`height` are inventory grid cells. Prices and fame are the
stock economy's values; a server's own `EconomyOverride.json` can differ (SACC could layer that in later from the user's
server config).
