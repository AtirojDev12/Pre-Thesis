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

    [ContextMenu("Save To Disk")]
    private void TestSave()
    {
        SaveManager.SaveToDisk();
    }
}