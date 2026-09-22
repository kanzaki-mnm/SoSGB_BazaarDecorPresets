namespace BazaarDecorPresets;

public static class PresetSummary
{
    public static string Describe(Preset preset, bool japanese)
    {
        if (preset == null) throw new ArgumentNullException(nameof(preset));
        var counts = new Dictionary<string, int>();
        foreach (var slot in preset.Slots)
        {
            counts.TryGetValue(slot.Category, out int count);
            counts[slot.Category] = count + 1;
        }
        string Label(string category) => japanese ? category switch
        {
            "Tent" => "テント",
            "Shelf" => "棚",
            "OrnamentS" => "小物",
            "OrnamentL" => "大物",
            "OrnamentSp" => "特殊",
            _ => category
        } : category;
        var parts = PresetStorage.Categories.Where(counts.ContainsKey)
            .Select(category => japanese ? $"{Label(category)} {counts[category]}" : $"{Label(category)} {counts[category]}");
        string layout = string.Join(japanese ? " / " : ", ", parts);
        return japanese
            ? $"「{preset.Name}」\n{layout}\n確認のみです。配置は変更しません。"
            : $"{preset.Name}\n{layout}\nPreview only. Your layout was not changed.";
    }
}
