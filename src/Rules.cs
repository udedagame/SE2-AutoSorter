namespace AutoSorter;

[Flags]
public enum Category { None = 0, Ore = 1, Ingots = 2, Components = 4 }

public readonly record struct ContainerRule(Category Accepts, bool Excluded)
{
    public bool Keeps(Category category) => !Excluded && category != Category.None && (Accepts & category) != 0;
    public static ContainerRule FromContents(IEnumerable<Category> contents, string? name = null)
    {
        name ??= "";
        bool Has(string tag) => name.Contains(tag, StringComparison.OrdinalIgnoreCase);
        Category category = Category.None;
        bool other = false;
        foreach (var item in contents)
        {
            if (item == Category.None) other = true;
            category |= item;
        }
        bool single = category is Category.Ore or Category.Ingots or Category.Components;
        return new(!other && single ? category : Category.None,
            Has("[NoSort]") || Has("[Не сортировать]"));
    }
}

// Pure policy used by the live adapter and the standalone regression tests.
public static class Routing
{
    public static bool ShouldMove(ContainerRule from, ContainerRule to, Category item, bool sameInventory, bool reachable)
        => !sameInventory && reachable && !from.Excluded && !from.Keeps(item) && to.Keeps(item);

    public static int Next(int cursor, int count) => count == 0 ? 0 : (cursor + 1) % count;
}
