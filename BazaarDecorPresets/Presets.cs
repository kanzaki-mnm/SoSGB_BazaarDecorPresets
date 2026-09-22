using System.Text.Json;

namespace BazaarDecorPresets;

public sealed class Slot : IEquatable<Slot>
{
    public string Category { get; }
    public int Index { get; }
    public uint ItemId { get; }
    public Slot(string category, int index, uint itemId) { Category = category; Index = index; ItemId = itemId; }
    public bool Equals(Slot other) => other != null && Category == other.Category && Index == other.Index && ItemId == other.ItemId;
    public override bool Equals(object other) => other is Slot slot && Equals(slot);
    public override int GetHashCode() => HashCode.Combine(Category, Index, ItemId);
}

public sealed class Preset
{
    public int? UiSlotIndex { get; set; }
    public string Name { get; set; } = "";
    public List<Slot> Slots { get; set; } = new();
    public Preset Copy() => new() { UiSlotIndex = UiSlotIndex, Name = Name, Slots = Slots.ToList() };
}

public sealed class PresetFile
{
    public int SchemaVersion { get; set; } = 2;
    public List<Preset> Presets { get; set; } = new();
    public PresetFile Copy() => new() { Presets = Presets.Select(p => p.Copy()).ToList() };
}

public static class PresetStorage
{
    public const string FileName = "BazaarDecorPresets.cfg";
    public static readonly string[] Categories = { "Tent", "Shelf", "OrnamentS", "OrnamentL", "OrnamentSp" };
    public static void Validate(PresetFile file)
    {
        if (file == null || file.SchemaVersion < 1 || file.SchemaVersion > 2 || file.Presets == null || file.Presets.Count > 100)
            throw new InvalidDataException("Unsupported preset file (schema / preset count).");
        var slots = new HashSet<int>();
        foreach (var preset in file.Presets)
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 60)
                throw new InvalidDataException("Invalid preset name.");
            if (file.SchemaVersion >= 2 && (!preset.UiSlotIndex.HasValue || preset.UiSlotIndex < 1 || preset.UiSlotIndex > 6 || !slots.Add(preset.UiSlotIndex.Value)))
                throw new InvalidDataException("Invalid or duplicate preset slot.");
            ValidateSlots(preset.Slots);
        }
    }

    public static void ValidateSlots(IReadOnlyList<Slot> slots)
    {
        if (slots == null || slots.Count == 0 || slots.Count > 64) throw new InvalidDataException("Invalid slot count.");
        var keys = new HashSet<(string, int)>();
        foreach (var slot in slots)
            if (slot == null || !Categories.Contains(slot.Category) || slot.Index < 0 || slot.Index > 63 || !keys.Add((slot.Category, slot.Index)))
                throw new InvalidDataException("Invalid or duplicate slot.");
    }

    public static PresetFile Load(string path)
    {
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Preset file is too large.");
        var file = JsonSerializer.Deserialize<PresetFile>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty JSON.");
        Validate(file);
        if (file.SchemaVersion == 1)
        {
            if (file.Presets.Count > 6) throw new InvalidDataException("Legacy preset file has more than six presets.");
            for (int i = 0; i < file.Presets.Count; i++) file.Presets[i].UiSlotIndex = i + 1;
            file.SchemaVersion = 2;
        }
        return file;
    }

    public static void Save(string path, PresetFile file)
    {
        Validate(file);
        string temp = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        bool ownsTemp = false;
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                ownsTemp = true;
                JsonSerializer.Serialize(stream, file, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
        finally
        {
            // Cleanup must not hide the original save error.
            try { if (ownsTemp) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public static Preset At(PresetFile file, int slot) => file.Presets.FirstOrDefault(p => p.UiSlotIndex == slot);
    public static void Put(PresetFile file, int slot, Preset preset)
    {
        if (slot < 1 || slot > 6) throw new ArgumentOutOfRangeException(nameof(slot));
        file.SchemaVersion = 2;
        file.Presets.RemoveAll(p => p.UiSlotIndex == slot);
        preset.UiSlotIndex = slot;
        file.Presets.Add(preset);
    }
    public static void Remove(PresetFile file, int slot) => file.Presets.RemoveAll(p => p.UiSlotIndex == slot);
}

public static class LayoutPlanner
{
    // Keep newly unlocked slots; reject saved slots which do not exist in this save.
    public static List<Slot> Plan(IReadOnlyList<Slot> current, IReadOnlyList<Slot> saved,
        IReadOnlyDictionary<(string Category, int Index), HashSet<uint>> choices,
        IReadOnlyDictionary<uint, int> available, ISet<uint> baseParts)
    {
        PresetStorage.ValidateSlots(current);
        PresetStorage.ValidateSlots(saved);
        var result = current.ToDictionary(s => (s.Category, s.Index));
        foreach (var slot in saved)
        {
            var key = (slot.Category, slot.Index);
            if (!result.TryGetValue(key, out var old)) throw new InvalidOperationException($"Slot unavailable: {slot.Category} {slot.Index}");
            if (old.ItemId != slot.ItemId && (!choices.TryGetValue(key, out var ids) || !ids.Contains(slot.ItemId)))
                throw new InvalidOperationException($"Item unavailable: {slot.ItemId} ({slot.Category} {slot.Index})");
            result[key] = slot;
        }
        foreach (var group in result.Values.Where(s => s.ItemId != 0).GroupBy(s => s.ItemId))
        {
            if (baseParts.Contains(group.Key)) continue;
            // Never add storage count to placed count: storage may already include equipped items.
            int knownPlaced = current.Count(s => s.ItemId == group.Key);
            int count = available.TryGetValue(group.Key, out int owned) ? Math.Max(owned, knownPlaced) : knownPlaced;
            if (group.Count() > count) throw new InvalidOperationException($"Not enough items: {group.Key} (need {group.Count()}, known {count})");
        }
        return result.Values.OrderBy(s => Array.IndexOf(PresetStorage.Categories, s.Category)).ThenBy(s => s.Index).ToList();
    }
}
