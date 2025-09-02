using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinSwitch.Models;

namespace WinSwitch.Services;

public class ManifestService : IManifestService
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true
    };

    public async Task SaveAsync(string path, BackupManifest manifest, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, manifest, _opts, ct);
    }

    public async Task<BackupManifest> LoadAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var obj = await JsonSerializer.DeserializeAsync<BackupManifest>(fs, _opts, ct);
        return obj ?? new BackupManifest();
    }
}
