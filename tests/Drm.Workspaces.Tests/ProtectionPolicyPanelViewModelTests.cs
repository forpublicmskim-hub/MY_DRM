using Drm.Application;
using Drm.Desktop.Localization;
using Drm.Desktop.Services;
using Drm.Desktop.ViewModels;
using Drm.Policy;

namespace Drm.Workspaces.Tests;

public sealed class ProtectionPolicyPanelViewModelTests
{
    [Fact]
    public async Task SuccessfulInspectionDisplaysSummaryWithoutClaimingEnforcement()
    {
        string json = CreatePolicyJson("Inspection Policy");
        await using ProtectionPolicyPanelViewModel viewModel = CreateViewModel(
            new QueuePicker("policy.json"),
            new QueueSource(ProtectionPolicySourceReadResult.Success(json)));

        await viewModel.LoadPolicyCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.Summary);
        Assert.Equal("Inspection Policy", viewModel.Summary.DisplayName);
        Assert.Equal("Policy.State.Trust.UnsignedDevelopmentDraft", viewModel.Summary.TrustState);
        Assert.Equal("Policy.State.Enforcement.NotApplied", viewModel.Summary.EnforcementState);
        Assert.Equal("Policy.Status.Loaded", viewModel.StatusMessage);
    }

    [Fact]
    public async Task FailedInspectionKeepsLastSuccessfulSummary()
    {
        string json = CreatePolicyJson("Retained Policy");
        await using ProtectionPolicyPanelViewModel viewModel = CreateViewModel(
            new QueuePicker("valid.json", "missing.json"),
            new QueueSource(
                ProtectionPolicySourceReadResult.Success(json),
                new ProtectionPolicySourceReadResult(PolicySourceReadStatus.NotFound)));

        await viewModel.LoadPolicyCommand.ExecuteAsync(null);
        ProtectionPolicySummaryViewModel summary = viewModel.Summary!;
        await viewModel.LoadPolicyCommand.ExecuteAsync(null);

        Assert.Same(summary, viewModel.Summary);
        Assert.Equal("Policy.Status.NotFound", viewModel.StatusMessage);
    }

    [Fact]
    public async Task PickerCancellationLeavesExistingStateUntouched()
    {
        await using ProtectionPolicyPanelViewModel viewModel = CreateViewModel(
            new QueuePicker((string?)null),
            new QueueSource());
        string originalStatus = viewModel.StatusMessage;

        await viewModel.LoadPolicyCommand.ExecuteAsync(null);

        Assert.Equal(originalStatus, viewModel.StatusMessage);
        Assert.Null(viewModel.Summary);
    }

    private static ProtectionPolicyPanelViewModel CreateViewModel(
        IPolicyFilePicker picker,
        IProtectionPolicySource source)
    {
        ProtectionPolicyLoader loader = new(source, new FixedClock(), PolicyTrustOptions.Development);
        return new ProtectionPolicyPanelViewModel(
            new ProtectionPolicyInspectionService(loader), picker, new KeyLocalizationService());
    }

    private static string CreatePolicyJson(string name)
    {
        ProtectionPolicyDraft draft = new() { DisplayName = name };
        draft.IncludedExtensions.Add(".pdf");
        return ProtectionPolicySerializer.Serialize(PolicyNormalizer.Normalize(draft));
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class QueuePicker(params string?[] locations) : IPolicyFilePicker
    {
        private readonly Queue<string?> _locations = new(locations);

        public ValueTask<string?> PickAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_locations.Dequeue());
        }
    }

    private sealed class QueueSource(params ProtectionPolicySourceReadResult[] results) : IProtectionPolicySource
    {
        private readonly Queue<ProtectionPolicySourceReadResult> _results = new(results);

        public ValueTask<ProtectionPolicySourceReadResult> ReadAsync(
            string location,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_results.Dequeue());
        }
    }

    private sealed class KeyLocalizationService : ILocalizationService
    {
        public string GetString(string key) => key;
        public string GetStringForCulture(string key, System.Globalization.CultureInfo culture) => key;
        public string Format(string key, params object?[] arguments) => key;
    }
}
