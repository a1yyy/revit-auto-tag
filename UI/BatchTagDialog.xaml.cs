using Autodesk.Revit.DB;
using RevitToolkit.Commands;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RevitToolkit.UI
{
    public partial class BatchTagDialog : Window
    {
        private readonly Document _hostDoc;
        private readonly IList<RevitLinkInstance> _links;
        private readonly PluginSettings _settings = PluginSettings.Load();

        private readonly List<(CheckBox Box, BuiltInCategory Cat)> _catBoxes
            = new List<(CheckBox, BuiltInCategory)>();
        private readonly List<(CheckBox Box, View View)> _viewBoxes
            = new List<(CheckBox, View)>();

        // ── Output properties ────────────────────────────────────────────────────────
        public Document TargetDoc                   { get; private set; }
        public RevitLinkInstance SelectedLink        { get; private set; }
        public List<BuiltInCategory> SelectedCategories { get; private set; }
        public FamilySymbol SelectedTagType          { get; private set; }
        public bool LeaderEnabled                    { get; private set; }
        public TagOrientation SelectedOrientation    { get; private set; }
        public bool OneTagPerType                    { get; private set; }
        public bool AvoidOverlaps                    { get; private set; }
        public List<View> SelectedViews              { get; private set; }
        public int MinElementThreshold               { get; private set; }

        // ── Category data (same as AutoKeynoteTagDialog) ─────────────────────────────
        private static readonly (string Name, BuiltInCategory Cat, int Group)[] AllCategories =
        {
            ("Walls",                BuiltInCategory.OST_Walls,               0),
            ("Floors",               BuiltInCategory.OST_Floors,              0),
            ("Ceilings",             BuiltInCategory.OST_Ceilings,            0),
            ("Roofs",                BuiltInCategory.OST_Roofs,               0),
            ("Doors",                BuiltInCategory.OST_Doors,               0),
            ("Windows",              BuiltInCategory.OST_Windows,             0),
            ("Columns (Arch)",       BuiltInCategory.OST_Columns,             0),
            ("Columns (Struct)",     BuiltInCategory.OST_StructuralColumns,   0),
            ("Beams",                BuiltInCategory.OST_StructuralFraming,   0),
            ("Stairs",               BuiltInCategory.OST_Stairs,              0),
            ("Railings",             BuiltInCategory.OST_Railings,            0),
            ("Ramps",                BuiltInCategory.OST_Ramps,               0),
            ("Casework",             BuiltInCategory.OST_Casework,            0),
            ("Planting",             BuiltInCategory.OST_Planting,            1),
            ("Generic Models",       BuiltInCategory.OST_GenericModel,        1),
            ("Site",                 BuiltInCategory.OST_Site,                1),
            ("Toposolid",            BuiltInCategory.OST_Toposolid,           1),
            ("Furniture",            BuiltInCategory.OST_Furniture,           1),
            ("Specialty Equipment",  BuiltInCategory.OST_SpecialityEquipment, 1),
            ("Detail Items",         BuiltInCategory.OST_DetailComponents,    2),
            ("Furniture Systems",    BuiltInCategory.OST_FurnitureSystems,    2),
            ("Mechanical Equipment", BuiltInCategory.OST_MechanicalEquipment, 2),
            ("Plumbing Fixtures",    BuiltInCategory.OST_PlumbingFixtures,    2),
            ("Electrical Fixtures",  BuiltInCategory.OST_ElectricalFixtures,  2),
            ("Lighting Fixtures",    BuiltInCategory.OST_LightingFixtures,    2),
        };

        private static readonly string[] GroupHeaders =
            { "— Architecture —", "— Landscape —", "— Other —" };

        private static readonly HashSet<BuiltInCategory> ArchPreset = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_Walls,             BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Ceilings,          BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Doors,             BuiltInCategory.OST_Windows,
            BuiltInCategory.OST_Columns,           BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Stairs,            BuiltInCategory.OST_Railings,
            BuiltInCategory.OST_Ramps,             BuiltInCategory.OST_Casework,
        };

        private static readonly HashSet<BuiltInCategory> LandscapePreset = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_Planting,          BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_Site,              BuiltInCategory.OST_Toposolid,
            BuiltInCategory.OST_Furniture,         BuiltInCategory.OST_SpecialityEquipment,
        };

        private static readonly ViewType[] SupportedViewTypes =
        {
            ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.Section,
            ViewType.Elevation, ViewType.Detail, ViewType.DraftingView,
            ViewType.AreaPlan,  ViewType.EngineeringPlan,
        };

        // ── Constructor ──────────────────────────────────────────────────────────────

        public BatchTagDialog(Document hostDoc, IList<RevitLinkInstance> links)
        {
            _hostDoc = hostDoc;
            _links   = links;
            InitializeComponent();
            PopulateControls();
        }

        // ── Initialisation ───────────────────────────────────────────────────────────

        private void PopulateControls()
        {
            // Model source
            var sources = new List<object>
            {
                new ModelSource { Label = $"[Current Model]  {_hostDoc.Title}", Doc = _hostDoc, Link = null }
            };
            foreach (var l in _links)
                sources.Add(new ModelSource
                {
                    Label = $"[Linked]  {l.GetLinkDocument().Title}",
                    Doc   = l.GetLinkDocument(),
                    Link  = l,
                });
            ModelSourceCombo.ItemsSource       = sources;
            ModelSourceCombo.DisplayMemberPath = "Label";
            ModelSourceCombo.SelectedIndex     = 0;

            // Keynote tag types
            var types = new FilteredElementCollector(_hostDoc)
                .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(fs =>
                {
                    var c = fs.Category;
                    return c != null &&
                           (c.Id.IntegerValue == (int)BuiltInCategory.OST_KeynoteTags ||
                            fs.Family?.FamilyCategory?.Id?.IntegerValue == (int)BuiltInCategory.OST_KeynoteTags);
                })
                .OrderBy(fs => fs.FamilyName).ThenBy(fs => fs.Name).ToList();

            TagTypeCombo.ItemsSource   = types;
            TagTypeCombo.DisplayMemberPath = "FamilyName";
            TagTypeCombo.SelectedIndex = types.Any() ? 0 : -1;
            if (!types.Any()) { TagTypeWarning.Visibility = Visibility.Visible; OkBtn.IsEnabled = false; }

            // Orientation
            OrientationCombo.ItemsSource   = new[] { "Horizontal", "Vertical" };
            OrientationCombo.SelectedIndex = 0;

            BuildCategoryPanel();
            BuildViewList(null);
            ApplySettings();
        }

        private void ApplySettings()
        {
            LeaderCheckBox.IsChecked        = _settings.LeaderEnabled;
            OneTagPerTypeCheckBox.IsChecked = _settings.OneTagPerType;
            AvoidOverlapsCheckBox.IsChecked = _settings.AvoidOverlaps;
            OrientationCombo.SelectedIndex  = _settings.OrientationVertical ? 1 : 0;

            if (!string.IsNullOrEmpty(_settings.LastTagFamilyName))
            {
                var match = TagTypeCombo.Items.Cast<FamilySymbol>()
                    .FirstOrDefault(fs => fs.FamilyName == _settings.LastTagFamilyName);
                if (match != null) TagTypeCombo.SelectedItem = match;
            }

            if (_settings.LastCategories.Any())
            {
                var saved = new HashSet<int>(_settings.LastCategories);
                foreach (var (cb, cat) in _catBoxes)
                    cb.IsChecked = saved.Contains((int)cat);
            }
        }

        private void BuildCategoryPanel()
        {
            CategoryPanel.Children.Clear();
            _catBoxes.Clear();

            var hdrBrush  = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#89B4FA"));
            var textBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4"));
            int curGroup  = -1;

            foreach (var (name, cat, group) in AllCategories)
            {
                if (Category.GetCategory(_hostDoc, cat) == null) continue;

                if (group != curGroup)
                {
                    curGroup = group;
                    CategoryPanel.Children.Add(new TextBlock
                    {
                        Text = GroupHeaders[group], Foreground = hdrBrush,
                        FontSize = 10, FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, group == 0 ? 0 : 6, 0, 3),
                    });
                }
                var cb = new CheckBox
                {
                    Content = name, Foreground = textBrush,
                    FontFamily = new FontFamily("Segoe UI"), FontSize = 11,
                    IsChecked = false, Margin = new Thickness(4, 1, 0, 1),
                };
                CategoryPanel.Children.Add(cb);
                _catBoxes.Add((cb, cat));
            }
        }

        private void BuildViewList(ViewType? filterType)
        {
            ViewPanel.Children.Clear();
            _viewBoxes.Clear();

            var textBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4"));
            var dimBrush  = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6C7086"));

            var views = new FilteredElementCollector(_hostDoc)
                .OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && SupportedViewTypes.Contains(v.ViewType))
                .Where(v => filterType == null || v.ViewType == filterType)
                .OrderBy(v => v.ViewType.ToString()).ThenBy(v => v.Name)
                .ToList();

            foreach (var view in views)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var cb = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                Grid.SetColumn(cb, 0);

                var name = new TextBlock
                {
                    Text = view.Name, Foreground = textBrush,
                    FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(name, 1);

                var typeLabel = new TextBlock
                {
                    Text = view.ViewType.ToString().Replace("Plan", " Plan"),
                    Foreground = dimBrush, FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(typeLabel, 2);

                row.Children.Add(cb);
                row.Children.Add(name);
                row.Children.Add(typeLabel);
                ViewPanel.Children.Add(row);
                _viewBoxes.Add((cb, view));
            }

            UpdateSelectionCount();
        }

        private void UpdateSelectionCount()
        {
            int n = _viewBoxes.Count(x => x.Box.IsChecked == true);
            SelectionCountText.Text = n == 0 ? "No views selected"
                                    : n == 1 ? "1 view selected"
                                             : $"{n} views selected";
            OkBtn.Content = n == 0 ? "Tag Selected Views →" : $"Tag {n} View(s) →";
        }

        // ── Preset / filter handlers ─────────────────────────────────────────────────

        private void PresetArch_Click(object sender, RoutedEventArgs e)      => ApplyPreset(ArchPreset);
        private void PresetLandscape_Click(object sender, RoutedEventArgs e) => ApplyPreset(LandscapePreset);
        private void CatAll_Click(object sender, RoutedEventArgs e)          => SetAllCats(true);
        private void CatNone_Click(object sender, RoutedEventArgs e)         => SetAllCats(false);

        private void ApplyPreset(HashSet<BuiltInCategory> preset)
        {
            foreach (var (cb, cat) in _catBoxes) cb.IsChecked = preset.Contains(cat);
        }
        private void SetAllCats(bool v) { foreach (var (cb, _) in _catBoxes) cb.IsChecked = v; }

        private void ViewTypeFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (ViewTypeFilter == null) return;
            var selected = (ViewTypeFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            ViewType? filter = null;
            if (!string.IsNullOrEmpty(selected) && System.Enum.TryParse(selected, out ViewType vt))
                filter = vt;
            BuildViewList(filter);
        }

        private void ViewSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var (cb, _) in _viewBoxes) cb.IsChecked = true;
            UpdateSelectionCount();
        }
        private void ViewSelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var (cb, _) in _viewBoxes) cb.IsChecked = false;
            UpdateSelectionCount();
        }

        // Subscribe checkboxes to update count
        private void ViewCheckBox_Changed(object sender, RoutedEventArgs e) => UpdateSelectionCount();

        // Override BuildViewList to wire up checkbox events
        // (called after _viewBoxes is populated — attach Changed handler)
        private void AttachViewCheckHandlers()
        {
            foreach (var (cb, _) in _viewBoxes)
                cb.Checked += ViewCheckBox_Changed;
            foreach (var (cb, _) in _viewBoxes)
                cb.Unchecked += ViewCheckBox_Changed;
        }

        // ── OK / Cancel ──────────────────────────────────────────────────────────────

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var src = ModelSourceCombo.SelectedItem as ModelSource;
            if (src == null) { Warn("Select a model source."); return; }

            var cats = _catBoxes.Where(x => x.Box.IsChecked == true).Select(x => x.Cat).ToList();
            if (cats.Count == 0) { Warn("Select at least one category."); return; }

            if (TagTypeCombo.SelectedItem == null)
            { Warn("No keynote tag type available.\nLoad a Keynote Tag family first."); return; }

            var selectedViews = _viewBoxes.Where(x => x.Box.IsChecked == true).Select(x => x.View).ToList();
            if (selectedViews.Count == 0) { Warn("Select at least one view."); return; }

            int threshold = 0;
            if (ThresholdCheckBox.IsChecked == true)
                int.TryParse(ThresholdValue.Text, out threshold);

            TargetDoc           = src.Doc;
            SelectedLink        = src.Link;
            SelectedCategories  = cats;
            SelectedTagType     = TagTypeCombo.SelectedItem as FamilySymbol;
            LeaderEnabled       = LeaderCheckBox.IsChecked == true;
            OneTagPerType       = OneTagPerTypeCheckBox.IsChecked == true;
            AvoidOverlaps       = AvoidOverlapsCheckBox.IsChecked == true;
            SelectedOrientation = OrientationCombo.SelectedIndex == 1
                                  ? TagOrientation.Vertical : TagOrientation.Horizontal;
            SelectedViews       = selectedViews;
            MinElementThreshold = threshold;

            _settings.LeaderEnabled       = LeaderEnabled;
            _settings.OneTagPerType       = OneTagPerType;
            _settings.AvoidOverlaps       = AvoidOverlaps;
            _settings.OrientationVertical = SelectedOrientation == TagOrientation.Vertical;
            _settings.LastTagFamilyName   = SelectedTagType?.FamilyName ?? "";
            _settings.LastCategories      = cats.Select(c => (int)c).ToList();
            _settings.Save();

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
        private void Warn(string m) => MessageBox.Show(m, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);

        private class ModelSource
        {
            public string Label          { get; set; }
            public Document Doc          { get; set; }
            public RevitLinkInstance Link { get; set; }
        }
    }
}
