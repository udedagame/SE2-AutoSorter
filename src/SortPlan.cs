namespace AutoSorter;

/// <summary>One kind of item present in the containers being sorted.</summary>
/// <param name="Key">Stable identity (the item definition's GUID).</param>
/// <param name="Rank">0 ore, 1 ingots/materials, 2 components, 3 everything else.</param>
/// <param name="Name">Language-independent name used to order items inside a rank.</param>
/// <param name="MassPerUnit">kg per unit; 0 when unknown (treated as weightless).</param>
/// <param name="Divisible">Ore and ingots can be split to fractions; components cannot.</param>
public sealed record SortItemType(Guid Key, int Rank, string Name, double MassPerUnit, bool Divisible);

/// <summary>Capacity of one container's inventory, in on-screen order.</summary>
public sealed record SortContainer(double MaxMass, int? MaxStacks);

/// <summary>A single transfer chosen by <see cref="SortPlan.NextMove"/>.</summary>
public readonly record struct SortMove(int From, int To, Guid Item, double Amount);

/// <summary>
/// Pure sorting policy, no game types, so it can be unit tested.
///
/// "Iron with iron, copper with copper", calmly (0.3.0 second design):
/// <list type="bullet">
/// <item>every kind gets ONE home for the whole press: the container holding most of it — or,
///   when that container is mixed (ore + components), a container holding only that category
///   with room for all of this kind ("components go to the components container");</item>
/// <item>only the OTHER, smaller piles move: to the home, or — if the home is full — to a bigger
///   pile of the same kind elsewhere;</item>
/// <item>moves never exceed the target's real free room right now, and nothing ever leaves a
///   home. So no ore is shuffled between full containers, and a second press does ~nothing.</item>
/// </list>
/// The first design re-planned all capacity from scratch, which evicted piles that were already
/// fine and pushed tonnes of ore around between full containers.
/// </summary>
public static class SortPlan
{
    public const double Epsilon = 1e-6;

    public static int Rank(bool ore, bool material, bool component)
        => ore ? 0 : material ? 1 : component ? 2 : 3;

    /// <summary>Order kinds are handled in: biggest piles first, then by kind and name.</summary>
    public static List<SortItemType> Order(IEnumerable<SortItemType> types, IReadOnlyDictionary<Guid, double> totals)
        => types
            .OrderByDescending(t => Get(totals, t.Key) * Math.Max(t.MassPerUnit, Epsilon))
            .ThenBy(t => t.Rank)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Key)
            .ToList();

    /// <summary>The home container of every kind present. Computed once per press.</summary>
    public static Dictionary<Guid, int> Homes(IReadOnlyList<SortContainer> containers,
        IReadOnlyList<SortItemType> types, IReadOnlyList<IReadOnlyDictionary<Guid, double>> current)
    {
        var byKey = types.ToDictionary(t => t.Key);
        int n = containers.Count;
        double Mass(int i) => current[i].Sum(kv => kv.Value * (byKey.TryGetValue(kv.Key, out var t) ? t.MassPerUnit : 0));
        bool Pure(int i, int rank) => current[i].Any(kv => kv.Value > Epsilon)
            && current[i].All(kv => kv.Value <= Epsilon || (byKey.TryGetValue(kv.Key, out var t) && t.Rank == rank));

        var homes = new Dictionary<Guid, int>();
        foreach (var type in types)
        {
            double total = 0;
            for (int i = 0; i < n; i++) total += Get(current[i], type.Key);
            if (total <= Epsilon) continue;

            int home = Enumerable.Range(0, n).OrderByDescending(i => Get(current[i], type.Key)).ThenBy(i => i).First();
            if (!Pure(home, type.Rank))
            {
                double need = total * type.MassPerUnit;
                var pure = Enumerable.Range(0, n)
                    .Where(i => i != home && Pure(i, type.Rank))
                    .Where(i => containers[i].MaxMass - Mass(i) + Get(current[i], type.Key) * type.MassPerUnit >= need - Epsilon)
                    .OrderByDescending(i => Get(current[i], type.Key))
                    .ThenBy(i => i)
                    .ToList();
                if (pure.Count > 0) home = pure[0];
            }
            homes[type.Key] = home;
        }
        return homes;
    }

    /// <summary>
    /// The next transfer, or null when done. <paramref name="freeMass"/> is each container's real
    /// free room (kg) right now; <paramref name="blocked"/> holds transfers the game refused.
    /// Smallest stray piles go first, so big piles are never split to make room.
    /// </summary>
    public static SortMove? NextMove(IReadOnlyList<IReadOnlyDictionary<Guid, double>> current,
        IReadOnlyList<double> freeMass, IReadOnlyList<SortItemType> orderedTypes,
        IReadOnlyDictionary<Guid, int> homes, ISet<(int From, Guid Item, int To)> blocked)
    {
        int n = current.Count;
        foreach (var type in orderedTypes)
        {
            if (!homes.TryGetValue(type.Key, out int home)) continue;
            double min = type.Divisible ? 1e-3 : 1 - Epsilon;

            var sources = Enumerable.Range(0, n)
                .Where(i => i != home && Get(current[i], type.Key) >= min)
                .OrderBy(i => Get(current[i], type.Key)).ThenBy(i => i);
            foreach (int from in sources)
            {
                double pile = Get(current[from], type.Key);
                var targets = new List<int> { home };
                targets.AddRange(Enumerable.Range(0, n)
                    .Where(i => i != home && i != from && Get(current[i], type.Key) > pile + Epsilon)
                    .OrderByDescending(i => Get(current[i], type.Key)).ThenBy(i => i));

                foreach (int to in targets)
                {
                    if (blocked.Contains((from, type.Key, to))) continue;
                    double room = type.MassPerUnit > Epsilon ? Math.Max(0, freeMass[to]) / type.MassPerUnit : double.PositiveInfinity;
                    if (!type.Divisible) room = Math.Floor(room + Epsilon);
                    double amount = Math.Min(pile, room);
                    if (amount < min) continue;
                    return new SortMove(from, to, type.Key, amount);
                }
            }
        }
        return null;
    }

    /// <summary>"[NoSort]" or "[Не сортировать]" anywhere in a container's name keeps the button away.</summary>
    public static bool IsExcludedName(string? name)
        => name != null && (name.Contains("[NoSort]", StringComparison.OrdinalIgnoreCase)
            || name.Contains("[Не сортировать]", StringComparison.OrdinalIgnoreCase));

    public static double Get(IReadOnlyDictionary<Guid, double> map, Guid key)
        => map.TryGetValue(key, out var v) ? v : 0;
}
