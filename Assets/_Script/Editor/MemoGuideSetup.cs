using UnityEditor;
using UnityEngine;

/// <summary>
/// 17 Nov 2026 (Mr.k, Task 8): Tools > Pre-Thesis > Designer Settings > Memo Book Texts
/// Creates (first time) and selects Resources/Tuning/MemoGuideTexts.asset: every sentence the live
/// Memo Book shows (task steps and rule survival steps). Rewrite them in the Inspector, then commit the file.
/// </summary>
public static class MemoGuideSetup
{
    [MenuItem("Tools/Pre-Thesis/Designer Settings/Memo Book Texts", false, 214)]
    private static void Select()
    {
        MemoGuideTexts asset = AssetDatabase.LoadAssetAtPath<MemoGuideTexts>(MemoGuideTexts.AssetPath);
        if (asset == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Tuning")) AssetDatabase.CreateFolder("Assets/Resources", "Tuning");
            asset = ScriptableObject.CreateInstance<MemoGuideTexts>();
            AssetDatabase.CreateAsset(asset, MemoGuideTexts.AssetPath);
            AssetDatabase.SaveAssets();
            MemoGuideTexts.Use(asset);
        }
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }
}
