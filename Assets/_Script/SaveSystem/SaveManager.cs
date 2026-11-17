using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public static class SaveManager
{
    private const string FILE_NAME = "playerdata.sav";

    public static SaveData Current { get; private set; }

    // กุญแจแบ่งเป็นชิ้น ๆ แล้วค่อยประกอบตอนรัน จะได้ไม่มี String เดี่ยว ๆ ให้ grep เจอง่าย ๆ
    private static readonly byte[] keyPart1 = { 0x4A, 0x2F, 0x8C, 0x11, 0x9D, 0x3E, 0x77, 0x60 };
    private static readonly byte[] keyPart2 = { 0xB4, 0x05, 0xE2, 0x9A, 0x1C, 0x88, 0x4F, 0x33 };
    private static readonly byte[] keyPart3 = { 0x6D, 0xF1, 0x2A, 0x00, 0x7C, 0x9B, 0x55, 0xE8 };
    private static readonly byte[] keyPart4 = { 0x91, 0x3D, 0xC6, 0x4E, 0x0F, 0xA7, 0x28, 0xBB };

    private static byte[] GetKey()
    {
        byte[] key = new byte[32];
        Buffer.BlockCopy(keyPart1, 0, key, 0, 8);
        Buffer.BlockCopy(keyPart2, 0, key, 8, 8);
        Buffer.BlockCopy(keyPart3, 0, key, 16, 8);
        Buffer.BlockCopy(keyPart4, 0, key, 24, 8);
        return key;
    }

    private static string SavePath => Path.Combine(Application.persistentDataPath, FILE_NAME);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        Current = Load();
        Debug.Log("Save loaded. Currency = " + Current.currency);
        ApplyMemoBookQuitRule(); // 17 Nov
    }

    // ---- Memo Book (17 Nov 2026, Mr.k, Task 8) ------------------------------------

    /// <summary>Has the Staff already given this save file its free Memo Book for this map?</summary>
    public static bool HasClaimedMemoBook(string mapId)
    {
        if (Current == null) return false;
        if (Current.memoBookClaimedMaps == null) Current.memoBookClaimedMaps = new List<string>();
        return Current.memoBookClaimedMaps.Contains(mapId);
    }

    /// <summary>The Staff gave the free Memo Book: own it and remember the claim. Saves.</summary>
    public static void ClaimMemoBook(string mapId, string itemId)
    {
        if (Current == null) return;
        if (Current.memoBookClaimedMaps == null) Current.memoBookClaimedMaps = new List<string>();
        if (!Current.memoBookClaimedMaps.Contains(mapId)) Current.memoBookClaimedMaps.Add(mapId);
        AddPermanentItem(itemId);
        SaveToDisk();
    }

    /// <summary>A match with your Memo Book starts (empty = it ended normally: results applied).</summary>
    public static void SetMemoBookInMatch(string mapId)
    {
        if (Current == null) return;
        mapId = mapId ?? "";
        if ((Current.memoBookInMatchMap ?? "") == mapId) return;
        Current.memoBookInMatchMap = mapId;
        SaveToDisk();
    }

    /// <summary>
    /// The marker is still set = the game was closed / the match was left before its results:
    /// the Memo Book you took in is destroyed. Run at start and when you are back in a lobby.
    /// </summary>
    public static void ApplyMemoBookQuitRule()
    {
        if (Current == null || string.IsNullOrEmpty(Current.memoBookInMatchMap)) return;
        string item = ItemCatalog.MemoBookId(Current.memoBookInMatchMap);
        RemovePermanentItem(item);
        Debug.Log("[SaveManager] You left a match with your Memo Book before it ended: the book is gone.");
        Current.memoBookInMatchMap = "";
        SaveToDisk();
    }

    public static void SaveToDisk()
    {
        string json = JsonUtility.ToJson(Current);
        byte[] plainBytes = Encoding.UTF8.GetBytes(json);

        using (Aes aes = Aes.Create())
        {
            aes.Key = GetKey();
            aes.GenerateIV();

            using (ICryptoTransform encryptor = aes.CreateEncryptor())
            {
                byte[] encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

                using (FileStream fs = new FileStream(SavePath, FileMode.Create))
                {
                    fs.Write(aes.IV, 0, aes.IV.Length);
                    fs.Write(encryptedBytes, 0, encryptedBytes.Length);
                }
            }
        }

        Debug.Log("Saved to: " + SavePath);
    }

    private static SaveData Load()
    {
        if (!File.Exists(SavePath))
        {
            Debug.Log("No save file yet, starting fresh.");
            return new SaveData();
        }

        try
        {
            byte[] fileBytes = File.ReadAllBytes(SavePath);

            byte[] iv = new byte[16];
            byte[] encryptedBytes = new byte[fileBytes.Length - 16];
            Buffer.BlockCopy(fileBytes, 0, iv, 0, 16);
            Buffer.BlockCopy(fileBytes, 16, encryptedBytes, 0, encryptedBytes.Length);

            using (Aes aes = Aes.Create())
            {
                aes.Key = GetKey();
                aes.IV = iv;

                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                {
                    byte[] plainBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
                    string json = Encoding.UTF8.GetString(plainBytes);
                    return JsonUtility.FromJson<SaveData>(json);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Save file unreadable (missing or tampered). Resetting. " + e.Message);
            return new SaveData();
        }
    }

    public static void AddCurrency(int amount)
    {
        Current.currency += amount;
    }

    public static bool SpendCurrency(int amount)
    {
        if (Current.currency < amount) return false;
        Current.currency -= amount;
        return true;
    }

    public static void AddConsumable(string itemID, int amount)
    {
        var item = Current.consumables.Find(x => x.itemID == itemID);
        if (item != null)
        {
            item.quantity += amount;
        }
        else
        {
            Current.consumables.Add(new ConsumableItemData(itemID, amount));
        }
    }

    public static bool ConsumeItem(string itemID, int amount = 1)
    {
        var item = Current.consumables.Find(x => x.itemID == itemID);
        if (item != null && item.quantity >= amount)
        {
            item.quantity -= amount;
            if (item.quantity <= 0)
            {
                Current.consumables.Remove(item);
            }
            return true;
        }
        return false;
    }

    public static int GetConsumableQuantity(string itemID)
    {
        var item = Current.consumables.Find(x => x.itemID == itemID);
        return item != null ? item.quantity : 0;
    }

    public static bool AddPermanentItem(string itemID)
    {
        var item = Current.permanentItems.Find(x => x.itemID == itemID);
        if (item != null)
        {
            if (item.isOwned) return false;
            item.isOwned = true;
            return true;
        }
        else
        {
            Current.permanentItems.Add(new PermanentItemData(itemID, true));
            return true;
        }
    }

    public static bool RemovePermanentItem(string itemID)
    {
        var item = Current.permanentItems.Find(x => x.itemID == itemID);
        if (item != null && item.isOwned)
        {
            Current.permanentItems.Remove(item);
            return true;
        }
        return false;
    }

    public static bool HasPermanentItem(string itemID)
    {
        var item = Current.permanentItems.Find(x => x.itemID == itemID);
        return item != null && item.isOwned;
    }
}