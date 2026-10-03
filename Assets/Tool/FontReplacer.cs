#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using TMPro;
using System.IO;

public class FontReplacer : EditorWindow
{
    private TMP_FontAsset newFont;

    [MenuItem("Tools/Font Replacer")]
    public static void ShowWindow()
    {
        GetWindow<FontReplacer>("Font Replacer");
    }

    private void OnGUI()
    {
        GUILayout.Label("Replace Fonts Across Entire Project", EditorStyles.boldLabel);
        
        newFont = (TMP_FontAsset)EditorGUILayout.ObjectField("New Font Asset", newFont, typeof(TMP_FontAsset), false);

        if (GUILayout.Button("Replace Everywhere (Scenes & Prefabs)"))
        {
            if (newFont == null)
            {
                EditorUtility.DisplayDialog("Warning", "Please select a New Font Asset first!", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Confirm Action", "This will modify all Prefabs and the current scene. Are you sure?", "Yes", "No"))
                return;

            int prefabCount = ReplaceInPrefabs();
            int sceneCount = ReplaceInActiveScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Success", 
                $"Finished!\n- Changed in Active Scene: {sceneCount} objects\n- Changed in Prefabs: {prefabCount} assets", "OK");
        }
    }

    private int ReplaceInActiveScene()
    {
        TMP_Text[] allTextObjects = Resources.FindObjectsOfTypeAll<TMP_Text>();
        int count = 0;

        foreach (TMP_Text textObj in allTextObjects)
        {
            if (textObj.hideFlags == HideFlags.None && !EditorUtility.IsPersistent(textObj.transform.root.gameObject))
            {
                Undo.RecordObject(textObj, "Replace Font");
                textObj.font = newFont;
                EditorUtility.SetDirty(textObj);
                count++;
            }
        }
        return count;
    }

    private int ReplaceInPrefabs()
    {
        // ค้นหาไฟล์ .prefab ทั้งหมดในโฟลเดอร์ Assets
        string[] allPrefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        int count = 0;

        foreach (string guid in allPrefabGuids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            
            // โหลด Prefab เข้ามาในหน่วยความจำเพื่อตรวจสอบ
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
            TMP_Text[] textsInPrefab = prefabRoot.GetComponentsInChildren<TMP_Text>(true);
            bool isModified = false;

            foreach (TMP_Text textObj in textsInPrefab)
            {
                if (textObj.font != newFont)
                {
                    textObj.font = newFont;
                    isModified = true;
                }
            }

            // ถ้ามีการแก้ไข ให้บันทึกความเปลี่ยนแปลงกลับลงไปในไฟล์ Prefab
            if (isModified)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, assetPath);
                count++;
            }

            // เคลียร์ Prefab ออกจากหน่วยความจำ
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        return count;
    }
}
#endif
