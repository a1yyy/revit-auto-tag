using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace RevitToolkit.UI
{
    public partial class AutoKeynoteTagDialog : Window
    {
        private readonly Document _hostDoc;
        private readonly List<RevitLinkInstance> _links;
        private readonly View _activeView;

        // Filtered to categories that exist in the current Revit version at runtime
        private (string Name, BuiltInCategory Cat)[] _availableCategories;

        // Output properties
        public Document SelectedDocument           { get; private set; }
        public RevitLinkInstance SelectedLinkInstance { get; private set; }
        public BuiltInCategory SelectedCategory    { get; private set; }
        public FamilySymbol SelectedKeynoteTagType { get; private set; }
        public bool LeaderEnabled                  { get; private set; }
        public TagOrientation SelectedOrientation  { get; private set; }
        public bool OneTagPerType                  { get; private set; }

        // Full master list — OST_Toposolid requires Revit 2024+.
        // Categories not present in the active Revit version are filtered
        // at runtime via Category.GetCategory so the DLL stays safe on 2022/2023.
        private static readonly (string Name, BuiltInCategory Cat)[] TaggableCategories =
        {
            ("Walls",              BuiltInCategory.OST_Walls),
            ("Floors",             BuiltInCategory.OST_Floors),
            ("Ceilings",           BuiltInCategory.OST_Ceilings),
            ("Roofs",              BuiltInCategory.OST_Roofs),
            ("Doors",              BuiltInCategory.OST_Doors),
            ("Windows",            BuiltInCategory.OST_Windows),
            ("Columns (Arch)",     BuiltInCategory.OST_Columns),
            ("Columns (Struct)",   BuiltInCategory.OST_StructuralColumns),
            ("Beams",              BuiltInCategory.OST_StructuralFraming),
            ("Furniture",          BuiltInCategory.OST_Furniture),
            ("Furniture Systems",  BuiltInCategory.OST_FurnitureSystems),
            ("Casework",           BuiltInCategory.OST_Casework),
            ("Mechanical Equip.",  BuiltInCategory.OST_MechanicalEquipment),
            ("Plumbing Fixtures",  BuiltInCategory.OST_PlumbingFixtures),
            ("Electrical Fixtures",BuiltInCategory.OST_ElectricalFixtures),
            ("Lighting Fixtures",  BuiltInCategory.OST_LightingFixtures),
            ("Specialty Equip.",   BuiltInCategory.OST_SpecialityEquipment),
            ("Generic Models",     BuiltInCategory.OST_GenericModel),
            ("Site",               BuiltInCategory.OST_Site),
            ("Toposolid",          BuiltInCategory.OST_Toposolid),
            ("Stairs",             BuiltInCategory.OST_Stairs),
            ("Railings",           BuiltInCategory.OST_Railings),
            ("Ramps",              BuiltInCategory.OST_Ramps),
        };

        public AutoKeynoteTagDialog(Document hostDoc, List<RevitLinkInstance> links, View activeView)
        {
            _hostDoc    = hostDoc;
            _links      = links;
            _activeView = activeView;
            InitializeComponent();
            PopulateControls();
        }

        private void PopulateControls()
        {
            // Model source combo
            var modelSources = new List<object>();
            modelSources.Add(new ModelSource { DisplayName = $"[Current Model]  {_hostDoc.Title}", Doc = _hostDoc, Link = null });
            foreach (var l in _links)
                modelSources.Add(new ModelSource { DisplayName = $"[Linked]  {l.GetLinkDocument().Title}", Doc = l.GetLinkDocument(), Link = l });

            ModelSourceCombo.ItemsSource       = modelSources;
            ModelSourceCombo.DisplayMemberPath = "DisplayName";
            ModelSourceCombo.SelectedIndex     = 0;

            // Filter master list to categories that exist in this Revit version
            _availableCategories = TaggableCategories
                .Where(c => Category.GetCategory(_hostDoc, c.Cat) != null)
                .ToArray();

            CategoryCombo.ItemsSource  = _availableCategories.Select(c => c.Name).ToList();
            CategoryCombo.SelectedIndex = 0;

            // Keynote tag types
            PopulateKeynoteTagTypes();

            // Orientation
            OrientationCombo.ItemsSource   = new[] { "Horizontal", "Vertical" };
            OrientationCombo.SelectedIndex = 0;
        }

        private void PopulateKeynoteTagTypes()
        {
            var keynoteTagTypes = new FilteredElementCollector(_hostDoc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs =>
                {
                    Category cat = fs.Category;
                    return cat != null &&
                           (cat.Id.IntegerValue == (int)BuiltInCategory.OST_KeynoteTags ||
                            (fs.Family?.FamilyCategory?.Id?.IntegerValue == (int)BuiltInCategory.OST_KeynoteTags));
                })
                .OrderBy(fs => fs.FamilyName)
                .ThenBy(fs => fs.Name)
                .ToList();

            if (!keynoteTagTypes.Any())
            {
                // Fallback: grab any IndependentTag family and let user figure it out
                keynoteTagTypes = new FilteredElementCollector(_hostDoc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(fs => fs.Category?.Id?.IntegerValue == (int)BuiltInCategory.OST_KeynoteTags)
                    .ToList();
            }

            TagTypeCombo.ItemsSource         = keynoteTagTypes;
            TagTypeCombo.DisplayMemberPath   = "FamilyName";  // custom template shows both
            TagTypeCombo.SelectedIndex       = keynoteTagTypes.Any() ? 0 : -1;

            if (!keynoteTagTypes.Any())
            {
                TagTypeWarning.Visibility = Visibility.Visible;
                OkBtn.IsEnabled = false;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var src = ModelSourceCombo.SelectedItem as ModelSource;
            if (src == null) { Warn("Select a model source."); return; }

            if (CategoryCombo.SelectedIndex < 0) { Warn("Select a category."); return; }
            if (TagTypeCombo.SelectedItem == null) { Warn("No keynote tag type available.\nLoad a Keynote Tag family into the project first."); return; }

            SelectedDocument       = src.Doc;
            SelectedLinkInstance   = src.Link;
            SelectedCategory       = _availableCategories[CategoryCombo.SelectedIndex].Cat;
            SelectedKeynoteTagType = TagTypeCombo.SelectedItem as FamilySymbol;
            LeaderEnabled          = LeaderCheckBox.IsChecked == true;
            OneTagPerType          = OneTagPerTypeCheckBox.IsChecked == true;
            SelectedOrientation    = OrientationCombo.SelectedIndex == 1
                                     ? TagOrientation.Vertical
                                     : TagOrientation.Horizontal;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        { DialogResult = false; Close(); }

        private void Warn(string msg) => MessageBox.Show(msg, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);

        private class ModelSource
        {
            public string DisplayName { get; set; }
            public Document Doc       { get; set; }
            public RevitLinkInstance Link { get; set; }
        }
    }
}
