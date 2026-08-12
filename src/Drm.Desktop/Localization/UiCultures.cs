using System.Globalization;

namespace Drm.Desktop.Localization;

public sealed record SupportedCulture(string Name, string NativeDisplayNameResourceKey);

public static class SupportedUiCultures
{
    public const string DefaultName = "en-US";

    public static IReadOnlyList<SupportedCulture> All { get; } =
    [
        new("en-US", "Language.English"),
        new("ko-KR", "Language.Korean")
    ];

    public static CultureInfo Default => CultureInfo.GetCultureInfo(DefaultName);

    public static bool TryResolve(string? name, out CultureInfo culture)
    {
        SupportedCulture? supported = All.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        culture = supported is null ? Default : CultureInfo.GetCultureInfo(supported.Name);
        return supported is not null;
    }
}

public enum UiCultureMode { System, Explicit }

public sealed record UserLanguagePreference(UiCultureMode Mode, string? CultureName)
{
    public static UserLanguagePreference UseSystem() => new(UiCultureMode.System, null);
    public static UserLanguagePreference Use(string cultureName) => new(UiCultureMode.Explicit, cultureName);
}

public static class UiCultureResolver
{
    public static CultureInfo Resolve(UserLanguagePreference preference, CultureInfo systemCulture)
    {
        ArgumentNullException.ThrowIfNull(preference);
        ArgumentNullException.ThrowIfNull(systemCulture);

        if (preference.Mode == UiCultureMode.Explicit)
            return SupportedUiCultures.TryResolve(preference.CultureName, out CultureInfo explicitCulture)
                ? explicitCulture : SupportedUiCultures.Default;

        if (SupportedUiCultures.TryResolve(systemCulture.Name, out CultureInfo exactCulture))
            return exactCulture;

        SupportedCulture? languageMatch = SupportedUiCultures.All.FirstOrDefault(item =>
            string.Equals(CultureInfo.GetCultureInfo(item.Name).TwoLetterISOLanguageName,
                systemCulture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase));

        return languageMatch is null
            ? SupportedUiCultures.Default
            : CultureInfo.GetCultureInfo(languageMatch.Name);
    }
}
