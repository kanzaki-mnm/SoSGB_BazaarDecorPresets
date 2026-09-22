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

var rowOrders = new[] { new[] { 30, 10, 20 }, new[] { 30, 10, 20 }, new[] { 30, 10, 20 } };
var validatedOrder = PreviewOrderValidation.Validate(rowOrders);
Check(validatedOrder.SequenceEqual(new[] { 30, 10, 20 }), "preview follows official order without an eleven-row assumption");
rowOrders[0][0] = 99;
Check(validatedOrder[0] == 30, "preview order snapshot is independent of its source");
Reject(() => PreviewOrderValidation.Validate(rowOrders), "reject disagreeing row orders");
Reject(() => PreviewOrderValidation.Validate(new[] { new[] { 1, 1 }, new[] { 1, 1 } }), "reject duplicate preview categories");
Reject(() => PreviewOrderValidation.Validate(new[] { new[] { 1, 2 } }), "reject row/category count mismatch");
Reject(() => PreviewOrderValidation.Validate(new int[][] { new[] { 1, 2 }, null }), "reject uninitialized preview row");
Reject(() => PreviewOrderValidation.Validate(Array.Empty<int[]>()), "reject empty official preview list");
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
string path = Path.Combine(directory, PresetStorage.FileName);
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
Check(Path.GetFileName(path) == "BazaarDecorPresets.cfg" && !File.Exists(path + ".tmp"),
    "new cfg path saves JSON without leaving a temporary file");
string original = File.ReadAllText(path);
string originalBackup = File.ReadAllText(path + ".bak");
using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
{
    bool failed = false;
    try { PresetStorage.Save(path, file); }
    catch (IOException) { failed = true; }
    Check(failed, "locked destination rejects replacement");
}
Check(File.ReadAllText(path) == original && File.ReadAllText(path + ".bak") == originalBackup,
    "failed replacement preserves both current file and backup");
Check(!File.Exists(path + ".tmp"), "failed replacement cleans up its temporary file");
var cleanupError = new InvalidOperationException("injected cleanup failure");
Exception reported = null;
bool remainingCleanupRan = false;
CleanupActions.Run(() => throw cleanupError, error => reported = error);
CleanupActions.Run(() => remainingCleanupRan = true, _ => throw new Exception("unexpected report"));
Check(ReferenceEquals(reported, cleanupError) && remainingCleanupRan,
    "cleanup failure reports original error and permits subsequent cleanup");
CleanupActions.Run(() => throw cleanupError, _ => throw new IOException("injected logger failure"));
Check(true, "cleanup logging failure does not escape into the game");
bool inputOpen = true, inputFooter = true, returnToSlots = false;
bool continueSave = NameInputCompletion.Complete(false, ref inputOpen, ref inputFooter, ref returnToSlots);
Check(!continueSave && !inputOpen && !inputFooter && returnToSlots,
    "unsuccessful name completion skips save and queues return before clearing input ownership");
NameInputCompletion.Complete(false, ref inputOpen, ref inputFooter, ref returnToSlots);
Check(returnToSlots, "duplicate cancellation preserves pending slot return");
returnToSlots = false;
NameInputCompletion.Complete(false, ref inputOpen, ref inputFooter, ref returnToSlots);
Check(!returnToSlots, "cancellation after input cleanup does not reopen slots");
inputOpen = inputFooter = true;
continueSave = NameInputCompletion.Complete(true, ref inputOpen, ref inputFooter, ref returnToSlots);
Check(continueSave && !inputOpen && !inputFooter && !returnToSlots,
    "successful name completion continues save without scheduling cancellation");
NameInputCompletion.Complete(false, ref inputOpen, ref inputFooter, ref returnToSlots);
Check(!returnToSlots, "cancel notification after successful completion cannot queue a return");
var closeTracker = new DialogCloseTracker();
var callbacks = new UiCallbackGate();
long menuRequest = callbacks.Begin();
int menuActions = 0;
Action chooseMenu = () => { if (callbacks.TryConsume(menuRequest)) menuActions++; };
chooseMenu();
chooseMenu();
Check(menuActions == 1, "duplicate choices execute their side effects only once");
long oldList = callbacks.Begin();
long deleteRequest = callbacks.Begin();
Check(!callbacks.TryConsume(oldList) && callbacks.TryConsume(deleteRequest),
    "Y navigation retires list choices without consuming the new confirmation");
long oldName = callbacks.Begin();
callbacks.Invalidate();
long newName = callbacks.Begin();
Check(!callbacks.TryConsume(oldName) && callbacks.TryConsume(newName),
    "reopening within one editor session rejects the previous name result");
foreach (string first in new[] { "success", "cancel", "fallback" })
{
    long nameRequest = callbacks.Begin();
    var actions = new List<string>();
    void NameResult(string kind)
    {
        if (callbacks.TryConsume(nameRequest)) actions.Add(kind);
    }
    NameResult(first);
    NameResult("success");
    NameResult("cancel");
    NameResult("fallback");
    Check(actions.SequenceEqual(new[] { first }),
        "name completion/cancel/fallback accept only the first terminal notification: " + first);
}
long retiringRequest = callbacks.Begin();
callbacks.Invalidate();
Check(!callbacks.TryConsume(retiringRequest), "menu cleanup rejects callbacks even before another menu opens");
long reentrantRequest = callbacks.Begin();
int callbackWrites = 0;
void ReentrantResult()
{
    if (!callbacks.TryConsume(reentrantRequest)) return;
    callbackWrites++;
    ReentrantResult();
}
ReentrantResult();
Check(callbackWrites == 1, "request is consumed before synchronous reentrant side effects");
closeTracker.TryBegin(true, out int retiredClose);
long retiredNavigation = callbacks.Begin();
callbacks.Invalidate();
bool retiredNavigationRan = false;
if (closeTracker.Complete(retiredClose) && callbacks.TryConsume(retiredNavigation)) retiredNavigationRan = true;
Check(!closeTracker.IsClosing && !retiredNavigationRan,
    "retired close releases stock wait without navigating or reopening UI");
closeTracker.TryBegin(true, out int liveClose);
long liveNavigation = callbacks.Begin();
int navigations = 0;
void CompleteNavigation()
{
    if (!closeTracker.Complete(liveClose)) return;
    if (callbacks.TryConsume(liveNavigation)) navigations++;
}
CompleteNavigation();
CompleteNavigation();
Check(navigations == 1 && !closeTracker.IsClosing, "duplicate close completion navigates once");
closeTracker.TryBegin(true, out int replacedClose);
long replacedNavigation = callbacks.Begin();
long replacementDialog = callbacks.Begin();
bool replacedNavigationRan = false;
if (closeTracker.Complete(replacedClose) && callbacks.TryConsume(replacedNavigation)) replacedNavigationRan = true;
Check(!replacedNavigationRan && !closeTracker.IsClosing && callbacks.TryConsume(replacementDialog),
    "late close cannot consume the replacement dialog callback");
for (int frame = 0; frame < 120; frame++)
    if (closeTracker.TryBegin(false, out _)) throw new Exception("Closed an absent or foreign dialog");
Check(closeTracker.TryBegin(true, out int lateClose), "late owned dialog remains eligible after waiting; foreign UI is ignored");
Check(!closeTracker.TryBegin(true, out _), "closing dialog is not closed twice");
Check(closeTracker.Complete(lateClose) && !closeTracker.IsClosing, "asynchronous cleanup close completes");
closeTracker.TryBegin(true, out int normalClose);
// A fault invalidates navigation, but completion must still release close tracking.
bool navigationRan = false;
bool navigationGenerationMatches = false;
if (closeTracker.Complete(normalClose) && navigationGenerationMatches) navigationRan = true;
Check(!navigationRan && closeTracker.TryBegin(true, out int nextClose), "fault during normal close cancels navigation without blocking later cleanup");
closeTracker.Reset();
closeTracker.TryBegin(true, out int reopenedClose);
Check(!closeTracker.Complete(normalClose) && closeTracker.IsClosing, "old completion cannot unlock a reopened session");
Check(closeTracker.Complete(reopenedClose), "new session close completes independently");
var preparationError = new InvalidOperationException("injected delegate conversion failure");
bool submitted = false;
try
{
    if (closeTracker.TryPrepare<Action>(true, _ => throw preparationError, out var prepared)) submitted = true;
    throw new Exception("Expected preparation failure");
}
catch (InvalidOperationException ex)
{
    Check(ReferenceEquals(ex, preparationError) && !closeTracker.IsClosing && !submitted,
        "preparation failure preserves exception and releases wait before stock submission");
}
Check(closeTracker.TryPrepare<Action>(true, ticket => () => closeTracker.Complete(ticket), out var completePrepared),
    "cleanup may prepare a new close after conversion failure");
try { throw new IOException("injected stock close submission failure"); }
catch (IOException) { }
Check(closeTracker.IsClosing && !closeTracker.TryPrepare<Action>(true, _ => () => { }, out _),
    "failure after stock submission retains wait to prevent duplicate close");
completePrepared();
Check(!closeTracker.IsClosing, "prepared completion releases its own wait");
closeTracker.TryBegin(true, out int activeClose);
bool factoryRan = false;
Check(!closeTracker.TryPrepare<Action>(true, _ => { factoryRan = true; return () => { }; }, out _) && !factoryRan,
    "duplicate preparation does not run the delegate factory");
closeTracker.Complete(activeClose);
var transactionBefore = new List<Slot> { new("Tent", 0, 30), new("OrnamentS", 0, 10) };
var transactionPlan = new List<Slot> { new("Tent", 0, 31), new("OrnamentS", 0, 11) };
var editState = transactionBefore.ToList();
void WriteSlot(Slot slot) => editState[editState.FindIndex(x => x.Category == slot.Category && x.Index == slot.Index)] = slot;
var applied = LayoutTransaction.Apply(transactionBefore, transactionPlan, WriteSlot, () => editState.ToList());
Check(applied.Status == LayoutApplyStatus.Applied && editState.SequenceEqual(transactionPlan) && applied.ModelsToRefresh.Count == 2,
    "verified application requests changed models");
int writes = 0;
var unchanged = LayoutTransaction.Apply(editState.ToList(), transactionPlan, _ => writes++, () => editState.ToList());
Check(unchanged.Status == LayoutApplyStatus.Applied && writes == 0 && unchanged.ModelsToRefresh.Count == 0,
    "identical layout does not write or refresh models");
editState = transactionBefore.ToList();
var mutationError = new IOException("injected failure after mutation");
var restored = LayoutTransaction.Apply(transactionBefore, transactionPlan, slot =>
{
    WriteSlot(slot);
    if (slot.ItemId == 11) throw mutationError;
}, () => editState.ToList());
Check(restored.Status == LayoutApplyStatus.Restored && editState.SequenceEqual(transactionBefore) && ReferenceEquals(restored.Errors[0], mutationError),
    "partial application restores the immediate pre-load layout and retains original failure");
Check(restored.ModelsToRefresh.SequenceEqual(transactionBefore), "verified recovery refreshes all original models including failing setter");
editState = transactionBefore.ToList();
int recoveryAttempts = 0;
var uncertain = LayoutTransaction.Apply(transactionBefore, transactionPlan, slot =>
{
    if (slot.ItemId == 30 || slot.ItemId == 10) recoveryAttempts++;
    if (slot.ItemId == 30) throw new IOException("injected recovery failure");
    WriteSlot(slot);
    if (slot.ItemId == 11) throw mutationError;
}, () => editState.ToList());
Check(uncertain.Status == LayoutApplyStatus.RecoveryUnconfirmed && recoveryAttempts == 2 && editState[1].ItemId == 10,
    "recovery continues after a failed slot and reports remaining mismatch");
Check(uncertain.ModelsToRefresh.Count == 0, "unverified recovery never displays a claimed restored layout");
editState = transactionBefore.ToList();
var unreadable = LayoutTransaction.Apply(transactionBefore, transactionPlan, WriteSlot,
    () => throw new IOException("injected snapshot failure"));
Check(unreadable.Status == LayoutApplyStatus.RecoveryUnconfirmed && editState.SequenceEqual(transactionBefore),
    "unreadable verification is never reported as restored even after successful setters");
editState = transactionBefore.ToList();
var ignoredWrite = LayoutTransaction.Apply(transactionBefore, transactionPlan, _ => { }, () => editState.ToList());
Check(ignoredWrite.Status == LayoutApplyStatus.Restored, "silent rejected application is detected and original state verified");
editState = transactionBefore.ToList();
var recoveryThrowsAfterWrite = LayoutTransaction.Apply(transactionBefore, transactionPlan, slot =>
{
    WriteSlot(slot);
    if (slot.ItemId == 11 || slot.ItemId == 30) throw mutationError;
}, () => editState.ToList());
Check(recoveryThrowsAfterWrite.Status == LayoutApplyStatus.Restored && recoveryThrowsAfterWrite.Errors.Count == 2,
    "verified actual layout governs recovery even when a recovery setter throws after writing");
string translationDirectory = Path.Combine(Path.GetTempPath(), "BDPTranslations-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(translationDirectory);
try
{
    var catalog = new TranslationCatalog();
    var warnings = new List<string>();
    string englishPath = Path.Combine(translationDirectory, "en.json");
    string japanesePath = Path.Combine(translationDirectory, "ja.json");
    File.WriteAllText(englishPath, "{\"presets.guide\":\"External English\"}");
    File.WriteAllText(japanesePath, "{\"presets.guide\":\"日本語\"}");
    catalog.Load(translationDirectory, warnings.Add);
    Check(catalog.Get("ja", "presets.guide") == "日本語", "selected translation takes priority");
    Check(catalog.Get("fr", "presets.guide") == "External English", "missing language uses external English");
    Check(catalog.Get("ja", "presets.save.completed") == "Preset saved.", "missing keys use embedded English");
    foreach (string invalidValue in new[] { "null", "\"\"", "\"   \"" })
    {
        File.WriteAllText(japanesePath, "{\"presets.guide\":" + invalidValue + "}");
        catalog.Load(translationDirectory, warnings.Add);
        Check(catalog.Get("ja", "presets.guide") == "External English", "invalid selected value falls back: " + invalidValue);
    }
    File.WriteAllText(englishPath, "{\"presets.guide\":null}");
    catalog.Load(translationDirectory, warnings.Add);
    Check(catalog.Get("ja", "presets.guide") == "Presets", "null English value uses embedded English");
    File.WriteAllText(japanesePath, "broken JSON");
    File.WriteAllText(englishPath, "null");
    catalog.Load(translationDirectory, warnings.Add);
    Check(catalog.Get("ja", "presets.guide") == "Presets" && warnings.Count == 2, "corrupt files warn and use embedded English");
    catalog.Load(Path.Combine(translationDirectory, "missing"), warnings.Add);
    Check(catalog.Get("ja", "presets.guide") == "Presets", "missing directory uses embedded English");
    catalog.Load(translationDirectory, warnings.Add, _ => throw new UnauthorizedAccessException("test"));
    Check(catalog.Get("ja", "presets.guide") == "Presets", "directory access failure preserves embedded English");
    var canonical = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
        File.ReadAllText("BazaarDecorPresets/i18n/en.json"));
    Check(canonical.All(pair => !string.IsNullOrWhiteSpace(pair.Value) && catalog.Get("ja", pair.Key) == pair.Value),
        "embedded resource contains every current English key and value");
    Check(catalog.Get("ja", "unknown.key") == "unknown.key", "unknown developer key remains identifiable");
}
finally
{
    foreach (string translationFile in Directory.GetFiles(translationDirectory)) File.Delete(translationFile);
    Directory.Delete(translationDirectory);
}
Console.WriteLine($"{passed} checks passed.");
