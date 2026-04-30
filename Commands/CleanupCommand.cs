using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitToolkit.UI;
using System.Linq;

namespace RevitToolkit.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CleanupCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc   = commandData.Application.ActiveUIDocument;
            Document doc       = uidoc.Document;
            View activeView    = doc.ActiveView;

            if (activeView is View3D || activeView is ViewSchedule || activeView is ViewSheet)
            {
                TaskDialog.Show("Tag Health",
                    "Open a floor plan, section, elevation, or detail view first.");
                return Result.Cancelled;
            }

            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Where(l => l.GetLinkDocument() != null)
                .ToList();

            new CleanupDialog(doc, activeView, links).ShowDialog();
            return Result.Succeeded;
        }
    }
}
