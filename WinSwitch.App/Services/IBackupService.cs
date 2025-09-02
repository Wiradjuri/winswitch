
using System;
using System.Threading;
using System.Threading.Tasks;
using WinSwitch.Models;

namespace WinSwitch.Services;

public record BackupProgress(long BytesCopied, long TotalBytes, string CurrentFile);

public interface IBackupService
{
    Task<(bool ok, string setPath, string message)> RunBackupAsync(
        BackupPlan plan,
        IProgress<BackupProgress> progress,
        CancellationToken ct);

    Task<bool> RestoreAsync(string backupSetPath, string restoreTargetFolder, IProgress<string> log, CancellationToken ct);
}
