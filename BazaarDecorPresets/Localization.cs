using System.Reflection;
using BokuMono;

namespace BazaarDecorPresets;

internal static class Localization
{
    private static readonly TranslationCatalog texts = new();
    private static Language language = Language.en;

    internal static bool IsJapanese => language == Language.ja;

    internal static void Load()
    {
        string directory = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "i18n");
        texts.Load(directory, message => Plugin.Logger.LogWarning(message));
    }
    internal static void SetLanguage(Language value) => language = value;

    internal static string Get(string key) => texts.Get(language.ToString(), key);
    internal static string Slot(int index, string name) => language == Language.ja ? $"{index}：{name}" : $"{index}: {name}";
}
