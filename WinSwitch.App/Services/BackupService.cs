using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using WinSwitch.Models;

namespace WinSwitch.Services;

public class BackupService : IBackupService
{
    private readonly IManifestService _manifest;

    public BackupService(IManifestService manifest) => _manifest = manifest;

    public async Task<(bool ok, string setPath, string message)> RunBackupAsync(
        BackupPlan plan,
        IProgress<BackupProgress> progress,
        CancellationToken ct)
    {
        var timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var setPath = Path.Combine(plan.DestinationRoot, "WinSwitch", "Backups", timeStamp);

        try
        {
            Directory.CreateDirectory(setPath);
        }
        catch (Exception ex)
        {
            return (false, setPath, $"Cannot create backup folder '{setPath}': {ex.Message}");
        }

        // Enumerate safely (skip inaccessible folders, avoid reparse loops)
        var fileList = new List<(string src, string dst, string rel, DateTime lastUtc, long size)>();

        foreach (var source in plan.SourcePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(source)) continue;

            var baseTrimmed = source.TrimEnd(Path.DirectorySeparatorChar);
            var baseLen = baseTrimmed.Length;

            IEnumerable<string> files;
            try
            {
                var eo = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };
                files = Directory.EnumerateFiles(source, "*", eo);
            }
            catch
            {
                continue; // root itself inaccessible
            }

            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var fi = new FileInfo(f);
                    var rel = f.Substring(Math.Min(f.Length, baseLen)).TrimStart(Path.DirectorySeparatorChar);
                    var dst = Path.Combine(setPath, "data", SanitizeForPath(source), rel);
                    fileList.Add((f, dst, Path.Combine(SanitizeForPath(source), rel), fi.LastWriteTimeUtc, fi.Length));
                }
                catch
                {
                    // Skip files we can't stat
                }
            }
        }

        long totalBytes = fileList.Sum(x => x.size);
        long copied = 0;

        var manifest = new BackupManifest
        {
            CreatedAt = DateTime.UtcNow.ToString("o"),
            MachineName = Environment.MachineName
        };

        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            foreach (var it in fileList)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(it.dst)!);

                    bool needCopy = true;
                    if (File.Exists(it.dst))
                    {
                        var di = new FileInfo(it.dst);
                        if (di.Length == it.size && di.LastWriteTimeUtc == it.lastUtc)
                            needCopy = false;
                    }

                    if (needCopy)
                    {
                        using var src = File.Open(it.src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var dst = File.Open(it.dst, FileMode.Create, FileAccess.Write, FileShare.None);

                        int read;
                        while ((read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                        {
                            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                            copied += read;
                            progress.Report(new BackupProgress(copied, totalBytes, it.src));
                        }
                        File.SetLastWriteTimeUtc(it.dst, it.lastUtc);
                    }
                    else
                    {
                        progress.Report(new BackupProgress(copied, totalBytes, $"Skipped: {it.src}"));
                    }

                    // Hash only if the destination exists (copy may have been skipped if identical)
                    if (File.Exists(it.dst))
                    {
                        var sha256 = await ComputeSha256Async(it.dst, buffer, ct);
                        manifest.Files.Add(new ManifestFileEntry
                        {
                            RelativePath = it.rel.Replace('\\', '/'),
                            SizeBytes = it.size,
                            LastWriteUtcTicks = it.lastUtc.Ticks,
                            Sha256 = sha256
                        });
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // Log error and continue with the next file
                    progress.Report(new BackupProgress(copied, totalBytes, $"Error: {it.src} — {ex.Message}"));
                }
            }

            try
            {
                await _manifest.SaveAsync(Path.Combine(setPath, "manifest.json"), manifest, ct);
            }
            catch (Exception ex)
            {
                return (true, setPath, $"Backup data complete but manifest write failed: {ex.Message}");
            }

            return (true, setPath, $"Backup complete. {manifest.Files.Count} files saved.");
        }
        catch (OperationCanceledException)
        {
            return (false, setPath, "Backup canceled.");
        }
        catch (Exception ex)
        {
            return (false, setPath, $"Backup failed: {ex.Message}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task<bool> RestoreAsync(string backupSetPath, string restoreTargetFolder, IProgress<string> log, CancellationToken ct)
    {
        try
        {
            var manifestPath = Path.Combine(backupSetPath, "manifest.json");
            if (!File.Exists(manifestPath)) { log.Report("Manifest not found."); return false; }

            var manifest = await _manifest.LoadAsync(manifestPath, ct);
            var dataRoot = Path.Combine(backupSetPath, "data");

            foreach (var file in manifest.Files)
            {
                ct.ThrowIfCancellationRequested();

                var src = Path.Combine(dataRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var dst = Path.Combine(restoreTargetFolder, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);

                try
                {
                    File.Copy(src, dst, overwrite: true);
                    File.SetLastWriteTimeUtc(dst, new DateTime(file.LastWriteUtcTicks, DateTimeKind.Utc));
                    log.Report($"Restored {file.RelativePath}");
                }
                catch (Exception ex)
                {
                    log.Report($"Restore error: {file.RelativePath} — {ex.Message}");
                }
            }

            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            log.Report($"Restore failed: {ex.Message}");
            return false;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, byte[] sharedBuffer, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        await using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        int read;
        while ((read = await fs.ReadAsync(sharedBuffer.AsMemory(0, sharedBuffer.Length), ct)) > 0)
        {
            sha.TransformBlock(sharedBuffer, 0, read, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    private static string SanitizeForPath(string path)
    {
        var root = Path.GetPathRoot(path) ?? "";
        var rest = path[root.Length..].Trim(Path.DirectorySeparatorChar);
        var safeRoot = root.Replace(":", "").Replace("\\", "");
        return (safeRoot + "_" + rest.Replace('\\', '_')).Trim('_');
    }
}
