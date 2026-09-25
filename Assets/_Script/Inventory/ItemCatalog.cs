using System.Collections.Generic;

/// <summary>
/// Every item the hotbar can hold, by string ID.
///
/// A static list for now, so adding the Walkie-Talkie needs no assets. When the
/// lobby shop arrives this becomes ScriptableObject assets (icon, price, held
/// model), and only <see cref="Find"/> changes — every other script already
/// talks in item IDs, the same IDs the save file uses (ConsumableItemData /
/// PermanentItemData).
/// </summary>
public static class ItemCatalog
{
    public const string WalkieTalkie = "walkie_talkie";

    public sealed class ItemInfo
    {
        public string id;
        public string displayName;
        /// <summary>Can be switched on/off and used as a radio.</summary>
        public bool isRadio;
    }

    // A List, not a Dictionary: a handful of entries, and it matches the
    // project rule for anything that may end up serialized.
    private static readonly List<ItemInfo> items = new List<ItemInfo>
    {
        new ItemInfo { id = WalkieTalkie, displayName = "Walkie-Talkie", isRadio = true },
    };

    public static ItemInfo Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < items.Count; i++)
            if (items[i].id == id) return items[i];
        return null;
    }

    public static bool Exists(string id) => Find(id) != null;

    public static string DisplayName(string id)
    {
        ItemInfo info = Find(id);
        return info != null ? info.displayName : id;
    }

    public static bool IsRadio(string id)
    {
        ItemInfo info = Find(id);
        return info != null && info.isRadio;
    }
}
