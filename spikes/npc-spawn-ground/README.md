# Spike: when does the game refuse an NPC spawn?

`#SpawnArmedNPC BP_x n Location "X Y Z"` sometimes answers **Cannot spawn at the specified location.** (once per NPC). The hypothesis is that the game traces down from the given Z and refuses when the ground is too far below, or when Z is underground.

## What we know from SCUM's client log (`%LOCALAPPDATA%\SCUM\Saved\Logs\SCUM.log`)

- A refusal is logged as `LogSCUM: Sender player state null for message Cannot spawn at the specified location.!`, once for each NPC requested.
- A successful NPC spawn logs **nothing**. Items log `Loading X...` / `Spawned X.`.
- An unknown blueprint logs `'BP_Wolf3' is not allowed to be spawned.`
- The client's config dump includes `scum.EnableSpawnOnGround:0`. This is probably the player respawn mode, but it may be related.

## Results

**Run 1 (2026-11-01 09:29)**, level-1 Drifter, origin X −524610 Y −276130 Z 13832 (flat ground). Data: `results-20261101-092931.csv`.

| Height relative to the ground at the spot (40 m east) | Result |
|---|---|
| −10 m | refused |
| −3 m, −1 m, −0.3 m, 0 | spawned |
| +0.3 m … +20 m | spawned |
| +50 m | refused |

| Distance east, at the origin's Z | Result |
|---|---|
| 10, 50, 100, 250 m | spawned |
| 500 m | **refused** |
| 1 km | spawned |

What this suggests:
- **It is a ground check, not a range limit.** 1 km away spawned while 500 m was refused, both at the same Z. The ground at 500 m was presumably too far above or below that Z.
- The accepted window runs from **somewhere between 3 and 10 m below ground** to **somewhere between 20 and 50 m above it**. That fits a downward trace that starts a few metres above Z and reaches a few tens of metres down.
- For SACC's GM spawn: aiming a little above the best-guess ground (e.g. Height offset +5 m to +10 m) tolerates guesses that are several metres off in either direction. Whether NPCs snap to the ground or drop from that height (fall damage) is still to be observed.

**Run 2 (2026-11-01 09:40)**, narrowing, new origin X −111141 Y −65943 Z 36992. Data: `results-20261101-094015.csv`.

| Height relative to the origin's Z (40 m east) | Result |
|---|---|
| −4 m … −9 m | refused |
| +22 m, +25 m, +30 m | spawned |
| +35 m … +45 m | refused |

**Combined:** the game accepts Z when the ground lies between **about 30–35 m below Z** and **about 3–4 m above Z**. That's a window roughly 33–38 m tall: a downward trace that starts 3–4 m above the given point and runs about 33–38 m down. The ground 40 m away isn't exactly at the origin's height, so each edge is uncertain by however much the terrain differs there.

Using it:
- **A refusal costs nothing.** No NPC is created, and it tells us the ground is *outside* [Z − ~33 m, Z + ~3 m].
- **A ladder covers rough terrain.** Try Z = guess + 15 m first, then step by about 30 m (overlapping windows) down and up: +15, −15, +45, −45 … Each step is about 2 s while SACC waits for the log, and five steps cover ±75 m around the guess.
- **Piggyback:** if a spawn at the top of the window lands the NPC on the ground (rather than dropping it 30 m), the game's own trace already finds the surface. Then SACC only needs a window that contains the ground, not an accurate height. *Still to observe:* did the Drifters at +20–30 m appear on the ground, or fall?

**Run 3 (2026-11-01 09:49)**, `-Offset 5` so the ground at the spot is about the player's own height. Data: `results-20261101-094908.csv`. +10 m and +20 m spawned; +30 m was refused. Observed in game: **no NPC ever appeared underground. They all appeared at or above ground.**

**Working rule:** the game accepts Z when the ground lies between about 3–4 m above Z and 20–30 m below it. It never puts an NPC under the surface, so a Z up to 3 m low is corrected onto the ground. A Z above ground is used as-is: the NPC spawns in the air and falls (confirmed in game). So the game's trace only *checks* for ground; it doesn't place the NPC on it.

**Design consequence: climb from below.** Refusals are free and successes are final, so the way to land on the ground (not 20 m up) is to start *below* the possible ground and step *up*. Every step until the ground is within reach is refused. The first accepted step is within one step of the surface, so the NPC appears at or just above it. With 3 m steps and the log checked after each send (refusals appear in the log at once), a ±30 m uncertain guess costs about 20 refusals, roughly 15 s worst case. A good guess (nearest marker, player or vehicle within 300 m) costs a few. Starting high and stepping down would succeed on the first window that contains the ground, up to about 25 m above it.

**Other spawn types (2026-11-01 09:54).** `#SpawnZombie`, `#SpawnAnimal BP_Chicken` and `#SpawnRazor` 10 m below the ground 5 m away were all accepted (no refusal). Only `#SpawnArmedNPC` is ground-checked.

**Built into SACC (GM spawn → Find the ground for NPC squads), live test 09:59.** The guess was the player's own Z; the spot was 10 m east of the player. The game refused 10 steps (30 m → 3 m below), each in about 0.5 s, then accepted Z 37256. SACC stopped there after about 8 s. **The Drifter appeared on the ground** (confirmed in game).

Next: narrow both edges, e.g. `-Heights -300,-350,2600,2800,3000,3200,3400 -SkipDistance`. For tighter edges, use `-Offset 2` so the spot's ground is effectively your own height.

## Running it

1. Stand still on **flat, open ground** with nothing for 40 m to the east (+X), then run `#ListPlayers` in game so your position is in the log.
2. Preview the lines it will send:
   `.\Run-Spike.ps1 -SteamId <your SteamID64> -DryRun`
3. Run it with chat closed, and don't touch the keyboard while it types (about a minute):
   `.\Run-Spike.ps1 -SteamId <your SteamID64>`

It sends one level-1 Drifter per test point, reads the log after each one, prints `spawned` / `REFUSED`, and saves `results-*.csv` beside the script.

- **Height sweep:** 40 m east of you, at your Z −10 m … +50 m. If the ground-distance idea is right, low offsets spawn and high ones are refused. The point where it switches is the limit.
- **Distance sweep:** your Z at 10 m … 1 km east. A refusal far away at ground level points to a range limit instead. The terrain isn't flat over 1 km, so treat this sweep as a hint only.

The Drifters are hostile. For a harmless control run (do animals follow the same rule?), use:
`-Template '#SpawnAnimal BP_Chicken 1 Location "{xyz}"'`

Narrow it down afterwards with your own steps, e.g. `-Heights 300,350,400,450,500 -SkipDistance`.

**Zombies are refused too (SCUM's log, 2026-11-01 22:04–22:08 UTC).** `#SpawnZombie` groups of 10 drew bursts of 10 `Cannot spawn at the specified location.`, once per zombie, so the "accepted 10 m underground" row above was not the whole story. Their window may differ from NPCs'. SACC 0.4.8 climbs zombie groups and beepers the same way. If only part of a group is refused, only that part moves to the next step. *Still to observe in game:* do climbed zombies land on the ground, or under it?
