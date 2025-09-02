using System.Threading;
using System.Threading.Tasks;
using WinSwitch.Models;

namespace WinSwitch.Services;

public interface IManifestService
{
    Task SaveAsync(string path, BackupManifest manifest, CancellationToken ct);
    Task<BackupManifest> LoadAsync(string path, CancellationToken ct);
}

