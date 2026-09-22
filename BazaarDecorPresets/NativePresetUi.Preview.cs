using BokuMono;
using BokuMono.Data;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarDecorPresets;

// Reuses the game's Bazaar record panel so its category symbols, item icons,
// layout and localized item names stay native to the active language.
internal static partial class NativePresetUi
{
    private const float PreviewOffsetX = 390f;
    private const float SlotDialogOffsetX = -420f;
    private static GameObject objectPreview;
    private static RectTransform shiftedDialog;
    private static Vector2 originalDialogPosition;
    private static RectTransform closingShiftedDialog;
    private static Vector2 closingDialogOriginalPosition;
    private static UICustomPartsListItem[] previewRows;
    private static BazaarCustomPageCategory[] previewOrder;
    private static int previewSlot = int.MinValue;
    private static bool previewLoadRequested;
    private static Il2CppSystem.Action previewLoadedCallback;

    private static void BeginObjectPreview()
    {
        previewSlot = int.MinValue;
        previewLoadRequested = false;
        EnsurePreviewTemplate();
    }

    private static void EnsurePreviewTemplate()
    {
        var prefabs = UnityEngine.Object.FindObjectOfType<UIPrefabsManager>();
        if (prefabs == null || previewLoadRequested) return;
        previewLoadRequested = true;
        previewLoadedCallback ??= DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)(() => previewLoadRequested = false));
        prefabs.Load(UILoadKey.UIBazaarMaxPriceCustomLogPage, previewLoadedCallback);
    }

    private static void UpdateObjectPreview()
    {
        if (!slotMenuOpen || slotDialog == null || !slotDialog.gameObject.activeInHierarchy) return;
        if (objectPreview == null)
        {
            BuildObjectPreview(slotDialog);
            return;
        }
        int selected = FocusedSlotIndex();
        int key = mode == Mode.Save ? -1 : selected;
        if (key != previewSlot) RefreshObjectPreview(key);
    }

    private static void BuildObjectPreview(UISelectDialog dialog)
    {
        var prefabs = UnityEngine.Object.FindObjectOfType<UIPrefabsManager>();
        var pagePrefab = prefabs?.UIPagePrefabCache(UILoadKey.UIBazaarMaxPriceCustomLogPage);
        if (pagePrefab == null)
        {
            EnsurePreviewTemplate();
            return;
        }
        var officialRows = pagePrefab.GetComponent<UIBazaarMaxPriceCustomLogPage>()?.bazaarCustomPartsIconList;
        if (officialRows == null || officialRows.Count == 0)
            throw new InvalidOperationException("Official preview rows are unavailable.");
        var sourceRows = officialRows.ToArray();
        if (sourceRows.Any(row => row == null) || sourceRows.Select(row => row.GetInstanceID()).Distinct().Count() != sourceRows.Length)
            throw new InvalidOperationException("Official preview rows are missing or duplicated.");
        var source = FindPreviewPanel(pagePrefab.transform, sourceRows);
        var dialogRect = dialog.GetComponent<RectTransform>();
        if (source == null || dialogRect?.parent == null) return;
        // Paths are derived from the official references for this exact clone,
        // not from names, a fixed hierarchy, or component enumeration order.
        var paths = sourceRows.Select(row => PreviewRowPath(source, row.transform)).ToArray();
        objectPreview = UnityEngine.Object.Instantiate(source.gameObject, dialogRect.parent, false);
        objectPreview.name = "BDP_PresetObjectPreview";
        var rect = objectPreview.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(PreviewOffsetX, 0f);
        rect.localScale = Vector3.one;
        rect.SetAsLastSibling();
        var group = objectPreview.GetComponent<CanvasGroup>() ?? objectPreview.AddComponent<CanvasGroup>();
        float visibleAlpha = group.alpha;
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        previewRows = paths.Select(path => ResolvePreviewRow(objectPreview.transform, path)).ToArray();
        if (objectPreview.GetComponentsInChildren<UICustomPartsListItem>(true).Length != previewRows.Length ||
            previewRows.Select(row => row.GetInstanceID()).Distinct().Count() != previewRows.Length)
            throw new InvalidOperationException("Cloned preview rows do not match the official list.");
        InitializePreviewRows();
        RefreshObjectPreview(mode == Mode.Save ? -1 : 0);
        shiftedDialog = dialogRect;
        originalDialogPosition = dialogRect.anchoredPosition;
        dialogRect.anchoredPosition = originalDialogPosition + new Vector2(SlotDialogOffsetX, 0f);
        group.alpha = visibleAlpha;
    }

    private static Transform FindPreviewPanel(Transform pageRoot, UICustomPartsListItem[] rows)
    {
        if (rows.Any(row => row.transform == pageRoot || !row.transform.IsChildOf(pageRoot)))
            throw new InvalidOperationException("Official preview row is outside its page.");

        Transform panel = null;
        // A wrapper may be inserted above a row. Identify the panel's role rather
        // than its name or distance from that row, without cloning the whole page.
        for (var candidate = rows[0].transform.parent; candidate != null && candidate != pageRoot; candidate = candidate.parent)
        {
            if (candidate.GetComponent<RectTransform>() == null ||
                candidate.GetComponent<Image>() == null ||
                candidate.GetComponent<VerticalLayoutGroup>() == null ||
                rows.Any(row => !row.transform.IsChildOf(candidate))) continue;
            if (panel != null) throw new InvalidOperationException("Official preview panel is ambiguous.");
            panel = candidate;
        }
        if (panel == null) throw new InvalidOperationException("Official preview panel is unavailable.");
        // Reject an enclosing panel with extra rows before instantiating any UI.
        var containedRows = panel.GetComponentsInChildren<UICustomPartsListItem>(true);
        var officialIds = new HashSet<int>(rows.Select(row => row.GetInstanceID()));
        if (containedRows.Length != rows.Length || containedRows.Any(row => !officialIds.Contains(row.GetInstanceID())))
            throw new InvalidOperationException("Official preview panel contains unexpected rows.");
        return panel;
    }

    private static int[] PreviewRowPath(Transform root, Transform row)
    {
        var path = new List<int>();
        while (row != root)
        {
            if (row == null) throw new InvalidOperationException("Official row is outside the cloned panel.");
            path.Add(row.GetSiblingIndex());
            row = row.parent;
        }
        path.Reverse();
        return path.ToArray();
    }

    private static UICustomPartsListItem ResolvePreviewRow(Transform root, int[] path)
    {
        foreach (int child in path) root = root.GetChild(child);
        var rows = root.GetComponents<UICustomPartsListItem>();
        if (rows.Length != 1) throw new InvalidOperationException("Ambiguous cloned preview row.");
        return rows[0];
    }

    private static void InitializePreviewRows()
    {
        var master = BokuMono.API.Bazaar.MDM?.ItemMaster;
        var parts = BokuMono.API.Bazaar.MDM?.CustomPartsMaster;
        ItemMasterData seed = null;
        if (master != null && parts?.list != null)
            foreach (var part in parts.list)
                if (part != null && master.TryGetData(part.Id, out var item) && item != null) { seed = item; break; }
        if (seed == null) throw new InvalidOperationException("No item is available to initialize the preview.");

        var orders = new List<BazaarCustomPageCategory[]>();
        foreach (var row in previewRows)
        {
            // Runtime observation showed that SetDisp populates the row's order.
            // Bootstrap at its first index while hidden, then validate before
            // using any other index or displaying preset contents.
            row.SetDisp(0, seed);
            orders.Add(row.pageToCategoryList?.ToArray());
        }
        previewOrder = PreviewOrderValidation.Validate(orders);
        var slots = new HashSet<(BazaarCustomItemData.PartsCategory, int)>();
        for (int i = 0; i < previewOrder.Length; i++)
        {
            var category = BazaarCustomItemData.ToPartsCategory(previewOrder[i]);
            int index = BazaarCustomItemData.ToPartsCategoryIndex(previewOrder[i]);
            if (!Enum.IsDefined(typeof(BazaarCustomPageCategory), previewOrder[i]) || index < 0 || !slots.Add((category, index)))
                throw new InvalidOperationException("Invalid or duplicated official preview slot.");
            // Initialize category symbols even for empty preset slots.
            previewRows[i].SetDisp(i, seed);
        }
    }

    private static void RefreshObjectPreview(int index)
    {
        if (objectPreview == null) return;
        previewSlot = index;
        var master = BokuMono.API.Bazaar.MDM?.ItemMaster;
        var preset = index >= 0 && index < SlotCount ? PresetStorage.At(file, index + 1) : null;
        Dictionary<string, uint> current = null;
        if (mode == Mode.Save)
        {
            current = new Dictionary<string, uint>();
            foreach (var slot in Snapshot()) current[slot.Category + ":" + slot.Index] = slot.ItemId;
        }
        for (int i = 0; i < previewRows.Length; i++)
        {
            var categoryPage = previewOrder[i];
            var category = BazaarCustomItemData.ToPartsCategory(categoryPage);
            int slotIndex = BazaarCustomItemData.ToPartsCategoryIndex(categoryPage);
            uint itemId = 0;
            if (current != null) current.TryGetValue(category + ":" + slotIndex, out itemId);
            else if (preset != null) itemId = preset.Slots.FirstOrDefault(slot => slot.Category == category.ToString() && slot.Index == slotIndex)?.ItemId ?? 0;
            var row = previewRows[i];
            if (itemId != 0 && master != null && master.TryGetData(itemId, out var item) && item != null)
            {
                row.SetDisp(i, item);
                row.partsIcon.enabled = true;
            }
            else
            {
                row.partsName.text = "—";
                row.partsIcon.enabled = false;
            }
            row.gameObject.SetActive(true);
        }
    }

    private static void RemoveObjectPreview(bool restoreDialogPosition = true)
    {
        if (restoreDialogPosition) RestoreClosedSlotDialogPosition();
        if (shiftedDialog != null)
        {
            if (restoreDialogPosition) shiftedDialog.anchoredPosition = originalDialogPosition;
            else
            {
                // The pooled dialog must stay left until its closing animation ends.
                closingShiftedDialog = shiftedDialog;
                closingDialogOriginalPosition = originalDialogPosition;
            }
        }
        shiftedDialog = null;
        if (objectPreview != null) UnityEngine.Object.Destroy(objectPreview);
        objectPreview = null;
        previewRows = null;
        previewOrder = null;
        previewSlot = int.MinValue;
        previewLoadRequested = false;
    }

    private static void RestoreClosedSlotDialogPosition()
    {
        if (closingShiftedDialog != null)
        {
            closingShiftedDialog.anchoredPosition = closingDialogOriginalPosition;
        }
        closingShiftedDialog = null;
    }
}
