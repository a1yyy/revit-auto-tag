using Autodesk.Revit.UI;
using System;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace RevitToolkit
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            string tabName = "Toolkit";
            try { application.CreateRibbonTab(tabName); } catch { }

            RibbonPanel panel = application.CreateRibbonPanel(tabName, "View Tools");

            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // --- Button 1: Copy Dimensions ---
            PushButtonData copyDimBtn = new PushButtonData(
                "CopyDimensions",
                "Copy\nDimensions",
                assemblyPath,
                "RevitToolkit.Commands.CopyDimensionsCommand")
            {
                ToolTip = "Copy dimensions from a source view to one or more target views.",
                LongDescription = "Select a source view containing dimensions, pick the target views, and this tool will replicate all dimension strings — preserving references, offsets, and text overrides.",
            };

            panel.AddItem(copyDimBtn);
            panel.AddSeparator();

            // --- Button 2: Auto Keynote Tag ---
            PushButtonData autoTagBtn = new PushButtonData(
                "AutoKeynoteTag",
                "Auto\nKeynote Tag",
                assemblyPath,
                "RevitToolkit.Commands.AutoKeynoteTagCommand")
            {
                ToolTip = "Automatically tag elements from a selected model with keynote tags.",
                LongDescription = "Choose a linked model (or current model), a category filter, and a keynote tag family. The tool places keynote tags on every visible, un-tagged element in the active view.",
            };

            panel.AddItem(autoTagBtn);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
    }
}
