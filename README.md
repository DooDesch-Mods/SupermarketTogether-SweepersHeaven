# SweepersHeaven

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/sweepersheaven](https://support.doodesch.de/sweepersheaven).

Collect dropped stolen products with a single broom sweep in Supermarket Together.

## Playing

Equip the broom, aim at a dropped product and left-click to collect nearby products. Right-click
throws them without collecting. The default sweep covers 2.5 metres around the aimed product and
collects at most five products. Items briefly lift before their normal game refund and removal.

The host owns collection, refunds and product motion. Install this version on the host and on each
player who wants synchronized sweeps and product motion. The host's settings apply to everyone;
each player's pickup key stays local. Players without the mod retain normal game collection.
When joining a host without support, synchronized sweeps stay disabled and the log explains why.

A moving Mini Transporter can collect a touched product when auto pickup is enabled. Both the
current vehicle and the older vehicle component are supported. The game's vacuum upgrade keeps
its native behavior. Optional bulk trash cleanup clears the current trash after one normal pickup.

## Install

Requires BepInEx 5.4.21 or compatible BepInEx 5. Put `SweepersHeaven.dll` in
`BepInEx/plugins/SweepersHeaven`, replacing the older DLL. Keep only one copy. Launch the game once
to create `BepInEx/config/SweepersHeaven.cfg`.

Downloads: [Thunderstore](https://thunderstore.io/c/supermarket-together/p/DooDesch/SweepersHeaven/)
or [GitHub releases](https://github.com/DooDesch-Mods/SupermarketTogether-SweepersHeaven/releases).

## Configuration

| Section / key | Default | Meaning |
| --- | --- | --- |
| General / Enabled | true | Enable shared sweeps on the host and participation on clients |
| Pickup Settings / Pickup Radius | 2.5 | Host radius in metres, 0.5 to 5 |
| Pickup Settings / Max Items to Pick | 5 | Host batch limit, 1 to 50 |
| Pickup Settings / Throw Items on Pickup | true | Lift collected products and allow right-click throws |
| Pickup Settings / Sweep Cooldown | 1 | Host minimum seconds between a player's sweeps, 0.15 to 5 |
| Controls / Pickup Key | Mouse0 | This player's collection key |
| Mini Transporter / Auto Pickup | true | Recover a product touched by a moving vehicle |
| Trash / Pick Up All Trash At Once | false | Clear the store's current trash after one pickup |

Existing gameplay configuration keys are preserved. Old debug/spawn keys are no longer used. The
old debug spawner could consume real stock; development fixtures now live in a separate test plugin.

## Development

This project is part of the local Supermarket Together workspace. Build with
`rtk proxy python -X utf8 Workspace/tools/workspace.py build --project SweepersHeaven` from the
workspace root. Normal builds do not deploy. Game references are local and are not distributed.
For a standalone checkout, point `SmtGameLibsDirectory` at `Supermarket Together_Data/Managed` and run
`dotnet build -c Release -p:SmtGameLibsDirectory="<path>"`. Game assemblies are not distributed with
the public source. GitHub CI uses a pinned private reference snapshot.

The collection protocol and real Unity input were exercised with two local game processes, including
repeat requests, refunds, product motion, joining and cleanup. Steam friends transport and real
Internet latency were not exercised in those checks.

Licensed under the MIT License. See [LICENSE.md](LICENSE.md).
