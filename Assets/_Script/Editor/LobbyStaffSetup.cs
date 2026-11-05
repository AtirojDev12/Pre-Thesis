using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 5 Nov 2026 (Mr.k): the new lobby lost its shop and storage. These tools put them back,
/// plus the Staff NPC that hands out the work sheet. Each one is placed WHERE THE SCENE
/// VIEW LOOKS (centre of the view), its front facing the Scene camera. Running a tool
/// again moves / remakes only that object. Then save the scene.
///
///   Tools > Pre-Thesis > Scenes > Lobby: Add Staff NPC (where the Scene view looks)
///   Tools > Pre-Thesis > Scenes > Lobby: Add Shop Counter (where the Scene view looks)
///   Tools > Pre-Thesis > Scenes > Lobby: Add Storage Locker (where the Scene view looks)
///   Tools > Pre-Thesis > Designer Settings > Map Guide (Cinema)   (work sheet text, pictures + zone goal)
///   Tools > Pre-Thesis > One Step > Items: Work Sheet   (the dropped / held model; also run by Add Staff NPC)
/// Greybox looks: swap the "Look" child for real models later (keep the collider).
/// </summary>
public static class LobbyStaffSetup
{
    private const string NpcName = "Staff NPC (work sheet)";
    private const string ShopName = "Shop Counter";
    private const string LockerName = "Storage Locker";
    private const string MaterialFolder = "Assets/Materials/Lobby";
    private const string GuideFolder = "Assets/Resources/MapGuides";

    [MenuItem("Tools/Pre-Thesis/Scenes/Lobby: Add Staff NPC (where the Scene view looks)", false, 47)]
    private static void AddNpc()
    {
        if (!CheckLobby()) return;
        WorksheetItemSetup.EnsureBuilt(); // 5 Nov: the sheet is a real object (drop / pick up)
        GameObject root = NewAtView(NpcName);
        Transform look = Child(root.transform, "Look");
        Material uniform = Mat("Staff_Uniform", new Color(0.62f, 0.1f, 0.12f));
        Material skin = Mat("Staff_Skin", new Color(0.86f, 0.72f, 0.6f));
        Material dark = Mat("Staff_Dark", new Color(0.08f, 0.08f, 0.1f));
        Part(look, PrimitiveType.Capsule, "Body", new Vector3(0f, 0.85f, 0f), new Vector3(0.5f, 0.85f, 0.4f), uniform, false);
        Part(look, PrimitiveType.Sphere, "Head", new Vector3(0f, 1.82f, 0f), Vector3.one * 0.3f, skin, false);
        Part(look, PrimitiveType.Cylinder, "Cap", new Vector3(0f, 1.98f, 0f), new Vector3(0.32f, 0.05f, 0.32f), uniform, false);
        Part(look, PrimitiveType.Cube, "Sheets In Hand", new Vector3(0.22f, 1.05f, 0.25f), new Vector3(0.25f, 0.02f, 0.32f), Mat("Staff_Paper", new Color(0.95f, 0.93f, 0.86f)), false)
            .transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
        Part(look, PrimitiveType.Cube, "Badge", new Vector3(0.12f, 1.35f, 0.21f), new Vector3(0.1f, 0.06f, 0.02f), dark, false);
        CapsuleCollider body = root.AddComponent<CapsuleCollider>(); // the E target (also blocks walking through)
        body.center = new Vector3(0f, 0.95f, 0f);
        body.height = 1.95f;
        body.radius = 0.35f;
        Sign(root.transform, "Sign", new Vector3(0f, 2.45f, 0f), "STAFF\n<size=60%>Take your work sheet</size>", 2.4f);
        root.AddComponent<StaffNpc>(); // adds the NetworkIdentity too (scene object, id set on save)
        Finish(root, "Staff NPC placed. Press E on it in the lobby to get a work sheet.");
    }

    [MenuItem("Tools/Pre-Thesis/Scenes/Lobby: Add Shop Counter (where the Scene view looks)", false, 48)]
    private static void AddShop()
    {
        if (!CheckLobby()) return;
        GameObject root = NewAtView(ShopName);
        Transform look = Child(root.transform, "Look");
        Part(look, PrimitiveType.Cube, "Counter", new Vector3(0f, 0.5f, 0f), new Vector3(2.2f, 1f, 0.7f), Mat("Shop_Counter", new Color(0.24f, 0.3f, 0.45f)), true);
        Part(look, PrimitiveType.Cube, "Screen", new Vector3(0f, 1.25f, -0.1f), new Vector3(0.7f, 0.45f, 0.06f), Mat("Shop_Screen", new Color(0.15f, 0.6f, 0.8f)), true);
        Sign(root.transform, "Sign", new Vector3(0f, 1.95f, 0f), "SHOP", 2.2f);
        root.AddComponent<ShopTerminal>();
        Finish(root, "Shop counter placed. Press E on it in the lobby to open the shop.");
    }

    [MenuItem("Tools/Pre-Thesis/Scenes/Lobby: Add Storage Locker (where the Scene view looks)", false, 49)]
    private static void AddLocker()
    {
        if (!CheckLobby()) return;
        GameObject root = NewAtView(LockerName);
        Transform look = Child(root.transform, "Look");
        Part(look, PrimitiveType.Cube, "Locker", new Vector3(0f, 1f, 0f), new Vector3(1.2f, 2f, 0.6f), Mat("Storage_Locker", new Color(0.35f, 0.38f, 0.33f)), true);
        Part(look, PrimitiveType.Cube, "Handle", new Vector3(0.35f, 1f, 0.31f), new Vector3(0.05f, 0.3f, 0.04f), Mat("Staff_Dark", new Color(0.08f, 0.08f, 0.1f)), false);
        Sign(root.transform, "Sign", new Vector3(0f, 2.4f, 0f), "STORAGE\n<size=60%>your items</size>", 2f);
        root.AddComponent<StorageTerminal>();
        Finish(root, "Storage locker placed. Press E on it in the lobby to open your storage.");
    }

    /// <summary>5 Nov: the asset keeps old text after the built-in text changes. This puts the new text in (pictures kept).</summary>
    [MenuItem("Tools/Pre-Thesis/Designer Settings/Map Guide (Cinema): Reset Text", false, 31)]
    private static void ResetGuideText()
    {
        string path = GuideFolder + "/" + RoomConfig.DemoMapID + ".asset";
        MapGuide guide = AssetDatabase.LoadAssetAtPath<MapGuide>(path);
        if (guide == null) { GuideAsset(); return; }
        if (!EditorUtility.DisplayDialog("Map Guide (Cinema)",
                "Replace ALL text in " + path + " with the built-in Cinema text?\n\nPictures are kept. Your own text edits are lost.",
                "Replace", "Cancel")) return;
        Undo.RecordObject(guide, "Reset Map Guide text");
        guide.ResetCinemaText();
        EditorUtility.SetDirty(guide);
        AssetDatabase.SaveAssets();
        Selection.activeObject = guide;
        Debug.Log("[LobbyStaffSetup] Map Guide text reset to the built-in Cinema text (3 zones: Ticket Zone, Screen Rooms, Toilets).", guide);
    }

    [MenuItem("Tools/Pre-Thesis/Designer Settings/Map Guide (Cinema)", false, 30)]
    private static void GuideAsset()
    {
        string path = GuideFolder + "/" + RoomConfig.DemoMapID + ".asset";
        MapGuide guide = AssetDatabase.LoadAssetAtPath<MapGuide>(path);
        if (guide == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(GuideFolder)) AssetDatabase.CreateFolder("Assets/Resources", "MapGuides");
            guide = ScriptableObject.CreateInstance<MapGuide>();
            guide.FillCinemaDefaults();
            AssetDatabase.CreateAsset(guide, path);
            AssetDatabase.SaveAssets();
        }
        Selection.activeObject = guide;
        EditorGUIUtility.PingObject(guide);
    }

    // ---- Helpers --------------------------------------------------------------------

    private static GameObject NewAtView(string name)
    {
        GameObject old = GameObject.Find(name);
        if (old != null) Undo.DestroyObjectImmediate(old);
        SceneView view = SceneView.lastActiveSceneView;
        Vector3 at = view != null ? view.pivot : Vector3.zero;
        Vector3 toCamera = view != null ? view.camera.transform.position - at : Vector3.back;
        toCamera.y = 0f;
        if (toCamera.sqrMagnitude < 0.01f) toCamera = Vector3.back;
        if (Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            at.y = hit.point.y;
        var root = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(root, name);
        root.transform.SetPositionAndRotation(at, Quaternion.LookRotation(toCamera.normalized, Vector3.up));
        return root;
    }

    private static Transform Child(Transform parent, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    private static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material, bool keepCollider)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    /// <summary>World text facing the parent's +Z (TextMeshPro reads from its -Z side).</summary>
    private static void Sign(Transform parent, string name, Vector3 localPosition, string text, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.rectTransform.sizeDelta = new Vector2(width, 1f);
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 1f;
        tmp.fontSizeMax = 6f;
    }

    private static Material Mat(string name, Color color)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Materials", "Lobby");
        string path = MaterialFolder + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var material = new Material(probe.GetComponent<Renderer>().sharedMaterial);
        Object.DestroyImmediate(probe);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static bool CheckLobby()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Lobby", "Stop Play mode first.", "OK"); return false; }
        if (EditorSceneManager.GetActiveScene().name != "Lobby")
        {
            EditorUtility.DisplayDialog("Lobby", "Open Assets/Scenes/Lobby.unity first.", "OK");
            return false;
        }
        return true;
    }

    private static void Finish(GameObject select, string message)
    {
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = select;
        EditorUtility.DisplayDialog("Lobby", message + "\n\nMove / turn it if needed, then SAVE the scene (Ctrl+S).", "OK");
    }
}
