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

        // ---- Flashlight (5 Oct). Edit these numbers to balance the two lights. ----
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
        // 5 Oct (Mr.k): free starter light. Short battery; mash Space to charge it.
        new ItemInfo
        {
            id = FlashlightBasic, displayName = "Basic Flashlight", isFlashlight = true,
            price = 0, permanent = true, inShop = true,
            throwable = true,
            worldPrefabPath = WorldFlashlightPath,
            lightRange = 9f, lightIntensity = 3f, spotAngle = 40f,
            batterySeconds = 45f, secondsPerCrank = 1.5f,
            description = "Free. Weak beam, short battery. Mash SPACE to charge it. Lost if you die (claim a new one).",
        },
        // 5 Oct (Mr.k): the paid version of the same light.
        new ItemInfo
        {
            id = Flashlight, displayName = "Flashlight", isFlashlight = true,
            price = 150, permanent = true, inShop = true,
            throwable = true,
            worldPrefabPath = WorldFlashlightPath,
            lightRange = 20f, lightIntensity = 8f, spotAngle = 50f,
            batterySeconds = 240f, secondsPerCrank = 0f, usesBatteries = true,
            description = "Bright, long beam, big battery. Cannot be charged by hand: press R to put in a new Battery. Lost if you die.",
        },
        // 5 Oct (Mr.k): refill for the paid Flashlight. Stacks in one slot.
        new ItemInfo
        {
            id = Battery, displayName = "Battery",
            price = 25, permanent = false, inShop = true, maxStack = 3,
            throwable = true,
            worldPrefabPath = WorldBatteryPath,
            description = "Flashlight in hand + R = full battery. Max 3, stack in one slot. Unused ones are kept if you survive.",
        },
    };

    public static ItemInfo Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < items.Count; i++)
            if (items[i].id == id) return items[i];
        return null;
    }

    public static bool Exists(string id) => Find(id) != null;

    /// <summary>Every item, in shop order. Read only.</summary>
    public static IReadOnlyList<ItemInfo> All => items;

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
