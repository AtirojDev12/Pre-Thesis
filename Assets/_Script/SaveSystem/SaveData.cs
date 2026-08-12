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

    public SaveData()
    {
        currency = 0;
        consumables = new List<ConsumableItemData>();
        permanentItems = new List<PermanentItemData>();
    }
}