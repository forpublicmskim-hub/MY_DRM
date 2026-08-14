using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Drm.PolicyMaker.Services;
using Drm.PolicyMaker.ViewModels;
using Drm.PolicyMaker.Views;

namespace Drm.PolicyMaker;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = new();
            window.DataContext = new MainViewModel(new AvaloniaPolicyFileDialog(() => window));
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
