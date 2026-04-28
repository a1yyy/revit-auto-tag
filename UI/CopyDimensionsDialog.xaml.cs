using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace RevitToolkit.UI
{
    public partial class CopyDimensionsDialog : Window
    {
        private readonly Document _doc;
        public View SelectedSourceView { get; private set; }
        public List<View> SelectedTargetViews { get; private set; } = new List<View>();

        // All views that can hold dimensions
        private static readonly ViewType[] SupportedViewTypes = new[]
        {
            ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.Elevation,
            ViewType.Section, ViewType.Detail, ViewType.DraftingView,
            ViewType.AreaPlan, ViewType.EngineeringPlan
        };

        public CopyDimensionsDialog(Document doc)
        {
            _doc = doc;
            InitializeComponent();
            PopulateViews();
        }

        private void PopulateViews()
        {
            var views = new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && SupportedViewTypes.Contains(v.ViewType))
                .OrderBy(v => v.ViewType.ToString())
                .ThenBy(v => v.Name)
                .ToList();

            SourceViewCombo.ItemsSource   = views;
            SourceViewCombo.DisplayMemberPath = "Name";

            TargetViewsList.ItemsSource   = views;
            TargetViewsList.DisplayMemberPath = "Name";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedSourceView  = SourceViewCombo.SelectedItem as View;
            SelectedTargetViews = TargetViewsList.SelectedItems.Cast<View>().ToList();

            if (SelectedSourceView == null)
            { MessageBox.Show("Please select a source view.", "Validation"); return; }

            if (SelectedTargetViews.Count == 0)
            { MessageBox.Show("Please select at least one target view.", "Validation"); return; }

            if (SelectedTargetViews.Any(v => v.Id == SelectedSourceView.Id))
            { MessageBox.Show("The source view cannot also be a target view.", "Validation"); return; }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e) =>
            TargetViewsList.SelectAll();

        private void ClearAll_Click(object sender, RoutedEventArgs e) =>
            TargetViewsList.UnselectAll();
    }
}
