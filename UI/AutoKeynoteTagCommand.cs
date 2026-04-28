using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitToolkit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitToolkit.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoKeynoteTagCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            // Validate active view supports tagging
            if (activeView is View3D || activeView is ViewSchedule || activeView is ViewSheet)
            {
                TaskDialog.Show("Auto Keynote Tag",
                    "Please activate a floor plan, section, elevation, or drafting view before running this command.");
                return Result.Cancelled;
            }

            try
            {
                // Collect linked models + host model
                var linkedDocs = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .Where(l => l.GetLinkDocument() != null)
                    .ToList();

                // Show configuration dialog
                var dialog = new AutoKeynoteTagDialog(doc, linkedDocs, activeView);
                if (dialog.ShowDialog() != true)
                    return Result.Cancelled;

                Document targetDoc                  = dialog.SelectedDocument;
                RevitLinkInstance link              = dialog.SelectedLinkInstance;
                List<BuiltInCategory> categories    = dialog.SelectedCategories;
                FamilySymbol tagType                = dialog.SelectedKeynoteTagType;
                bool leaderEnabled                  = dialog.LeaderEnabled;
                bool oneTagPerType                  = dialog.OneTagPerType;
                TagOrientation orientation          = dialog.SelectedOrientation;

                if (tagType == null)
                {
                    TaskDialog.Show("Auto Keynote Tag", "No keynote tag type selected.");
                    return Result.Cancelled;
                }

                // Collect from every selected category; deduplicate by ElementId
                var candidates = categories
                    .SelectMany(cat => CollectTaggableElements(doc, targetDoc, link, activeView, cat))
                    .GroupBy(e => e.Id)
                    .Select(g => g.First())
                    .ToList();

                var existingTaggedIds = GetAlreadyTaggedElementIds(doc, activeView);

                List<Element> toTag;
                if (oneTagPerType)
                {
                    // One tag per unique family type: group candidates by TypeId,
                    // skip any group where at least one instance is already tagged.
                    toTag = candidates
                        .GroupBy(e => e.GetTypeId())
                        .Where(g => !g.Any(e => existingTaggedIds.Contains(e.Id)))
                        .Select(g => g.First())
                        .ToList();
                }
                else
                {
                    toTag = candidates
                        .Where(e => !existingTaggedIds.Contains(e.Id))
                        .ToList();
                }

                if (toTag.Count == 0)
                {
                    TaskDialog.Show("Auto Keynote Tag",
                        "All visible elements in the selected categories are already tagged, or no elements were found.");
                    return Result.Succeeded;
                }

                int placed = 0;
                int skipped = 0;
                var errors = new List<string>();

                using (Transaction tx = new Transaction(doc, "Auto Place Keynote Tags"))
                {
                    tx.Start();

                    // Activate tag symbol if needed
                    if (!tagType.IsActive)
                        tagType.Activate();

                    foreach (Element elem in toTag)
                    {
                        try
                        {
                            XYZ tagPoint = ComputeTagInsertionPoint(doc, elem, link, activeView);
                            if (tagPoint == null) { skipped++; continue; }

                            Reference elemRef = link != null
                                ? new Reference(elem).CreateLinkReference(link)
                                : new Reference(elem);

                            IndependentTag.Create(
                                doc,
                                tagType.Id,
                                activeView.Id,
                                elemRef,
                                leaderEnabled,
                                orientation,
                                tagPoint);

                            placed++;
                        }
                        catch (Exception ex)
                        {
                            skipped++;
                            if (errors.Count < 5)
                                errors.Add($"  • {elem.Name} (id {elem.Id}): {ex.Message}");
                        }
                    }

                    tx.Commit();
                }

                string mode    = oneTagPerType ? "one per family type" : "all instances";
                string summary = $"✅ Keynote tags placed: {placed}  ({mode})\n" +
                                 $"⏭  Skipped (no geometry / already tagged): {skipped}\n" +
                                 $"   Categories tagged: {categories.Count}";
                if (errors.Any())
                    summary += "\n\nFirst errors:\n" + string.Join("\n", errors);

                TaskDialog.Show("Auto Keynote Tag — Results", summary);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ─── Helpers ────────────────────────────────────────────────────────────────

        private IEnumerable<Element> CollectTaggableElements(
            Document hostDoc,
            Document targetDoc,
            RevitLinkInstance link,
            View activeView,
            BuiltInCategory category)
        {
            // For host model: use view-scoped collector
            if (link == null)
            {
                return new FilteredElementCollector(hostDoc, activeView.Id)
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .ToElements();
            }

            // For linked model: collect from link document
            // We still filter by the host view's crop box bounding volume
            BoundingBoxXYZ viewBB = activeView.CropBox;
            Outline viewOutline = new Outline(
                link.GetTransform().Inverse.OfPoint(viewBB.Min),
                link.GetTransform().Inverse.OfPoint(viewBB.Max));

            var bbFilter = new BoundingBoxIntersectsFilter(viewOutline);

            return new FilteredElementCollector(targetDoc)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .WherePasses(bbFilter)
                .ToElements();
        }

        private HashSet<ElementId> GetAlreadyTaggedElementIds(Document doc, View view)
        {
            var taggedIds = new HashSet<ElementId>();

            var existingTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>();

            foreach (var tag in existingTags)
            {
                try
                {
                    // GetTaggedLocalElementIds available in Revit 2022+
                    var ids = tag.GetTaggedLocalElementIds();
                    foreach (var id in ids) taggedIds.Add(id);
                }
                catch
                {
                    // Fallback for older API
                    try { taggedIds.Add(tag.TaggedLocalElementId); } catch { }
                }
            }

            return taggedIds;
        }

        private XYZ ComputeTagInsertionPoint(Document doc, Element elem, RevitLinkInstance link, View view)
        {
            BoundingBoxXYZ bb = elem.get_BoundingBox(null) ?? elem.get_BoundingBox(view);
            if (bb == null) return null;

            XYZ center = (bb.Min + bb.Max) / 2.0;

            // Transform linked element center into host model coordinates
            if (link != null)
                center = link.GetTransform().OfPoint(center);

            // Project to view plane (flatten Z to view origin)
            double elevation = view.Origin.Z;
            return new XYZ(center.X, center.Y, elevation);
        }
    }
}
