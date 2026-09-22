using BazaarDecorPresets;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    passed++;
    Console.WriteLine("PASS: " + name);
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (Exception ex) when (ex is InvalidDataException || ex is InvalidOperationException) { Check(true, name); return; }
    throw new Exception("FAIL: expected rejection: " + name);
}

var current = new List<Slot> { new Slot("OrnamentS", 0, 10), new Slot("OrnamentS", 1, 20), new Slot("Tent", 0, 30) };
var choices = new Dictionary<(string, int), HashSet<uint>>
{
    [("OrnamentS", 0)] = new() { 0, 10, 20, 40 },
    [("OrnamentS", 1)] = new() { 0, 10, 20, 40 },
    [("Tent", 0)] = new() { 30 }
};
var available = new Dictionary<uint, int> { [10] = 1, [20] = 1, [30] = 1, [40] = 1 };
var baseline = new HashSet<uint>();
List<Slot> Plan(params Slot[] target) => LayoutPlanner.Plan(current, target, choices, available, baseline);
var swap = Plan(new Slot("OrnamentS", 0, 20), new Slot("OrnamentS", 1, 10));
Check(swap.Single(s => s.Category == "Tent").ItemId == 30 && swap.Single(s => s.Category == "OrnamentS" && s.Index == 0).ItemId == 20,
    "swap single owned copies; preserve untargeted slots");
Check(current[0].ItemId == 10, "planning does not mutate current layout");
Reject(() => Plan(new Slot("OrnamentS", 0, 20)), "reject over-allocation including untouched slots");
Reject(() => Plan(new Slot("OrnamentS", 2, 10)), "reject unavailable slot before mutation");
Reject(() => Plan(new Slot("OrnamentS", 0, 999)), "reject unavailable item");
Reject(() => Plan(new Slot("Tent", 0, 10)), "reject wrong slot category");
Reject(() => Plan(new Slot("OrnamentS", 0, 10), new Slot("OrnamentS", 0, 20)), "reject duplicate slots");
Check(Plan(new Slot("OrnamentS", 0, 0)).Single(s => s.Category == "OrnamentS" && s.Index == 0).ItemId == 0, "empty slots round trip when remove is available");
choices[("OrnamentS", 0)].Remove(0);
Reject(() => Plan(new Slot("OrnamentS", 0, 0)), "reject unsupported removal");
available[10] = 0;
Check(Plan(new Slot("OrnamentS", 0, 10)).Count == 3, "existing placed item remains valid when storage excludes equipped copies");
Reject(() => Plan(new Slot("OrnamentS", 1, 10)), "never sum owned and placed counts");
baseline.Add(40);
Check(Plan(new Slot("OrnamentS", 0, 40), new Slot("OrnamentS", 1, 40)).Count == 3, "base parts can repeat");

string directory = Path.Combine(Directory.GetCurrentDirectory(), "tests", "artifacts", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
string path = Path.Combine(directory, "presets.json");
var file = new PresetFile { Presets = new() { new Preset { UiSlotIndex = 1, Name = "料理用 🍳", Slots = current } } };
PresetStorage.Save(path, file);
var roundtrip = PresetStorage.Load(path);
Check(roundtrip.Presets[0].Name == "料理用 🍳" && roundtrip.Presets[0].Slots.SequenceEqual(current), "Unicode name and slot IDs survive JSON");
roundtrip.Presets[0].Name = "加工品用";
PresetStorage.Save(path, roundtrip);
Check(PresetStorage.Load(path + ".bak").Presets[0].Name == "料理用 🍳", "atomic replacement preserves previous file as backup");
var bad = roundtrip.Copy();
bad.SchemaVersion = 9;
Reject(() => PresetStorage.Save(path, bad), "reject unsupported schema");
Check(PresetStorage.Load(path).Presets[0].Name == "加工品用", "failed validation preserves saved file");
bad = roundtrip.Copy();
bad.Presets.Add(bad.Presets[0].Copy());
Reject(() => PresetStorage.Validate(bad), "reject duplicate preset slots");
bad.Presets.Clear();
Check(roundtrip.Presets.Count == 1, "editing a copied file does not mutate saved presets");
File.WriteAllText(Path.Combine(directory, "null.json"), "{\"SchemaVersion\":1,\"Presets\":null}");
Reject(() => PresetStorage.Load(Path.Combine(directory, "null.json")), "reject malformed null preset list");
var six = new PresetFile();
for (int i = 1; i <= 6; i++) PresetStorage.Put(six, i, new Preset { Name = "同名", Slots = current });
Check(six.Presets.Count == 6 && PresetStorage.At(six, 4).UiSlotIndex == 4, "six fixed slots allow repeated display names");
PresetStorage.Put(six, 4, new Preset { Name = "上書き", Slots = current });
Check(six.Presets.Count == 6 && PresetStorage.At(six, 4).Name == "上書き", "saving a slot replaces only that slot");
PresetStorage.Remove(six, 4);
Check(six.Presets.Count == 5 && PresetStorage.At(six, 4) == null, "removing a slot leaves other slots intact");
var legacy = new PresetFile { SchemaVersion = 1, Presets = new() { new Preset { Name = "旧形式", Slots = current } } };
string legacyPath = Path.Combine(directory, "legacy.json");
File.WriteAllText(legacyPath, System.Text.Json.JsonSerializer.Serialize(legacy));
Check(PresetStorage.Load(legacyPath).SchemaVersion == 2 && PresetStorage.Load(legacyPath).Presets[0].UiSlotIndex == 1,
    "legacy file migrates in memory to first fixed slot");
Console.WriteLine($"{passed} checks passed.");
