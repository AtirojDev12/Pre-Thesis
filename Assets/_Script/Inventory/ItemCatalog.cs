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

    /// <summary>
    /// 24 Oct (Mr.k): the map of this place. MATCH ONLY: not sold, never saved,
    /// 1 per player from the map box (MapPickup). Hold the read key to read it.
    /// The model is Assets/Prefab/Map.prefab (designer's), used inside WorldMap.
    /// </summary>
    public const string Map = "map";
    public const string WorldMapPath = "Items/WorldMap";

    /// <summary>
    /// 31 Oct (Mr.k): the Missing Parts ghost's body parts. MATCH ONLY, found in the map,
    /// given back by holding E on the ghost (MissingPartsGhost). Never sold, never saved.
    /// </summary>
    public const string GhostLeftArm = "ghost_left_arm";
    public const string GhostRightArm = "ghost_right_arm";
    public const string GhostHead = "ghost_head";
    public const string GhostUpperBody = "ghost_upper_body";

    /// <summary>
    /// 5 Nov (Mr.k): the WORK SHEET from the lobby Staff NPC. Takes a hotbar slot.
    /// Match only: carried from the lobby hotbar into ONE match, then gone. Never saved.
    /// Press the read key to open it (WorksheetReader). 5 Nov: a real object: Q drops it,
    /// anyone can pick it up. Model: Assets/Prefab/WorkSheet.prefab inside WorldWorksheet.
    /// </summary>
    public const string Worksheet = "worksheet";
    public const string WorldWorksheetPath = "Items/WorldWorksheet";

    // ---- 17 Nov 2026 (Mr.k): Phase 4 (Task 6 + 7) ----------------------------------------
    /// <summary>Free walkie (price 0, one copy, lost on death). Runs on a battery (GearTuning).</summary>
    public const string WalkieFree = "walkie_talkie_free";
    /// <summary>Paid syringe: heals part of the missing health, or gets a downed friend up.</summary>
    public const string Syringe = "syringe";
    /// <summary>Free syringe: carry 1, flat heal.</summary>
    public const string SyringeFree = "syringe_free";
    /// <summary>The toilet rule's black box. A HOTBAR token: the box itself is a scene object (BlackBoxCarry).</summary>
    public const string BlackBox = "black_box";
    /// <summary>The displaced Ghost Flavor. A HOTBAR token: the bottle itself is a scene object (GhostFavorRecovery).</summary>
    public const string GhostFlavor = "ghost_flavor";
    /// <summary>17 Nov (Task 5): the ghost-room film reel. A HOTBAR token: the reel itself is a scene object (CarryProp).</summary>
    public const string FilmReelGhost = "film_reel_ghost";

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

        // ---- Match only (24 Oct) ----
        /// <summary>Exists only during a match (the Map): not in the shop, never written to the save.</summary>
        public bool matchOnly;
        /// <summary>5 Nov: a match-only item that a player may carry from the LOBBY hotbar into the next match (the work sheet).</summary>
        public bool carryFromLobby;

        // ---- Stacking (5 Oct) ----
        /// <summary>How many fit in ONE hotbar slot. 1 = no stacking. Also the most you can own.</summary>
        public int maxStack = 1;
        public bool Stackable => maxStack > 1;
        /// <summary>Not permanent = a consumable: the save keeps a quantity (ConsumableItemData). Match-only items are neither.</summary>
        public bool Consumable => !permanent && !matchOnly;
        // ---- 17 Nov (Phase 4) ----
        /// <summary>The model in your hand (first person) and in other players' views. Empty = the item's default.</summary>
        public string heldPrefabPath;
        /// <summary>A radio that runs on a battery (InventorySlot.charge). The free walkie.</summary>
        public bool usesWalkieBattery;
        /// <summary>A syringe: hold the use key to heal, or use it on a downed friend.</summary>
        public bool isSyringe;
        /// <summary>Heals this part of the MISSING health (0.75 = 75%).</summary>
        public float healPartOfMissing;
        /// <summary>Heals this much on top (flat).</summary>
        public float healFlat;
        /// <summary>
        /// Only a hotbar place-holder for a scene object that is carried (black box, ghost flavor).
        /// Cannot be thrown or dropped with Q; it leaves the hotbar when the object goes back.
        /// </summary>
        public bool carryToken;
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
            heldPrefabPath = "Items/WalkieTalkie",
            description = "Talk to every teammate who carries a switched-on walkie, at any distance. Lost if you die.",
        },
        // 17 Nov (Mr.k): the free walkie. Name, price, text and battery in GearTuning.
        new ItemInfo
        {
            id = WalkieFree, displayName = "Free Walkie-Talkie", isRadio = true, usesWalkieBattery = true,
            price = 0, permanent = true, inShop = true,
            throwable = true,
            worldPrefabPath = "Items/WorldWalkieTalkieFree",
            heldPrefabPath = "Items/WalkieTalkieFree",
        },
        // 5 Oct (Mr.k): flashlights + battery. Name, price, text and all numbers
        // are in the designer asset FlashlightTuning (Tools > Pre-Thesis > Flashlight Settings).
        new ItemInfo
        {
            id = FlashlightBasic, isFlashlight = true,
            permanent = true, inShop = true, throwable = true,
            worldPrefabPath = "Items/WorldFlashlightFree", // 17 Nov: its own model (falls back to the paid one if missing)
            heldPrefabPath = "Items/FlashlightFree",
        },
        new ItemInfo
        {
            id = Flashlight, isFlashlight = true,
            permanent = true, inShop = true, throwable = true,
            worldPrefabPath = WorldFlashlightPath,
            heldPrefabPath = "Items/Flashlight",
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
        // 24 Oct (Mr.k): the map. Taken from the map box in the match, gone after it.
        new ItemInfo
        {
            id = Map, displayName = "Map", matchOnly = true,
            permanent = false, inShop = false, throwable = true,
            worldPrefabPath = WorldMapPath,
            description = "Map of this place. Hold the read key to look at it. Only for this match.",
        },
        // 5 Nov (Mr.k): the work sheet from the lobby Staff NPC (free, match only, carried in once).
        new ItemInfo
        {
            id = Worksheet, displayName = "Work Sheet", matchOnly = true, carryFromLobby = true,
            permanent = false, inShop = false, throwable = true,
            worldPrefabPath = WorldWorksheetPath,
            description = "Your job list from the Staff. Press the read key to open it. Q = drop it. Carried into ONE match, then gone.",
        },
        // 17 Nov (Mr.k): syringes. Name, price, carry and heal in GearTuning.
        new ItemInfo
        {
            id = Syringe, isSyringe = true,
            permanent = false, inShop = true, throwable = true, maxStack = 3,
            worldPrefabPath = "Items/WorldSyringe",
            heldPrefabPath = "Items/Syringe",
        },
        new ItemInfo
        {
            id = SyringeFree, isSyringe = true,
            permanent = false, inShop = true, throwable = true, maxStack = 1,
            worldPrefabPath = "Items/WorldSyringeFree",
            heldPrefabPath = "Items/SyringeFree",
        },
        // 17 Nov (Mr.k): hotbar tokens for carried scene objects (Task 6: black box + ghost flavor = hotbar).
        new ItemInfo
        {
            id = BlackBox, displayName = "Black Box", matchOnly = true, carryToken = true,
            permanent = false, inShop = false, throwable = false,
            description = "Ghost food for the toilet. Hold it and press E on the drop spot. R (in hand) = put it back.",
        },
        new ItemInfo
        {
            id = GhostFlavor, displayName = "Ghost Flavor", matchOnly = true, carryToken = true,
            permanent = false, inShop = false, throwable = false,
            description = "The special ingredient. Bring it back to the popcorn station. R (in hand) = drop it.",
        },
        new ItemInfo
        {
            id = FilmReelGhost, displayName = "Ghost Film Reel", matchOnly = true, carryToken = true,
            permanent = false, inShop = false, throwable = false,
            description = "For the ghost room projector. Hold it and hold E at the projector. R (in hand) = put it back on its shelf.",
        },
        // 31 Oct (Mr.k): the Missing Parts ghost's parts. World prefabs made by the setup tool.
        GhostPartItem(GhostLeftArm, "Left Arm", "Items/WorldGhostPart_LeftArm"),
        GhostPartItem(GhostRightArm, "Right Arm", "Items/WorldGhostPart_RightArm"),
        GhostPartItem(GhostHead, "Head", "Items/WorldGhostPart_Head"),
        GhostPartItem(GhostUpperBody, "Upper Body", "Items/WorldGhostPart_UpperBody"),
    };

    private static ItemInfo GhostPartItem(string id, string name, string worldPath) => new ItemInfo
    {
        id = id, displayName = name, matchOnly = true,
        permanent = false, inShop = false, throwable = true,
        worldPrefabPath = worldPath,
        description = "A ghost's " + name.ToLowerInvariant() + ". Hold it, then hold E on the ghost to give it back.",
    };

    /// <summary>31 Oct: one of the Missing Parts ghost's body parts.</summary>
    public static bool IsGhostPart(string id) =>
        id == GhostLeftArm || id == GhostRightArm || id == GhostHead || id == GhostUpperBody;

    // ---- Designer values (FlashlightTuning) --------------------------------------

    private static int syncedChanges = int.MinValue;
    private static int syncedSanityChanges = int.MinValue;

    /// <summary>Copies the designer asset into the items. Cheap when nothing changed.</summary>
    private static int syncedGearChanges = int.MinValue;

    private static void SyncTuning()
    {
        SyncSanityItems();
        SyncGear();
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

    /// <summary>17 Nov: walkies and syringes come from GearTuning.</summary>
    private static void SyncGear()
    {
        if (syncedGearChanges == GearTuning.Changes) return;
        GearTuning g = GearTuning.Current; // may bump Changes once while loading
        syncedGearChanges = GearTuning.Changes;
        CopyWalkie(FindRaw(WalkieTalkie), g.paidWalkie);
        CopyWalkie(FindRaw(WalkieFree), g.freeWalkie);
        CopySyringe(FindRaw(Syringe), g.paidSyringe);
        CopySyringe(FindRaw(SyringeFree), g.freeSyringe);
    }

    private static void CopyWalkie(ItemInfo item, GearTuning.Walkie walkie)
    {
        if (item == null || walkie == null) return;
        item.displayName = walkie.displayName;
        item.description = walkie.description;
        item.price = Mathf.Max(0, walkie.price);
    }

    private static void CopySyringe(ItemInfo item, GearTuning.Syringe syringe)
    {
        if (item == null || syringe == null) return;
        item.displayName = syringe.displayName;
        item.description = syringe.description;
        item.price = Mathf.Max(0, syringe.price);
        item.maxStack = Mathf.Max(1, syringe.maxCarry);
        item.healPartOfMissing = Mathf.Clamp01(syringe.healPartOfMissing);
        item.healFlat = Mathf.Max(0f, syringe.healFlat);
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

    /// <summary>24 Oct: the map (match only).</summary>
    public static bool IsMap(string id) => id == Map;

    /// <summary>5 Nov: the work sheet (lobby Staff NPC).</summary>
    public static bool IsWorksheet(string id) => id == Worksheet;

    public static bool IsFlashlight(string id)
    {
        ItemInfo info = Find(id);
        return info != null && info.isFlashlight;
    }

    /// <summary>17 Nov: the free walkie (radio with a battery in InventorySlot.charge).</summary>
    public static bool UsesWalkieBattery(string id)
    {
        ItemInfo info = Find(id);
        return info != null && info.usesWalkieBattery;
    }

    /// <summary>17 Nov: the item keeps a battery in InventorySlot.charge (flashlights, the free walkie).</summary>
    public static bool UsesCharge(string id)
    {
        ItemInfo info = Find(id);
        return info != null && (info.isFlashlight || info.usesWalkieBattery);
    }

    public static bool IsSyringe(string id)
    {
        ItemInfo info = Find(id);
        return info != null && info.isSyringe;
    }

    /// <summary>17 Nov: a hotbar place-holder for a carried scene object (black box, ghost flavor).</summary>
    public static bool IsCarryToken(string id)
    {
        ItemInfo info = Find(id);
        return info != null && info.carryToken;
    }

    /// <summary>
    /// 17 Nov: loads the item's model (held, else world). A missing new prefab falls back to
    /// <paramref name="fallbackPath"/>, so the game still works before the setup tool has run.
    /// </summary>
    public static GameObject LoadPrefab(string path, string fallbackPath)
    {
        GameObject prefab = string.IsNullOrEmpty(path) ? null : Resources.Load<GameObject>(path);
        if (prefab == null && !string.IsNullOrEmpty(fallbackPath)) prefab = Resources.Load<GameObject>(fallbackPath);
        return prefab;
    }

    /// <summary>17 Nov: the world (floor) prefab of an item, with the paid model as fallback for the new free models.</summary>
    public static GameObject LoadWorldPrefab(string id)
    {
        ItemInfo info = Find(id);
        if (info == null || string.IsNullOrEmpty(info.worldPrefabPath)) return null;
        string fallback = id == WalkieFree ? PlayerItemThrow.WorldPrefabPath : id == FlashlightBasic ? WorldFlashlightPath : null;
        return LoadPrefab(info.worldPrefabPath, fallback);
    }

    /// <summary>Has an on/off switch (InventorySlot.poweredOn): radios and flashlights.</summary>
    public static bool HasPowerSwitch(string id)
    {
        ItemInfo info = Find(id);
        return info != null && (info.isRadio || info.isFlashlight);
    }
}
