using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Drm.Desktop;
using Drm.Desktop.Localization;
using Drm.Domain;

namespace Drm.Workspaces.Tests;

public sealed partial class LocalizationTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");

    [Fact]
    public void EveryWorkspaceValidationCodeMapsToAResourceInEverySupportedCulture()
    {
        Dictionary<string, string> neutral = LoadResource("Strings.resx");
        Dictionary<string, string> korean = LoadResource("Strings.ko-KR.resx");

        foreach (WorkspaceValidationCode code in Enum.GetValues<WorkspaceValidationCode>())
        {
            string key = WorkspaceMessageKeys.ForValidation(code);
            Assert.True(neutral.ContainsKey(key), $"Missing neutral resource: {key}");
            Assert.True(korean.ContainsKey(key), $"Missing ko-KR resource: {key}");
        }
    }

    [Fact]
    public void UnknownWorkspaceValidationCodeFallsBackToCommonErrorKey()
    {
        Assert.Equal("Common.UnexpectedError",
            WorkspaceMessageKeys.ForValidation((WorkspaceValidationCode)int.MaxValue));
    }

    [Fact]
    public void ValidationResultDoesNotStoreUserFacingMessage()
    {
        PropertyInfo[] properties = typeof(WorkspaceValidationResult).GetProperties();

        Assert.DoesNotContain(properties, property => property.Name is "UserMessage" or "Message");
    }

    [Fact]
    public void ExplicitCultureLookupReturnsEnglishFallbackAndKoreanTranslation()
    {
        LocalizationService localization = new();

        Assert.Equal("Protected Workspaces", localization.GetStringForCulture("Workspace.Title", English));
        Assert.Equal("보호 작업공간", localization.GetStringForCulture("Workspace.Title", Korean));
        Assert.Equal("Add Folder", localization.GetStringForCulture("Workspace.Add", English));
        Assert.Equal("폴더 추가", localization.GetStringForCulture("Workspace.Add", Korean));
        Assert.Equal("The selected folder does not exist.",
            localization.GetStringForCulture("Workspace.Validation.DoesNotExist", English));
        Assert.Equal("선택한 폴더가 존재하지 않습니다.",
            localization.GetStringForCulture("Workspace.Validation.DoesNotExist", Korean));
    }

    [Fact]
    public void MissingKeyFallsBackToLocalizedCommonError()
    {
        LocalizationService localization = new();

        Assert.Equal("An unexpected error occurred.", localization.GetStringForCulture("Missing.Key", English));
        Assert.Equal("예상하지 못한 오류가 발생했습니다.", localization.GetStringForCulture("Missing.Key", Korean));
    }

    [Fact]
    public void FormatUsesTheRequestedCultureResource()
    {
        LocalizationService localization = new();

        Assert.Equal("Protected Workspaces", localization.FormatForCulture("Workspace.Title", English));
        Assert.Equal("보호 작업공간", localization.FormatForCulture("Workspace.Title", Korean));
    }

    [Fact]
    public void SupportedCultureCatalogMatchesResourceFiles()
    {
        string localizationDirectory = GetLocalizationDirectory();
        string[] cultureFiles = Directory.EnumerateFiles(localizationDirectory, "Strings.*.resx")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => name!["Strings.".Length..])
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] catalogCultures = SupportedUiCultures.All
            .Where(item => item.Name != SupportedUiCultures.DefaultName)
            .Select(item => item.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(catalogCultures, cultureFiles);
        Assert.Equal("en-US", SupportedUiCultures.Default.Name);
    }

    [Fact]
    public void AssemblyDeclaresEnglishAsItsNeutralFallbackLanguage()
    {
        NeutralResourcesLanguageAttribute attribute = Assert.Single(
            typeof(LocalizationService).Assembly.GetCustomAttributes<NeutralResourcesLanguageAttribute>());

        Assert.Equal(SupportedUiCultures.DefaultName, attribute.CultureName);
    }

    [Fact]
    public void EverySupportedCultureHasExactlyTheNeutralKeyContract()
    {
        Dictionary<string, string> neutral = LoadResource("Strings.resx");

        foreach (SupportedCulture culture in SupportedUiCultures.All)
        {
            Dictionary<string, string> resources = culture.Name == SupportedUiCultures.DefaultName
                ? neutral : LoadResource($"Strings.{culture.Name}.resx");
            Assert.Equal(neutral.Keys.Order(StringComparer.Ordinal), resources.Keys.Order(StringComparer.Ordinal));
            Assert.All(resources, item => Assert.False(string.IsNullOrWhiteSpace(item.Value),
                $"Empty resource {item.Key} in {culture.Name}"));
            Assert.True(resources.ContainsKey(culture.NativeDisplayNameResourceKey));
        }
    }

    [Fact]
    public void FormatPlaceholderIndexesMatchAcrossCulturesAndAreValid()
    {
        Dictionary<string, string> neutral = LoadResource("Strings.resx");
        Dictionary<string, string> korean = LoadResource("Strings.ko-KR.resx");

        foreach ((string key, string value) in neutral)
        {
            int[] neutralIndexes = PlaceholderIndexes(value);
            int[] koreanIndexes = PlaceholderIndexes(korean[key]);
            Assert.Equal(neutralIndexes, koreanIndexes);
            int argumentCount = neutralIndexes.DefaultIfEmpty(-1).Max() + 1;
            object?[] arguments = Enumerable.Range(0, argumentCount).Cast<object?>().ToArray();
            _ = string.Format(English, value, arguments);
            _ = string.Format(Korean, korean[key], arguments);
        }
    }

    [Theory]
    [InlineData(UiCultureMode.Explicit, "ko-KR", "ja-JP", "ko-KR")]
    [InlineData(UiCultureMode.Explicit, "fr-FR", "ko-KR", "en-US")]
    [InlineData(UiCultureMode.System, null, "ko-KR", "ko-KR")]
    [InlineData(UiCultureMode.System, null, "ko", "ko-KR")]
    [InlineData(UiCultureMode.System, null, "en-GB", "en-US")]
    [InlineData(UiCultureMode.System, null, "fr-CA", "en-US")]
    public void CultureResolverUsesExplicitExactLanguageAndDefaultFallback(
        UiCultureMode mode, string? selected, string system, string expected)
    {
        UserLanguagePreference preference = new(mode, selected);

        CultureInfo resolved = UiCultureResolver.Resolve(preference, CultureInfo.GetCultureInfo(system));

        Assert.Equal(expected, resolved.Name);
    }

    [Fact]
    public void AvaloniaApplicationResourcesLoad()
    {
        App application = new();
        application.Initialize();
        Assert.NotEmpty(application.Styles);
    }

    [Fact]
    public void LocalizationFilesAreValidUtf8WithoutReplacementCharacters()
    {
        UTF8Encoding strictUtf8 = new(false, true);
        string[] paths = Directory.EnumerateFiles(GetLocalizationDirectory(), "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".cs" or ".resx")
            .ToArray();

        foreach (string path in paths)
        {
            string text = strictUtf8.GetString(File.ReadAllBytes(path));
            Assert.DoesNotContain('\uFFFD', text);
        }
    }

    private static Dictionary<string, string> LoadResource(string fileName)
    {
        XDocument document = XDocument.Load(Path.Combine(GetLocalizationDirectory(), fileName));
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
        return entries.ToDictionary(entry => entry.Key!, entry => entry.Value!, StringComparer.Ordinal);
    }

    private static int[] PlaceholderIndexes(string value) => PlaceholderRegex().Matches(value)
        .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
        .Distinct().Order().ToArray();

    private static string GetLocalizationDirectory() => Path.Combine(
        FindRepositoryRoot(), "src", "Drm.Desktop", "Localization");

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

    [GeneratedRegex(@"(?<!\{)\{(\d+)(?:[^}]*)\}(?!\})", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
}
