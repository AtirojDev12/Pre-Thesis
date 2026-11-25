using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ONE CLICK: Tools > Pre-Thesis > Scenes > Menu: Add Timecode + Blink
///
/// MainMenu.unity, under "Main Menu UI":
///   "Time"      gets VcrTimecode: counts up from 0:00:00 when the game starts.
///   "PressPlay" gets VcrBlink:    blinks on / off forever.
/// Only adds the two components (and shows 0:00:00 in the editor). Safe to run again.
/// </summary>
public static class MainMenuOsdSetup
{
    private const string MenuPath = "Tools/Pre-Thesis/Scenes/Menu: Add Timecode + Blink";
    private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";

    [MenuItem(MenuPath, false, 39)]
    private static void Add()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Timecode + Blink", "Stop Play mode first.", "OK");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != MainMenuScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
        }

        string log = "";
        GameObject time = Find("Main Menu UI/Time");
        if (time != null)
        {
            if (time.GetComponent<VcrTimecode>() == null) Undo.AddComponent<VcrTimecode>(time);
            TMP_Text label = time.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                Undo.RecordObject(label, "Add Timecode");
                label.text = "0:00:00";
                EditorUtility.SetDirty(label);
            }
            log += "- Time: counts up from 0:00:00" + (label == null ? " (no text found!)" : "") + "\n";
        }
        else log += "- 'Main Menu UI/Time' not found\n";

        GameObject press = Find("Main Menu UI/PressPlay");
        if (press != null)
        {
            if (press.GetComponent<VcrBlink>() == null) Undo.AddComponent<VcrBlink>(press);
            log += "- PressPlay: blinks\n";
        }
        else log += "- 'Main Menu UI/PressPlay' not found\n";

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[MainMenuOsdSetup] Done. Save the scene to keep it.\n" + log);
    }

    private const string MovePath = "Tools/Pre-Thesis/Scenes/Menu: Move OSD Into Main Panel (full screen)";
    private static readonly string[] Osd = { "CH", "Time", "PressPlay", "Date" };

    /// "Main Panel" stops being a 560x760 box on the left: it fills the screen, its Vertical
    /// Layout Group is removed (title + buttons stay exactly where they are on screen), and
    /// CH / Time / PressPlay / Date move inside it (also where they are), so they hide
    /// together with the main panel when Create Room / Room Browser / Settings open.
    [MenuItem(MovePath, false, 40)]
    private static void MoveIntoPanel()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Main Panel", "Stop Play mode first.", "OK");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != MainMenuScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
        }
        GameObject root = Find("Main Menu UI");
        var panel = root != null ? root.transform.Find("Main Panel") as RectTransform : null;
        if (panel == null)
        {
            EditorUtility.DisplayDialog("Main Panel", "'Main Menu UI/Main Panel' not found. Nothing was changed.", "OK");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(root, "Move OSD Into Main Panel");

        // 1. Remember where every current child is on screen.
        int n = panel.childCount;
        var kids = new RectTransform[n];
        var worldPos = new Vector3[n];
        var sizes = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            kids[i] = panel.GetChild(i) as RectTransform;
            if (kids[i] == null) continue;
            worldPos[i] = kids[i].position;
            sizes[i] = kids[i].rect.size;
        }
        Vector3 oldScale = panel.localScale;

        // 2. No layout group: nothing moves the children any more.
        var layout = panel.GetComponent<LayoutGroup>();
        if (layout != null) Undo.DestroyObjectImmediate(layout);
        var fitter = panel.GetComponent<ContentSizeFitter>();
        if (fitter != null) Undo.DestroyObjectImmediate(fitter);

        // 3. Panel = the whole screen (same rect as Main Menu UI), scale 1.
        panel.anchorMin = Vector2.zero;
        panel.anchorMax = Vector2.one;
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.offsetMin = Vector2.zero;
        panel.offsetMax = Vector2.zero;
        panel.localScale = Vector3.one;

        // 4. Put the children back where they were. Anchored left-middle, like the old box,
        //    so they keep the same place on other screen shapes. Old panel scale goes on them.
        for (int i = 0; i < n; i++)
        {
            RectTransform k = kids[i];
            if (k == null) continue;
            k.anchorMin = k.anchorMax = new Vector2(0f, 0.5f);
            k.sizeDelta = sizes[i];
            k.localScale = Vector3.Scale(k.localScale, oldScale);
            k.position = worldPos[i];
            EditorUtility.SetDirty(k);
        }

        // 5. Move the on-screen display pieces in, keeping their place.
        string log = "";
        foreach (string name in Osd)
        {
            Transform t = root.transform.Find(name);
            if (t == null) { log += "- " + name + ": not under Main Menu UI (skipped)\n"; continue; }
            Undo.SetTransformParent(t, panel, true, "Move OSD Into Main Panel");
            t.SetAsLastSibling();
            log += "- " + name + ": moved into Main Panel\n";
        }

        EditorUtility.SetDirty(panel);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = panel.gameObject;
        Debug.Log("[MainMenuOsdSetup] Main Panel now fills the screen (layout group removed, nothing moved on screen).\n" + log +
                  "Save the scene to keep it. Undo: Ctrl+Z.");
    }

    private static GameObject Find(string path)
    {
        GameObject go = GameObject.Find(path);
        if (go != null) return go;
        // Also find it when it is switched off.
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            int slash = path.IndexOf('/');
            if (slash < 0) { if (root.name == path) return root; continue; }
            if (root.name != path.Substring(0, slash)) continue;
            Transform t = root.transform.Find(path.Substring(slash + 1));
            if (t != null) return t.gameObject;
        }
        return null;
    }
}
