using System.Collections.Generic;

namespace WinSwitch.Models;

public class BackupManifest
{
    public string AppName { get; set; } = "WinSwitch";
    public string Version { get; set; } = "1.0.0";
    public string CreatedAt { get; set; } = "";
    public string MachineName { get; set; } = "";
    public List<ManifestFileEntry> Files { get; set; } = new();
    public string? Notes { get; set; }
}
