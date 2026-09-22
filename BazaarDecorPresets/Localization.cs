using System.Reflection;
using System.Text.Json;
using BokuMono;

namespace BazaarDecorPresets;

internal static class Localization
{
    private static readonly Dictionary<string, Dictionary<string, string>> texts = new(StringComparer.OrdinalIgnoreCase);
    private static Language language = Language.en;

    internal static bool IsJapanese => language == Language.ja;

    internal static void Load()
    {
        texts.Clear();
        string directory = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "i18n");
        if (!Directory.Exists(directory))
        {
            Plugin.Logger.LogWarning($"BDP i18n folder not found: {directory}");
            return;
        }

        foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var file = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                if (file != null) texts[Path.GetFileNameWithoutExtension(path)] = file;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"BDP could not load translation file {Path.GetFileName(path)}: {ex.Message}");
            }
        }
    }

    internal static void SetLanguage(Language value) => language = value;

    internal static string Get(string key)
    {
        string selected = language.ToString();
        if (texts.TryGetValue(selected, out var translation) && translation.TryGetValue(key, out var text)) return text;
        if (texts.TryGetValue("en", out var english) && english.TryGetValue(key, out text)) return text;
        return key;
    }
    internal static string Slot(int index, string name) => language == Language.ja ? $"{index}：{name}" : $"{index}: {name}";
}
