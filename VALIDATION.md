# Validation — AutoSorter 0.3.0

## Automated (run by `package.ps1` / `build.cmd`)

- Release build against the installed Space Engineers 2 DLLs (game assembly 4.0.0.3165), 0 warnings, 0 errors.
- Policy tests (`tests/`): the 0.2.x routing rules plus the 0.3.0 sort plan — two real in-game layouts (exact expected transfers), a full-containers layout (nothing pushed into full cargo, second press does nothing), and 500 randomised layouts checking that amounts are conserved, capacity is never exceeded, items never become more scattered and sorting always terminates.

## In game (2026-11-05, local world, 5 cargo containers)

- Sort button appears in both inventory lists; the terminal works normally.
- One press: 9 transfers, 0 refused, ~1.8 s. Components from a mixed container moved to the components container, ore consolidated by kind. Second press: 1 transfer (fresh ore arriving by conveyor).
- Tools/consumables in containers stayed in place (logged as `KEEP IN PLACE`).
- Alt + right click quick deposit kept 4 extra (modded) tool kinds in the backpack.
- Earlier iterations found and fixed: containers wrongly skipped as "no access", ore shuffled between full containers by the first plan design, modded tools moved, lost 0.2.3 quick-deposit protection.

## Not covered

Remote and dedicated servers, multiplayer with other players, every mod combination, and future game updates.
