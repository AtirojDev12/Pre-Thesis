using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 25 Nov 2026: makes the Memo Book PREFAB (Assets/Resources/UI/Memo Book.prefab) from the pictures in
/// Assets/Sprite2D/MemoBook. It is made by itself once when it is missing; after that it is yours to edit.
///   Tools > Pre-Thesis > Designer Settings > Memo Book Prefab              = open it (Prefab Mode)
///   Tools > Pre-Thesis > Designer Settings > Memo Book Prefab: Make Again  = throw away your edits, make it new
/// </summary>
[InitializeOnLoad]
public static class MemoBookPrefabSetup
{
    private const string ArtFolder = "Assets/Sprite2D/MemoBook/";

    static MemoBookPrefabSetup()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MemoBookView.PrefabPath) != null) return;
            MemoBookView.Art art = LoadArt();
            if (art.cover == null) return; // the pictures are not imported yet
            if (Make(art) != null) Debug.Log("[Memo Book] Made the prefab " + MemoBookView.PrefabPath + ". Edit it: Tools > Pre-Thesis > Designer Settings > Memo Book Prefab.");
        };
    }

    [MenuItem("Tools/Pre-Thesis/Designer Settings/Memo Book Prefab", false, 216)]
    private static void Open()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MemoBookView.PrefabPath);
        if (prefab == null) prefab = Make(LoadArt());
        if (prefab == null) return;
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        AssetDatabase.OpenAsset(prefab);
    }

    [MenuItem("Tools/Pre-Thesis/Designer Settings/Memo Book Prefab: Make Again", false, 217)]
    private static void MakeAgain()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(MemoBookView.PrefabPath) != null &&
            !EditorUtility.DisplayDialog("Memo Book prefab", "Make the Memo Book prefab again from the pictures in " + ArtFolder +
                                         "?\n\nEvery change you made in the prefab is lost.", "Make it again", "Cancel"))
            return;
        GameObject prefab = Make(LoadArt());
        if (prefab != null) AssetDatabase.OpenAsset(prefab);
    }

    /// <summary>The pictures: Memo-BG, Memo-PaperNoBlood, Memo-PaperWithBlood, Memo-Postit (cut, top to bottom), Arrow (left, right), Close.</summary>
    public static MemoBookView.Art LoadArt()
    {
        Sprite[] arrows = Sprites("Arrow.png").OrderBy(s => s.rect.x).ToArray();
        return new MemoBookView.Art
        {
            cover = Sprites("Memo-BG.png").FirstOrDefault(),
            paper = Sprites("Memo-PaperNoBlood.png").FirstOrDefault(),
            bloodyPaper = Sprites("Memo-PaperWithBlood.png").FirstOrDefault(),
            tabs = Sprites("Memo-Postit.png").OrderByDescending(s => s.rect.y).ToArray(),
            previous = arrows.Length > 0 ? arrows[0] : null,
            next = arrows.Length > 1 ? arrows[1] : null,
            close = Sprites("Close.png").FirstOrDefault(),
        };
    }

    private static Sprite[] Sprites(string file) =>
        AssetDatabase.LoadAllAssetsAtPath(ArtFolder + file).OfType<Sprite>().ToArray();

    private static GameObject Make(MemoBookView.Art art)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/UI")) AssetDatabase.CreateFolder("Assets/Resources", "UI");

        Scene scene = EditorSceneManager.NewPreviewScene();
        MemoBookView view = null;
        try
        {
            view = MemoBookView.Build(art);
            SceneManager.MoveGameObjectToScene(view.gameObject, scene);
            return PrefabUtility.SaveAsPrefabAsset(view.gameObject, MemoBookView.PrefabPath);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Memo Book] Could not make the prefab: " + e);
            return null;
        }
        finally
        {
            if (view != null) Object.DestroyImmediate(view.gameObject);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
