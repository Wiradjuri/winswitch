using System;
using System.Collections.ObjectModel;

namespace WinSwitch.Services;

public sealed class DriveInfoItem
{
    public string Root { get; init; } = "";
    public string Label { get; init; } = "";
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public bool IsSystem { get; init; }
    public string DisplayName => $"{Label} ({Root}) — {FreeBytes / (1024*1024*1024)} GB free";
}

public interface IDriveService : IDisposable
{
    ReadOnlyObservableCollection<DriveInfoItem> Drives { get; }
    void Refresh();
}
