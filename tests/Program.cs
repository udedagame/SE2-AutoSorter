using AutoSorter;
int passed = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; }
ContainerRule Rule(params Category[] contents) => ContainerRule.FromContents(contents);
var ore = Rule(Category.Ore);
var ingots = Rule(Category.Ingots, Category.Ingots);
var components = Rule(Category.Components);
var mixed = Rule(Category.Ore, Category.Components);
var plain = Rule();
var excluded = ContainerRule.FromContents(new[] { Category.Ore }, "[NoSort]");
Check(ore.Keeps(Category.Ore), "Ore sample");
Check(ingots.Keeps(Category.Ingots), "Multiple ingot types");
Check(components.Keeps(Category.Components), "Component sample");
Check(mixed.Accepts == Category.None, "Mixed is general");
Check(plain.Accepts == Category.None, "Empty resets");
Check(Rule(Category.None).Accepts == Category.None, "Tools are general");
Check(Rule(Category.Ore, Category.None).Accepts == Category.None, "Ore plus tools is general");
Check(ContainerRule.FromContents(new[] { Category.Ore }, "[Ingots]").Keeps(Category.Ore), "Ignore old category tags");
Check(ContainerRule.FromContents(Array.Empty<Category>(), "[Ore]").Accepts == Category.None, "Old tag cannot assign empty cargo");
Check(!excluded.Keeps(Category.Ore), "Exclusion wins");
Check(ContainerRule.FromContents(new[] { Category.Ore }, "[НЕ СОРТИРОВАТЬ]").Excluded, "Russian exclusion");
Check(Routing.ShouldMove(mixed, ore, Category.Ore, false, true), "General supplies specialized cargo");
Check(!Routing.ShouldMove(mixed, ore, Category.Ore, false, false), "Blocked route");
Check(!Routing.ShouldMove(mixed, ore, Category.Ore, true, true), "Self transfer");
Check(!Routing.ShouldMove(excluded, ore, Category.Ore, false, true), "Excluded source");
Check(!Routing.ShouldMove(mixed, excluded, Category.Ore, false, true), "Excluded destination");
Check(!Routing.ShouldMove(ore, ore, Category.Ore, false, true), "No balancing");
Check(!Routing.ShouldMove(mixed, plain, Category.Ore, false, true), "Empty cargo never auto-assigned");
Check(!Routing.ShouldMove(mixed, mixed, Category.Ore, false, true), "No general destination");
Check(!Routing.ShouldMove(mixed, components, Category.None, false, true), "Tools stay");
foreach (var category in new[] { Category.Ore, Category.Ingots, Category.Components }) {
    Check(Rule(category).Keeps(category), "Seeding assigns");
    Check(Rule().Accepts == Category.None, "Removing last item resets");
    Check(Rule(Category.Ore, Category.Ingots, Category.Components).Accepts == Category.None, "Dumping a mixture is general");
}
var rules = new[] { plain, ore, ingots, components, mixed, excluded };
foreach (var from in rules)
foreach (var to in rules)
foreach (var kind in new[] { Category.Ore, Category.Ingots, Category.Components, Category.None })
    if (Routing.ShouldMove(from, to, kind, false, true)) {
        Check(Rule(to.Accepts, kind).Keeps(kind), "Transfer preserves destination category");
        foreach (var next in rules) Check(!Routing.ShouldMove(to, next, kind, false, true), "No oscillation");
    }
var visited = new HashSet<int>();
int cursor = 0;
for (int i = 0; i < 101; i++) { visited.Add(cursor); cursor = Routing.Next(cursor, 101); }
Check(visited.Count == 101 && cursor == 0, "Round robin coverage");
Check(Routing.Next(9, 0) == 0, "Empty list");

// ---- 0.3.0/0.3.1 button sort plan (home-based, calm, spills into empty containers) ------------------------------------------
{
    var ids = new Dictionary<string, Guid>();
    SortItemType T(string name, int rank, double mass, bool divisible = true)
    {
        ids[name] = Guid.NewGuid();
        return new SortItemType(ids[name], rank, name, mass, divisible);
    }
    Dictionary<Guid, double> Inv(params (string, double)[] items)
    {
        var d = new Dictionary<Guid, double>();
        foreach (var (n, a) in items) d[ids[n]] = SortPlan.Get(d, ids[n]) + a;
        return d;
    }
    (int moves, double massMoved) Simulate(List<SortContainer> cont, List<Dictionary<Guid, double>> cur, List<SortItemType> kinds)
    {
        var byKey = kinds.ToDictionary(k => k.Key);
        var totals = new Dictionary<Guid, double>();
        foreach (var c in cur) foreach (var (k, a) in c) totals[k] = SortPlan.Get(totals, k) + a;
        var ordered = SortPlan.Order(kinds.Where(k => totals.ContainsKey(k.Key)), totals);
        var view = () => cur.Cast<IReadOnlyDictionary<Guid, double>>().ToList();
        var homes = SortPlan.Homes(cont, ordered, view());
        var blocked = new HashSet<(int, Guid, int)>();
        int moves = 0; double massMoved = 0;
        while (moves < 3000)
        {
            var free = cur.Select((c, i) => cont[i].MaxMass - c.Sum(x => x.Value * byKey[x.Key].MassPerUnit)).ToList();
            var next = SortPlan.NextMove(view(), free, ordered, homes, blocked, cont.Select(c => c.OnlyRank).ToList());
            if (next == null) break;
            var m = next.Value; var kind = byKey[m.Item];
            moves++;
            Check(m.Amount * kind.MassPerUnit <= free[m.To] + 1e-6, "Never more than the target's free room");
            cur[m.From][m.Item] -= m.Amount;
            if (cur[m.From][m.Item] <= 1e-9) cur[m.From].Remove(m.Item);
            cur[m.To][m.Item] = SortPlan.Get(cur[m.To], m.Item) + m.Amount;
            massMoved += m.Amount * kind.MassPerUnit;
        }
        Check(moves < 3000, "Sort terminates");
        return (moves, massMoved);
    }
    var cargo = Enumerable.Repeat(new SortContainer(67200, null), 3).ToList();

    // First screenshot: 49 Ni beside 17.15k Fe, 20 Fe beside 1.64k Ni.
    var kinds = new List<SortItemType> { T("Ni", 0, 1), T("Fe", 0, 1), T("Ice", 0, 1), T("Au", 0, 1), T("Plate", 2, 20, false), T("Motor", 2, 24, false) };
    var state = new List<Dictionary<Guid, double>> {
        Inv(("Ni", 49), ("Fe", 17150)), Inv(("Plate", 452), ("Motor", 9)), Inv(("Ice", 6420), ("Ni", 1640), ("Au", 1160), ("Fe", 20)) };
    var (screenshotMoves, _) = Simulate(cargo, state, kinds);
    Check(screenshotMoves == 2, "Screenshot needs exactly two transfers");
    Check(SortPlan.Get(state[0], ids["Fe"]) == 17170 && !state[0].ContainsKey(ids["Ni"]), "Iron gathered with iron");
    Check(SortPlan.Get(state[2], ids["Ni"]) == 1689 && !state[2].ContainsKey(ids["Fe"]), "Nickel gathered with nickel");

    // Second screenshot: components in a mixed container go to the components-only container.
    var k2 = new List<SortItemType> { T("Ni2", 0, 1), T("Fe2", 0, 1), T("Ice2", 0, 1),
        T("Plate2", 2, 20, false), T("Tube2", 2, 12, false), T("Batt2", 2, 25, false), T("Motor2", 2, 25, false) };
    var s2 = new List<Dictionary<Guid, double>> {
        Inv(("Ni2", 49), ("Fe2", 17150), ("Plate2", 11), ("Tube2", 173), ("Batt2", 8), ("Motor2", 6)),
        Inv(("Plate2", 452), ("Tube2", 8), ("Batt2", 79), ("Motor2", 81)),
        Inv(("Ice2", 60000), ("Ni2", 1640), ("Fe2", 20)) };
    Simulate(cargo, s2, k2);
    Check(SortPlan.Get(s2[1], ids["Tube2"]) == 181 && SortPlan.Get(s2[1], ids["Plate2"]) == 463
        && SortPlan.Get(s2[1], ids["Batt2"]) == 87 && SortPlan.Get(s2[1], ids["Motor2"]) == 87, "Components join the components container");
    Check(SortPlan.Get(s2[2], ids["Ni2"]) == 1689, "Nickel joins nickel");

    // Full ore containers + an empty one (the 18:55 log): calm, nothing pushed into full cargo,
    // a second press does nothing.
    var k3 = new List<SortItemType> { T("Fe3", 0, 1), T("Ice3", 0, 1), T("Au3", 0, 1) };
    var s3 = new List<Dictionary<Guid, double>> { Inv(("Ice3", 60000), ("Au3", 7200)), Inv(("Ice3", 6424), ("Fe3", 30000)), Inv(), Inv(("Au3", 100)) };
    var four = Enumerable.Repeat(new SortContainer(67200, null), 4).ToList();
    var first = Simulate(four, s3, k3);
    Check(SortPlan.Get(s3[0], ids["Ice3"]) == 60000 && SortPlan.Get(s3[0], ids["Au3"]) == 7200, "Full home untouched");
    Check(SortPlan.Get(s3[3], ids["Au3"]) == 100, "Nowhere to go: small pile stays");
    Check(Simulate(four, s3, k3).moves == 0, "Second press does nothing");
    Check(SortPlan.Get(s3[2], ids["Ice3"]) == 6424 && !s3[1].ContainsKey(ids["Ice3"]), "0.3.1: ice beside iron spills into the empty container");

    // 0.3.1, the 2026-11-06 log: homes full, new ore dumped among other ore, new empty containers.
    // The biggest stray pile takes the empty container instead of staying stuck.
    var k4 = new List<SortItemType> { T("Fe4", 0, 1), T("Au4", 0, 1), T("Si4", 0, 1), T("Plate4", 2, 20, false) };
    var s4 = new List<Dictionary<Guid, double>> { Inv(("Plate4", 2000), ("Si4", 3020)), Inv(("Fe4", 67200)), Inv(("Au4", 67200)),
        Inv(("Fe4", 5000), ("Au4", 3000), ("Si4", 1000)), Inv() };
    var five = Enumerable.Repeat(new SortContainer(67200, null), 5).ToList();
    Simulate(five, s4, k4);
    Check(SortPlan.Get(s4[4], ids["Fe4"]) == 5000 && !s4[3].ContainsKey(ids["Fe4"]), "0.3.1: iron spills into the new empty container");
    Check(SortPlan.Get(s4[1], ids["Fe4"]) == 67200 && SortPlan.Get(s4[2], ids["Au4"]) == 67200, "0.3.1: full homes untouched");
    Check(s4[0].Count == 1 && SortPlan.Get(s4[3], ids["Si4"]) == 4020, "0.3.1: ore leaves the components container");
    Check(Simulate(five, s4, k4).moves == 0, "0.3.1: second press does nothing");

    // 0.3.1: ore lying among components, ore home full, no empty container -> goes to an ore-only container with room.
    var k5 = new List<SortItemType> { T("Fe5", 0, 1), T("Ni5", 0, 1), T("Plate5", 2, 20, false) };
    var s5 = new List<Dictionary<Guid, double>> { Inv(("Fe5", 1000)), Inv(("Fe5", 67200)), Inv(("Plate5", 3000), ("Fe5", 500)), Inv(("Ni5", 100)) };
    Simulate(Enumerable.Repeat(new SortContainer(67200, null), 4).ToList(), s5, k5);
    Check(!s5[2].ContainsKey(ids["Fe5"]), "0.3.1: ore leaves the components container when its home is full");

    // Randomised: conserved, never over capacity, never more scattered, always terminates.
    var rng = new Random(12345);
    for (int trial = 0; trial < 500; trial++)
    {
        int n = rng.Next(2, 7); double cap = new[] { 1000.0, 5000, 67200 }[rng.Next(3)];
        var rk = Enumerable.Range(0, rng.Next(1, 12)).Select(i => T($"r{trial}_{i}", rng.Next(4), new[] { 0.37, 1, 2, 20, 0 }[rng.Next(5)], rng.Next(2) == 0)).ToList();
        var cur = Enumerable.Range(0, n).Select(_ => new Dictionary<Guid, double>()).ToList();
        foreach (var c in cur)
        {
            double fill = rng.NextDouble() * cap, used = 0;
            for (int j = rng.Next(9); j > 0; j--)
            {
                var k = rk[rng.Next(rk.Count)];
                double a = k.Divisible ? rng.NextDouble() * 300 : rng.Next(1, 300);
                if (used + a * k.MassPerUnit > fill) continue;
                c[k.Key] = SortPlan.Get(c, k.Key) + a; used += a * k.MassPerUnit;
            }
        }
        double Total(Guid k) => cur.Sum(c => SortPlan.Get(c, k));
        int Piles() => rk.Sum(k => cur.Count(c => SortPlan.Get(c, k.Key) > 1e-6));
        var before = rk.ToDictionary(k => k.Key, k => Total(k.Key));
        int pilesBefore = Piles();
        Simulate(Enumerable.Repeat(new SortContainer(cap, null), n).ToList(), cur, rk);
        Check(rk.All(k => Math.Abs(before[k.Key] - Total(k.Key)) < 1e-6), "Random: amounts conserved");
        Check(Piles() <= pilesBefore, "Random: never more scattered");
        Check(cur.All(c => c.Sum(x => x.Value * rk.First(k => k.Key == x.Key).MassPerUnit) <= cap + 1e-3), "Random: never over capacity");
    }
    // ---- 0.4.0: containers named for a category ("Руда 0.1", "Руда 0.2", ...) -------------------------
    Check(SortPlan.NameRank("Руда 0.1") == 0 && SortPlan.NameRank("Руда0.2") == 0 && SortPlan.NameRank("РУДА 0.50") == 0
        && SortPlan.NameRank("Склад руды") == 0 && SortPlan.NameRank("[Ore] 3") == 0 && SortPlan.NameRank("Ores") == 0, "Ore names");
    Check(SortPlan.NameRank("Core storage") == null && SortPlan.NameRank("Store") == null && SortPlan.NameRank("Рудник") == null
        && SortPlan.NameRank("Cargo Container") == null && SortPlan.NameRank("Большой контейнер 2") == null && SortPlan.NameRank(null) == null, "Ordinary names");
    Check(SortPlan.NameRank("Слитки 1") == 1 && SortPlan.NameRank("Ingots") == 1 && SortPlan.NameRank("Компоненты") == 2
        && SortPlan.NameRank("Детали 2") == 2 && SortPlan.NameRank("Components A") == 2, "Ingot / component names");
    Check(SortPlan.NameRank("Руда и слитки") == null, "Two categories in one name: ordinary container");
    {
        var kt = new List<SortItemType> { T("FeT", 0, 1), T("IceT", 0, 1), T("NiT", 0, 1), T("SteelT", 1, 1),
            T("PlateT", 2, 20, false), T("WireT", 2, 0.2, false), T("MotorT", 2, 24, false) };
        SortContainer Ore(double cap = 67200) => new(cap, null, 0);
        SortContainer Plain(double cap = 67200) => new(cap, null);
        double Wrong(List<SortContainer> c, List<Dictionary<Guid, double>> cur)
            => Enumerable.Range(0, c.Count).Where(i => c[i].OnlyRank != null)
                .Sum(i => cur[i].Where(x => kt.First(k => k.Key == x.Key).Rank != c[i].OnlyRank).Sum(x => x.Value));
        double OreOutside(List<SortContainer> c, List<Dictionary<Guid, double>> cur)
            => Enumerable.Range(0, c.Count).Where(i => c[i].OnlyRank != 0)
                .Sum(i => cur[i].Where(x => kt.First(k => k.Key == x.Key).Rank == 0).Sum(x => x.Value));

        // The user's case: "Руда 0.1", "Руда 0.2" (new, empty) + mixed ordinary containers.
        var c1 = new List<SortContainer> { Ore(), Ore(), Plain(), Plain() };
        var s1 = new List<Dictionary<Guid, double>> { Inv(), Inv(),
            Inv(("FeT", 5000), ("IceT", 3000), ("PlateT", 200), ("WireT", 300), ("SteelT", 400)), Inv(("NiT", 2000), ("MotorT", 50)) };
        Simulate(c1, s1, kt);
        Check(OreOutside(c1, s1) == 0, "0.4.0: every ore goes into the 'Руда' containers");
        Check(Wrong(c1, s1) == 0, "0.4.0: no components or ingots in the 'Руда' containers");
        Check(SortPlan.Get(s1[3], ids["PlateT"]) == 200 && SortPlan.Get(s1[3], ids["WireT"]) == 300 && SortPlan.Get(s1[3], ids["MotorT"]) == 50
            && SortPlan.Get(s1[2], ids["SteelT"]) == 400 && s1[2].Count == 1,
            "0.4.0: with the ore gone, components gather in the components container, ingots stay apart");
        Check(SortPlan.Get(s1[0], ids["FeT"]) + SortPlan.Get(s1[1], ids["FeT"]) == 5000 && SortPlan.Get(s1[0], ids["IceT"]) + SortPlan.Get(s1[1], ids["IceT"]) == 3000,
            "0.4.0: ore amounts conserved");
        Check(Simulate(c1, s1, kt).moves == 0, "0.4.0: second press does nothing");

        // Components dumped into an ore container are taken out, even when their home is full.
        var c2 = new List<SortContainer> { Ore(), Plain(2000), Plain() };
        var s2t = new List<Dictionary<Guid, double>> { Inv(("FeT", 1000), ("PlateT", 50), ("WireT", 10)), Inv(("PlateT", 100)), Inv(("NiT", 10)) };
        Simulate(c2, s2t, kt);
        Check(Wrong(c2, s2t) == 0 && SortPlan.Get(s2t[0], ids["FeT"]) == 1000 && SortPlan.Get(s2t[0], ids["NiT"]) == 10, "0.4.0: components leave the ore container, ore comes in");
        Check(s2t.Sum(c => SortPlan.Get(c, ids["PlateT"])) == 150, "0.4.0: plates conserved");

        // Ore containers fill in order and are split across; what does not fit stays, nothing overflows.
        var c3 = new List<SortContainer> { Ore(5000), Ore(5000), Plain() };
        var s3t = new List<Dictionary<Guid, double>> { Inv(), Inv(), Inv(("IceT", 12000), ("PlateT", 10)) };
        Simulate(c3, s3t, kt);
        Check(SortPlan.Get(s3t[0], ids["IceT"]) == 5000 && SortPlan.Get(s3t[1], ids["IceT"]) == 5000 && SortPlan.Get(s3t[2], ids["IceT"]) == 2000,
            "0.4.0: ice fills Руда 1, then Руда 2, rest stays");
        Check(Simulate(c3, s3t, kt).moves == 0, "0.4.0: full ore containers: second press does nothing");

        // Several ores share the ore containers (any ore, mixed together, is fine there).
        var c4 = new List<SortContainer> { Ore(), Plain(), Plain() };
        var s4t = new List<Dictionary<Guid, double>> { Inv(("FeT", 100)), Inv(("IceT", 50), ("MotorT", 3)), Inv(("NiT", 70), ("FeT", 30)) };
        Simulate(c4, s4t, kt);
        Check(SortPlan.Get(s4t[0], ids["FeT"]) == 130 && SortPlan.Get(s4t[0], ids["IceT"]) == 50 && SortPlan.Get(s4t[0], ids["NiT"]) == 70,
            "0.4.0: all ores together in the ore container");

        // Randomised with named containers: conserved, capacity, wrong-category never grows, terminates.
        var rngT = new Random(777);
        for (int trial = 0; trial < 400; trial++)
        {
            int n = rngT.Next(2, 7); double cap = new[] { 1000.0, 5000, 67200 }[rngT.Next(3)];
            var cont = Enumerable.Range(0, n).Select(_ => new SortContainer(cap, null, rngT.Next(3) == 0 ? rngT.Next(3) : null)).ToList();
            var cur = Enumerable.Range(0, n).Select(_ => new Dictionary<Guid, double>()).ToList();
            foreach (var c in cur)
            {
                double fill = rngT.NextDouble() * cap, used = 0;
                for (int j = rngT.Next(8); j > 0; j--)
                {
                    var k = kt[rngT.Next(kt.Count)];
                    double a = k.Divisible ? Math.Round(rngT.NextDouble() * 300, 3) : rngT.Next(1, 300);
                    if (used + a * k.MassPerUnit > fill) continue;
                    c[k.Key] = SortPlan.Get(c, k.Key) + a; used += a * k.MassPerUnit;
                }
            }
            var before = kt.ToDictionary(k => k.Key, k => cur.Sum(c => SortPlan.Get(c, k.Key)));
            double wrongBefore = Wrong(cont, cur);
            Simulate(cont, cur, kt);
            Check(kt.All(k => Math.Abs(before[k.Key] - cur.Sum(c => SortPlan.Get(c, k.Key))) < 1e-6), "Named random: conserved");
            Check(Wrong(cont, cur) <= wrongBefore + 1e-6, "Named random: wrong category never grows");
            Check(cur.Select((c, i) => c.Sum(x => x.Value * kt.First(k => k.Key == x.Key).MassPerUnit) <= cap + 1e-3).All(x => x), "Named random: capacity");
        }
    }

    // ---- 0.4.1: overflow goes into as few containers as possible ----------------------------------
    {
        var kg = new List<SortItemType> { T("GridG", 2, 10, false), T("PlateG", 2, 20, false) };
        // Home (#0) is full; grids lie in #1, #2, #3 (equal piles in #1 and #2). All should end up
        // in ONE container, not stay spread over three.
        var cg = new List<SortContainer> { new(1000, null), new(67200, null), new(67200, null), new(67200, null) };
        var sg = new List<Dictionary<Guid, double>> { Inv(("GridG", 100)), Inv(("GridG", 30), ("PlateG", 5)), Inv(("GridG", 30)), Inv(("GridG", 10), ("PlateG", 1)) };
        Simulate(cg, sg, kg);
        var gridPlaces = Enumerable.Range(1, 3).Where(i => SortPlan.Get(sg[i], ids["GridG"]) > 0).ToList();
        Check(SortPlan.Get(sg[0], ids["GridG"]) == 100 && gridPlaces.Count == 1 && SortPlan.Get(sg[gridPlaces[0]], ids["GridG"]) == 70,
            "0.4.1: full home, the rest of the grids gather in one container");
        Check(Simulate(cg, sg, kg).moves == 0, "0.4.1: second press does nothing");
    }

    Check(SortPlan.IsExcludedName("Склад [Не сортировать]") && SortPlan.IsExcludedName("x [nosort]") && !SortPlan.IsExcludedName("Cargo"), "NoSort names");
}

Console.WriteLine($"PASS: {passed} assertions. Policy tests only; live transfers require an in-game test.");
