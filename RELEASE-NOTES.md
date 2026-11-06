# AutoSorter 0.3.0 — Sort button / кнопка «Сортировать»

**English** · [Русский ниже](#русский)

## English

### New: one-click Sort button

A three-bar button now sits in the header of every terminal inventory list, next to "show/hide empty" and "connected only". It sorts the cargo containers of that list:

- every item kind is gathered in its home — the container that already holds the most of it;
- components in a mixed ore+components container move to a components-only container if it has room;
- when the home is full, piles go to a bigger pile of the same kind; otherwise they stay;
- items already at home are never moved; nothing is pushed into a full container; a second press does almost nothing;
- transfers use the game's own drag-and-drop path and wait for the screen to update after each one;
- only ore, ingots/materials and components move. Tools, weapons (incl. modded), consumables, datapads and quest items stay put;
- character inventory, production blocks, traders and connectors are never touched; `[NoSort]` excludes a container.

### Changed

- The timed background sorter is **off by default** (`"AutoSort": false`) and not recommended: it is the 0.2.1 sorter without the 0.2.2 docking fixes. Existing `AutoSorter.json` files keep working.
- Quick deposit (Alt + right click) tool protection from 0.2.3 is kept: registered tools and weapons, including modded ones, stay in the backpack.
- The log records every transfer and the keep/sort decision for each item kind.

### Install

Download **AutoSorter-0.3.0.zip** below (not "Source code"), extract the AutoSorter folder (e.g. `D:\SE2Mods\AutoSorter`) and add it to Steam launch options:

```text
"-plugins:D:\SE2Mods\AutoSorter\AutoSorter.dll"
```

With BuildPlanner: `"-plugins:D:\SE2Mods\BuildPlanner\BuildPlanner.dll;D:\SE2Mods\AutoSorter\AutoSorter.dll"`. Full instructions: README.md.

### Status

Tested in a live local world on game assembly 4.0.0.3165: sorting of 5 cargo containers, components moved to the components container, ore consolidated, tools kept in place, Alt + right click deposit with modded tools. Remote/dedicated servers are not supported. Back up your save before first use.

---

## Русский

### Новое: кнопка «Сортировать»

В шапке каждого списка инвентаря терминала, рядом со штатными «показать/скрыть пустые» и «только подключённые», появилась кнопка с тремя полосками. Она сортирует грузовые контейнеры этого списка:

- каждый вид предметов собирается в своём «доме» — контейнере, где его уже больше всего;
- компоненты из смешанного контейнера (руда + компоненты) переезжают в контейнер только с компонентами, если там есть место;
- если дом полон, кучка уходит к более крупной кучке того же вида, иначе остаётся;
- то, что уже лежит дома, не трогается; в полный контейнер ничего не толкается; повторное нажатие почти ничего не делает;
- переносы идут штатным путём, как перетаскивание мышью, с ожиданием обновления окна после каждого;
- переносятся только руда, слитки/материалы и компоненты. Инструменты, оружие (в том числе модовые), расходники, датапады и квестовые предметы остаются на месте;
- инвентарь персонажа, производственные блоки, торговцы и коннекторы не трогаются; `[NoSort]` исключает контейнер.

### Изменения

- Фоновая сортировка по таймеру **выключена по умолчанию** (`"AutoSort": false`) и не рекомендуется: это сортировщик 0.2.1 без исправлений 0.2.2 для стыковки. Старые файлы `AutoSorter.json` подходят.
- Сохранена защита инструментов при быстрой выгрузке Alt + ПКМ из 0.2.3: зарегистрированные инструменты и оружие, включая модовые, остаются в рюкзаке.
- В журнал записывается каждый перенос и решение по каждому виду предметов.

### Установка

Скачайте ниже **AutoSorter-0.3.0.zip** (не «Source code»), распакуйте папку AutoSorter (например, в `D:\SE2Mods\AutoSorter`) и добавьте в параметры запуска Steam:

```text
"-plugins:D:\SE2Mods\AutoSorter\AutoSorter.dll"
```

Вместе с BuildPlanner: `"-plugins:D:\SE2Mods\BuildPlanner\BuildPlanner.dll;D:\SE2Mods\AutoSorter\AutoSorter.dll"`. Подробно — в README-RU.md.

### Статус

Проверено в живом локальном мире на сборке игры 4.0.0.3165: сортировка 5 грузовых контейнеров, компоненты ушли в контейнер с компонентами, руда собрана по видам, инструменты остались на местах, выгрузка Alt + ПКМ с модовыми инструментами. Удалённые и выделенные серверы не поддерживаются. Перед первым использованием сделайте копию сохранения.
