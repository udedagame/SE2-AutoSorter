using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Keen.Game2.Client.UI.TerminalScreen.Inventory;
using MonoMod.RuntimeDetour;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace AutoSorter;

/// <summary>
/// Adds a "sort" button to the header of every inventory list in the terminal, next to Keen's
/// "show/hide empty" and "show connected only" buttons.
///
/// Layout facts (from the compiled InventoryList.axaml): the header is a Grid with 7 columns —
/// 0 source carousel, 2 search box, 4 PART_ShowHideEmptyButton, 6 PART_ShowConnectedButton, with
/// 1/3/5 as spacers. We append a spacer and an Auto column and put our button in it, then shrink
/// the search box by the same width so the header keeps its size.
/// </summary>
internal static class ManualSortButton
{
    private const string ButtonName = "AutoSorter_SortButton";
    private static Hook? _hook;
    private delegate void InitOriginal(InventoryList self, bool loadXaml);

    public static void Install()
    {
        if (_hook != null) return;
        var init = typeof(InventoryList).GetMethod("InitializeComponent",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null)
            ?? throw new MissingMethodException("InventoryList.InitializeComponent(bool)");
        _hook = new Hook(init, (InitOriginal orig, InventoryList self, bool loadXaml) =>
        {
            orig(self, loadXaml);
            try { AddButton(self); }
            catch (Exception e) { Log.Write("Could not add the sort button (the terminal still works): " + e); }
        });
        Log.Write("Hook installed on InventoryList.InitializeComponent.");
    }

    public static void Uninstall()
    {
        _hook?.Dispose();
        _hook = null;
    }

    private static void AddButton(InventoryList list)
    {
        var connected = list.FindControl<Button>("PART_ShowConnectedButton");
        if (connected?.Parent is not Grid header)
        {
            Log.Write("Sort button: PART_ShowConnectedButton or its header grid not found; layout changed in a game update?");
            return;
        }
        foreach (var child in header.Children)
            if (child is Control { Name: ButtonName }) return;

        int column = Grid.GetColumn(connected);
        var spacer = column > 0 && column - 1 < header.ColumnDefinitions.Count
            ? header.ColumnDefinitions[column - 1].Width
            : new GridLength(8);
        header.ColumnDefinitions.Add(new ColumnDefinition(spacer));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var button = new Button
        {
            Name = ButtonName,
            Content = SortIcon(),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Classes.Add("SmallButton");
        Grid.SetColumn(button, header.ColumnDefinitions.Count - 1);
        Grid.SetRow(button, Grid.GetRow(connected));
        if (!double.IsNaN(connected.Width)) button.Width = connected.Width;
        if (!double.IsNaN(connected.Height)) button.Height = connected.Height;
        const string tip = "Сортировать: одинаковые предметы в один контейнер\nSort: same items into one container";
        ToolTip.SetTip(button, tip);

        button.Click += (_, _) =>
        {
            try
            {
                button.IsEnabled = false;
                bool started = ManualSorter.Start(button, status =>
                {
                    if (status == null)
                    {
                        button.IsEnabled = true;
                        ToolTip.SetTip(button, tip);
                    }
                    else ToolTip.SetTip(button, status);
                });
                if (!started) button.IsEnabled = true;
            }
            catch (Exception e)
            {
                button.IsEnabled = true;
                Log.Write("Sort could not start: " + e);
            }
        };

        header.Children.Add(button);
        ShrinkSearchBoxOnce(list, button, spacer);
        Log.Write("Sort button added to an inventory list.");
    }

    /// <summary>Give back the space the new button takes, so the header does not grow.</summary>
    private static void ShrinkSearchBoxOnce(InventoryList list, Button button, GridLength spacer)
    {
        var search = list.FindControl<TextBox>("PART_InventorySearchBox");
        if (search == null || double.IsNaN(search.Width)) return;
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (button.Bounds.Width <= 0) return;
            button.LayoutUpdated -= handler;
            double take = button.Bounds.Width + (spacer.IsAbsolute ? spacer.Value : 8);
            search.Width = Math.Max(80, search.Width - take);
        };
        button.LayoutUpdated += handler;
    }

    /// <summary>Three bars, long to short: a "sort" glyph that needs no font or game asset.</summary>
    private static Control SortIcon() => new ShapePath
    {
        Data = Geometry.Parse("M0,0 H16 V2.5 H0 Z M0,6 H11 V8.5 H0 Z M0,12 H6 V14.5 H0 Z"),
        Fill = new SolidColorBrush(Color.FromRgb(0xD0, 0xD8, 0xDC)),
        Stretch = Stretch.None,
        Width = 16,
        Height = 15,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
