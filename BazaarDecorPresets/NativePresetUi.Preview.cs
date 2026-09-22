using BokuMono;
using BokuMono.Data;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

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
    private static UICustomPartsListItem[] previewRows;
    private static int previewSlot = int.MinValue;
    private static bool previewLoadRequested;
    private static Il2CppSystem.Action previewLoadedCallback;
    private static readonly BazaarCustomPageCategory[] FallbackPreviewOrder =
    {
        BazaarCustomPageCategory.Tent,
        BazaarCustomPageCategory.ShelfLeft,
        BazaarCustomPageCategory.ShelfCenter,
        BazaarCustomPageCategory.ShelfRight,
        BazaarCustomPageCategory.OrnamentSLeftOutSide,
        BazaarCustomPageCategory.OrnamentSLeftInSide,
        BazaarCustomPageCategory.OrnamentSRightInSide,
        BazaarCustomPageCategory.OrnamentSRightOutSide,
        BazaarCustomPageCategory.OrnamentLLeft,
        BazaarCustomPageCategory.OrnamentLRightInside,
        BazaarCustomPageCategory.OrnamentLRightOutside
    };

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
        var focused = slotDialog.GetComponentsInChildren<UIDialogChoiceBar>(true)
            .FirstOrDefault(bar => bar != null && bar.gameObject.activeInHierarchy && bar.IsFocused);
        int selected = focused?.data?.id ?? -1;
        int key = mode == Mode.Save ? -1 : selected;
        if (key != previewSlot) RefreshObjectPreview(key);
    }

    private static void BuildObjectPreview(UISelectDialog dialog)
    {
        var prefabs = UnityEngine.Object.FindObjectOfType<UIPrefabsManager>();
        var pagePrefab = prefabs?.UIPagePrefabCache(UILoadKey.UIBazaarMaxPriceCustomLogPage);
        var sourceRows = pagePrefab?.GetComponentsInChildren<UICustomPartsListItem>(true);
        if (sourceRows == null || sourceRows.Length != FallbackPreviewOrder.Length)
        {
            EnsurePreviewTemplate();
            return;
        }
        var source = sourceRows[0].transform.parent;
        var dialogRect = dialog.GetComponent<RectTransform>();
        if (source == null || dialogRect?.parent == null) return;
        objectPreview = UnityEngine.Object.Instantiate(source.gameObject, dialogRect.parent, false);
        objectPreview.name = "BDP_PresetObjectPreview";
        var rect = objectPreview.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(PreviewOffsetX, 0f);
        rect.localScale = Vector3.one;
        rect.SetAsLastSibling();
        var group = objectPreview.GetComponent<CanvasGroup>() ?? objectPreview.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        shiftedDialog = dialogRect;
        originalDialogPosition = dialogRect.anchoredPosition;
        dialogRect.anchoredPosition = originalDialogPosition + new Vector2(SlotDialogOffsetX, 0f);
        RefreshObjectPreview(mode == Mode.Save ? -1 : 0);
    }

    private static void RefreshObjectPreview(int index)
    {
        if (objectPreview == null) return;
        previewSlot = index;
        previewRows ??= objectPreview.GetComponentsInChildren<UICustomPartsListItem>(true).ToArray();
        var prefabs = UnityEngine.Object.FindObjectOfType<UIPrefabsManager>();
        var recordPage = prefabs?.UIPagePrefabCache(UILoadKey.UIBazaarMaxPriceCustomLogPage)?.GetComponent<UIBazaarMaxPriceCustomLogPage>();
        var order = recordPage?.pageToCategoryList;
        var master = BokuMono.API.Bazaar.MDM?.ItemMaster;
        var preset = index >= 0 && index < SlotCount ? PresetStorage.At(file, index + 1) : null;
        Dictionary<string, uint> current = null;
        if (mode == Mode.Save)
        {
            current = new Dictionary<string, uint>();
            foreach (var slot in Snapshot()) current[slot.Category + ":" + slot.Index] = slot.ItemId;
        }
        ItemMasterData seed = null;
        var parts = BokuMono.API.Bazaar.MDM?.CustomPartsMaster;
        if (master != null && parts?.list != null)
            foreach (var part in parts.list)
                if (part != null && master.TryGetData(part.Id, out var item) && item != null) { seed = item; break; }
        for (int i = 0; i < previewRows.Length; i++)
        {
            var categoryPage = order != null && order.Count == previewRows.Length ? order[i] : FallbackPreviewOrder[i];
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
                if (seed != null) row.SetDisp(i, seed);
                row.partsName.text = "—";
                row.partsIcon.enabled = false;
            }
            row.gameObject.SetActive(true);
        }
    }

    private static void RemoveObjectPreview()
    {
        if (shiftedDialog != null) shiftedDialog.anchoredPosition = originalDialogPosition;
        shiftedDialog = null;
        if (objectPreview != null) UnityEngine.Object.Destroy(objectPreview);
        objectPreview = null;
        previewRows = null;
        previewSlot = int.MinValue;
        previewLoadRequested = false;
    }
}
