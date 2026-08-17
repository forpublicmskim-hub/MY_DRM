using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Drm.Policy;
using Drm.PolicyMaker.Services;

namespace Drm.PolicyMaker.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PreviewDebounceInterval = TimeSpan.FromMilliseconds(250);
    private static readonly HashSet<string> PreviewInputProperties =
    [
        nameof(DisplayName), nameof(Enabled), nameof(ProtectNewFiles), nameof(ProtectExistingFiles),
        nameof(IncludedExtensions), nameof(ExcludedExtensions), nameof(MaximumFileSizeBytes),
        nameof(ValidFromDate), nameof(ValidFromTime), nameof(ValidUntilDate), nameof(ValidUntilTime)
    ];
    private readonly IPolicyFileDialog _fileDialog;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _previewRefresh;
    private ProtectionPolicyDocument? _baseline;
    private bool _suppressPreviewRefresh;

    public MainViewModel(IPolicyFileDialog fileDialog)
    {
        _fileDialog = fileDialog;
        ResetDraft();
    }

    [ObservableProperty] public partial string PolicyId { get; set; } = string.Empty;
    [ObservableProperty] public partial int PolicyVersion { get; set; }
    [ObservableProperty] public partial string DisplayName { get; set; } = string.Empty;
    [ObservableProperty] public partial bool Enabled { get; set; }
    [ObservableProperty] public partial bool ProtectNewFiles { get; set; }
    [ObservableProperty] public partial bool ProtectExistingFiles { get; set; }
    [ObservableProperty] public partial string IncludedExtensions { get; set; } = string.Empty;
    [ObservableProperty] public partial string ExcludedExtensions { get; set; } = string.Empty;
    [ObservableProperty] public partial string MaximumFileSizeBytes { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTime? ValidFromDate { get; set; }
    [ObservableProperty] public partial TimeSpan? ValidFromTime { get; set; }
    [ObservableProperty] public partial DateTime? ValidUntilDate { get; set; }
    [ObservableProperty] public partial TimeSpan? ValidUntilTime { get; set; }
    [ObservableProperty] public partial string JsonPreview { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsBusy { get; set; }

    public ObservableCollection<string> ValidationErrors { get; } = [];

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_suppressPreviewRefresh && e.PropertyName is not null && PreviewInputProperties.Contains(e.PropertyName))
            SchedulePreviewRefresh();
    }

    partial void OnValidFromDateChanged(DateTime? value)
    {
        if (value is null) ValidFromTime = null;
        else ValidFromTime ??= TimeSpan.Zero;
    }

    partial void OnValidUntilDateChanged(DateTime? value)
    {
        if (value is null) ValidUntilTime = null;
        else ValidUntilTime ??= TimeSpan.Zero;
    }

    private void SchedulePreviewRefresh()
    {
        CancellationTokenSource next = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        CancellationTokenSource? previous = Interlocked.Exchange(ref _previewRefresh, next);
        previous?.Cancel();
        previous?.Dispose();
        _ = RefreshPreviewAfterDelayAsync(next);
    }

    private void CancelPreviewRefresh()
    {
        CancellationTokenSource? pending = Interlocked.Exchange(ref _previewRefresh, null);
        pending?.Cancel();
        pending?.Dispose();
    }

    private async Task RefreshPreviewAfterDelayAsync(CancellationTokenSource refresh)
    {
        try
        {
            await Task.Delay(PreviewDebounceInterval, refresh.Token);
            RefreshPreview(showSuccessStatus: false);
        }
        catch (OperationCanceledException) when (refresh.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _previewRefresh, null, refresh), refresh))
                refresh.Dispose();
        }
    }

    private void RefreshPreview(bool showSuccessStatus)
    {
        if (!TryBuildDocument(out ProtectionPolicyDocument? document))
        {
            StatusMessage = "현재 입력에 오류가 있어 마지막 유효 JSON을 유지합니다.";
            return;
        }

        JsonPreview = ProtectionPolicySerializer.Serialize(document!);
        StatusMessage = showSuccessStatus
            ? "유효한 unsigned development Draft입니다."
            : "입력 내용이 JSON 미리보기에 반영되었습니다.";
    }

    [RelayCommand]
    private void NewPolicy() => ResetDraft();

    [RelayCommand]
    private void Validate()
    {
        CancelPreviewRefresh();
        RefreshPreview(showSuccessStatus: true);
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            string? path = await _fileDialog.OpenAsync(_lifetime.Token);
            if (path is null) return;
            PolicyLoadResult result = await PolicyFileStore.LoadAsync(path, _lifetime.Token);
            if (!result.IsSuccess)
            {
                ShowErrors(result.Validation.Errors);
                StatusMessage = "정책 파일을 열 수 없습니다.";
                return;
            }
            LoadDocument(result.Document!);
            StatusMessage = "개발용 Draft를 불러왔습니다.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = "정책 파일을 열지 못했습니다.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        if (IsBusy) return;
        CancelPreviewRefresh();
        ValidationErrors.Clear();
        if (!TryBuildDocument(out ProtectionPolicyDocument? document)) return;
        document = ApplyVersion(document!);
        string? path;
        try { path = await _fileDialog.SaveAsAsync(SafeSuggestedName(document.DisplayName), _lifetime.Token); }
        catch (InvalidOperationException)
        {
            StatusMessage = "이 환경에서는 로컬 정책 파일을 저장할 수 없습니다.";
            return;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        if (path is null) return;

        IsBusy = true;
        try
        {
            await PolicyFileStore.SaveAsync(document, path, _lifetime.Token);
            _baseline = document;
            PolicyVersion = document.PolicyVersion;
            JsonPreview = ProtectionPolicySerializer.Serialize(document);
            StatusMessage = "검증된 unsigned development Draft를 저장했습니다.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "정책 파일을 원자적으로 저장하지 못했습니다.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { IsBusy = false; }
    }

    private bool TryBuildDocument(out ProtectionPolicyDocument? document)
    {
        document = null;
        ValidationErrors.Clear();
        if (!Guid.TryParse(PolicyId, out Guid policyId))
            ValidationErrors.Add("policyId: 올바른 GUID가 필요합니다.");
        if (!TryNullableLong(MaximumFileSizeBytes, out long? maximum))
            ValidationErrors.Add("protection.maximumFileSizeBytes: 양의 정수가 필요합니다.");
        DateTimeOffset? validFrom = CombineUtc(ValidFromDate, ValidFromTime);
        DateTimeOffset? validUntil = CombineUtc(ValidUntilDate, ValidUntilTime);
        if (ValidationErrors.Count > 0) return false;

        ProtectionPolicyDraft draft = new()
        {
            PolicyId = policyId,
            PolicyVersion = PolicyVersion,
            DisplayName = DisplayName,
            Enabled = Enabled,
            ProtectNewFiles = ProtectNewFiles,
            ProtectExistingFiles = ProtectExistingFiles,
            MaximumFileSizeBytes = maximum,
            ValidFromUtc = validFrom,
            ValidUntilUtc = validUntil
        };
        draft.IncludedExtensions.Clear();
        foreach (string extension in SplitExtensions(IncludedExtensions)) draft.IncludedExtensions.Add(extension);
        draft.ExcludedExtensions.Clear();
        foreach (string extension in SplitExtensions(ExcludedExtensions)) draft.ExcludedExtensions.Add(extension);
        PolicyValidationResult validation = ProtectionPolicyValidator.Validate(draft);
        if (!validation.IsValid)
        {
            ShowErrors(validation.Errors);
            StatusMessage = "검증 오류를 수정해 주세요.";
            return false;
        }
        document = PolicyNormalizer.Normalize(draft);
        return true;
    }

    private ProtectionPolicyDocument ApplyVersion(ProtectionPolicyDocument candidate)
    {
        if (_baseline is null || _baseline.PolicyId != candidate.PolicyId) return candidate with { PolicyVersion = 1 };
        ProtectionPolicyDocument sameVersion = candidate with { PolicyVersion = _baseline.PolicyVersion };
        bool changed = ProtectionPolicySerializer.Serialize(sameVersion) != ProtectionPolicySerializer.Serialize(_baseline);
        return sameVersion with { PolicyVersion = changed ? _baseline.PolicyVersion + 1 : _baseline.PolicyVersion };
    }

    private void LoadDocument(ProtectionPolicyDocument document)
    {
        CancelPreviewRefresh();
        _suppressPreviewRefresh = true;
        try
        {
            ProtectionPolicyDraft draft = PolicyNormalizer.ToDraft(document);
            PolicyId = draft.PolicyId.ToString("D");
            PolicyVersion = draft.PolicyVersion;
            DisplayName = draft.DisplayName;
            Enabled = draft.Enabled;
            ProtectNewFiles = draft.ProtectNewFiles;
            ProtectExistingFiles = draft.ProtectExistingFiles;
            IncludedExtensions = string.Join(Environment.NewLine, draft.IncludedExtensions);
            ExcludedExtensions = string.Join(Environment.NewLine, draft.ExcludedExtensions);
            MaximumFileSizeBytes = draft.MaximumFileSizeBytes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            DateTimeOffset? validFrom = draft.ValidFromUtc?.ToUniversalTime();
            ValidFromDate = validFrom?.UtcDateTime.Date;
            ValidFromTime = validFrom?.TimeOfDay;
            DateTimeOffset? validUntil = draft.ValidUntilUtc?.ToUniversalTime();
            ValidUntilDate = validUntil?.UtcDateTime.Date;
            ValidUntilTime = validUntil?.TimeOfDay;
            JsonPreview = ProtectionPolicySerializer.Serialize(document);
            ValidationErrors.Clear();
        }
        finally { _suppressPreviewRefresh = false; }
        _baseline = document;
    }

    private void ResetDraft()
    {
        ProtectionPolicyDraft draft = new() { DisplayName = "새 보호 정책" };
        LoadDocument(PolicyNormalizer.Normalize(draft));
        _baseline = null;
        StatusMessage = "서명되지 않은 개발용 Draft입니다.";
    }

    private void ShowErrors(IEnumerable<PolicyValidationError> errors)
    {
        ValidationErrors.Clear();
        foreach (PolicyValidationError error in errors)
            ValidationErrors.Add($"{error.Path}: {ToUserMessage(error)}");
    }

    private static string ToUserMessage(PolicyValidationError error) => error.Code switch
    {
        PolicyValidationCodes.Required => "필수 값입니다.",
        PolicyValidationCodes.InvalidPolicyVersion => "정책 버전은 1 이상이어야 합니다.",
        PolicyValidationCodes.InvalidExtension => "올바른 확장자 형식이 아닙니다.",
        PolicyValidationCodes.DuplicateExtension => "중복 확장자입니다.",
        PolicyValidationCodes.ExtensionConflict => "포함 및 제외 목록에 동시에 존재합니다.",
        PolicyValidationCodes.ProtectedExtensionIncluded => "보호 컨테이너 확장자는 포함할 수 없습니다.",
        PolicyValidationCodes.InvalidMaximumSize => "최대 크기는 양수여야 합니다.",
        PolicyValidationCodes.InvalidValidityRange => "종료 시간은 시작 시간보다 늦어야 합니다.",
        PolicyValidationCodes.UnsupportedCapability => "이 클라이언트가 지원하지 않는 기능입니다.",
        PolicyValidationCodes.MissingCapability => "선택한 옵션에 필요한 capability가 누락되었습니다.",
        PolicyValidationCodes.UnexpectedCapability => "정책 내용과 관계없는 capability가 선언되었습니다.",
        PolicyValidationCodes.ValueTooLong => "허용 길이를 초과했습니다.",
        PolicyValidationCodes.TooManyValues => "허용 개수를 초과했습니다.",
        PolicyValidationCodes.DocumentTooLarge => "정책 문서가 허용 크기를 초과했습니다.",
        PolicyValidationCodes.InvalidSchemaVersion => "지원하지 않는 schemaVersion입니다.",
        _ => "유효하지 않은 정책 값입니다."
    };

    private static string[] SplitExtensions(string value) =>
        value.Split([',', ';', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static bool TryNullableLong(string value, out long? result)
    {
        if (string.IsNullOrWhiteSpace(value)) { result = null; return true; }
        bool parsed = long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long number);
        result = parsed ? number : null;
        return parsed;
    }

    private static DateTimeOffset? CombineUtc(DateTime? date, TimeSpan? time)
    {
        if (date is null) return null;
        TimeSpan selectedTime = time ?? TimeSpan.Zero;
        return new DateTimeOffset(
            date.Value.Year, date.Value.Month, date.Value.Day,
            selectedTime.Hours, selectedTime.Minutes, selectedTime.Seconds,
            TimeSpan.Zero);
    }

    private static string SafeSuggestedName(string displayName)
    {
        string safe = string.Concat(displayName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
        return (safe.Length == 0 ? "policy" : safe) + ".json";
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        CancelPreviewRefresh();
        _lifetime.Dispose();
    }
}
