using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Drm.Desktop;
using Drm.Desktop.Localization;
using Drm.Domain;

namespace Drm.Workspaces.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void EveryWorkspaceValidationCodeHasANonEmptyKoreanResource()
    {
        LocalizationService localization = new();

        foreach (WorkspaceValidationCode code in Enum.GetValues<WorkspaceValidationCode>())
        {
            string key = WorkspaceErrorLocalizer.GetResourceKey(code);
            Assert.True(LocalizationService.ContainsResource(key), $"Missing resource: {key}");
            Assert.False(string.IsNullOrWhiteSpace(localization.GetString(key)));
        }
    }

    [Fact]
    public void UnknownWorkspaceValidationCodeFallsBackToCommonError()
    {
        LocalizationService localization = new();
        WorkspaceErrorLocalizer errors = new(localization);

        string message = errors.GetMessage((WorkspaceValidationCode)int.MaxValue);

        Assert.Equal(localization.GetString("Common.UnexpectedError"), message);
    }

    [Fact]
    public void ValidationResultDoesNotStoreUserFacingMessage()
    {
        PropertyInfo[] properties = typeof(WorkspaceValidationResult).GetProperties();

        Assert.DoesNotContain(properties, property => property.Name is "UserMessage" or "Message");
    }

    [Fact]
    public void KoreanBaselineResourcesAreReadableWithoutEncodingDamage()
    {
        LocalizationService localization = new();

        Assert.Equal("보호 작업공간", localization.GetString("Workspace.Title"));
        Assert.Equal("폴더 추가", localization.GetString("Workspace.Add"));
        Assert.Equal("선택한 폴더가 존재하지 않습니다.",
            localization.GetString("Workspace.Validation.DoesNotExist"));
        Assert.DoesNotContain('\uFFFD', localization.GetString("Workspace.Description"));
    }

    [Fact]
    public void BaselineResourceHasNoDuplicateOrEmptyEntries()
    {
        string resourcePath = Path.Combine(FindRepositoryRoot(),
            "src/Drm.Desktop/Localization/Strings.resx");
        XDocument document = XDocument.Load(resourcePath);
        var entries = document.Root!.Elements("data")
            .Select(element => new
            {
                Key = (string?)element.Attribute("name"),
                Value = (string?)element.Element("value")
            }).ToArray();

        Assert.All(entries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Key));
            Assert.False(string.IsNullOrWhiteSpace(entry.Value));
        });
        Assert.Equal(entries.Length, entries.Select(entry => entry.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AvaloniaApplicationResourcesLoad()
    {
        App application = new();

        application.Initialize();

        Assert.NotEmpty(application.Styles);
    }

    [Fact]
    public void UserFacingSourceFilesAreValidUtf8()
    {
        string root = FindRepositoryRoot();
        string[] relativePaths =
        [
            "src/Drm.Desktop/Localization/Strings.resx",
            "src/Drm.Desktop/Views/MainWindow.axaml",
            "src/Drm.Desktop/ViewModels/MainViewModel.cs",
            "src/Drm.Desktop/Services/FolderPicker.cs",
            "docs/architecture.md"
        ];
        UTF8Encoding strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        foreach (string relativePath in relativePaths)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, relativePath));
            string text = strictUtf8.GetString(bytes);
            Assert.DoesNotContain('\uFFFD', text);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Drm.slnx"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
