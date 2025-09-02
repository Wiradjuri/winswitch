using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace WinSwitch.Services;

public class DriveService : IDriveService
{
    private readonly ObservableCollection<DriveInfoItem> _drives = new();
    private readonly System.Timers.Timer _timer; // fully-qualified to avoid WinForms timer

    public ReadOnlyObservableCollection<DriveInfoItem> Drives { get; }

    public DriveService()
    {
        Drives = new ReadOnlyObservableCollection<DriveInfoItem>(_drives);
        _timer = new System.Timers.Timer(2000);
        _timer.Elapsed += (_, _) => Refresh();
        _timer.AutoReset = true;
        _timer.Start();
        Refresh();
    }

    public void Refresh()
    {
        try
        {
            var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            var cur = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Where(d =>
                    d.DriveType == DriveType.Removable ||
                    (d.DriveType == DriveType.Fixed && d.RootDirectory.FullName != systemRoot))
                .Select(d => new DriveInfoItem
                {
                    Root = d.RootDirectory.FullName,
                    Label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "External" : d.VolumeLabel,
                    TotalBytes = d.TotalSize,
                    FreeBytes = d.TotalFreeSpace,
                    IsSystem = d.RootDirectory.FullName == systemRoot
                })
                .ToList();

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                for (int i = _drives.Count - 1; i >= 0; i--)
                {
                    if (!cur.Any(c => c.Root == _drives[i].Root))
                        _drives.RemoveAt(i);
                }

                foreach (var c in cur)
                {
                    var existing = _drives.FirstOrDefault(x => x.Root == c.Root);
                    if (existing == null) _drives.Add(c);
                    else
                    {
                        var idx = _drives.IndexOf(existing);
                        _drives[idx] = c;
                    }
                }
            });
        }
        catch
        {
            // ignore transient polling errors
        }
    }

    public void Dispose() => _timer.Dispose();
}
