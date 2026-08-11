using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Drm.Application;
using Drm.Desktop.Services;
using Drm.Domain;
using Drm.Platform.Abstractions;

namespace Drm.Desktop.ViewModels;

public sealed partial class MainViewModel(
    WorkspaceService workspaces,
    IFolderPicker folderPicker,
    IWorkspacePathLauncher pathLauncher) : ViewModelBase, IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();

    public ObservableCollection<WorkspaceItemViewModel> Items { get; } = [];

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

    public string ActivityMessage => IsLoading ? "목록을 불러오는 중..."
        : IsRegistering ? "폴더를 등록하는 중..."
        : IsUnregistering ? "등록을 해제하는 중..." : string.Empty;

    public async ValueTask InitializeAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        OnPropertyChanged(nameof(ActivityMessage));
        ErrorMessage = null;
        try { await ReloadAsync(_lifetime.Token); }
        catch (WorkspaceRegistryCorruptedException) { ErrorMessage = "작업공간 설정이 손상되었습니다. 설정 파일을 확인해 주세요."; }
        catch (WorkspaceRegistryException) { ErrorMessage = "작업공간 목록을 불러오지 못했습니다."; }
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
        catch (InvalidOperationException exception) { ErrorMessage = exception.Message; return; }
        if (path is null) return;

        IsRegistering = true;
        OnPropertyChanged(nameof(ActivityMessage));
        try
        {
            WorkspaceRegistrationResult result = await workspaces.RegisterAsync(path, _lifetime.Token);
            if (!result.IsSuccess) { ErrorMessage = result.Validation.UserMessage; return; }
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
            if (!removed) { ErrorMessage = "이미 등록 해제되었거나 찾을 수 없는 작업공간입니다."; return; }
            Items.Remove(selected);
            SelectedItem = null;
        }
        catch (WorkspaceRegistryException) { ErrorMessage = "등록 해제 내용을 저장하지 못했습니다. 폴더와 파일은 변경되지 않았습니다."; }
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
        { ErrorMessage = "등록된 폴더를 열 수 없습니다."; }
    }

    private async ValueTask ReloadAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProtectedWorkspace> loaded = await workspaces.GetAllAsync(cancellationToken);
        Items.Clear();
        foreach (ProtectedWorkspace workspace in loaded.OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            Items.Add(new WorkspaceItemViewModel(workspace));
    }

    public ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        workspaces.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class WorkspaceItemViewModel(ProtectedWorkspace workspace)
{
    public ProtectedWorkspace Workspace { get; } = workspace;
    public WorkspaceId Id => Workspace.Id;
    public string DisplayName => Workspace.DisplayName;
    public string Path => Workspace.Location.DisplayPath;
    public string Status => Workspace.RegistrationState == WorkspaceRegistrationState.Registered ? "등록됨" : "접근 불가";
    public string ProtectionNotice => Workspace.ProtectionState == WorkspaceProtectionState.NotActivated
        ? "파일 보호 기능은 아직 활성화되지 않았습니다."
        : "파일 보호 상태를 확인해 주세요.";
}
