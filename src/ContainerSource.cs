using System.Runtime.CompilerServices;
using DynamicData;
using Keen.Game2.Client.UI.TerminalScreen.Inventory;
using Keen.Game2.Simulation.StreamedUI.Terminal.Inventories;
using Keen.Game2.Simulation.WorldObjects.Items;

namespace AutoSorter;

/// <summary>
/// Which inventories one press of the button may touch: the cargo containers of the list the
/// button belongs to, in the order the screen shows them.
/// </summary>
internal static class ContainerSource
{
    private const ItemTypes Sortable = ItemTypes.Ore | ItemTypes.Material | ItemTypes.Component;

    // The list's full, unfiltered container set. The visible collection hides empty containers
    // while "hide empty" is on, and empty containers are exactly the room a sort needs.
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_inventoryContainers")]
    private static extern ref SourceList<InventoryContainerViewModel> AllContainers(InventoryListViewModel list);

    // The "which grid" dropdown. Applied ourselves to the full set so a hidden container from a
    // different grid is never pulled in.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "PassPrimaryFilter")]
    private static extern bool PassPrimaryFilter(InventoryListViewModel list, InventoryContainerViewModel container);

    public static List<InventoryModel> SortableInventories(InventoryListViewModel list)
    {
        var ordered = new List<InventoryContainerViewModel>();
        var seen = new HashSet<InventoryContainerViewModel>();

        foreach (var container in list.InventoryContainers)
            if (container != null && seen.Add(container)) ordered.Add(container);

        try
        {
            foreach (var container in AllContainers(list).Items)
                if (container != null && !seen.Contains(container) && PassPrimaryFilter(list, container) && seen.Add(container))
                    ordered.Add(container);
        }
        catch (Exception e)
        {
            // Game update renamed something: fall back to what is visible.
            Log.Write("Note: hidden (empty) containers are not used this time: " + e.GetType().Name + ": " + e.Message);
        }

        var result = new List<InventoryModel>();
        foreach (var container in ordered)
        {
            var inventory = Eligible(container, out var reason);
            if (inventory != null) result.Add(inventory);
            else Log.Write($"  skip '{container.Model?.DisplayName}': {reason}");
        }
        return result;
    }

    private static InventoryModel? Eligible(InventoryContainerViewModel container, out string reason)
    {
        reason = "";
        if (container.IsCharacter) { reason = "character"; return null; }
        if (container.IsTrader) { reason = "trader"; return null; }
        var inventories = container.Model?.Inventories;
        if (inventories == null || inventories.Count != 1) { reason = $"{inventories?.Count ?? 0} inventories (production block?)"; return null; }
        var inventory = inventories[0];
        if (inventory == null) { reason = "no inventory"; return null; }
        if (!inventory.IsConnected) { reason = "not connected"; return null; }
        // NOTE: InventoryModel.IsAccessed is NOT a permission flag: it is false for every container
        // except the one the terminal was opened through. Permissions are checked by the server
        // on each transfer anyway.
        if (SortPlan.IsExcludedName(inventory.EntityName)) { reason = "[NoSort] in name"; return null; }

        var definition = inventory.Definition;
        if (definition == null) { reason = "no definition"; return null; }
        string defName = definition.DebugNameShort ?? "";
        if (defName.Contains("Connector", StringComparison.OrdinalIgnoreCase)
            || (inventory.EntityName ?? "").Contains("Коннектор", StringComparison.OrdinalIgnoreCase)
            || (inventory.EntityName ?? "").Contains("Connector", StringComparison.OrdinalIgnoreCase))
        { reason = $"connector ({defName})"; return null; }
        if (!definition.WhitelistedItems.IsDefault && definition.WhitelistedItems.Count > 0) { reason = "item whitelist"; return null; }
        if ((definition.WhitelistedTypes & Sortable) != Sortable) { reason = $"accepts only {definition.WhitelistedTypes}"; return null; }
        if ((double)inventory.MaxMass <= 0) { reason = "no capacity"; return null; }
        Log.Write($"  use '{inventory.EntityName}' ({defName})");
        return inventory;
    }
}
