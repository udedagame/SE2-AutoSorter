using System.Reflection;
using System.Runtime.CompilerServices;
using Keen.Game2.Game.DefaultDefinitions;
using Keen.Game2.Simulation.WorldObjects.Characters;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Collections.Readers;
using Keen.VRage.Library.Definitions;
using Keen.VRage.Multiplayer.Replications.FailureStrategies;
using MonoMod.RuntimeDetour;

namespace AutoSorter;

/// <summary>
/// Tools and weapons are never moved by AutoSorter, and are kept in the backpack by the game's
/// quick deposit (Alt + right mouse button on a container).
///
/// "Is it a tool" comes from the game's own registry — ToolsConfiguration.ItemPrefabs (hand
/// tools) and GunItems (weapons) — which mods such as AreaTools also register into. Item type
/// flags are NOT trusted: a mod may flag its tool as a component.
///
/// Quick deposit (InProcessGameServerSessionComponent.TryDepositAllNonEssentialItemsImpl) moves
/// everything except InventoryConfiguration.EssentialItems. That list knows the vanilla tools only,
/// so for the duration of ONE deposit we add every registered tool to it, then restore it.
/// (Restored the 0.2.3 behaviour that 0.3.0 had dropped.)
/// </summary>
internal static class ToolProtection
{
    private static HashSet<Guid>? _protected;
    private static Hook? _depositHook;

    private delegate void DepositOriginal(object self, SkipOnReplicationFail<Entity> from, SkipOnReplicationFail<Entity> to);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "<EssentialItems>k__BackingField")]
    private static extern ref HashSetReader<ItemDefinition> EssentialItems(InventoryConfiguration configuration);

    /// <summary>Registered tools and weapons (vanilla and modded).</summary>
    public static bool IsTool(ItemDefinition? definition)
        => definition != null && Registry().Contains(definition.Guid);

    private static HashSet<Guid> Registry()
    {
        if (_protected != null) return _protected;
        var set = new HashSet<Guid>();
        try
        {
            foreach (var item in Tools()) set.Add(item.Guid);
            Log.Write($"Tool registry: {set.Count} tool/weapon item(s) protected.");
        }
        catch (Exception e)
        {
            Log.Write("Tool registry unavailable (only type/stack checks protect tools now): " + e.Message);
            return set; // do not cache a failure; retry next time
        }
        return _protected = set;
    }

    private static IEnumerable<ItemDefinition> Tools()
    {
        var tools = Keen.VRage.Library.Utils.Singleton<DefinitionManager>.Instance.GetConfiguration<ToolsConfiguration>();
        foreach (var item in tools.ItemPrefabs.Values) if (item != null) yield return item;
        foreach (var item in tools.GunItems.Values) if (item != null) yield return item;
    }

    public static void Install()
    {
        if (_depositHook != null) return;
        var type = typeof(InventoryConfiguration).Assembly.GetType("Keen.Game2.Game.SessionComponents.InProcessGameServerSessionComponent")
            ?? throw new TypeLoadException("InProcessGameServerSessionComponent");
        var method = type.GetMethod("TryDepositAllNonEssentialItemsImpl", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new MissingMethodException("TryDepositAllNonEssentialItemsImpl");
        _depositHook = new Hook(method, (DepositOriginal orig, object self, SkipOnReplicationFail<Entity> from, SkipOnReplicationFail<Entity> to) =>
        {
            InventoryConfiguration? config = null;
            HashSetReader<ItemDefinition> original = default;
            bool swapped = false;
            try
            {
                config = Keen.VRage.Library.Utils.Singleton<DefinitionManager>.Instance.GetConfiguration<InventoryConfiguration>();
                original = EssentialItems(config);
                var widened = new HashSet<ItemDefinition>();
                if (!original.IsDefault) foreach (var item in original) widened.Add(item);
                int before = widened.Count;
                foreach (var tool in Tools()) widened.Add(tool);
                EssentialItems(config) = new HashSetReader<ItemDefinition>(widened);
                swapped = true;
                Log.Write($"Quick deposit: keeping {widened.Count - before} extra tool kind(s) in the backpack.");
            }
            catch (Exception e)
            {
                Log.Write("Quick deposit tool protection skipped: " + e.Message);
            }
            try { orig(self, from, to); }
            finally
            {
                if (swapped && config != null) EssentialItems(config) = original;
            }
        });
        Log.Write("Quick deposit (Alt+RMB) tool protection installed.");
    }

    public static void Uninstall()
    {
        _depositHook?.Dispose();
        _depositHook = null;
    }
}
