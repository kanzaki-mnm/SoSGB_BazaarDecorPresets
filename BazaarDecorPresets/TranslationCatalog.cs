using System.Text.Json;

namespace BazaarDecorPresets;

internal sealed class TranslationCatalog
{
    private readonly Dictionary<string, string> embedded;
    private readonly Dictionary<string, Dictionary<string, string>> texts = new(StringComparer.OrdinalIgnoreCase);

    internal TranslationCatalog()
    {
        using var stream = typeof(TranslationCatalog).Assembly.GetManifestResourceStream("BazaarDecorPresets.i18n.en.json")
            ?? throw new InvalidDataException("Embedded English translations are missing.");
        embedded = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException("Embedded English translations are invalid.");
    }

    internal void Load(string directory, Action<string> warn,
        Func<string, IEnumerable<string>> enumerate = null)
    {
        texts.Clear();
        try
        {
            // Enumeration can fail during iteration as well as when it starts.
            foreach (string path in (enumerate ?? (d => Directory.EnumerateFiles(d, "*.json")))(directory))
            {
                try
                {
                    var file = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                    if (file == null) throw new InvalidDataException("Translation dictionary is null.");
                    texts[Path.GetFileNameWithoutExtension(path)] = file;
                }
                catch (Exception ex)
                {
                    warn($"BDP could not load translation file {Path.GetFileName(path)}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            warn($"BDP could not enumerate translations: {ex.Message}");
        }
    }

    internal string Get(string language, string key)
    {
        if (texts.TryGetValue(language, out var selected) && TryText(selected, key, out var text)) return text;
        if (texts.TryGetValue("en", out var english) && TryText(english, key, out text)) return text;
        return TryText(embedded, key, out text) ? text : key;
    }

    private static bool TryText(Dictionary<string, string> source, string key, out string text) =>
        source.TryGetValue(key, out text) && !string.IsNullOrWhiteSpace(text);
}
