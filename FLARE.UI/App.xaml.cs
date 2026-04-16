using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using FLARE.UI.Services;
using FLARE.UI.ViewModels;

namespace FLARE.UI;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
