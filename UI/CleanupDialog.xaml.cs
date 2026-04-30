using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace RevitToolkit.UI
{
    public partial class CleanupDialog : Window
    {
        private readonly Document _doc;
        private readonly View _view;
        private readonly IList<RevitLinkInstance> _links;

        private List<ElementId> _orphanedIds  = new List<ElementId>();
        private List<ElementId> _duplicateIds = new List<ElementId>(); // extras to delete

        public CleanupDialog(Document doc, View view, IList<RevitLinkInstance> links)
        {
            _doc   = doc;
            _view  = view;
            _links = links;
            InitializeComponent();
            ViewNameText.Text = view.Name;
            Scan();
        }

        // ── Scan ─────────────────────────────────────────────────────────────────────

        private void Scan()
        {
            var allKeynote = new FilteredElementCollector(_doc, _view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .Where(t => t.Category?.Id?.IntegerValue == (int)BuiltInCategory.OST_KeynoteTags)
                .ToList();

            _orphanedIds  = new List<ElementId>();
            _duplicateIds = new List<ElementId>();
            var blankIds  = new List<ElementId>();

            // Group non-orphaned tags by the TypeId of their tagged element.
            // This detects both "same element tagged twice" and "same type tagged twice".
            var tagsByTypeId = new Dictionary<ElementId, List<IndependentTag>>();

            foreach (var tag in allKeynote)
            {
                if (IsOrphaned(tag))
                {
                    _orphanedIds.Add(tag.Id);
                    continue;
                }

                ElementId elemId = GetPrimaryTaggedId(tag);
                if (elemId == null || elemId == ElementId.InvalidElementId) continue;

                Element elem = GetElementFromAnyDoc(elemId);
                if (elem == null) continue;

                ElementId typeId = elem.GetTypeId();
                if (!tagsByTypeId.ContainsKey(typeId))
                    tagsByTypeId[typeId] = new List<IndependentTag>();
                tagsByTypeId[typeId].Add(tag);

                if (HasBlankKeynote(elem))
                    blankIds.Add(tag.Id);
            }

            // Keep the tag with the lowest ElementId per type group; flag the rest
            foreach (var group in tagsByTypeId.Values.Where(g => g.Count > 1))
            {
                foreach (var extra in group.OrderBy(t => t.Id.IntegerValue).Skip(1))
                    _duplicateIds.Add(extra.Id);
            }

            // Update UI labels
            int total  = allKeynote.Count;
            int issues = _orphanedIds.Count + _duplicateIds.Count + blankIds.Count;

            TotalText.Text     = total.ToString();
            ValidText.Text     = (total - _orphanedIds.Count - _duplicateIds.Count).ToString();
            OrphanedText.Text  = _orphanedIds.Count.ToString();
            DuplicateText.Text = _duplicateIds.Count.ToString();
            BlankText.Text     = blankIds.Count.ToString();

            StatusText.Text    = issues == 0 ? "✓  View is clean." : $"⚠  {issues} issue(s) found.";
            StatusText.Foreground = issues == 0
                ? System.Windows.Media.Brushes.LightGreen
                : System.Windows.Media.Brushes.Orange;

            DeleteOrphanedBtn.IsEnabled  = _orphanedIds.Count  > 0;
            DeleteDuplicateBtn.IsEnabled = _duplicateIds.Count > 0;
        }

        // ── Actions ───────────────────────────────────────────────────────────────────

        private void DeleteOrphaned_Click(object sender, RoutedEventArgs e)
        {
            if (_orphanedIds.Count == 0) return;
            using (var tx = new Transaction(_doc, "Delete Orphaned Keynote Tags"))
            {
                tx.Start();
                _doc.Delete(_orphanedIds);
                tx.Commit();
            }
            Scan();
        }

        private void DeleteDuplicate_Click(object sender, RoutedEventArgs e)
        {
            if (_duplicateIds.Count == 0) return;
            using (var tx = new Transaction(_doc, "Delete Duplicate Keynote Tags"))
            {
                tx.Start();
                _doc.Delete(_duplicateIds);
                tx.Commit();
            }
            Scan();
        }

        private void Rescan_Click(object sender, RoutedEventArgs e) => Scan();
        private void Close_Click(object sender, RoutedEventArgs e)  => Close();

        // ── Helpers ───────────────────────────────────────────────────────────────────

        private bool IsOrphaned(IndependentTag tag)
        {
            try
            {
                var ids = tag.GetTaggedLocalElementIds();
                if (ids == null || !ids.Any()) return true;

                foreach (var id in ids)
                {
                    if (id == ElementId.InvalidElementId) return true;
                    if (GetElementFromAnyDoc(id) != null) return false;
                }
                return true;
            }
            catch { return true; }
        }

        private ElementId GetPrimaryTaggedId(IndependentTag tag)
        {
            try { return tag.GetTaggedLocalElementIds()?.FirstOrDefault(); }
            catch { return null; }
        }

        private Element GetElementFromAnyDoc(ElementId id)
        {
            var elem = _doc.GetElement(id);
            if (elem != null) return elem;
            foreach (var link in _links)
            {
                elem = link.GetLinkDocument()?.GetElement(id);
                if (elem != null) return elem;
            }
            return null;
        }

        private bool HasBlankKeynote(Element elem)
        {
            try
            {
                Element type = GetElementFromAnyDoc(elem.GetTypeId());
                var param = type?.get_Parameter(BuiltInParameter.KEYNOTE_PARAM);
                return param == null || string.IsNullOrWhiteSpace(param.AsString());
            }
            catch { return false; }
        }
    }
}
