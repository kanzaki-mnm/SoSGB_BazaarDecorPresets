namespace BazaarDecorPresets;

internal static class UiTextIds
{
    internal const uint MenuSave = 0xBD710001;
    internal const uint MenuInspect = 0xBD710002;
    internal const uint FooterPresets = 0xBD710004;
    internal const uint FooterDelete = 0xBD710005;
    internal const uint Slot = 0xBD710020;
    internal const uint Notice = 0xBD710040;
    internal const uint DeleteConfirm = 0xBD710041;
    internal const uint StockCancel = 1010;
    // Stock KeyButtonGuideText entry for B / Cancel.  This is distinct from
    // the DialogChoiceText ID above despite having the same visible wording.
    internal const uint StockCancelFooter = 1200;
    // Stock KeyButtonGuideText entry for A / Confirm. Text IDs are scoped to
    // their localization table, so this is separate from choice ID 1000.
    internal const uint StockConfirmFooter = 1000;
    internal const uint StockYes = 1000;
    // A stock keyboard prompt, replaced only while our request owns the callback.
    internal const uint NameInputTextId = 101031;
    // Stock confirmation text used by the BuyPetAnimal keyboard flow.
    internal const uint NameConfirmTextId = 101045;
}
