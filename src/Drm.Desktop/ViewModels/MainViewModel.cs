using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Drm.Application;
using Drm.Desktop.Services;
using Drm.Desktop.Localization;
using Drm.Domain;
using Drm.Platform.Abstractions;

namespace Drm.Desktop.ViewModels;

public sealed partial class MainViewModel(
    WorkspaceService workspaces,
    IFolderPicker folderPicker,
    IWorkspacePathLauncher pathLauncher,
    ILocalizationService localization) : ViewModelBase, IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();

    public ObservableCollection<WorkspaceItemViewModel> Items { get; } = [];
    public string AppTitle => localization.GetString("App.Title");
    public string WorkspaceTitle => localization.GetString("Workspace.Title");
    public string WorkspaceDescription => localization.GetString("Workspace.Description");
    public string AddFolderLabel => localization.GetString("Workspace.Add");
    public string UnregisterLabel => localization.GetString("Workspace.Unregister");
    public string OpenLocationLabel => localization.GetString("Workspace.OpenLocation");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnregisterCommand), nameof(OpenPathCommand))]
    public partial WorkspaceItemViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFolderCommand), nameof(UnregisterCommand), nameof(OpenPathCommand))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFolderCommand), nameof(UnregisterCommand), nameof(OpenPathCommand))]
    public partial bool IsRegistering { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFolderCommand), nameof(UnregisterCommand), nameof(OpenPathCommand))]
    public partial bool IsUnregistering { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public string ActivityMessage => IsLoading ? localization.GetString("Workspace.Activity.Loading")
        : IsRegistering ? localization.GetString("Workspace.Activity.Registering")
        : IsUnregistering ? localization.GetString("Workspace.Activity.Unregistering") : string.Empty;

    public async ValueTask InitializeAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        OnPropertyChanged(nameof(ActivityMessage));
        ErrorMessage = null;
        try { await ReloadAsync(_lifetime.Token); }
        catch (WorkspaceRegistryCorruptedException) { ErrorMessage = localization.GetString("Workspace.Load.Corrupted"); }
        catch (WorkspaceRegistryException) { ErrorMessage = localization.GetString("Workspace.Load.Failed"); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { IsLoading = false; OnPropertyChanged(nameof(ActivityMessage)); }
    }

    private bool CanAddFolder() => !IsLoading && !IsRegistering && !IsUnregistering;

    [RelayCommand(CanExecute = nameof(CanAddFolder))]
    private async Task AddFolderAsync()
    {
        ErrorMessage = null;
        string? path;
        try { path = await folderPicker.PickAsync(_lifetime.Token); }
        catch (OperationCanceledException) { return; }
        catch (FolderPickerUnavailableException) { ErrorMessage = localization.GetString("Workspace.Picker.Unavailable"); return; }
        if (path is null) return;

        IsRegistering = true;
        OnPropertyChanged(nameof(ActivityMessage));
        try
        {
            WorkspaceRegistrationResult result = await workspaces.RegisterAsync(path, _lifetime.Token);
            if (!result.IsSuccess)
            {
                ErrorMessage = localization.GetString(WorkspaceMessageKeys.ForValidation(result.Validation.Code));
                return;
            }
            await ReloadAsync(_lifetime.Token);
            SelectedItem = Items.FirstOrDefault(item => item.Id == result.Workspace!.Id);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { IsRegistering = false; OnPropertyChanged(nameof(ActivityMessage)); }
    }

    private bool CanUseSelection() => SelectedItem is not null && !IsLoading && !IsRegistering && !IsUnregistering;

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private async Task UnregisterAsync()
    {
        WorkspaceItemViewModel? selected = SelectedItem;
        if (selected is null) return;
        IsUnregistering = true;
        OnPropertyChanged(nameof(ActivityMessage));
        ErrorMessage = null;
        try
        {
            bool removed = await workspaces.UnregisterAsync(selected.Id, _lifetime.Token);
            if (!removed) { ErrorMessage = localization.GetString("Workspace.Unregister.NotFound"); return; }
            Items.Remove(selected);
            SelectedItem = null;
        }
        catch (WorkspaceRegistryException) { ErrorMessage = localization.GetString("Workspace.Unregister.SaveFailed"); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { IsUnregistering = false; OnPropertyChanged(nameof(ActivityMessage)); }
    }

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private async Task OpenPathAsync()
    {
        if (SelectedItem is null) return;
        ErrorMessage = null;
        try { await pathLauncher.OpenAsync(SelectedItem.Workspace.Location, _lifetime.Token); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { ErrorMessage = localization.GetString("Workspace.Open.Failed"); }
    }

    private async ValueTask ReloadAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProtectedWorkspace> loaded = await workspaces.GetAllAsync(cancellationToken);
        Items.Clear();
        foreach (ProtectedWorkspace workspace in loaded.OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            Items.Add(new WorkspaceItemViewModel(workspace, localization));
    }

    public ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        workspaces.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class WorkspaceItemViewModel(ProtectedWorkspace workspace, ILocalizationService localization)
{
    public ProtectedWorkspace Workspace { get; } = workspace;
    public WorkspaceId Id => Workspace.Id;
    public string DisplayName => Workspace.DisplayName;
    public string Path => Workspace.Location.DisplayPath;
    public string Status => Workspace.RegistrationState == WorkspaceRegistrationState.Registered
        ? localization.GetString("Workspace.Status.Registered") : localization.GetString("Workspace.Status.Unavailable");
    public string ProtectionNotice => Workspace.ProtectionState == WorkspaceProtectionState.NotActivated
        ? localization.GetString("Workspace.Protection.NotActivated")
        : localization.GetString("Workspace.Protection.CheckStatus");
}
