# Map data: base game vs scum-map.com

This file records which map features SACC treats as **base-game facts** (built by SACC itself) and which are **scum-map.com's own work** (used only with NOTIMEO's permission). See CLEANROOM.md for the permission itself.

Status key:
- ✅ **base game:** confirmed by the admin on SCUM's in-game map (2026-10-30), or by independent public sources.
- ⚙️ **ours:** SACC's own arithmetic or measurement.
- ✉️ **ask NOTIMEO:** scum-map.com's own compiled or original work.

## Base game: SACC builds these itself

The in-game SCUM map shows all of these (confirmed by the admin, 2026-10-30):

| Feature | Notes |
|---|---|
| **Sector grid lines and labels**: 5×5 sectors, rows Z, A, B, C, D (south to north), columns 0–4 (east to west), e.g. "B2" | Each sector is 304,800 world units. Community references use the same naming ([xgamingserver](https://xgamingserver.com/blog/wp-json/wp/v2/posts/22032), [scum.wiki.gg](https://scum.wiki.gg/wiki/Map)). |
| **Town, city and village names** (e.g. Samobor, Prigorje, Blato) | Names are base game. **Positions** for label placement are a separate question; see below. |
| **Bridge names** (Dr. Tuđman, Krk, Pelješac) | As above. |
| **Safe zones** (four circles) | Also available from scum-map's marker categories under the existing permission. |
| **No-build areas** | The zones are base game. **Their exact outlines** are a separate question; see below. |

**Built in SACC 0.3.1:** sector and keypad grids on the Map tab, the keypad readout under the cursor and at the clicked spot, the Go-to box, and Sector columns on Players and Locations (`src/Sacc.Core/SectorGrid.cs`, tested in `SectorGridTests`). Town, bridge and no-build geometry wait on NOTIMEO's reply.

SACC's own additions:

| Feature | Notes |
|---|---|
| ⚙️ **Keypad sub-grid** (3×3 per sector, numbered like a numeric keypad: 7-8-9 on top, e.g. "B27") | A generic convention applied to the base-game sectors. |
| ⚙️ **Whole-map 15×15 cell names** (A0 at top-left) | Grid arithmetic. |
| ⚙️ **World ↔ map coordinate conversion** | Our own measurement, checked against scum-map's Copy TP output, the old tool's sector labels, and player positions. |

### Geometry for base-game features

The game shows these features, but SACC needs numbers to draw them: a position per town or bridge label, and an outline per no-build area. The only ready-made source is scum-map.com's compiled data. Facts are base game, but a compiled dataset can carry EU database rights (NOTIMEO is Polish), so we ask NOTIMEO as a courtesy. If they decline, an admin can record positions in game (SACC's Locations tab and `#Location` already support this), or the labels are left out.

## scum-map.com's own work: ask NOTIMEO

| Feature | How their site delivers it |
|---|---|
| Hunting biomes (+ wildlife guide) | `images/map/hunting_biomes.svg` |
| No-build zone outlines (the zones are base game, the tracing is theirs) | `images/map/no_build_zones.svg` |
| Power lines (53 routes, 158 poles) | Built into their page code |
| Mech walk paths (~94 routes) | Built into their page code |
| Temperature grid (°C per keypad cell) | Built into their page code |
| In-game event zones (CTF, Cargo Assault, 6 Deathmatch variants): the event types are base game, the zone shapes their compilation | Built into their page code |
| Samobor street names | Built into their page code |
| Town/village/bridge label positions | Built into their page code |
| MGRS-style references (e.g. "B2 GH 187 957") | Computed in their page; their own scheme |
