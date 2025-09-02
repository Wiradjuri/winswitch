namespace WinSwitch.Models;

public class ManifestFileEntry
{
    public string RelativePath { get; set; } = "";
    public long SizeBytes { get; set; }
    public long LastWriteUtcTicks { get; set; }
    public string Sha256 { get; set; } = "";
}
