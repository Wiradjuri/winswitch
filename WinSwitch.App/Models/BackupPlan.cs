using System.Collections.Generic;

namespace WinSwitch.Models;

public class BackupPlan
{
    public required List<string> SourcePaths { get; init; }
    public required string DestinationRoot { get; init; } // e.g. "E:\WinSwitch\Backups\[timestamp]"
    public required string HumanTimestamp { get; init; }  // e.g. "2025-09-02 09:15:22"
}
