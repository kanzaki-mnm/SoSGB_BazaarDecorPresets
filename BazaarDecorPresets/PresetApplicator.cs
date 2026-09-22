using BokuMono;
using UnityEngine;

namespace BazaarDecorPresets;

// Applies only to the game's unconfirmed editor state.  The caller owns the
// final SaveAndCloseBazaarCustom / CloseBazaarCustom choice.
internal static class PresetApplicator
{
    internal static List<Slot> Plan(UIBazaarCustomPage page, Preset preset, IReadOnlyList<Slot> current)
    {
        if (page?.cacheCustomPartsListDic == null) throw new InvalidOperationException("Choice list is unavailable.");
        var inventory = InventoryManager.Instance;
        var storage = inventory?.BazaarCustomPartsStorage ?? throw new InvalidOperationException("Parts storage is unavailable.");
        var choices = new Dictionary<(string Category, int Index), HashSet<uint>>();

        foreach (var currentSlot in current)
        {
            if (!TryCategory(currentSlot.Category, out var partCategory))
                throw new InvalidOperationException("Invalid current category: " + currentSlot.Category);
            var pageCategory = BazaarCustomItemData.ToPageCategory(partCategory, currentSlot.Index);
            if (!page.cacheCustomPartsListDic.TryGetValue(pageCategory, out var list) || list == null)
                throw new InvalidOperationException($"Choice list unavailable: {currentSlot.Category} {currentSlot.Index}");

            var ids = new HashSet<uint>();
            for (int i = 0; i < list.Count; i++)
            {
                var choice = list[i];
                if (choice?.IsUiRemove == true) ids.Add(0);
                else if (choice?.PartsData != null) ids.Add(choice.PartsData.Id);
            }
            choices[(currentSlot.Category, currentSlot.Index)] = ids;
        }

        var available = new Dictionary<uint, int>();
        foreach (var item in preset.Slots.Where(slot => slot.ItemId != 0).Select(slot => slot.ItemId).Distinct())
            available[item] = storage.GetStack(item);

        return LayoutPlanner.Plan(current, preset.Slots, choices, available, new HashSet<uint>());
    }

    internal static void Apply(BazaarManager editor, IReadOnlyList<Slot> before, IReadOnlyList<Slot> plan, Func<List<Slot>> snapshot)
    {
        try
        {
            var changed = new List<Slot>();
            foreach (var slot in plan)
            {
                var old = before.First(item => item.Category == slot.Category && item.Index == slot.Index);
                if (old.ItemId == slot.ItemId) continue;
                if (!TryCategory(slot.Category, out var category)) throw new InvalidOperationException("Invalid plan category: " + slot.Category);
                editor.SetCustomParts(slot.ItemId, category, slot.Index);
                changed.Add(slot);
            }

            if (!SameLayout(plan, snapshot())) throw new InvalidOperationException("Editor did not accept the planned layout.");
            RefreshChangedModels(changed);
        }
        catch
        {
            Restore(editor, before);
            throw;
        }
    }

    private static void Restore(BazaarManager editor, IReadOnlyList<Slot> before)
    {
        foreach (var slot in before)
        {
            if (!TryCategory(slot.Category, out var category)) continue;
            try { editor.SetCustomParts(slot.ItemId, category, slot.Index); }
            catch (Exception ex) { Plugin.Logger.LogError("BDP PresetRollbackError " + ex); }
        }
    }

    private static void RefreshChangedModels(IReadOnlyList<Slot> changed)
    {
        if (changed.Count == 0) return;
        var shop = UnityEngine.Object.FindObjectOfType<BazaarMyShop>();
        if (shop == null)
        {
            Plugin.Logger.LogWarning("BDP PresetModelRefreshSkipped shop-unavailable");
            return;
        }

        foreach (var slot in changed)
        {
            try
            {
                if (!TryCategory(slot.Category, out var category)) continue;
                // This is the same model-replacement route the game uses after
                // an accepted part change.  It only touches BazaarMyShop visuals.
                shop.RefreshBazaarPartsModel(slot.ItemId, category, slot.Index, true, false);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"BDP PresetModelRefreshFailed {slot.Category}:{slot.Index} {ex.Message}");
            }
        }
    }

    private static bool SameLayout(IReadOnlyList<Slot> left, IReadOnlyList<Slot> right) =>
        left.Count == right.Count && left.All(item => right.Any(other => item.Equals(other)));

    private static bool TryCategory(string value, out BazaarCustomItemData.PartsCategory category)
    {
        category = value switch
        {
            "Tent" => BazaarCustomItemData.PartsCategory.Tent,
            "Shelf" => BazaarCustomItemData.PartsCategory.Shelf,
            "OrnamentS" => BazaarCustomItemData.PartsCategory.OrnamentS,
            "OrnamentL" => BazaarCustomItemData.PartsCategory.OrnamentL,
            "OrnamentSp" => BazaarCustomItemData.PartsCategory.OrnamentSp,
            _ => BazaarCustomItemData.PartsCategory.None
        };
        return category != BazaarCustomItemData.PartsCategory.None;
    }
}
