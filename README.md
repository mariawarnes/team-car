# Team Car for GTA V

A single-player ScriptHookVDotNet mod that gives the player a cycling-style
support car. The car follows you on foot, on a bicycle, or in another vehicle.

## Features

- `F10` summons or dismisses the team car.
- Uses GTA V's stock **Zirconium Stratum** (`stratum`).
- A support driver keeps a configurable gap and adapts to your speed.
- The car slows down near you, avoids aggressive driving, and recovers if stuck.
- If it becomes lost or destroyed, it can automatically return.
- Protected from Cycletopia's ambient-traffic-to-bicycle conversion.
- All important keys, distances, colours, and behaviour are configurable.

## Requirements

- GTA V Legacy single-player
- [Script Hook V](http://www.dev-c.com/gtav/scripthookv/)
- [ScriptHookVDotNet](https://github.com/scripthookvdotnet/scripthookvdotnet)

Use a ScriptHookV and ScriptHookVDotNet build compatible with your current GTA V
version. Do not use this in GTA Online.

## Install

1. Install Script Hook V and ScriptHookVDotNet in the folder containing
   `GTA5.exe`.
2. Create a `scripts` folder there if it does not exist.
3. Copy both files from this repository's `scripts` folder into it:
   - `TeamCar.3.cs`
   - `TeamCar.ini`
4. Start story mode and press `F10`.

ScriptHookVDotNet compiles `.3.cs` scripts when loading them, so no separate build
step is required. Open its console with `F4` and use `Reload()` after editing the
configuration.

## Using `stratum.ytd` and `stratum+hi.ytd`

Those two files are texture dictionaries, not a complete add-on vehicle. This mod
already requests the stock model named `stratum`, so replacement textures with
those exact names are compatible.

Install the texture replacement with OpenIV into a mods-folder copy of the archive
where your edition of GTA V stores the Stratum. Enable **Edit mode**, use OpenIV's
**Search** for `stratum.ytd`, and replace both `stratum.ytd` and
`stratum+hi.ytd` in the matching vehicle archive. Make a backup first. Archive
locations vary between game editions and replacement packs, so searching is safer
than assuming a fixed path.

If the download also contains `stratum.yft` and `stratum_hi.yft`, install those
matching model files alongside the textures. Do not rename unrelated `.ytd` files
to these names.

## Configuration

Edit `scripts/TeamCar.ini`. Distances are metres and speeds are metres per second
(`10 m/s` is about `36 km/h`).

The default driving style is deliberately cautious. GTA's AI may still make
occasional poor traffic decisions; increase `FollowDistance` if the car gets too
close during fast descents.

## Troubleshooting

- **Nothing happens on F10:** inspect `ScriptHookVDotNet.log` in the GTA V folder
  and confirm the filename remains `TeamCar.3.cs`.
- **The stock paint/texture appears:** the replacement `.ytd` files are not in the
  archive currently loaded by your edition of the game.
- **The car cannot reach a trail:** press F10 twice when back on a road, or enable
  `RespawnWhenLost`.
- **Reloading leaves an old car:** dismiss it with F10 before `Reload()` when
  possible. The script also cleans up on abort.

## Development

The distributable is intentionally a source script so users do not need a matching
SDK DLL at build time. The code targets ScriptHookVDotNet's v3 API. Developers can
also run `dotnet build TeamCar.csproj`; the resulting DLL is written to `dist`.
