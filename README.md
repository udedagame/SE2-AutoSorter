# AutoSorter for Space Engineers 2

[Русская версия](README-RU.md)

**One click in the terminal inventory: iron goes to iron, nickel to nickel, components to the components container.**

AutoSorter adds a **Sort** button (three bars) to the header of every inventory list in the terminal, next to the game's own "show/hide empty" and "connected only" buttons. Press it and the containers of that list are tidied up. Nothing happens in the background unless you ask for it.

It is a manually installed **plugin** (loaded with the game's `-plugins:` launch option), not a Steam Workshop mod. BuildPlanner is not required.

## What the button does

- **Same items together.** Every kind of item gets a *home*: the container that already holds the most of it. Smaller piles of that kind are brought there.
- **Components go to the components container.** If the home is a mixed container (ore *and* components), and another container holds only components and has room, that one becomes the home.
- **Spill only when full.** If the home is full, a pile goes to a *bigger* pile of the same kind elsewhere; if there is no room anywhere, it stays where it is.
- **Calm.** Items already at home are never moved, nothing is pushed into a full container, and a second press does (almost) nothing.
- **Game-checked transfers.** Each transfer is exactly what dragging an item with the mouse does, so the game itself checks conveyors, access and capacity. After each transfer the plugin waits until the screen shows the result before the next one.
- **Only cargo containers** of the list you pressed it in. Empty containers hidden by "hide empty" are used too. The character inventory, refineries, assemblers, traders and connectors are never touched.
- **Only ore, ingots and components are sorted.** Tools and weapons (vanilla *and* modded), consumables, datapads, quest items and schematics stay exactly where they are.
- **Exclude a container** by adding `[NoSort]` or `[Не сортировать]` anywhere in its name.

## Tools stay in your backpack

The game's quick deposit (**Alt + right mouse button** on a container) unloads everything except a short list of "essential" items, which does not include modded tools. AutoSorter adds every tool and weapon the game has registered (including mods such as AreaTools) to that list for the duration of the deposit, so your tools stay in the backpack.

## Installation

1. Download **`AutoSorter-0.3.0.zip`** from the release's **Assets** (not GitHub's "Source code" archives).
2. Close the game. Extract the whole **AutoSorter** folder to a permanent place, for example `D:\SE2Mods\AutoSorter`. Keep all DLLs together.
3. In Steam: **Space Engineers 2 → Properties → General → Launch Options**, enter:

   ```text
   "-plugins:D:\SE2Mods\AutoSorter\AutoSorter.dll"
   ```

   With BuildPlanner, use **one** argument with both paths separated by `;`:

   ```text
   "-plugins:D:\SE2Mods\BuildPlanner\BuildPlanner.dll;D:\SE2Mods\AutoSorter\AutoSorter.dll"
   ```

   Use your real paths. If you already have a `-plugins:` argument, add the path to it with `;` — do not add a second `-plugins:`.
4. Start the game, open a container, **Inventory** tab: the three-bar button is at the top right of each list.

`AutoSorter.log` next to the DLL should contain `AutoSorter 0.3.0 starting` and `Hook installed on InventoryList.InitializeComponent`.

**Updating from 0.2.x:** close the game and replace the files. Your old `AutoSorter.json` keeps working; with it the button is on and the old timed auto-sort is **off**.

**Uninstall:** remove the path from `-plugins:`. The plugin adds no blocks and changes no game files; items it moved stay where they are in your save.

## Settings

`AutoSorter.json` next to the DLL (restart the game after editing). Missing keys use the defaults.

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Master switch |
| `ManualButton` | `true` | The Sort button |
| `AutoSort` | `false` | Legacy background sorter (every `IntervalSeconds`). It is the 0.2.1 sorter **without** the 0.2.2 docking fixes: it may move cargo between a station and a docked ship. Not recommended |
| `StepDelayMs` | `50` | Pause between transfers of one press, 16–2000 ms |
| `TransferTimeoutMs` | `5000` | Max wait for the game to confirm and show a transfer; the press stops safely when exceeded |
| `MaxTransfersPerPress` | `500` | Safety cap for one press |
| `DryRun` | `false` | Log what would be moved, move nothing |
| `IntervalSeconds`, `MaxTransferAttempts`, `MaxItemChecks`, `SoftTimeBudgetMs` | | Only used by `AutoSort` |

## Log

`AutoSorter.log` lists, for every press: the containers used (numbered top to bottom), skipped inventories and why, the decision for each item kind (`sort` / `KEEP IN PLACE`), and every transfer (`Move 173 x SteelTube_Item: #1 -> #2`). Please attach it when reporting a problem, with your game version.

## Limits

- Local worlds (server running inside the game process). Remote and dedicated servers are not supported.
- Containers with more than one inventory (production blocks) are not sorted.
- The button sorts every container shown in that list. If the list shows several grids (for example your station **and** a docked ship), pick a single grid in the list's dropdown first.
- Built and tested against game assembly 4.0.0.3165. A game update may change the internal API; the plugin then logs the failed hook and the game keeps working without the button.

## Building from source

Requires the .NET 9 SDK and your own installed copy of Space Engineers 2.

```powershell
dotnet build AutoSorter.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2"
dotnet run --project tests/AutoSorter.Tests.csproj -c Release
```

`package.ps1` builds, runs the tests and creates `AutoSorter-0.3.0.zip`; `build.cmd` does the same with a double click and installs into `D:\SE2Mods\AutoSorter` if that folder exists.

## Credits and licenses

MIT licensed (`LICENSE`). [BuildPlanner by Vito Pistelli](https://github.com/viligante8/SpaceEngineers2BuildPlanner) was the reference for plugin loading and the terminal UI (MIT, `LICENSE-BuildPlanner.txt`). Bundled MonoMod / Mono.Cecil / iced notices: `THIRD-PARTY-NOTICES.txt`. No game files are distributed.
