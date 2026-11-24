# AutoSorter 0.4.0 — containers by name, ice fix / контейнеры по названию, исправление льда

**English** · [Русский ниже](#русский)

## English

### New: "Ore" containers (and "Ingots", "Components")

Put a category word in a container's name — `Ore 1`, `Ore 2`, `Руда 0.50` — and the Sort button keeps that container for that category only:

- all ore (iron, nickel, ice, … together) goes into the "Ore" containers, filling them in screen order; a pile may be split across them;
- wire, plates, rods, grids, motors and every other non-ore item are never put there, and are moved out if they already lie there;
- when every "Ore" container is full, the rest of the ore stays where it is;
- also works with `Ingots` / `Слитки` and `Components` / `Parts` / `Компоненты` / `Детали`. Whole words only ("Core", "Store" are not tags).

### Fixed: ice did not finish moving

O₂/H₂ generators pull ice through the conveyor while the button sorts. The game then moves what is left and answers "did not fit"; 0.3.x took that as a refusal and left that ice for the rest of the press. Now partial transfers count as success, refusals are retried up to 3 times with fresh amounts, and the wait for the screen checks the expected change (a generator draining ice is no longer mistaken for the sorter's own transfer).

### Install / update

Download **AutoSorter-0.4.0.zip** below (not "Source code"), close the game and replace the files in your AutoSorter folder (e.g. `D:\SE2Mods\AutoSorter`). Launch option unchanged:

```text
"-plugins:D:\SE2Mods\AutoSorter\AutoSorter.dll"
```

Your `AutoSorter.json` keeps working. The log line should read `AutoSorter 0.4.0 starting`.

## Русский

### Новое: контейнеры «Руда» (а также «Слитки», «Компоненты»)

Впишите категорию в название контейнера — `Руда 0.1`, `Руда 0.2`, `Руда 0.50`, `Руда 0.60` — и кнопка «Сортировать» будет держать его только для этой категории:

- вся руда (железо, никель, лёд… вместе) идёт в контейнеры «Руда», заполняя их по порядку на экране; кучу можно разделить между ними;
- проволока, пластины, стержни, решётки, моторы и всё остальное, что не руда, туда не кладутся, а если уже лежат — выносятся;
- если все контейнеры «Руда» полны, остаток руды остаётся на месте;
- так же работают `Слитки` / `Ingots` и `Компоненты` / `Детали` / `Components` / `Parts`. Ищется целое слово («Рудник», «Core» — не метки).

### Исправлено: лёд не уходил до конца

Генераторы O₂/H₂ забирают лёд по конвейеру прямо во время сортировки. Игра переносит остаток и отвечает «не поместилось»; 0.3.x считала это отказом и до конца нажатия не трогала этот лёд. Теперь частичный перенос считается успехом, отказ повторяется до 3 раз со свежими количествами, а ожидание обновления окна проверяет ожидаемое изменение (убыль льда от генератора больше не принимается за перенос сортировщика).

### Установка / обновление

Скачайте **AutoSorter-0.4.0.zip** ниже (не «Source code»), закройте игру и замените файлы в папке AutoSorter (например, `D:\SE2Mods\AutoSorter`). Параметр запуска прежний. Старый `AutoSorter.json` подходит. В журнале должна быть строка `AutoSorter 0.4.0 starting`.
