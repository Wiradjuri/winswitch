using System;
using System.Windows; // WPF
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WinSwitch.Services;
using WinSwitch.ViewModels;

namespace WinSwitch;

public partial class App : System.Windows.Application
{
    public static IHost AppHost { get; private set; } =
        Host.CreateDefaultBuilder()
            .ConfigureServices((ctx, services) =>
            {
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<IDriveService, DriveService>();
                services.AddSingleton<IManifestService, ManifestService>();
                services.AddSingleton<IBackupService, BackupService>();
            })
            .Build();

    public IServiceProvider Services => AppHost.Services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        await AppHost.StartAsync();
        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await AppHost.StopAsync();
        base.OnExit(e);
    }
}
