using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keen.Game2.Client.UI.TerminalScreen;
using Keen.Game2.Client.UI.TerminalScreen.Inventory;
using Keen.Game2.Simulation.StreamedUI.Terminal.Inventories;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.Library.Mathematics;
using KeenTask = Keen.VRage.Library.Threading.Task<(Keen.Game2.Simulation.WorldObjects.Items.ItemTransferResult, Keen.VRage.Library.Mathematics.FixedPoint, Keen.VRage.Library.Mathematics.FixedPoint)?>;

namespace AutoSorter;

/// <summary>
/// One press of the sort button: sorts the containers of ONE inventory list (the panel the
/// button sits in), using the game's own drag-and-drop transfer.
///
/// Why the game's transfer and not InventoryComponent.TransferByDef:
/// <list type="bullet">
/// <item>it is exactly what dragging an item with the mouse does, so permissions, conveyor
///   connection, filters and capacity are checked by the game itself;</item>
/// <item>it works through the same client→server path in single player and multiplayer.</item>
/// </list>
///
/// The catch: the server finds the dragged stack by its index in the inventory model. So after
/// every transfer we WAIT until the screen shows the result before looking at the inventories
/// again; acting on a stale view could move the wrong stack. Everything runs on the UI thread from
/// a DispatcherTimer; nothing here blocks or awaits.
/// </summary>
internal sealed class ManualSorter
{
    private static readonly ConditionalWeakTable<InventoryListViewModel, ManualSorter> Running = new();

    private enum Phase { NextStep, WaitTransfer, WaitScreen, Done }

    private readonly Control _owner;
    private readonly InventoryListViewModel _list;
    private readonly TerminalInventoryInteraction _interaction;
    private readonly Action<string?> _status;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<(int, Guid, int)> _blocked = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private List<InventoryModel> _inventories = new();
    private List<SortItemType> _types = new();
    private Dictionary<Guid, int> _homes = new();

    private Phase _phase = Phase.NextStep;
    private KeenTask _pending;
    private SortMove _move;
    private InventoryModel? _waitFrom, _waitTo;
    private double _fromCountBefore, _toCountBefore;
    private long _waitStarted;
    private int _transfers, _failures;

    private ManualSorter(Control owner, InventoryListViewModel list, TerminalInventoryInteraction interaction, Action<string?> status)
    {
        _owner = owner;
        _list = list;
        _interaction = interaction;
        _status = status;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(16, Settings.Current.StepDelayMs)) };
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>
    /// Entry point from the button. <paramref name="owner"/> is the button itself: it inherits the
    /// list's DataContext (the InventoryListViewModel Keen's own header buttons bind to).
    /// Returns false if nothing could be started.
    /// </summary>
    public static bool Start(Control owner, Action<string?> status)
    {
        if (owner.DataContext is not InventoryListViewModel list)
        {
            Log.Write("Sort: the inventory list has no view model; nothing to sort.");
            return false;
        }
        if (Running.TryGetValue(list, out var already) && already._phase != Phase.Done)
        {
            Log.Write("Sort: already running for this list.");
            return false;
        }

        var terminal = FindTerminalViewModel(owner);
        var interaction = terminal?.InventoryInteraction;
        if (interaction == null)
        {
            Log.Write("Sort: terminal inventory interaction not found; is the terminal open?");
            return false;
        }

        var sorter = new ManualSorter(owner, list, interaction, status);
        if (!sorter.Prepare()) return false;
        Running.AddOrUpdate(list, sorter);
        sorter._timer.Start();
        return true;
    }

    private static TerminalScreenViewModel? FindTerminalViewModel(Control control)
    {
        foreach (var visual in control.GetVisualAncestors())
            if (visual is Control { DataContext: TerminalScreenViewModel vm }) return vm;
        for (var logical = control.Parent; logical != null; logical = logical.Parent)
            if (logical.DataContext is TerminalScreenViewModel vm) return vm;
        return null;
    }

    // ---- planning -------------------------------------------------------------------------

    private bool Prepare()
    {
        _inventories = ContainerSource.SortableInventories(_list);
        if (_inventories.Count < 2)
        {
            Log.Write($"Sort: {_inventories.Count} sortable container(s) in this list; need at least 2.");
            _status(_inventories.Count == 0 ? "AutoSorter: нет подходящих контейнеров / no cargo containers" : null);
            return false;
        }

        var current = Snapshot(out var types);
        var totals = new Dictionary<Guid, double>();
        foreach (var inv in current)
            foreach (var (key, amount) in inv) totals[key] = SortPlan.Get(totals, key) + amount;

        _types = SortPlan.Order(types.Values, totals);
        // Items the button never moves (tools, consumables, ...) keep their mass and slots.
        var capacity = _inventories.Select(i =>
        {
            double fixedMass = 0;
            int fixedStacks = 0;
            foreach (var item in i.Items)
            {
                var def = item?.Definition;
                if (def == null || IsSortable(item!)) continue;
                fixedMass += (double)item!.TotalMass;
                fixedStacks++;
            }
            int? stacks = i.MaxStacks.HasValue ? Math.Max(0, i.MaxStacks.Value - fixedStacks) : null;
            return new SortContainer(Math.Max(0, (double)i.MaxMass - fixedMass), stacks);
        }).ToList();
        _homes = SortPlan.Homes(capacity, _types, current);

        LogKinds();
        Log.Write($"Sort started: {_inventories.Count} container(s), {_types.Count} item kind(s): "
            + string.Join(", ", _inventories.Select((i, n) => $"#{n + 1} '{i.EntityName}' {(double)i.Mass:0}/{(double)i.MaxMass:0}kg")));
        return true;
    }

    /// <summary>One line per item kind with the decision, so a wrongly moved mod item is easy to spot.</summary>
    private void LogKinds()
    {
        var seen = new HashSet<Guid>();
        foreach (var inv in _inventories)
            foreach (var item in inv.Items)
            {
                var def = item?.Definition;
                if (def == null || !seen.Add(def.Guid)) continue;
                Log.Write($"  kind {def.DebugNameShort}: type={def.Type}, stackable={def.IsStackable}, meta={(item!.MetaData != null || def.DefaultMetaData != null)} -> {(IsSortable(item) ? "sort" : "KEEP IN PLACE")}");
            }
    }

    private const ItemTypes NeverMove = ItemTypes.Item | ItemTypes.Consumable | ItemTypes.Datapad | ItemTypes.Quest | ItemTypes.BlockSchematic;

    /// <summary>
    /// Only ore, ingots/materials and components are sorted. Tools and weapons (vanilla AND modded:
    /// decided by the item's type, not its name), consumables, datapads, quest items and schematics
    /// are never moved by the button.
    /// </summary>
    internal static bool IsSortable(InventoryItemModel item)
    {
        var def = item.Definition;
        if (def == null) return false;
        var type = def.Type;
        if ((type & (ItemTypes.Ore | ItemTypes.Material | ItemTypes.Component)) == 0 || (type & NeverMove) != 0) return false;
        // Mods sometimes type a tool as a component. A tool is still recognisable: it does not
        // stack (shows "x1") and carries per-item state (durability etc.). Resources always stack.
        if (ToolProtection.IsTool(def)) return false; // game's tool/weapon registry, mods included
        if (!def.IsStackable) return false;
        if (item.MetaData != null || def.DefaultMetaData != null) return false;
        return true;
    }

    /// <summary>Current amounts per container, read from the models the screen shows.</summary>
    private List<IReadOnlyDictionary<Guid, double>> Snapshot(out Dictionary<Guid, SortItemType> types)
    {
        types = new();
        var result = new List<IReadOnlyDictionary<Guid, double>>();
        foreach (var inv in _inventories)
        {
            var amounts = new Dictionary<Guid, double>();
            foreach (var item in inv.Items)
            {
                var def = item?.Definition;
                if (def == null || !IsSortable(item!)) continue; // tools etc. stay where they are
                double amount = (double)item!.Amount;
                if (amount <= 0) continue;
                amounts[def.Guid] = SortPlan.Get(amounts, def.Guid) + amount;
                if (!types.ContainsKey(def.Guid))
                {
                    var t = def.Type;
                    types[def.Guid] = new SortItemType(def.Guid,
                        SortPlan.Rank((t & ItemTypes.Ore) != 0, (t & ItemTypes.Material) != 0, (t & ItemTypes.Component) != 0),
                        def.DebugNameShort ?? def.Guid.ToString(),
                        Math.Max(0, (double)def.MassPerUnit),
                        def.IsDivisible);
                }
            }
            result.Add(amounts);
        }
        return result;
    }

    // ---- the step machine -----------------------------------------------------------------

    private void Tick()
    {
        try
        {
            if (!_owner.IsAttachedToVisualTree() || !ReferenceEquals(_owner.DataContext, _list))
            {
                Finish("terminal closed");
                return;
            }
            switch (_phase)
            {
                case Phase.NextStep: Step(); break;
                case Phase.WaitTransfer: WaitTransfer(); break;
                case Phase.WaitScreen: WaitScreen(); break;
            }
        }
        catch (Exception e)
        {
            Log.Write("Sort aborted after error: " + e);
            Finish("error");
        }
    }

    private void Step()
    {
        if (_transfers + _failures >= Settings.Current.MaxTransfersPerPress)
        {
            Finish($"stopped at the limit of {Settings.Current.MaxTransfersPerPress} transfers; press again to continue");
            return;
        }

        var current = Snapshot(out _);
        // Real free room right now, as the game shows it (includes tools and anything else).
        var free = _inventories.Select(i => Math.Max(0, (double)i.MaxMass - (double)i.Mass)).ToList();
        var move = SortPlan.NextMove(current, free, _types, _homes, _blocked);
        if (move == null)
        {
            Finish("done");
            return;
        }

        var m = move.Value;
        var from = _inventories[m.From];
        var to = _inventories[m.To];

        // Biggest stack of that kind first, so one transfer usually covers it.
        InventoryItemModel? stack = null;
        foreach (var item in from.Items)
            if (item?.Definition?.Guid == m.Item && !item.Disabled && (stack == null || item.Amount > stack.Amount))
                stack = item;
        if (stack == null)
        {
            _blocked.Add((m.From, m.Item, m.To));
            return;
        }

        double stackAmount = (double)stack.Amount;
        FixedPoint amount = m.Amount >= stackAmount - SortPlan.Epsilon ? stack.Amount : (FixedPoint)m.Amount;
        if (amount <= FixedPoint.Zero)
        {
            _blocked.Add((m.From, m.Item, m.To));
            return;
        }

        _move = m;
        _waitFrom = from;
        _waitTo = to;
        _fromCountBefore = Count(from, m.Item);
        _toCountBefore = Count(to, m.Item);
        if (Settings.Current.DryRun)
        {
            Log.Write($"DryRun: would move {(double)amount:0.##} x {stack.Definition.DebugNameShort} '{from.EntityName}' -> '{to.EntityName}'");
            _blocked.Add((m.From, m.Item, m.To));
            _transfers++;
            return;
        }

        Log.Write($"Move {(double)amount:0.##} x {stack.Definition.DebugNameShort}: #{m.From + 1} -> #{m.To + 1}");
        _pending = _interaction.TransferItemAsync(to, stack, amount, null);
        _waitStarted = _clock.ElapsedMilliseconds;
        _phase = Phase.WaitTransfer;
        _status($"AutoSorter: {_transfers + 1}…");
    }

    private void WaitTransfer()
    {
        if (!_pending.IsCompleted)
        {
            if (_clock.ElapsedMilliseconds - _waitStarted > Settings.Current.TransferTimeoutMs)
                Finish("the game did not answer a transfer in time");
            return;
        }

        (ItemTransferResult, FixedPoint, FixedPoint)? outcome;
        if (!_pending.TryGetResult(out outcome)) outcome = null;
        var result = outcome?.Item1 ?? ItemTransferResult.ErrorMissingItem;
        var moved = outcome?.Item2 ?? FixedPoint.Zero;

        if (moved > FixedPoint.Zero)
        {
            _transfers++;
            // Room was freed in the source: earlier refusals INTO it may succeed now.
            _blocked.RemoveWhere(b => b.Item3 == _move.From);
        }
        if (result != ItemTransferResult.Success || moved <= FixedPoint.Zero)
        {
            _failures++;
            _blocked.Add((_move.From, _move.Item, _move.To));
            Log.Write($"Transfer '{_waitFrom?.EntityName}' -> '{_waitTo?.EntityName}': {result}, moved {(double)moved:0.##}.");
        }

        if (moved > FixedPoint.Zero)
        {
            _waitStarted = _clock.ElapsedMilliseconds;
            _phase = Phase.WaitScreen;
        }
        else _phase = Phase.NextStep;
    }

    /// <summary>
    /// Wait until both inventories on screen show the transfer. Only then is the model (and with
    /// it every stack index the server uses) up to date for the next step.
    /// </summary>
    private void WaitScreen()
    {
        bool fromChanged = _waitFrom == null || Math.Abs(Count(_waitFrom, _move.Item) - _fromCountBefore) > SortPlan.Epsilon;
        bool toChanged = _waitTo == null || Math.Abs(Count(_waitTo, _move.Item) - _toCountBefore) > SortPlan.Epsilon;
        if (fromChanged && toChanged)
        {
            _phase = Phase.NextStep;
            return;
        }
        if (_clock.ElapsedMilliseconds - _waitStarted > Settings.Current.TransferTimeoutMs)
            Finish("the screen did not update after a transfer; stopped for safety");
    }

    private static double Count(InventoryModel inventory, Guid item)
    {
        double sum = 0;
        foreach (var stack in inventory.Items)
            if (stack?.Definition?.Guid == item) sum += (double)stack.Amount;
        return sum;
    }

    private void Finish(string why)
    {
        if (_phase == Phase.Done) return;
        _phase = Phase.Done;
        _timer.Stop();
        Running.Remove(_list);
        Log.Write($"Sort finished ({why}): {_transfers} transfer(s), {_failures} refused, {_clock.ElapsedMilliseconds} ms.");
        _status(null);
    }
}
