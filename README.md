# RevitToolkit Plugin

Professional annotation tools for architecture and landscape projects.  
Designed for **sheet-file workflows** where a separate file links the main Revit model.

---

## Features

| Ribbon button | What it does |
|---|---|
| **Copy Dimensions** | Replicate dimension strings from a source view into one or more target views |
| **Auto Keynote Tag** | Place keynote tags on every un-tagged element in the active view |
| **Tag Health** | Scan the active view for orphaned / duplicate tags and fix them |
| **Batch Tag Views** | Run Auto Keynote Tag across multiple views in one pass |

---

## Project Structure

```
RevitToolkit/
├── App.cs                              # Ribbon registration
├── RevitToolkit.csproj
├── RevitToolkit.addin
├── Commands/
│   ├── CopyDimensionsCommand.cs
│   ├── AutoKeynoteTagCommand.cs
│   ├── BatchTagCommand.cs
│   ├── CleanupCommand.cs
│   └── PluginSettings.cs              # Persistent user preferences
└── UI/
    ├── CopyDimensionsDialog.xaml/.cs
    ├── AutoKeynoteTagDialog.xaml/.cs
    ├── BatchTagDialog.xaml/.cs
    └── CleanupDialog.xaml/.cs
```

---

## Requirements

| Requirement | Version |
|---|---|
| Autodesk Revit | 2022 – 2025 |
| .NET Framework | 4.8 |
| Visual Studio | 2022 (recommended) |
| Windows | 10 / 11 x64 |

> **Toposolid** category requires Revit 2024+. It is hidden automatically on older versions.

---

## Build & Install

### Option A — Build from source (Visual Studio)

1. Open `RevitToolkit.csproj`
2. Adjust the `HintPath` in the csproj if your Revit is not installed at the default path:
   ```xml
   <HintPath>C:\Program Files\Autodesk\Revit 2024\RevitAPI.dll</HintPath>
   ```
3. **Ctrl+Shift+B** — the post-build event copies the DLL and `.addin` to:
   ```
   %AppData%\Autodesk\Revit\Addins\2024\
   ```
4. Launch Revit → **Toolkit** tab appears in the ribbon.

### Option B — No-admin install (distribute to team)

Build in Release mode, then distribute the two files:
- `RevitToolkit.dll`
- `RevitToolkit.addin`

Recipients double-click **`Install.bat`** (included in the repo). No administrator rights required — Revit loads addins from the user-profile path:
```
%AppData%\Autodesk\Revit\Addins\<version>\
```

For Revit versions other than 2022–2025:
```powershell
.\Install.ps1 -Versions 2026
```

---

## Feature 1: Copy Dimensions

Replicates all `Dimension` elements from a source view into one or more target views.

**Supports linked model references** — dimensions that reference faces or edges in a linked model are correctly remapped using their stable reference strings, so they are no longer silently skipped.

### What gets copied
- Single-segment and multi-segment (ordinate/chain) dimensions
- Text overrides: Above / Below / Prefix / Suffix / Value Override per segment
- Dimension type/style (preserved via `CopyElements`)

### Skipped dimensions
A dimension is skipped when any of its references are not visible in the target view. A summary is shown after the operation.

---

## Feature 2: Auto Keynote Tag

Places keynote tags on visible, un-tagged elements in the active view.

### Categories
A scrollable grouped checkbox list with quick-select presets:

| Preset | Categories included |
|---|---|
| **Arch** | Walls, Floors, Ceilings, Roofs, Doors, Windows, Columns ×2, Beams, Stairs, Railings, Ramps, Casework |
| **Landscape** | Planting, Generic Models, Site, Toposolid, Furniture, Specialty Equipment |

Other available categories: Detail Items, Furniture Systems, Mechanical, Plumbing, Electrical, Lighting.

### Options
| Option | Default | Description |
|---|---|---|
| Include leader line | Off | Adds a leader from tag head to element |
| One per family type | **On** | Tags only one instance of each type; re-running is safe — already-tagged types are skipped |
| Avoid overlaps | **On** | After placing, iteratively nudges overlapping tags apart (up to 8 passes, one undo step) |

### Linked model support
Select a linked model as the source. Tags are placed in the host document referencing linked elements via `Reference.CreateLinkReference`.

### Settings persistence
All options (categories, tag family, orientation, checkboxes) are remembered between sessions in `%AppData%\RevitToolkit\settings.dat`.

---

## Feature 3: Tag Health

Scans every keynote tag in the active view and reports three issue types.

| Issue | Description | Action |
|---|---|---|
| **Orphaned** | Tag whose referenced element no longer exists in any loaded model | Delete All |
| **Duplicates** | Multiple tags pointing to elements of the same family type | Delete Extras (keeps the first-placed tag per type) |
| **Blank keynote** | Tag that will render empty because the Keynote parameter on the element type is not set | Fix in Type Properties → Identity Data → Keynote |

The dialog re-scans automatically after each delete action. All deletions are wrapped in named transactions with full undo support.

---

## Feature 4: Batch Tag Views

Runs Auto Keynote Tag across multiple views in one operation.

### Quality controls
- **View type filter** — show only Floor Plans, Sections, Elevations, etc.
- **Threshold guard** — skip views with fewer than N untagged elements (catches empty construction views)

### Execution
Each view runs in its own named transaction, so individual views can be undone independently. A per-view result summary is shown on completion:
```
Batch complete: 171 tags placed across 3 view(s)

✓  L1 Floor Plan   — 72 tagged, 3 skipped
✓  L2 Floor Plan   — 58 tagged, 1 skipped
✓  Site Plan       — 41 tagged, 0 skipped
```

---

## Troubleshooting

| Issue | Solution |
|---|---|
| "No Keynote Tag families found" | Load a tag family via Insert → Load Family → Annotations → Tags |
| Dimensions not copied to a view | Referenced grids/walls/elements must be visible in the target view |
| Tags placed at wrong location | Re-link the model if it was moved (link instance transform may be stale) |
| Build error — RevitAPI.dll not found | Update `HintPath` in `.csproj` to match your Revit install path |
| Plugin not visible after install | Confirm `.addin` and `.dll` are both in `%AppData%\Autodesk\Revit\Addins\<version>\` |

---

## License
MIT — free to use, modify, and distribute. Attribution appreciated.
