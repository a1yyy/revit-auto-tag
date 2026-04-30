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
    public class CopyDimensionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // Show dialog to pick source view and target views
                var dialog = new CopyDimensionsDialog(doc);
                if (dialog.ShowDialog() != true)
                    return Result.Cancelled;

                View sourceView = dialog.SelectedSourceView;
                List<View> targetViews = dialog.SelectedTargetViews;

                if (sourceView == null || targetViews == null || targetViews.Count == 0)
                {
                    TaskDialog.Show("Copy Dimensions", "Please select a source view and at least one target view.");
                    return Result.Cancelled;
                }

                // Collect all dimensions from source view
                var sourceDimensions = new FilteredElementCollector(doc, sourceView.Id)
                    .OfClass(typeof(Dimension))
                    .Cast<Dimension>()
                    .ToList();

                if (sourceDimensions.Count == 0)
                {
                    TaskDialog.Show("Copy Dimensions", $"No dimensions found in view: '{sourceView.Name}'.");
                    return Result.Cancelled;
                }

                int successCount = 0;
                int failCount = 0;
                var failedViews = new List<string>();

                using (Transaction tx = new Transaction(doc, "Copy Dimensions to Views"))
                {
                    tx.Start();

                    foreach (View targetView in targetViews)
                    {
                        if (targetView.Id == sourceView.Id) continue;

                        try
                        {
                            CopyDimensionsToView(doc, sourceDimensions, sourceView, targetView);
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            failedViews.Add($"{targetView.Name}: {ex.Message}");
                        }
                    }

                    tx.Commit();
                }

                // Report results
                string report = $"✅ Dimensions copied successfully to {successCount} view(s).\n" +
                                $"   Source: {sourceView.Name}\n" +
                                $"   Dimensions copied: {sourceDimensions.Count}";

                if (failCount > 0)
                    report += $"\n\n⚠️ Failed for {failCount} view(s):\n" + string.Join("\n", failedViews);

                TaskDialog.Show("Copy Dimensions — Results", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private void CopyDimensionsToView(Document doc, List<Dimension> sourceDimensions, View sourceView, View targetView)
        {
            // Build a set of host-model element IDs visible in the target view
            var targetVisibleIds = new FilteredElementCollector(doc, targetView.Id)
                .WhereElementIsNotElementType()
                .ToElementIds()
                .ToHashSet();

            var idsToExclude = new HashSet<ElementId>();

            foreach (Dimension dim in sourceDimensions)
            {
                try
                {
                    if (!ValidateDimensionRefs(doc, dim, targetVisibleIds))
                    {
                        idsToExclude.Add(dim.Id);
                        continue;
                    }

                    ICollection<ElementId> copiedIds = ElementTransformUtils.CopyElements(
                        sourceView,
                        new List<ElementId> { dim.Id },
                        targetView,
                        Transform.Identity,
                        new CopyPasteOptions());

                    RestoreTextOverrides(doc, dim, copiedIds);
                }
                catch
                {
                    idsToExclude.Add(dim.Id);
                }
            }
        }

        private bool ValidateDimensionRefs(Document doc, Dimension dim, HashSet<ElementId> targetIds)
        {
            if (dim.References == null) return false;

            foreach (Reference r in dim.References)
            {
                if (r.ElementId == ElementId.InvalidElementId) continue;

                // Linked model reference: the ElementId points to a RevitLinkInstance.
                // We only require the link to be loaded — the sub-element reference
                // is encoded in the stable reference string and survives CopyElements.
                if (doc.GetElement(r.ElementId) is RevitLinkInstance linkInst)
                {
                    if (linkInst.GetLinkDocument() == null) return false;
                    continue;
                }

                // Host model reference: element must be visible in the target view.
                if (!targetIds.Contains(r.ElementId)) return false;
            }
            return true;
        }

        private void RestoreTextOverrides(Document doc, Dimension sourceDim, ICollection<ElementId> copiedIds)
        {
            if (copiedIds == null || copiedIds.Count == 0) return;

            Dimension copiedDim = doc.GetElement(copiedIds.First()) as Dimension;
            if (copiedDim == null) return;

            // Copy segment-level text overrides if multi-segment dimension
            if (sourceDim.NumberOfSegments > 1 && copiedDim.NumberOfSegments == sourceDim.NumberOfSegments)
            {
                for (int i = 0; i < sourceDim.NumberOfSegments; i++)
                {
                    DimensionSegment srcSeg = sourceDim.Segments.get_Item(i);
                    DimensionSegment dstSeg = copiedDim.Segments.get_Item(i);

                    if (!string.IsNullOrEmpty(srcSeg.Above))   dstSeg.Above   = srcSeg.Above;
                    if (!string.IsNullOrEmpty(srcSeg.Below))   dstSeg.Below   = srcSeg.Below;
                    if (!string.IsNullOrEmpty(srcSeg.Prefix))  dstSeg.Prefix  = srcSeg.Prefix;
                    if (!string.IsNullOrEmpty(srcSeg.Suffix))  dstSeg.Suffix  = srcSeg.Suffix;
                    if (!string.IsNullOrEmpty(srcSeg.ValueOverride)) dstSeg.ValueOverride = srcSeg.ValueOverride;
                }
            }
            else
            {
                // Single segment overrides
                if (!string.IsNullOrEmpty(sourceDim.Above))   copiedDim.Above   = sourceDim.Above;
                if (!string.IsNullOrEmpty(sourceDim.Below))   copiedDim.Below   = sourceDim.Below;
                if (!string.IsNullOrEmpty(sourceDim.Prefix))  copiedDim.Prefix  = sourceDim.Prefix;
                if (!string.IsNullOrEmpty(sourceDim.Suffix))  copiedDim.Suffix  = sourceDim.Suffix;
            }
        }
    }
}
