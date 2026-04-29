using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitToolkit.Commands
{
    /// <summary>
    /// Persists user preferences to %APPDATA%\RevitToolkit\settings.dat.
    /// All operations are silent — if the file is missing or corrupt, defaults are used.
    /// </summary>
    public class PluginSettings
    {
        // ── Defaults ─────────────────────────────────────────────────────────────────
        public bool   LeaderEnabled         { get; set; } = false;
        public bool   OneTagPerType         { get; set; } = true;
        public bool   AvoidOverlaps         { get; set; } = true;
        public bool   OrientationVertical   { get; set; } = false;
        public string LastTagFamilyName     { get; set; } = "";
        public string LastModelTitle        { get; set; } = "";

        // BuiltInCategory integer values of the last-selected categories
        public List<int> LastCategories     { get; set; } = new List<int>();

        // ── File path ─────────────────────────────────────────────────────────────────
        private static readonly string FolderPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "RevitToolkit");

        private static readonly string FilePath =
            Path.Combine(FolderPath, "settings.dat");

        // ── Load ──────────────────────────────────────────────────────────────────────
        public static PluginSettings Load()
        {
            var s = new PluginSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;

                foreach (var raw in File.ReadAllLines(FilePath))
                {
                    var line  = raw.Trim();
                    var split = line.IndexOf('=');
                    if (split < 0) continue;
                    var key = line.Substring(0, split).Trim();
                    var val = line.Substring(split + 1).Trim();

                    switch (key)
                    {
                        case "LeaderEnabled":       s.LeaderEnabled       = val == "True"; break;
                        case "OneTagPerType":        s.OneTagPerType       = val != "False"; break;
                        case "AvoidOverlaps":        s.AvoidOverlaps       = val != "False"; break;
                        case "OrientationVertical":  s.OrientationVertical = val == "True"; break;
                        case "LastTagFamilyName":    s.LastTagFamilyName   = val; break;
                        case "LastModelTitle":       s.LastModelTitle      = val; break;
                        case "LastCategories":
                            s.LastCategories = val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                   .Select(x => int.TryParse(x, out int v) ? v : 0)
                                                   .Where(v => v != 0)
                                                   .ToList();
                            break;
                    }
                }
            }
            catch { /* use defaults */ }
            return s;
        }

        // ── Save ──────────────────────────────────────────────────────────────────────
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(FolderPath);
                File.WriteAllLines(FilePath, new[]
                {
                    $"LeaderEnabled={LeaderEnabled}",
                    $"OneTagPerType={OneTagPerType}",
                    $"AvoidOverlaps={AvoidOverlaps}",
                    $"OrientationVertical={OrientationVertical}",
                    $"LastTagFamilyName={LastTagFamilyName}",
                    $"LastModelTitle={LastModelTitle}",
                    $"LastCategories={string.Join(",", LastCategories)}",
                });
            }
            catch { /* silently ignore write errors */ }
        }
    }
}
