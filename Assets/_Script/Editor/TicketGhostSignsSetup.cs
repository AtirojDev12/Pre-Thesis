using Mirror;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 7 Nov 2026 (Mr.k): the two RESTROOM BREAK signs for the Ticket Zone timed ghost.
///
///   Tools > Pre-Thesis > Scenes > Map: Ticket Ghost Restroom Break Signs
///
/// Adds "Ticket Ghost Visit" to the open map (Cinema_GamePlay) with:
///   Popcorn Sign / Ticket Sign             the signs (network scene objects, hidden until the warning)
///   ... Appear Spot (orange)               where each sign appears        -> MOVE it
///   ... Put Here Spot (blue)               where each sign must be put    -> MOVE it
///   ... Seller Area (green)                the box the ghost checks for players (scale = size)
///   Ghost Roam Area (yellow)               where the ghost roams after both signs (scale = size)
/// First guess: near the popcorn stations / ticket counter. Move the spots, then save the scene.
/// Running it again asks before rebuilding (rebuilding resets your positions).
/// </summary>
public static class TicketGhostSignsSetup
{
    private const string RootName = "Ticket Ghost Visit";
    private const string MaterialFolder = "Assets/Materials/Items";

    [MenuItem("Tools/Pre-Thesis/Scenes/Map: Ticket Ghost Restroom Break Signs", false, 62)]
    private static void Build()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Restroom Break Signs", "Stop Play mode first.", "OK"); return; }
        if (Object.FindAnyObjectByType<GhostManager>(FindObjectsInactive.Include) == null &&
            !EditorUtility.DisplayDialog("Restroom Break Signs", "No GhostManager (timed ghost) in the open scene. Add the signs anyway?", "Add", "Cancel")) return;

        GameObject old = GameObject.Find(RootName);
        if (old != null)
        {
            if (!EditorUtility.DisplayDialog("Restroom Break Signs", "\"" + RootName + "\" is already in the scene.\n\nRebuild it? Your spot positions are reset.", "Rebuild", "Keep it"))
            {
                Selection.activeGameObject = old;
                return;
            }
            Undo.DestroyObjectImmediate(old);
        }

        Vector3 popcorn = PopcornCentre();
        Vector3 ticket = TicketCentre(popcorn);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Restroom break signs");
        TicketGhostVisit visit = root.AddComponent<TicketGhostVisit>();

        RestBreakSign popcornSign = MakeStation(root.transform, "Popcorn", popcorn);
        RestBreakSign ticketSign = MakeStation(root.transform, "Ticket", ticket);

        // Roam area = the Ticket Zone light area if it has one, else a box around both sellers.
        Transform roam = new GameObject("Ghost Roam Area (yellow)").transform;
        roam.SetParent(root.transform, false);
        if (!TicketLightArea(out Vector3 centre, out Vector3 size))
        {
            centre = (popcorn + ticket) * 0.5f + Vector3.up * 1.5f;
            size = new Vector3(Mathf.Abs(popcorn.x - ticket.x) + 8f, 4f, Mathf.Abs(popcorn.z - ticket.z) + 8f);
        }
        roam.position = centre;
        roam.localScale = size;

        var so = new SerializedObject(visit);
        SerializedProperty list = so.FindProperty("signs");
        list.ClearArray();
        list.InsertArrayElementAtIndex(0);
        list.GetArrayElementAtIndex(0).objectReferenceValue = popcornSign;
        list.InsertArrayElementAtIndex(1);
        list.GetArrayElementAtIndex(1).objectReferenceValue = ticketSign;
        so.FindProperty("roamArea").objectReferenceValue = roam;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log("[TicketGhostSignsSetup] Added the restroom break signs. Move the ORANGE (appear) and BLUE (put here) spots, " +
                  "the GREEN seller areas and the YELLOW roam area, then save the scene.", root);
    }

    private static RestBreakSign MakeStation(Transform root, string seller, Vector3 at)
    {
        Vector3 floor = Floor(at);

        Transform appear = new GameObject(seller + " Sign - Appear Spot (orange)").transform;
        appear.SetParent(root, false);
        appear.position = floor + new Vector3(2.5f, 0f, 0f);

        Transform place = new GameObject(seller + " Sign - Put Here Spot (blue)").transform;
        place.SetParent(root, false);
        place.position = floor + new Vector3(-1.2f, 0f, 0f);

        Transform area = new GameObject(seller + " Seller Area (green)").transform;
        area.SetParent(root, false);
        area.position = floor + Vector3.up * 1.5f;
        area.localScale = new Vector3(5f, 3f, 4f);

        var go = new GameObject(seller + " Sign");
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(appear.position, appear.rotation);
        go.AddComponent<NetworkIdentity>(); // Mirror's NetworkBehaviour does not add it by itself
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.5f, 0f);
        box.size = new Vector3(0.65f, 1f, 0.45f);

        Material yellow = Mat("RestBreakSign_Board", new Color(0.98f, 0.78f, 0.1f));
        Material dark = Mat("RestBreakSign_Frame", new Color(0.12f, 0.1f, 0.08f));
        Part(go.transform, "Front Board", new Vector3(0f, 0.47f, -0.09f), new Vector3(0.55f, 0.9f, 0.03f), new Vector3(-11f, 0f, 0f), yellow);
        Part(go.transform, "Back Board", new Vector3(0f, 0.47f, 0.09f), new Vector3(0.55f, 0.9f, 0.03f), new Vector3(11f, 0f, 0f), yellow);
        Part(go.transform, "Top", new Vector3(0f, 0.93f, 0f), new Vector3(0.58f, 0.04f, 0.06f), Vector3.zero, dark);
        Label(go.transform, "Front Text", new Vector3(0f, 0.5f, -0.115f), new Vector3(-11f, 0f, 0f));
        Label(go.transform, "Back Text", new Vector3(0f, 0.5f, 0.115f), new Vector3(11f, 180f, 0f));

        RestBreakSign sign = go.AddComponent<RestBreakSign>();
        var so = new SerializedObject(sign);
        so.FindProperty("appearSpot").objectReferenceValue = appear;
        so.FindProperty("placeSpot").objectReferenceValue = place;
        so.FindProperty("sellerArea").objectReferenceValue = area;
        so.FindProperty("sellerName").stringValue = seller;
        so.ApplyModifiedPropertiesWithoutUndo();
        return sign;
    }

    private static void Part(Transform parent, string name, Vector3 position, Vector3 scale, Vector3 euler, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.transform.localEulerAngles = euler;
        go.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static void Label(Transform parent, string name, Vector3 position, Vector3 euler)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localEulerAngles = euler;
        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.text = "<b>RESTROOM\nBREAK</b>\n<size=60%>back soon</size>";
        tmp.fontSize = 1.4f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.1f, 0.08f, 0.06f);
        tmp.rectTransform.sizeDelta = new Vector2(0.5f, 0.8f);
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
    }

    // ---- First guesses ------------------------------------------------------------------------

    private static Vector3 PopcornCentre()
    {
        PopcornStation[] stations = Object.FindObjectsByType<PopcornStation>(FindObjectsInactive.Include);
        if (stations.Length > 0)
        {
            Vector3 sum = Vector3.zero;
            foreach (PopcornStation s in stations) sum += s.transform.position;
            return sum / stations.Length;
        }
        return ViewPivot();
    }

    private static Vector3 TicketCentre(Vector3 popcorn)
    {
        TicketMinigame tickets = Object.FindAnyObjectByType<TicketMinigame>(FindObjectsInactive.Include);
        if (tickets != null)
        {
            Transform counter = tickets.GhostFavorDestination;
            return counter != null ? counter.position : tickets.transform.position;
        }
        return popcorn + new Vector3(6f, 0f, 0f);
    }

    private static bool TicketLightArea(out Vector3 centre, out Vector3 size)
    {
        centre = size = Vector3.zero;
        BuildingLightManager power = Object.FindAnyObjectByType<BuildingLightManager>(FindObjectsInactive.Include);
        if (power == null || power.Zones == null) return false;
        foreach (BuildingLightManager.LightZone zone in power.Zones)
            if (zone != null && zone.zoneId == "TicketAndPopcorn" && zone.areaSize.x > 0f && zone.areaSize.z > 0f)
            {
                centre = zone.areaCenter;
                size = zone.areaSize;
                return true;
            }
        return false;
    }

    private static Vector3 ViewPivot()
    {
        SceneView view = SceneView.lastActiveSceneView;
        return view != null ? view.pivot : Vector3.zero;
    }

    private static Vector3 Floor(Vector3 at)
    {
        if (Physics.Raycast(at + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return hit.point;
        return at;
    }

    private static Material Mat(string name, Color color)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Materials", "Items");
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
}
