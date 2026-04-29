using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RevitToolkit.UI
{
    public partial class AutoKeynoteTagDialog : Window
    {
        private readonly Document _hostDoc;
        private readonly List<RevitLinkInstance> _links;
        private readonly View _activeView;

        private readonly List<(CheckBox Box, BuiltInCategory Cat)> _categoryCheckBoxes
            = new List<(CheckBox, BuiltInCategory)>();

        // ── Output properties ────────────────────────────────────────────────────────
        public Document SelectedDocument              { get; private set; }
        public RevitLinkInstance SelectedLinkInstance { get; private set; }
        public List<BuiltInCategory> SelectedCategories { get; private set; }
        public FamilySymbol SelectedKeynoteTagType    { get; private set; }
        public bool LeaderEnabled                     { get; private set; }
        public TagOrientation SelectedOrientation     { get; private set; }
        public bool OneTagPerType                     { get; private set; }
        public bool AvoidOverlaps                     { get; private set; }

        // ── Category master list ─────────────────────────────────────────────────────
        // group 0 = Architecture, 1 = Landscape, 2 = Other
        // OST_Toposolid requires Revit 2024+; filtered at runtime via Category.GetCategory.
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

        // ── Constructor ──────────────────────────────────────────────────────────────

        public AutoKeynoteTagDialog(Document hostDoc, List<RevitLinkInstance> links, View activeView)
        {
            _hostDoc    = hostDoc;
            _links      = links;
            _activeView = activeView;
            InitializeComponent();
            PopulateControls();
        }

        // ── Initialisation ───────────────────────────────────────────────────────────

        private void PopulateControls()
        {
            var sources = new List<object>
            {
                new ModelSource { DisplayName = $"[Current Model]  {_hostDoc.Title}", Doc = _hostDoc, Link = null }
            };
            foreach (var l in _links)
                sources.Add(new ModelSource
                {
                    DisplayName = $"[Linked]  {l.GetLinkDocument().Title}",
                    Doc  = l.GetLinkDocument(),
                    Link = l
                });

            ModelSourceCombo.ItemsSource       = sources;
            ModelSourceCombo.DisplayMemberPath = "DisplayName";
            ModelSourceCombo.SelectedIndex     = 0;

            BuildCategoryPanel();
            PopulateKeynoteTagTypes();

            OrientationCombo.ItemsSource   = new[] { "Horizontal", "Vertical" };
            OrientationCombo.SelectedIndex = 0;
        }

        private void BuildCategoryPanel()
        {
            CategoryPanel.Children.Clear();
            _categoryCheckBoxes.Clear();

            var headerBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#89B4FA"));
            var textBrush   = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4"));

            int currentGroup = -1;

            foreach (var (name, cat, group) in AllCategories)
            {
                if (Category.GetCategory(_hostDoc, cat) == null) continue;

                if (group != currentGroup)
                {
                    currentGroup = group;
                    CategoryPanel.Children.Add(new TextBlock
                    {
                        Text       = GroupHeaders[group],
                        Foreground = headerBrush,
                        FontSize   = 10,
                        FontWeight = FontWeights.SemiBold,
                        Margin     = new Thickness(0, group == 0 ? 0 : 8, 0, 4),
                    });
                }

                var cb = new CheckBox
                {
                    Content    = name,
                    Foreground = textBrush,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize   = 12,
                    IsChecked  = false,
                    Margin     = new Thickness(4, 2, 0, 2),
                };
                CategoryPanel.Children.Add(cb);
                _categoryCheckBoxes.Add((cb, cat));
            }
        }

        private void PopulateKeynoteTagTypes()
        {
            var types = new FilteredElementCollector(_hostDoc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs =>
                {
                    var cat = fs.Category;
                    return cat != null &&
                           (cat.Id.IntegerValue == (int)BuiltInCategory.OST_KeynoteTags ||
                            fs.Family?.FamilyCategory?.Id?.IntegerValue == (int)BuiltInCategory.OST_KeynoteTags);
                })
                .OrderBy(fs => fs.FamilyName)
                .ThenBy(fs => fs.Name)
                .ToList();

            if (!types.Any())
                types = new FilteredElementCollector(_hostDoc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(fs => fs.Category?.Id?.IntegerValue == (int)BuiltInCategory.OST_KeynoteTags)
                    .ToList();

            TagTypeCombo.ItemsSource   = types;
            TagTypeCombo.DisplayMemberPath = "FamilyName";
            TagTypeCombo.SelectedIndex = types.Any() ? 0 : -1;

            if (!types.Any())
            {
                TagTypeWarning.Visibility = Visibility.Visible;
                OkBtn.IsEnabled = false;
            }
        }

        // ── Preset buttons ───────────────────────────────────────────────────────────

        private void PresetArch_Click(object sender, RoutedEventArgs e)      => ApplyPreset(ArchPreset);
        private void PresetLandscape_Click(object sender, RoutedEventArgs e) => ApplyPreset(LandscapePreset);
        private void SelectAll_Click(object sender, RoutedEventArgs e)       => SetAll(true);
        private void ClearAll_Click(object sender, RoutedEventArgs e)        => SetAll(false);

        private void ApplyPreset(HashSet<BuiltInCategory> preset)
        {
            foreach (var (cb, cat) in _categoryCheckBoxes)
                cb.IsChecked = preset.Contains(cat);
        }

        private void SetAll(bool check)
        {
            foreach (var (cb, _) in _categoryCheckBoxes)
                cb.IsChecked = check;
        }

        // ── OK / Cancel ──────────────────────────────────────────────────────────────

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var src = ModelSourceCombo.SelectedItem as ModelSource;
            if (src == null) { Warn("Select a model source."); return; }

            var selected = _categoryCheckBoxes
                .Where(x => x.Box.IsChecked == true)
                .Select(x => x.Cat)
                .ToList();
            if (selected.Count == 0) { Warn("Select at least one category."); return; }

            if (TagTypeCombo.SelectedItem == null)
            { Warn("No keynote tag type available.\nLoad a Keynote Tag family into the project first."); return; }

            SelectedDocument       = src.Doc;
            SelectedLinkInstance   = src.Link;
            SelectedCategories     = selected;
            SelectedKeynoteTagType = TagTypeCombo.SelectedItem as FamilySymbol;
            LeaderEnabled          = LeaderCheckBox.IsChecked == true;
            OneTagPerType          = OneTagPerTypeCheckBox.IsChecked == true;
            AvoidOverlaps          = AvoidOverlapsCheckBox.IsChecked == true;
            SelectedOrientation    = OrientationCombo.SelectedIndex == 1
                                     ? TagOrientation.Vertical
                                     : TagOrientation.Horizontal;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

        private void Warn(string msg) =>
            MessageBox.Show(msg, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);

        private class ModelSource
        {
            public string DisplayName    { get; set; }
            public Document Doc          { get; set; }
            public RevitLinkInstance Link { get; set; }
        }
    }
}
