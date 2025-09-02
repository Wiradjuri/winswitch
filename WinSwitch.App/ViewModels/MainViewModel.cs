using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using WF = System.Windows.Forms; // WinForms alias
using WinSwitch.Models;
using WinSwitch.Services;
using WinSwitch.Utilities;

namespace WinSwitch.ViewModels;

public class MainViewModel : ObservableObject
{
    public ICommand ClearLogCommand { get; }
    private readonly IDriveService _driveService;
    private readonly IBackupService _backupService;

    public ObservableCollection<SourceItem> Sources { get; } = new()
    {
        new SourceItem { Name = "Desktop",   Path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), IsSelected = true },
        new SourceItem { Name = "Documents", Path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),      IsSelected = true },
        new SourceItem { Name = "Pictures",  Path = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),       IsSelected = true },
        new SourceItem { Name = "Videos",    Path = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),         IsSelected = false },
        new SourceItem { Name = "Music",     Path = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),          IsSelected = false },
        new SourceItem { Name = "Downloads", Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), IsSelected = false },
    };

    public ObservableCollection<SourceItem> CustomSources { get; } = new();

    public ObservableCollection<DriveInfoItem> Destinations { get; } = new();

    private DriveInfoItem? _selectedDestination;
    public DriveInfoItem? SelectedDestination
    {
        get => _selectedDestination;
        set
        {
            if (SetProperty(ref _selectedDestination, value))
                UpdateCanStart();
        }
    }

    private bool _isBackingUp;
    public bool IsBackingUp
    {
        get => _isBackingUp;
        set
        {
            if (SetProperty(ref _isBackingUp, value))
                UpdateCanStart();
        }
    }

    private int _percent;
    public int Percent { get => _percent; set => SetProperty(ref _percent, value); }

    private string _statusText = "Idle";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    private string _logText = "";
    public string LogText {
        get => _logText;
        set {
            if (SetProperty(ref _logText, value))
                OnLogTextChanged();
        }
    }
    private void OnLogTextChanged() {
        var app = System.Windows.Application.Current;
        if (app?.MainWindow is System.Windows.Window win) {
            var logBox = win.FindName("LogTextBox") as System.Windows.Controls.TextBox;
            logBox?.ScrollToEnd();
        }
    }

    // Button binding depends on this property — we must raise PropertyChanged when its inputs change
    public bool CanStartBackup => (!IsBackingUp && SelectedDestination != null) &&
                                  (Sources.Any(s => s.IsSelected) || CustomSources.Any(s => s.IsSelected));

    public ICommand RefreshDestinationsCommand { get; }
    public ICommand AddCustomSourceCommand { get; }
    public ICommand RemoveCustomSourceCommand { get; }
    public ICommand BackupCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RestoreCommand { get; }

    private CancellationTokenSource? _cts;

    public MainViewModel()
        : this(
            ((App)System.Windows.Application.Current).Services.GetService(typeof(IDriveService)) as IDriveService ?? new DriveService(),
            ((App)System.Windows.Application.Current).Services.GetService(typeof(IBackupService)) as IBackupService ?? new BackupService(new ManifestService()))
    {
    }

    public MainViewModel(IDriveService drives, IBackupService backupService)
    {
        _driveService = drives;
        _backupService = backupService;

        // keep Destinations in sync
        foreach (var d in drives.Drives) Destinations.Add(d);
        drives.Refresh();

        // react to source selection changes
        foreach (var s in Sources) s.PropertyChanged += Source_PropertyChanged;
        CustomSources.CollectionChanged += CustomSources_CollectionChanged;

    ClearLogCommand = new RelayCommand(() => LogText = "");
    RefreshDestinationsCommand = new RelayCommand(() =>
        {
            Destinations.Clear();
            foreach (var d in _driveService.Drives) Destinations.Add(d);
            if (SelectedDestination == null) SelectedDestination = Destinations.FirstOrDefault();
            UpdateCanStart();
        });

        AddCustomSourceCommand = new RelayCommand(() =>
        {
            using var dlg = new WF.FolderBrowserDialog
            {
                Description = "Select a folder to include in the backup",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };

            if (dlg.ShowDialog() == WF.DialogResult.OK && Directory.Exists(dlg.SelectedPath))
            {
                var picked = Path.GetFullPath(dlg.SelectedPath);

                if (CustomSources.Any(s =>
                        Path.GetFullPath(s.Path).Equals(picked, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                var item = new SourceItem
                {
                    Name = new DirectoryInfo(picked).Name,
                    Path = picked,
                    IsSelected = true
                };
                item.PropertyChanged += Source_PropertyChanged;

                CustomSources.Add(item);
                UpdateCanStart();
            }
        });

        RemoveCustomSourceCommand = new RelayCommand(obj =>
        {
            if (obj is SourceItem si)
            {
                si.PropertyChanged -= Source_PropertyChanged;
                CustomSources.Remove(si);
                UpdateCanStart();
            }
        });

        BackupCommand = new AsyncRelayCommand(DoBackupAsync, () => CanStartBackup);
        CancelCommand = new RelayCommand(() => _cts?.Cancel());
        RestoreCommand = new AsyncRelayCommand(DoRestoreAsync);

        SelectedDestination = Destinations.FirstOrDefault();
        UpdateCanStart();
    }

    private void Source_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SourceItem.IsSelected))
            UpdateCanStart();
    }

    private void CustomSources_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Subscribe/unsubscribe to item PropertyChanged so IsSelected changes are observed
        if (e.OldItems != null)
        {
            foreach (var it in e.OldItems.OfType<SourceItem>())
                it.PropertyChanged -= Source_PropertyChanged;
        }
        if (e.NewItems != null)
        {
            foreach (var it in e.NewItems.OfType<SourceItem>())
                it.PropertyChanged += Source_PropertyChanged;
        }
        UpdateCanStart();
    }

    private void UpdateCanStart()
    {
        // Notify WPF that CanStartBackup may have changed
        RaisePropertyChanged(nameof(CanStartBackup));
        // Enable/disable command
        (BackupCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task DoBackupAsync()
    {
        if (SelectedDestination == null) return;

        var sources = Sources.Where(s => s.IsSelected).Select(s => s.Path).ToList();
        sources.AddRange(CustomSources.Where(s => s.IsSelected).Select(s => s.Path));

        sources = sources
            .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
            .Select(p => Path.GetFullPath(p.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sources.Count == 0) return;

        IsBackingUp = true;
        Percent = 0;
        LogText = "";
        StatusText = "Preparing backup…";
        _cts = new CancellationTokenSource();

        var plan = new BackupPlan
        {
            SourcePaths = sources,
            DestinationRoot = SelectedDestination.Root,
            HumanTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        long total = 1;
        var progress = new Progress<BackupProgress>(p =>
        {
            total = Math.Max(1, p.TotalBytes);
            var percent = (int)((p.BytesCopied * 100L) / total);
            Percent = percent;
            StatusText = $"{Percent}% — {p.CurrentFile}";
            if (!string.IsNullOrWhiteSpace(p.CurrentFile))
            {
                LogText += $"[{Percent}%] {p.CurrentFile}\n";
            }
        });

        var (ok, setPath, message) = await _backupService.RunBackupAsync(plan, progress, _cts.Token);

        LogText += message + Environment.NewLine;
        StatusText = ok ? $"Done — {setPath}" : "Stopped";
        IsBackingUp = false;
        _cts = null;
    }

    private async Task DoRestoreAsync()
    {
        using var pickSet = new WF.FolderBrowserDialog
        {
            Description = "Select a WinSwitch backup set folder (contains manifest.json)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (pickSet.ShowDialog() != WF.DialogResult.OK) return;
        if (!File.Exists(Path.Combine(pickSet.SelectedPath, "manifest.json")))
        {
            LogText += "Selected folder does not contain a manifest.json." + Environment.NewLine;
            return;
        }

        using var pickTarget = new WF.FolderBrowserDialog
        {
            Description = "Select a folder to restore into",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (pickTarget.ShowDialog() != WF.DialogResult.OK) return;

        var log = new Progress<string>(s => LogText += s + Environment.NewLine);
        var cts = new CancellationTokenSource();
        StatusText = "Restoring…";
        IsBackingUp = true;
    var ok = await _backupService.RestoreAsync(pickSet.SelectedPath, pickTarget.SelectedPath, log, cts.Token);
        StatusText = ok ? "Restore complete." : "Restore failed.";
        IsBackingUp = false;
    }
}
