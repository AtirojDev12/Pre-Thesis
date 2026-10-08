using System.Collections.Generic;
using UnityEngine;

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
    /// <summary>5 Oct: free in the shop. Weak, short battery, mash Space to charge.</summary>
    public const string FlashlightBasic = "flashlight_basic";
    /// <summary>5 Oct: bought in the shop. Brighter, longer range, longer battery.</summary>
    public const string Flashlight = "flashlight";
    public const string WorldFlashlightPath = "Items/WorldFlashlight";
    /// <summary>5 Oct: consumable. Press R with the paid Flashlight in hand = full battery.</summary>
    public const string Battery = "battery";
    public const string WorldBatteryPath = "Items/WorldBattery";

    // 8 Oct: sanity items. Names, prices and text are in SanitySettings (designer asset).
    /// <summary>Consumable. Eat it (+sanity) or feed a friend.</summary>
    public const string Snack = "snack";
    /// <summary>Holy items (permanent): carrying one makes chanting faster.</summary>
    public const string HolyBook = "holy_book";
    public const string Amulet = "amulet";
    public const string HolyCross = "holy_cross";
    public const string WorldSnackPath = "Items/WorldSnack";
    public const string WorldHolyBookPath = "Items/WorldHolyBook";
    public const string WorldAmuletPath = "Items/WorldAmulet";
    public const string WorldHolyCrossPath = "Items/WorldHolyCross";

    public sealed class ItemInfo
    {
        public string id;
        public string displayName;
        /// <summary>Can be switched on/off and used as a radio.</summary>
        public bool isRadio;
        /// <summary>Shop price in currency. 0 = free to claim (if inShop).</summary>
        public int price;
        /// <summary>Listed in the lobby shop (5 Oct: a price of 0 now means FREE, not "not sold").</summary>
        public bool inShop;
        /// <summary>Permanent item: max 1 copy, kept between runs, LOST if you die in a match.</summary>
        public bool permanent;
        public bool throwable;
        public string worldPrefabPath;
        public string description;

        // ---- Flashlight (5 Oct). Values come from FlashlightTuning (designer asset). ----
        public bool isFlashlight;
        /// <summary>How far the beam reaches (metres).</summary>
        public float lightRange;
        /// <summary>Beam brightness (Unity light intensity).</summary>
        public float lightIntensity;
        /// <summary>Beam cone width (degrees).</summary>
        public float spotAngle;
        /// <summary>Seconds of light from a full battery.</summary>
        public float batterySeconds;
        /// <summary>Seconds of light added by ONE Space press. 0 = cannot be charged by hand.</summary>
        public float secondsPerCrank;
        /// <summary>Recharged by using a Battery item (press R). 5 Oct: the paid flashlight.</summary>
        public bool usesBatteries;

        // ---- Sanity (8 Oct) ----
        /// <summary>Holy Book / Amulet / Cross: carried = faster chanting.</summary>
        public bool isHolyItem;

        // ---- Stacking (5 Oct) ----
        /// <summary>How many fit in ONE hotbar slot. 1 = no stacking. Also the most you can own.</summary>
        public int maxStack = 1;
        public bool Stackable => maxStack > 1;
        /// <summary>Not permanent = a consumable: the save keeps a quantity (ConsumableItemData).</summary>
        public bool Consumable => !permanent;
    }

    // A List, not a Dictionary: a handful of entries, and it matches the
    // project rule for anything that may end up serialized.
    private static readonly List<ItemInfo> items = new List<ItemInfo>
    {
        // 1 Oct: no longer free. Bought in the lobby shop.
        new ItemInfo
        {
            id = WalkieTalkie, displayName = "Walkie-Talkie", isRadio = true,
            price = 100, permanent = true, inShop = true,
            throwable = true,
            worldPrefabPath = PlayerItemThrow.WorldPrefabPath,
            description = "Talk to every teammate who carries a switched-on walkie, at any distance. Lost if you die.",
        },
        // 5 Oct (Mr.k): flashlights + battery. Name, price, text and all numbers
        // are in the designer asset FlashlightTuning (Tools > Pre-Thesis > Flashlight Settings).
        new ItemInfo
        {
            id = FlashlightBasic, isFlashlight = true,
            permanent = true, inShop = true, throwable = true,
            worldPrefabPath = WorldFlashlightPath,
        },
        new ItemInfo
        {
            id = Flashlight, isFlashlight = true,
            permanent = true, inShop = true, throwable = true,
            worldPrefabPath = WorldFlashlightPath,
        },
        new ItemInfo
        {
            id = Battery,
            permanent = false, inShop = true, throwable = true,
            worldPrefabPath = WorldBatteryPath,
        },
        // 8 Oct (Mr.k): sanity items. Name, price, text in SanitySettings.
        new ItemInfo
        {
            id = Snack,
            permanent = false, inShop = true, throwable = true,
            worldPrefabPath = WorldSnackPath,
        },
        new ItemInfo
        {
            id = HolyBook, isHolyItem = true,
            permanent = true, inShop = true, throwable = true,
            worldPrefabPath = WorldHolyBookPath,
        },
        new ItemInfo
        {
            id = Amulet, isHolyItem = true,
            permanent = true, inShop = true, throwable = true,
            worldPrefabPath = WorldAmuletPath,
        },
        new ItemInfo
        {
            id = HolyCross, isHolyItem = true,
            permanent = true, inShop = true, throwable = true,
            worldPrefabPath = WorldHolyCrossPath,
        },
    };

    // ---- Designer values (FlashlightTuning) --------------------------------------

    private static int syncedChanges = int.MinValue;
    private static int syncedSanityChanges = int.MinValue;

    /// <summary>Copies the designer asset into the items. Cheap when nothing changed.</summary>
    private static void SyncTuning()
    {
        SyncSanityItems();
        if (syncedChanges == FlashlightTuning.Changes) return;
        FlashlightTuning t = FlashlightTuning.Current; // may bump Changes once while loading
        syncedChanges = FlashlightTuning.Changes;
        CopyTorch(FindRaw(FlashlightBasic), t.basic);
        CopyTorch(FindRaw(Flashlight), t.paid);
        ItemInfo battery = FindRaw(Battery);
        if (battery != null)
        {
            battery.displayName = t.battery.displayName;
            battery.description = t.battery.description;
            battery.price = Mathf.Max(0, t.battery.price);
            battery.maxStack = Mathf.Max(1, t.battery.maxCarry);
        }
    }

    /// <summary>8 Oct: names, prices and text of the sanity items come from SanitySettings.</summary>
    private static void SyncSanityItems()
    {
        if (syncedSanityChanges == SanitySettings.Changes) return;
        SanitySettings s = SanitySettings.Current; // may bump Changes once while loading
        syncedSanityChanges = SanitySettings.Changes;
        CopyShop(FindRaw(Snack), s.snack);
        ItemInfo snack = FindRaw(Snack);
        if (snack != null) snack.maxStack = Mathf.Max(1, s.snackMaxCarry);
        CopyShop(FindRaw(HolyBook), s.holyBook);
        CopyShop(FindRaw(Amulet), s.amulet);
        CopyShop(FindRaw(HolyCross), s.holyCross);
    }

    private static void CopyShop(ItemInfo item, SanitySettings.ShopItem shop)
    {
        if (item == null || shop == null) return;
        item.displayName = shop.displayName;
        item.description = shop.description;
        item.price = Mathf.Max(0, shop.price);
    }

    private static void CopyTorch(ItemInfo item, FlashlightTuning.Torch torch)
    {
        if (item == null || torch == null) return;
        item.displayName = torch.displayName;
        item.description = torch.description;
        item.price = Mathf.Max(0, torch.price);
        item.lightRange = torch.range;
        item.lightIntensity = torch.brightness;
        item.spotAngle = torch.coneAngle;
        item.batterySeconds = Mathf.Max(1f, torch.batterySeconds);
        item.secondsPerCrank = Mathf.Max(0f, torch.secondsPerSpacePress);
        item.usesBatteries = torch.usesBatteries;
    }

    private static ItemInfo FindRaw(string id)
    {
        for (int i = 0; i < items.Count; i++)
            if (items[i].id == id) return items[i];
        return null;
    }

    public static ItemInfo Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        SyncTuning();
        return FindRaw(id);
    }

    public static bool Exists(string id) => Find(id) != null;

    /// <summary>Every item, in shop order. Read only.</summary>
    public static IReadOnlyList<ItemInfo> All
    {
        get { SyncTuning(); return items; }
    }

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

    /// <summary>How many fit in one slot (1 for normal items).</summary>
    public static int MaxStack(string id)
    {
        ItemInfo info = Find(id);
        return info != null ? Mathf.Max(1, info.maxStack) : 1;
    }

    /// <summary>8 Oct: Holy Book / Amulet / Cross.</summary>
    public static bool IsHolyItem(string id)
    {
        ItemInfo info = Find(id);
        return info != null && info.isHolyItem;
    }

    public static bool IsFlashlight(string id)
    {
        ItemInfo info = Find(id);
        return info != null && info.isFlashlight;
    }

    /// <summary>Has an on/off switch (InventorySlot.poweredOn): radios and flashlights.</summary>
    public static bool HasPowerSwitch(string id)
    {
        ItemInfo info = Find(id);
        return info != null && (info.isRadio || info.isFlashlight);
    }
}
