using System;
using System.Collections.Generic;

[Serializable]
public class ConsumableItemData
{
    public string itemID;
    public int quantity;

    public ConsumableItemData(string id, int qty)
    {
        itemID = id;
        quantity = qty;
    }
}

[Serializable]
public class PermanentItemData
{
    public string itemID;
    public bool isOwned;

    public PermanentItemData(string id, bool owned)
    {
        itemID = id;
        isOwned = owned;
    }
}

[Serializable]
public class SaveData
{
    public int currency;
    public List<ConsumableItemData> consumables;
    public List<PermanentItemData> permanentItems;

    // 17 Nov 2026 (Mr.k approved, Task 8 save change a + c). The Memo Book itself is a normal
    // permanent item per map (b), so it lives in permanentItems.
    /// <summary>(a) Maps whose free Memo Book the Staff already gave to this save file (map IDs).</summary>
    public List<string> memoBookClaimedMaps;
    /// <summary>(c) Map ID while a match with your Memo Book is running ("" = none). Still set at the next start = you quit mid-match: the book is destroyed.</summary>
    public string memoBookInMatchMap;

    public SaveData()
    {
        currency = 0;
        consumables = new List<ConsumableItemData>();
        permanentItems = new List<PermanentItemData>();
        memoBookClaimedMaps = new List<string>();
        memoBookInMatchMap = "";
    }
}