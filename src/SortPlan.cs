namespace AutoSorter;

/// <summary>One kind of item present in the containers being sorted.</summary>
/// <param name="Key">Stable identity (the item definition's GUID).</param>
/// <param name="Rank">0 ore, 1 ingots/materials, 2 components, 3 everything else.</param>
/// <param name="Name">Language-independent name used to order items inside a rank.</param>
/// <param name="MassPerUnit">kg per unit; 0 when unknown (treated as weightless).</param>
/// <param name="Divisible">Ore and ingots can be split to fractions; components cannot.</param>
public sealed record SortItemType(Guid Key, int Rank, string Name, double MassPerUnit, bool Divisible);

/// <summary>Capacity of one container's inventory, in on-screen order.</summary>
/// <param name="OnlyRank">0.4.0: set when the container's NAME says what it is for ("Руда", "Ore",
/// "Слитки", "Компоненты", ...): only items of that rank may be put there, and everything else is
/// moved out. Null for an ordinary container.</param>
public sealed record SortContainer(double MaxMass, int? MaxStacks, int? OnlyRank = null);

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
/// <item>0.3.1: when the home is full, a pile lying among other kinds spills, whole, into an
///   empty container (or, if it lies among another category, into a container of its own
///   category) — so newly built containers are used instead of everything staying stuck.</item>
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
        double Free(int i) => containers[i].MaxMass - Mass(i);
        bool Allowed(int i, int rank) => containers[i].OnlyRank is null || containers[i].OnlyRank == rank;
        var named = containers.Where(c => c.OnlyRank != null).Select(c => c.OnlyRank!.Value).ToHashSet();
        // 0.4.0: items that this press takes out of container i anyway (ore from an ordinary container
        // when "Руда" containers exist; anything from a container named for another category).
        bool Leaving(int i, int rank) => containers[i].OnlyRank is int own ? own != rank : named.Contains(rank);
        bool Stays(int i, KeyValuePair<Guid, double> kv) => kv.Value > Epsilon
            && !(byKey.TryGetValue(kv.Key, out var t) && Leaving(i, t.Rank));
        bool Pure(int i, int rank) => current[i].Any(kv => Stays(i, kv))
            && current[i].All(kv => !Stays(i, kv) || (byKey.TryGetValue(kv.Key, out var t) && t.Rank == rank));

        var homes = new Dictionary<Guid, int>();
        foreach (var type in types)
        {
            double total = 0;
            for (int i = 0; i < n; i++) total += Get(current[i], type.Key);
            if (total <= Epsilon) continue;
            double need = total * type.MassPerUnit;

            // 0.4.0: containers named for this category ("Руда 1", "Руда 2", ...) are the home.
            var own = Enumerable.Range(0, n).Where(i => containers[i].OnlyRank == type.Rank).ToList();
            if (own.Count > 0)
            {
                var holding = own.Where(i => Get(current[i], type.Key) > Epsilon)
                    .OrderByDescending(i => Get(current[i], type.Key)).ThenBy(i => i).ToList();
                homes[type.Key] = holding.Count > 0 ? holding[0]
                    : own.Where(i => Free(i) >= need - Epsilon).Select(i => (int?)i).FirstOrDefault()
                      ?? own.OrderByDescending(Free).ThenBy(i => i).First();
                continue;
            }

            var allowed = Enumerable.Range(0, n).Where(i => Allowed(i, type.Rank)).ToList();
            if (allowed.Count == 0) continue; // every container is named for another category
            int home = allowed.OrderByDescending(i => Get(current[i], type.Key)).ThenBy(i => i).First();
            if (!Pure(home, type.Rank))
            {
                var pure = allowed
                    .Where(i => i != home && Pure(i, type.Rank))
                    .Where(i => Free(i) + Get(current[i], type.Key) * type.MassPerUnit >= need - Epsilon)
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
        IReadOnlyDictionary<Guid, int> homes, ISet<(int From, Guid Item, int To)> blocked,
        IReadOnlyList<int?>? onlyRank = null)
    {
        int n = current.Count;
        var rankOf = orderedTypes.ToDictionary(t => t.Key, t => t.Rank);
        int? Tag(int i) => onlyRank != null && i < onlyRank.Count ? onlyRank[i] : null;
        foreach (var type in orderedTypes)
        {
            if (!homes.TryGetValue(type.Key, out int home)) continue;
            double min = type.Divisible ? 1e-3 : 1 - Epsilon;
            bool Allowed(int i) => Tag(i) is null || Tag(i) == type.Rank;
            bool Own(int i) => Tag(i) == type.Rank;
            bool anyOwn = Enumerable.Range(0, n).Any(Own);

            var sources = Enumerable.Range(0, n)
                .Where(i => i != home && Get(current[i], type.Key) >= min)
                // 0.4.0: items lying in a container named for ANOTHER category leave first.
                .OrderBy(i => Allowed(i) ? 1 : 0)
                .ThenBy(i => Get(current[i], type.Key)).ThenBy(i => i);
            foreach (int from in sources)
            {
                double pile = Get(current[from], type.Key);
                var targets = new List<int> { home };
                if (anyOwn && !Own(from))
                    // 0.4.0: into the named containers, one after another (Руда 1, Руда 2, ...), split if needed.
                    targets.AddRange(Enumerable.Range(0, n).Where(i => i != home && i != from && Own(i)));
                else
                    targets.AddRange(Enumerable.Range(0, n)
                        .Where(i => i != home && i != from && Allowed(i) && (!anyOwn || Own(i))
                            && Bigger(Get(current[i], type.Key), i, pile, from))
                        .OrderByDescending(i => Get(current[i], type.Key)).ThenBy(i => i));

                foreach (int to in targets)
                {
                    if (blocked.Contains((from, type.Key, to))) continue;
                    double amount = Math.Min(pile, Room(type, freeMass[to]));
                    if (amount < min) continue;
                    return new SortMove(from, to, type.Key, amount);
                }

                // 0.4.0: a pile in a container named for another category must leave even when its
                // home is full: to any container that may hold it — same kind, then same category,
                // then empty, then any ordinary one. Split if needed. It can never come back.
                if (!Allowed(from))
                {
                    var away = Enumerable.Range(0, n).Where(i => i != from && i != home && Allowed(i))
                        .OrderByDescending(i => Get(current[i], type.Key) > Epsilon)
                        .ThenByDescending(i => !IsEmpty(current[i]) && current[i].All(kv => kv.Value <= Epsilon
                            || (rankOf.TryGetValue(kv.Key, out int r) && r == type.Rank)))
                        .ThenByDescending(i => IsEmpty(current[i]))
                        .ThenBy(i => i);
                    foreach (int to in away)
                    {
                        if (blocked.Contains((from, type.Key, to))) continue;
                        double amount = Math.Min(pile, Room(type, freeMass[to]));
                        if (amount < min) continue;
                        return new SortMove(from, to, type.Key, amount);
                    }
                    continue;
                }
                // Already in a named container of its own category: stays there.
                if (Own(from)) continue;

                // 0.3.1: the home (and every bigger pile) is full. A pile that shares its container
                // with other kinds spills into the next container instead of staying stuck there:
                // first an EMPTY container (new cargo gets used), then — only for a pile lying among
                // other categories — a container holding just this category. Only whole piles move
                // here (never split), and the target is never shared afterwards by more categories,
                // so the result is stable: a second press does nothing.
                if (!Shared(current[from], type.Key, min)) continue;
                bool otherRank = current[from].Any(kv => kv.Key != type.Key && kv.Value > Epsilon
                    && rankOf.TryGetValue(kv.Key, out int r) && r != type.Rank);
                var spill = Enumerable.Range(0, n).Where(i => i != from && i != home && Tag(i) is null && IsEmpty(current[i])).ToList();
                if (otherRank)
                    spill.AddRange(Enumerable.Range(0, n).Where(i => i != from && i != home && Tag(i) is null && !IsEmpty(current[i])
                        && current[i].All(kv => kv.Value <= Epsilon || (rankOf.TryGetValue(kv.Key, out int r) && r == type.Rank))));
                foreach (int to in spill)
                {
                    if (blocked.Contains((from, type.Key, to))) continue;
                    if (Room(type, freeMass[to]) < pile - Epsilon) continue;
                    return new SortMove(from, to, type.Key, pile);
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 0.4.1: pile (amount a at i) outranks pile (b at j): bigger, or — when equal — earlier on screen.
    /// Before, two equal piles (30 + 30) in different containers were never joined when the home
    /// was full. A strict order still guarantees that nothing moves back and forth.
    /// </summary>
    private static bool Bigger(double a, int i, double b, int j)
        => a > b + Epsilon || (Math.Abs(a - b) <= Epsilon && a > Epsilon && i < j);

    private static double Room(SortItemType type, double freeMass)
    {
        double room = type.MassPerUnit > Epsilon ? Math.Max(0, freeMass) / type.MassPerUnit : double.PositiveInfinity;
        return type.Divisible ? room : Math.Floor(room + Epsilon);
    }

    private static bool IsEmpty(IReadOnlyDictionary<Guid, double> inv) => inv.All(kv => kv.Value <= Epsilon);

    /// <summary>True when the container holds some OTHER kind besides <paramref name="key"/>.</summary>
    private static bool Shared(IReadOnlyDictionary<Guid, double> inv, Guid key, double min)
        => inv.Any(kv => kv.Key != key && kv.Value > Epsilon);

    private static readonly (int Rank, System.Text.RegularExpressions.Regex Pattern)[] NameTags =
    {
        (0, Pattern("руда|руды|руд|ore|ores")),
        (1, Pattern("слитки|слитков|слиток|ingot|ingots")),
        (2, Pattern("компоненты|компонентов|компонент|детали|components|component|parts")),
    };

    private static System.Text.RegularExpressions.Regex Pattern(string words)
        => new(@"(?<!\p{L})(?:" + words + @")(?!\p{L})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// 0.4.0: what a container is for, from its name: 0 ore ("Руда", "Руда 0.1", "[Ore] 2"), 1 ingots
    /// ("Слитки"), 2 components ("Компоненты", "Детали"). Whole words only, so "Core" or "Store" is
    /// not "Ore". Null for an ordinary name, or when a name mentions two different categories.
    /// </summary>
    public static int? NameRank(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        int? found = null;
        foreach (var (rank, pattern) in NameTags)
            if (pattern.IsMatch(name))
            {
                if (found != null) return null;
                found = rank;
            }
        return found;
    }

    /// <summary>"[NoSort]" or "[Не сортировать]" anywhere in a container's name keeps the button away.</summary>
    public static bool IsExcludedName(string? name)
        => name != null && (name.Contains("[NoSort]", StringComparison.OrdinalIgnoreCase)
            || name.Contains("[Не сортировать]", StringComparison.OrdinalIgnoreCase));

    public static double Get(IReadOnlyDictionary<Guid, double> map, Guid key)
        => map.TryGetValue(key, out var v) ? v : 0;
}
