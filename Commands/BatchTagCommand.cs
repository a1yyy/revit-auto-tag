using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitToolkit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RevitToolkit.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchTagCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc     = uidoc.Document;

            try
            {
                var links = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .Where(l => l.GetLinkDocument() != null)
                    .ToList();

                var dialog = new BatchTagDialog(doc, links);
                if (dialog.ShowDialog() != true)
                    return Result.Cancelled;

                Document targetDoc                  = dialog.TargetDoc;
                RevitLinkInstance link              = dialog.SelectedLink;
                List<BuiltInCategory> categories    = dialog.SelectedCategories;
                FamilySymbol tagType                = dialog.SelectedTagType;
                bool leaderEnabled                  = dialog.LeaderEnabled;
                bool oneTagPerType                  = dialog.OneTagPerType;
                bool avoidOverlaps                  = dialog.AvoidOverlaps;
                TagOrientation orientation          = dialog.SelectedOrientation;
                List<View> views                    = dialog.SelectedViews;
                int minThreshold                    = dialog.MinElementThreshold;

                if (!tagType.IsActive)
                {
                    using (var tx = new Transaction(doc, "Activate Tag Symbol"))
                    {
                        tx.Start();
                        tagType.Activate();
                        tx.Commit();
                    }
                }

                var report = new StringBuilder();
                int totalPlaced  = 0;
                int totalSkipped = 0;

                foreach (View view in views)
                {
                    var candidates = categories
                        .SelectMany(cat => CollectTaggableElements(doc, targetDoc, link, view, cat))
                        .GroupBy(e => e.Id)
                        .Select(g => g.First())
                        .ToList();

                    var existingTaggedIds = GetAlreadyTaggedElementIds(doc, view);

                    List<Element> toTag;
                    if (oneTagPerType)
                    {
                        toTag = candidates
                            .GroupBy(e => e.GetTypeId())
                            .Where(g => !g.Any(e => existingTaggedIds.Contains(e.Id)))
                            .Select(g => g.First())
                            .ToList();
                    }
                    else
                    {
                        toTag = candidates.Where(e => !existingTaggedIds.Contains(e.Id)).ToList();
                    }

                    if (toTag.Count < minThreshold)
                    {
                        report.AppendLine($"⏭  {view.Name}  — skipped ({toTag.Count} < threshold {minThreshold})");
                        continue;
                    }

                    int placed  = 0;
                    int skipped = 0;
                    var placedTagIds = new List<ElementId>();

                    using (var tx = new Transaction(doc, $"Auto Tag: {view.Name}"))
                    {
                        tx.Start();
                        foreach (Element elem in toTag)
                        {
                            try
                            {
                                XYZ tagPoint = ComputeTagInsertionPoint(elem, link, view);
                                if (tagPoint == null) { skipped++; continue; }

                                Reference elemRef = link != null
                                    ? new Reference(elem).CreateLinkReference(link)
                                    : new Reference(elem);

                                IndependentTag newTag = IndependentTag.Create(
                                    doc, tagType.Id, view.Id, elemRef,
                                    leaderEnabled, orientation, tagPoint);

                                placedTagIds.Add(newTag.Id);
                                placed++;
                            }
                            catch { skipped++; }
                        }
                        tx.Commit();
                    }

                    if (avoidOverlaps && placedTagIds.Count > 1)
                        ResolveTagOverlaps(doc, view, placedTagIds);

                    totalPlaced  += placed;
                    totalSkipped += skipped;
                    report.AppendLine($"✓  {view.Name}  — {placed} tagged, {skipped} skipped");
                }

                string summary =
                    $"Batch complete: {totalPlaced} tags placed across {views.Count} view(s)\n\n"
                    + report.ToString();
                TaskDialog.Show("Batch Auto Tag — Results", summary);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Helpers (mirrors AutoKeynoteTagCommand) ──────────────────────────────────

        private IEnumerable<Element> CollectTaggableElements(
            Document hostDoc, Document targetDoc, RevitLinkInstance link,
            View view, BuiltInCategory category)
        {
            if (link == null)
                return new FilteredElementCollector(hostDoc, view.Id)
                    .OfCategory(category).WhereElementIsNotElementType().ToElements();

            BoundingBoxXYZ viewBB  = view.CropBox;
            Outline viewOutline    = new Outline(
                link.GetTransform().Inverse.OfPoint(viewBB.Min),
                link.GetTransform().Inverse.OfPoint(viewBB.Max));

            return new FilteredElementCollector(targetDoc)
                .OfCategory(category).WhereElementIsNotElementType()
                .WherePasses(new BoundingBoxIntersectsFilter(viewOutline))
                .ToElements();
        }

        private HashSet<ElementId> GetAlreadyTaggedElementIds(Document doc, View view)
        {
            var ids = new HashSet<ElementId>();
            foreach (var tag in new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
            {
                try { foreach (var id in tag.GetTaggedLocalElementIds()) ids.Add(id); }
                catch { }
            }
            return ids;
        }

        private XYZ ComputeTagInsertionPoint(Element elem, RevitLinkInstance link, View view)
        {
            BoundingBoxXYZ bb = elem.get_BoundingBox(null) ?? elem.get_BoundingBox(view);
            if (bb == null) return null;
            XYZ center = (bb.Min + bb.Max) / 2.0;
            if (link != null) center = link.GetTransform().OfPoint(center);
            return new XYZ(center.X, center.Y, view.Origin.Z);
        }

        private void ResolveTagOverlaps(Document doc, View view, List<ElementId> tagIds)
        {
            const int MaxIter   = 8;
            const double Margin = 0.02;

            var tags = tagIds.Select(id => doc.GetElement(id) as IndependentTag)
                             .Where(t => t != null).ToList();

            var halfW = new double[tags.Count];
            var halfH = new double[tags.Count];
            for (int i = 0; i < tags.Count; i++)
            {
                var bb = tags[i].get_BoundingBox(view);
                if (bb != null)
                {
                    halfW[i] = (bb.Max.X - bb.Min.X) / 2.0;
                    halfH[i] = (bb.Max.Y - bb.Min.Y) / 2.0;
                }
            }

            var pos = tags.Select(t => t.TagHeadPosition).ToArray();

            for (int iter = 0; iter < MaxIter; iter++)
            {
                bool anyOverlap = false;
                var delta = Enumerable.Repeat(XYZ.Zero, tags.Count).ToArray();

                for (int i = 0; i < tags.Count; i++)
                for (int j = i + 1; j < tags.Count; j++)
                {
                    double gapX = Math.Abs(pos[i].X - pos[j].X) - (halfW[i] + halfW[j] + Margin);
                    double gapY = Math.Abs(pos[i].Y - pos[j].Y) - (halfH[i] + halfH[j] + Margin);
                    if (gapX >= 0 || gapY >= 0) continue;

                    anyOverlap = true;
                    XYZ dir = new XYZ(pos[i].X - pos[j].X, pos[i].Y - pos[j].Y, 0);
                    if (dir.IsZeroLength()) dir = XYZ.BasisX;
                    dir = dir.Normalize();
                    double push = (Math.Min(-gapX, -gapY) / 2.0) + 0.001;
                    delta[i] = delta[i].Add(dir.Multiply(push));
                    delta[j] = delta[j].Add(dir.Negate().Multiply(push));
                }

                if (!anyOverlap) break;
                for (int i = 0; i < pos.Length; i++)
                    pos[i] = pos[i].Add(delta[i]);
            }

            using (var tx = new Transaction(doc, "Resolve Tag Overlaps"))
            {
                tx.Start();
                for (int i = 0; i < tags.Count; i++)
                    try { tags[i].TagHeadPosition = pos[i]; } catch { }
                tx.Commit();
            }
        }
    }
}
