using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Keen.Game2.Client.Input;
using Keen.Game2.Client.GameSystems.PlayerControl;
using Keen.Game2.Game.SessionComponents;
using Keen.Game2.Simulation.RuntimeSystems.CoreScenes;
using Keen.Game2.Simulation.GameSystems.Ownership;
using Keen.Game2.Simulation.Utils;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.WorldObjects.CubeGrids.ResourceDistribution.Conveyors;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.Core.Plugins;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Localization;
using MonoMod.RuntimeDetour;

namespace AutoSorter;

public sealed class AutoSorterPlugin : IPlugin, IDisposable
{
    private static Hook? _inputHook, _updateHook;
    private static Entity? _host;
    private static Settings _settings = new();
    private static bool _installed;
    private static readonly ConditionalWeakTable<Session, RunState> States = new();
    private static readonly Guid CargoKind = new("39699122-1fe4-4ed4-a9e2-9f2797067398");
    private delegate void InitOriginal(InputGameComponent self);
    private delegate void UpdateOriginal(Session self, bool updateLifetime);

    public AutoSorterPlugin() { Install(); }
    public AutoSorterPlugin(PluginHost host) { Install(); }

    public void Dispose()
    {
        ManualSortButton.Uninstall();
        ToolProtection.Uninstall();
        _updateHook?.Dispose(); _updateHook = null;
        _inputHook?.Dispose(); _inputHook = null;
        _host = null;
        States.Clear();
        _installed = false;
        Log.Write("AutoSorter stopped; hooks removed.");
    }

    private static void Install()
    {
        if (_updateHook != null || _installed) return;
        _installed = true;
        try
        {
            _settings = Settings.Load();
            Settings.Current = _settings;
        }
        catch (Exception e)
        {
            Log.Write("DISABLED: AutoSorter.json is invalid: " + e.Message);
            return;
        }
        Log.Write($"AutoSorter 0.3.0 starting. Enabled={_settings.Enabled}, ManualButton={_settings.ManualButton}, AutoSort={_settings.AutoSort}, DryRun={_settings.DryRun}. Game assembly={typeof(InventoryComponent).Assembly.GetName().Version}");
        if (!_settings.Enabled) return;

        try { ToolProtection.Install(); }
        catch (Exception e) { Log.Write("Quick deposit tool protection DISABLED: hook failed: " + e); }

        if (_settings.ManualButton)
        {
            try { ManualSortButton.Install(); }
            catch (Exception e) { Log.Write("Sort button DISABLED: hook failed: " + e); }
        }

        if (!_settings.AutoSort) return;
        try
        {
            var init = typeof(InputGameComponent).GetMethod("Init", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null) ?? throw new MissingMethodException("InputGameComponent.Init");
            var update = typeof(Session).GetMethod(nameof(Session.Update), new[] { typeof(bool) })
                ?? throw new MissingMethodException("Session.Update(bool)");
            _inputHook = new Hook(init, (InitOriginal orig, InputGameComponent self) =>
            {
                orig(self);
                _host = self.Entity;
                Log.Write("Input host captured.");
            });
            _updateHook = new Hook(update, (UpdateOriginal orig, Session self, bool lifetime) =>
            {
                orig(self, lifetime);
                OnUpdate(self, lifetime);
            });
            Log.Write("Background auto-sort ready (content-based).");
        }
        catch (Exception e)
        {
            _inputHook?.Dispose(); _inputHook = null;
            _updateHook?.Dispose(); _updateHook = null;
            Log.Write("Background auto-sort DISABLED: initialization failed: " + e);
        }
    }

    private static void OnUpdate(Session session, bool lifetime)
    {
        if (!_settings.Enabled || !_settings.AutoSort || !lifetime) return;
        RunState? state = null;
        try
        {
            var scene = _host?.Scene?.UserObject as GameCoreScene;
            var world = scene?.GameServer?.TryGet<WorldSessionComponent>();
            if (world == null || !ReferenceEquals(world.OwnedSession, session) || session.IsUnloading) return;
            var client = scene?.GameClient?.TryGet<WorldSessionComponent>();
            if (client == null || world.Pause.IsPaused || client.Pause.IsPaused || world.IsSaving) return;
            state = States.GetValue(session, _ => new RunState());
            if (state.Failed || Stopwatch.GetTimestamp() < state.NextRun) return;
            state.NextRun = Stopwatch.GetTimestamp() + (long)(_settings.IntervalSeconds * Stopwatch.Frequency);
            // Never turn an arbitrary/background thread into a DCS writer.
            if (!session.Scene.IsCurrentThreadDCS())
                throw new InvalidOperationException("Session.Update returned outside the synchronized DCS thread");
            var players = client.OwnedSession?.SessionComponents?.TryGet<ClientPlayersSessionComponent>();
            if (players == null || !players.LocalPlayerIdentity.IsValid()) return;
            Sort(session, players.LocalPlayerIdentity, state);
        }
        catch (Exception e)
        {
            if (state != null) state.Failed = true;
            Log.Write("Sorting disabled for this world session after error; reload world after fixing: " + e);
        }
    }

    private sealed class RunState
    {
        public long NextRun;
        public int Cursor;
        public bool Failed;
        public long NextReport;
    }

    private sealed record Cargo(InventoryComponent Inventory, ContainerRule Rule, string Name);
    private sealed record Work(Cargo Source, ItemDefinition Item, Category Category);

    private static void Sort(Session session, IdentityId player, RunState state)
    {
        var tools = Keen.VRage.Library.Utils.Singleton<Keen.VRage.Library.Definitions.DefinitionManager>.Instance
            .GetConfiguration<Keen.Game2.Simulation.WorldObjects.Characters.ToolsConfiguration>();
        var protectedItems = new HashSet<Guid>();
        foreach (var item in tools.ItemPrefabs.Values) protectedItems.Add(item.Guid);
        foreach (var item in tools.GunItems.Values) protectedItems.Add(item.Guid);
        Category Classify(ItemDefinition item) => protectedItems.Contains(item.Guid) ? Category.None : GetCategory(item.Type);
        var cargos = new Dictionary<InventoryComponent, Cargo>();
        var work = new List<Work>();
        foreach (var entity in session.GetEntitiesOfType<FunctionalBlockComponent>())
        {
            var block = entity.TryGet<FunctionalBlockComponent>();
            if (block == null || !block.Functional || block.Definition.BlockKind?.Guid != CargoKind) continue;
            // A block can supply an owner itself, otherwise ownership belongs to its grid.
            var owner = entity.TryGetOwner() ?? block.Grid?.Entity.TryGetOwner();
            if (!owner.HasValue || !owner.Value.Equals(player)) continue;
            var name = block.DisplayName.EvaluateText();
            foreach (var component in entity.Components)
            {
                if (component is not InventoryComponent inv || inv.ConveyorSystem == null) continue;
                // Classify once per pass; transfers cannot turn a source into a destination mid-pass.
                var categories = new List<Category>();
                foreach (var stack in inv.Items) categories.Add(Classify(stack.Definition));
                var rule = ContainerRule.FromContents(categories, name);
                if (rule.Excluded) continue;
                var cargo = new Cargo(inv, rule, name);
                cargos.Add(inv, cargo);
                var seen = new HashSet<ItemDefinition>();
                foreach (var stack in inv.Items)
                {
                    var item = stack.Definition;
                    var category = Classify(item);
                    if (category != Category.None && !rule.Keeps(category) && seen.Add(item))
                        work.Add(new Work(cargo, item, category));
                }
            }
        }
        if (Stopwatch.GetTimestamp() >= state.NextReport)
        {
            Log.Write($"World scan: {cargos.Count} owned connected cargo inventories; {cargos.Values.Count(x => x.Rule.Accepts != Category.None)} content-assigned destinations.");
            state.NextReport = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency;
        }
        if (work.Count == 0) { state.Cursor = 0; return; }
        int attempts = 0, moved = 0, planned = 0, checkedItems = 0;
        var watch = Stopwatch.StartNew();
        state.Cursor %= work.Count;
        while (checkedItems < Math.Min(work.Count, _settings.MaxItemChecks))
        {
            var task = work[state.Cursor];
            state.Cursor = Routing.Next(state.Cursor, work.Count);
            checkedItems++;
            var source = task.Source.Inventory;
            // Enumerate with the REAL item and outbound direction; TransferByDef itself has no path check.
            foreach (var destination in ConveyorSystemComponent.IterateReachableInventories(source, task.Item, followEdgeDirection: true))
            {
                if (attempts >= _settings.MaxTransferAttempts || watch.ElapsedMilliseconds >= _settings.SoftTimeBudgetMs) break;
                if (!cargos.TryGetValue(destination, out var target)
                    || !Routing.ShouldMove(task.Source.Rule, target.Rule, task.Category, ReferenceEquals(source, destination), true)) continue;
                if (!session.IsEntityInScene(source.Entity) || !session.IsEntityInScene(destination.Entity)) break;
                if (source.CountItem(task.Item) <= 0) break;
                if (protectedItems.Contains(task.Item.Guid)) continue;
                attempts++;
                if (_settings.DryRun) { planned++; continue; }
                var amount = source.TransferByDef(destination, task.Item, null, null, true);
                if (amount > 0) moved++;
            }
            if (attempts >= _settings.MaxTransferAttempts || watch.ElapsedMilliseconds >= _settings.SoftTimeBudgetMs) break;
        }
        if (moved > 0 || planned > 0)
            Log.Write($"Pass: moved={moved}, dryRunCandidates={planned}, attempts={attempts}, itemChecks={checkedItems}, pendingTypes={work.Count}.");
    }

    private static Category GetCategory(ItemTypes type)
    {
        // Single category avoids mixed-flag items bouncing between distinct tagged containers.
        if ((type & ItemTypes.Ore) != 0) return Category.Ore;
        if ((type & ItemTypes.Material) != 0) return Category.Ingots;
        if ((type & ItemTypes.Component) != 0) return Category.Components;
        return Category.None;
    }
}

