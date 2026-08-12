using UnityEngine;


public class SaveTester : MonoBehaviour
{
    [ContextMenu("Add 50 Currency")]
    private void TestAdd()
    {
        SaveManager.AddCurrency(50);
        Debug.Log("Currency ตอนนี้: " + SaveManager.Current.currency);
    }


    [ContextMenu("Spend 20 Currency")]
    private void TestSpend()
    {
        bool success = SaveManager.SpendCurrency(20);
        Debug.Log("Spend สำเร็จ: " + success + " | Currency ตอนนี้: " + SaveManager.Current.currency);
    }


    [ContextMenu("Add Medkit (Consumable)")]
    private void TestAddConsumable()
    {
        SaveManager.AddConsumable("medkit", 2);
        Debug.Log("Added 2 Medkits. Total: " + SaveManager.GetConsumableQuantity("medkit"));
    }


    [ContextMenu("Use Medkit (Consumable)")]
    private void TestUseConsumable()
    {
        bool success = SaveManager.ConsumeItem("medkit", 1);
        Debug.Log("Used 1 Medkit: " + success + " | Total now: " + SaveManager.GetConsumableQuantity("medkit"));
    }


    [ContextMenu("Buy Flashlight (Permanent)")]
    private void TestAddPermanent()
    {
        SaveManager.AddPermanentItem("flashlight");
        Debug.Log("Bought Flashlight. Owned: " + SaveManager.HasPermanentItem("flashlight"));
    }


    [ContextMenu("Check Flashlight")]
    private void TestCheckPermanent()
    {
        Debug.Log("Has Flashlight: " + SaveManager.HasPermanentItem("flashlight"));
    }


    [ContextMenu("Save To Disk")]
    private void TestSave()
    {
        SaveManager.SaveToDisk();
    }
}

