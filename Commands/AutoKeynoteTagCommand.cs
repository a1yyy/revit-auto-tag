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
                bool avoidOverlaps                  = dialog.AvoidOverlaps;
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
                var placedTagIds = new List<ElementId>();

                using (Transaction tx = new Transaction(doc, "Auto Place Keynote Tags"))
                {
                    tx.Start();

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

                            IndependentTag newTag = IndependentTag.Create(
                                doc,
                                tagType.Id,
                                activeView.Id,
                                elemRef,
                                leaderEnabled,
                                orientation,
                                tagPoint);

                            placedTagIds.Add(newTag.Id);
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

                if (avoidOverlaps && placedTagIds.Count > 1)
                    ResolveTagOverlaps(doc, activeView, placedTagIds);

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

        // ─── Overlap resolution ──────────────────────────────────────────────────────

        // Iteratively nudges tags apart using their initial bounding-box sizes.
        // All final positions are written in one transaction (one undo step).
        private void ResolveTagOverlaps(Document doc, View view, List<ElementId> tagIds)
        {
            const int MaxIterations = 8;
            const double Margin = 0.02; // ~6 mm padding between tags (internal feet units)

            var tags = tagIds
                .Select(id => doc.GetElement(id) as IndependentTag)
                .Where(t => t != null)
                .ToList();

            // Snapshot initial bounding-box half-extents (fixed — tags don't resize when moved)
            var halfW = new double[tags.Count];
            var halfH = new double[tags.Count];
            for (int i = 0; i < tags.Count; i++)
            {
                BoundingBoxXYZ bb = tags[i].get_BoundingBox(view);
                if (bb != null)
                {
                    halfW[i] = (bb.Max.X - bb.Min.X) / 2.0;
                    halfH[i] = (bb.Max.Y - bb.Min.Y) / 2.0;
                }
            }

            // Work on positions in memory
            var pos = tags.Select(t => t.TagHeadPosition).ToArray();

            for (int iter = 0; iter < MaxIterations; iter++)
            {
                bool anyOverlap = false;
                var delta = new XYZ[tags.Count];
                for (int k = 0; k < delta.Length; k++) delta[k] = XYZ.Zero;

                for (int i = 0; i < tags.Count; i++)
                {
                    for (int j = i + 1; j < tags.Count; j++)
                    {
                        double gapX = Math.Abs(pos[i].X - pos[j].X) - (halfW[i] + halfW[j] + Margin);
                        double gapY = Math.Abs(pos[i].Y - pos[j].Y) - (halfH[i] + halfH[j] + Margin);

                        if (gapX >= 0 || gapY >= 0) continue; // no overlap

                        anyOverlap = true;

                        // Push along the axis with the smaller gap (least effort to separate)
                        XYZ dir = new XYZ(pos[i].X - pos[j].X, pos[i].Y - pos[j].Y, 0);
                        if (dir.IsZeroLength()) dir = XYZ.BasisX;
                        dir = dir.Normalize();

                        double push = (Math.Min(-gapX, -gapY) / 2.0) + 0.001;
                        delta[i] = delta[i].Add(dir.Multiply(push));
                        delta[j] = delta[j].Add(dir.Negate().Multiply(push));
                    }
                }

                if (!anyOverlap) break;

                for (int i = 0; i < pos.Length; i++)
                    pos[i] = pos[i].Add(delta[i]);
            }

            // Write all resolved positions in one transaction
            using (Transaction tx = new Transaction(doc, "Resolve Tag Overlaps"))
            {
                tx.Start();
                for (int i = 0; i < tags.Count; i++)
                {
                    try { tags[i].TagHeadPosition = pos[i]; } catch { }
                }
                tx.Commit();
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

            // For linked model: collect all elements of the category — no bbox filter.
            // Elements not visible in the view will cause IndependentTag.Create to throw,
            // which is caught in the placement loop and counted as skipped.
            return new FilteredElementCollector(targetDoc)
                .OfCategory(category)
                .WhereElementIsNotElementType()
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
                catch { }
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
