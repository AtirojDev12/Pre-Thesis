using UnityEditor;
using UnityEngine;

/// <summary>
/// 25 Nov 2026: Tools > Pre-Thesis > Designer Settings > Memo Book Look
/// Creates (first time) and selects Resources/Tuning/MemoBookLook.asset: the pictures of the Memo Book.
/// Drag new pictures into its slots in the Inspector, then commit the file.
/// </summary>
public static class MemoBookLookSetup
{
    [MenuItem("Tools/Pre-Thesis/Designer Settings/Memo Book Look", false, 215)]
    private static void Select()
    {
        MemoBookLook asset = AssetDatabase.LoadAssetAtPath<MemoBookLook>(MemoBookLook.AssetPath);
        if (asset == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Tuning")) AssetDatabase.CreateFolder("Assets/Resources", "Tuning");
            asset = ScriptableObject.CreateInstance<MemoBookLook>();
            AssetDatabase.CreateAsset(asset, MemoBookLook.AssetPath);
            AssetDatabase.SaveAssets();
            MemoBookLook.Use(asset);
        }
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }
}
