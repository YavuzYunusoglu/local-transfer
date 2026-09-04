using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace LocalTransfer;

internal static class Localizer
{
    private static readonly Lazy<LocaleCatalog> Catalog = new(LoadCatalog);
    private static readonly Lazy<string> RawCatalog = new(ReadCatalogJson);

    public static IReadOnlyList<LanguageOption> Languages => Catalog.Value.Languages;
    public static string ClientCatalogJson => RawCatalog.Value;

    public static string ResolveLanguage(string? requested)
    {
        var value = string.IsNullOrWhiteSpace(requested)
            ? CultureInfo.CurrentUICulture.Name
            : requested.Trim();

        var exact = Languages.FirstOrDefault(language =>
            string.Equals(language.Code, value, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact.Code;

        if (value.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase))
            return "zh-TW";
        if (value.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return "zh-CN";
        if (value.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
            return "pt-BR";

        var neutral = value.Split('-', '_')[0];
        return Languages.FirstOrDefault(language =>
            string.Equals(language.Code.Split('-')[0], neutral, StringComparison.OrdinalIgnoreCase))?.Code ?? "en";
    }

    public static bool IsRightToLeft(string languageCode) =>
        Languages.FirstOrDefault(language => string.Equals(language.Code, languageCode, StringComparison.OrdinalIgnoreCase))?.Rtl == true;

    public static bool TryFindEnglishKey(string text, out string key)
    {
        foreach (var pair in Catalog.Value.Translations["en"])
        {
            if (string.Equals(pair.Value, text, StringComparison.Ordinal))
            {
                key = pair.Key;
                return true;
            }
        }
        key = string.Empty;
        return false;
    }

    public static string Get(string languageCode, string key, params object[] arguments)
    {
        var resolved = ResolveLanguage(languageCode);
        var translations = Catalog.Value.Translations;
        var value = translations.TryGetValue(resolved, out var selected) && selected.TryGetValue(key, out var localized)
            ? localized
            : translations["en"].TryGetValue(key, out var fallback) ? fallback : key;
        return arguments.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, arguments);
    }

    private static LocaleCatalog LoadCatalog() =>
        JsonSerializer.Deserialize<LocaleCatalog>(RawCatalog.Value, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("The embedded localization catalog is invalid.");

    private static string ReadCatalogJson()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("Localization.locales.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The embedded localization catalog was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class LocaleCatalog
    {
        public List<LanguageOption> Languages { get; set; } = [];
        public Dictionary<string, Dictionary<string, string>> Translations { get; set; } = [];
    }
}

internal sealed class LanguageOption
{
    public string Code { get; set; } = "en";
    public string Name { get; set; } = "English";
    public bool Rtl { get; set; }
    public override string ToString() => Name;
}
