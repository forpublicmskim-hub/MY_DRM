using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Drm.Application;
using Drm.Desktop.ViewModels;
using Drm.Desktop.Views;
using Drm.Desktop.Services;
using Drm.Infrastructure;
using Drm.Platform.Local;

namespace Drm.Desktop;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = new();
            string settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Drm", "workspaces.json");
            LocalWorkspaceLocationResolver locations = new();
            WorkspaceService workspaceService = new(
                new JsonWorkspaceRegistry(settingsPath), locations,
                new WorkspaceRegistrationPolicy(locations), new SystemClock());
            window.DataContext = new MainViewModel(workspaceService,
                new AvaloniaFolderPicker(() => window), new LocalWorkspacePathLauncher());
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
