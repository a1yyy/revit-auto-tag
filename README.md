# RevitToolkit Plugin

A professional Revit plugin with two powerful workflow tools:

1. **Copy Dimensions Between Views** — replicate all dimension strings from a source view into one or more target views
2. **Auto Keynote Tag** — automatically place keynote tags on every un-tagged element in the active view, supporting both host and linked models

---

## Project Structure

```
RevitToolkit/
├── App.cs                              # IExternalApplication — ribbon registration
├── RevitToolkit.csproj                 # Project file (.NET 4.8 / WPF)
├── RevitToolkit.addin                  # Revit manifest
├── Commands/
│   ├── CopyDimensionsCommand.cs        # Feature 1 logic
│   └── AutoKeynoteTagCommand.cs        # Feature 2 logic
└── UI/
    ├── CopyDimensionsDialog.xaml       # WPF dialog — source/target view picker
    ├── CopyDimensionsDialog.xaml.cs
    ├── AutoKeynoteTagDialog.xaml       # WPF dialog — tag configuration
    └── AutoKeynoteTagDialog.xaml.cs
```

---

## Requirements

| Requirement | Version |
|---|---|
| Autodesk Revit | 2022 – 2025 |
| .NET Framework | 4.8 |
| Visual Studio | 2022 (recommended) |
| Windows | 10 / 11 x64 |

---

## Build & Install

### 1. Set Revit API references
Open `RevitToolkit.csproj` and verify the `HintPath` values point to your Revit installation:

```xml
<HintPath>C:\Program Files\Autodesk\Revit 2024\RevitAPI.dll</HintPath>
<HintPath>C:\Program Files\Autodesk\Revit 2024\RevitAPIUI.dll</HintPath>
```

Change `2024` to your installed version (2022 / 2023 / 2025).

### 2. Build
```
dotnet build -c Release
```
or build via Visual Studio (**Ctrl+Shift+B**).

The post-build event automatically copies `RevitToolkit.dll` and `RevitToolkit.addin` to:
```
%AppData%\Autodesk\Revit\Addins\2024\
```

### 3. Launch Revit
Start Revit — you will see a new **Toolkit** tab in the ribbon with two buttons.

---

## Feature 1: Copy Dimensions

### How to Use
1. Open a project with dimensions in at least one view
2. Click **Toolkit → Copy Dimensions**
3. In the dialog:
   - **Source View** — pick the view whose dimensions you want to copy
   - **Target Views** — multi-select one or more destination views
4. Click **Copy Dimensions →**

### What Gets Copied
- All `Dimension` elements in the source view
- Single-segment and multi-segment (ordinate/chain) dimensions
- Text overrides: **Above / Below / Prefix / Suffix / Value Override** per segment
- Dimension type (style) is preserved automatically via `CopyElements`

### Skipped Dimensions
A dimension is silently skipped when any of its **references** (faces, edges, grids, levels) are missing or not visible in the target view. A summary is shown after the operation.

### Technical Notes
- Uses `ElementTransformUtils.CopyElements` with `Transform.Identity` — no position shift
- Wrapped in a single `Transaction` for full undo support
- Compatible with: floor plans, ceiling plans, sections, elevations, detail views, drafting views

---

## Feature 2: Auto Keynote Tag

### Prerequisites
- At least one **Keynote Tag** family must be loaded in the project  
  (`Insert → Load Family → Annotations → Tags → Keynote Tag`)
- Elements must have a **Keynote** parameter value assigned  
  (via the element's Type Properties → Identity Data → Keynote)

### How to Use
1. Open the view where you want tags placed (plan, section, elevation, detail)
2. Click **Toolkit → Auto Keynote Tag**
3. In the dialog:
   - **Model Source** — choose the current model or a linked model
   - **Element Category** — pick the category to tag (Walls, Doors, Windows, etc.)
   - **Keynote Tag Type** — choose the tag family/type
   - **Tag Orientation** — Horizontal or Vertical
   - **Include leader line** — toggle leader on/off
4. Click **Place Tags →**

### Supported Categories
Walls, Floors, Ceilings, Roofs, Doors, Windows, Columns (Arch & Struct),  
Structural Framing, Furniture, Furniture Systems, Casework, Mechanical Equipment,  
Plumbing Fixtures, Electrical Fixtures, Lighting Fixtures, Specialty Equipment,  
Generic Models, Site, Stairs, Railings, Ramps

### Smart Skipping
- Elements **already tagged** in the active view → skipped
- Elements with **no bounding box** (e.g. detail lines) → skipped
- **Linked model elements** are tagged via `Reference.CreateLinkReference`
- Elements filtered to those **visible within the view's crop region**

### Tag Placement
Tags are inserted at the **bounding box centre** of each element, projected onto the view plane. For linked models, the transform of the link instance is applied to compute host-space coordinates.

---

## Extending the Plugin

### Adding More Categories
In `AutoKeynoteTagDialog.xaml.cs`, add entries to the `TaggableCategories` array:
```csharp
("Curtain Panels", BuiltInCategory.OST_CurtainWallPanels),
```

### Supporting Revit 2022 API
`tag.GetTaggedLocalElementIds()` was introduced in Revit 2022. For Revit 2019–2021, the fallback
`tag.TaggedLocalElementId` is already in the code.

### Packaging for Distribution
1. Build in `Release` mode
2. Distribute `RevitToolkit.dll` + `RevitToolkit.addin`
3. Installer target: `%AppData%\Autodesk\Revit\Addins\<version>\`

---

## Troubleshooting

| Issue | Solution |
|---|---|
| "No Keynote Tag families found" | Load a tag family via Insert → Load Family |
| Dimensions not copied to a view | Check that referenced grids/levels/elements exist and are visible in the target |
| Tags placed at wrong location | Ensure the link instance transform is correct (re-link if model was moved) |
| Build error — can't find RevitAPI.dll | Update `HintPath` in `.csproj` to match your Revit install path |

---

## License
MIT — free to use, modify, and distribute. Attribution appreciated.
