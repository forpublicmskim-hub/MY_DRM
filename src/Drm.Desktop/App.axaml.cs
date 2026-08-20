using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Drm.Application;
using Drm.Desktop.ViewModels;
using Drm.Desktop.Views;
using Drm.Desktop.Services;
using Drm.Desktop.Localization;
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
            LocalizationService localization = new();
            WorkspaceService workspaceService = new(
                new JsonWorkspaceRegistry(settingsPath), locations,
                new WorkspaceRegistrationPolicy(locations), new SystemClock());
            LocalWorkspaceScanner scanner = new();
            WorkspaceMonitorManager monitors = new(
                new FileSystemWatcherWorkspaceMonitorFactory(scanner));
            PolicyTrustOptions policyTrust =
#if DEBUG
                PolicyTrustOptions.Development;
#else
                PolicyTrustOptions.Production;
#endif
            ProtectionPolicyLoader policyLoader = new(
                new LocalFileProtectionPolicySource(), new SystemClock(), policyTrust);
            ProtectionPolicyInspectionService policyService = new(policyLoader);
            ProtectionPolicyPanelViewModel policyPanel = new(
                policyService,
                new AvaloniaPolicyFilePicker(() => window, localization),
                localization);
            ProtectionCandidateInspectionProcessor processor = new(
                new ProtectionCandidateCollector(new LocalProtectionCandidateMetadataReader()),
                policyService,
                new SystemClock());
            ProtectionInspectionPipeline inspectionPipeline = new(monitors, processor);
            window.DataContext = new MainViewModel(workspaceService, inspectionPipeline,
                new AvaloniaFolderPicker(() => window, localization), new LocalWorkspacePathLauncher(),
                localization, policyPanel, policyService);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
