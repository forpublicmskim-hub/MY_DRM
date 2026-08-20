using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Drm.Application;
using Drm.Desktop.Localization;
using Drm.Desktop.Services;
using Drm.Policy;

namespace Drm.Desktop.ViewModels;

public sealed partial class ProtectionPolicyPanelViewModel(
    ProtectionPolicyInspectionService policies,
    IPolicyFilePicker picker,
    ILocalizationService localization) : ViewModelBase, IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();

    public string Title => localization.GetString("Policy.Inspection.Title");
    public string Description => localization.GetString("Policy.Inspection.Description");
    public string LoadLabel => localization.GetString("Policy.Inspection.Load");
    public string NameLabel => localization.GetString("Policy.Field.Name");
    public string IdLabel => localization.GetString("Policy.Field.Id");
    public string VersionLabel => localization.GetString("Policy.Field.Version");
    public string IncludedLabel => localization.GetString("Policy.Field.IncludedExtensions");
    public string ExcludedLabel => localization.GetString("Policy.Field.ExcludedExtensions");
    public string MaximumSizeLabel => localization.GetString("Policy.Field.MaximumSize");
    public string SourceLabel => localization.GetString("Policy.Field.Source");
    public string LoadedAtLabel => localization.GetString("Policy.Field.LoadedAt");
    public string DocumentStateLabel => localization.GetString("Policy.Field.DocumentState");
    public string TrustStateLabel => localization.GetString("Policy.Field.TrustState");
    public string EnforcementStateLabel => localization.GetString("Policy.Field.EnforcementState");

    public ObservableCollection<string> ValidationErrors { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadPolicyCommand))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } =
        localization.GetString("Policy.Status.NotLoaded");

    [ObservableProperty]
    public partial ProtectionPolicySummaryViewModel? Summary { get; set; }

    private bool CanLoadPolicy() => !IsLoading;

    [RelayCommand(CanExecute = nameof(CanLoadPolicy))]
    private async Task LoadPolicyAsync()
    {
        string? location;
        try
        {
            location = await picker.PickAsync(_lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (PolicyFilePickerUnavailableException)
        {
            StatusMessage = localization.GetString("Policy.Status.PickerUnavailable");
            return;
        }

        if (location is null) return;

        IsLoading = true;
        ValidationErrors.Clear();
        try
        {
            ProtectionPolicyLoadResult result = await policies.LoadAsync(location, _lifetime.Token);
            StatusMessage = localization.GetString(PolicyMessageKeys.ForLoadStatus(result.Status));
            foreach (PolicyValidationError error in result.Errors)
                ValidationErrors.Add(localization.GetString(PolicyMessageKeys.ForValidation(error.Code)));
            if (result.IsLoaded)
                Summary = new ProtectionPolicySummaryViewModel(result.Snapshot!, localization);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (LoadPolicyCommand.ExecutionTask is Task execution)
        {
            try
            {
                await execution.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        _lifetime.Dispose();
    }
}

public sealed class ProtectionPolicySummaryViewModel(
    InspectedProtectionPolicy snapshot,
    ILocalizationService localization)
{
    private readonly EffectiveProtectionPolicy _policy = snapshot.Policy;

    public string DisplayName => _policy.DisplayName;
    public string PolicyId => _policy.PolicyId.ToString();
    public string PolicyVersion => _policy.PolicyVersion.ToString(CultureInfo.CurrentCulture);
    public string IncludedExtensions => FormatExtensions(_policy.IncludedExtensions);
    public string ExcludedExtensions => FormatExtensions(_policy.ExcludedExtensions);
    public string MaximumFileSize => _policy.MaximumFileSizeBytes is long bytes
        ? FormatBytes(bytes)
        : localization.GetString("Policy.Value.NoLimit");
    public string SourceLocation => snapshot.SourceLocation;
    public string LoadedAt => snapshot.LoadedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public string DocumentState => localization.GetString("Policy.State.Document.Valid");
    public string TrustState => localization.GetString("Policy.State.Trust.UnsignedDevelopmentDraft");
    public string EnforcementState => localization.GetString("Policy.State.Enforcement.NotApplied");

    private string FormatExtensions(IEnumerable<string> extensions)
    {
        string value = string.Join(", ", extensions.Order(StringComparer.Ordinal));
        return value.Length == 0 ? localization.GetString("Policy.Value.None") : value;
    }

    private static string FormatBytes(long bytes)
    {
        const long gibibyte = 1024L * 1024 * 1024;
        const long mebibyte = 1024L * 1024;
        return bytes >= gibibyte && bytes % gibibyte == 0
            ? $"{bytes / gibibyte:N0} GiB"
            : bytes >= mebibyte && bytes % mebibyte == 0
                ? $"{bytes / mebibyte:N0} MiB"
                : $"{bytes:N0} B";
    }
}
