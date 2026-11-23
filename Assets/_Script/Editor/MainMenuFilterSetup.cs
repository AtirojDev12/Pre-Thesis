using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// MainMenu.unity only. Two clicks, each undoable (Ctrl+Z):
///
///   Tools > Pre-Thesis > Scenes > Menu: Main Menu UI Behind Game Filter
///       "Main Menu UI" canvas: Screen Space - Overlay -> Screen Space - Camera on the
///       scene's Main Camera, so the game's Retro Horror Filter (PC_Renderer) draws its
///       scanlines / vignette / pixelation over the menu text too.
///
///   Tools > Pre-Thesis > Scenes > Menu: Main Menu UI Back To Overlay
///       Puts it back exactly as MainMenuBuilder makes it.
///
/// Only the canvas's Render Mode, Camera and Plane Distance change. Nothing inside the
/// menu, the filter, PC_Renderer or any other scene is touched.
///
/// Known side effect: the Brightness setting (BrightnessApplier, camera post exposure)
/// now also dims / brightens the menu text in this scene.
/// </summary>
public static class MainMenuFilterSetup
{
    private const string BehindPath = "Tools/Pre-Thesis/Scenes/Menu: Main Menu UI Behind Game Filter";
    private const string OverlayPath = "Tools/Pre-Thesis/Scenes/Menu: Main Menu UI Back To Overlay";
    private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
    private const string MenuRootName = "Main Menu UI";

    [MenuItem(BehindPath, false, 35)]
    private static void Behind()
    {
        if (!TryGetMenuCanvas(out Scene scene, out Canvas canvas)) return;

        Camera cam = FindSceneCamera(scene);
        if (cam == null)
        {
            EditorUtility.DisplayDialog("Main Menu UI", "No camera found in " + scene.name + ". Nothing was changed.", "OK");
            return;
        }

        Undo.RecordObject(canvas, "Main Menu UI Behind Game Filter");
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        // Closer than everything else (VCR Background sits at near + 0.05), so no 3D
        // object can come in front of a button. sortingOrder 10 is kept.
        canvas.planeDistance = cam.nearClipPlane + 0.03f;
        EditorUtility.SetDirty(canvas);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = canvas.gameObject;
        Debug.Log("[MainMenuFilterSetup] '" + MenuRootName + "' now renders through camera '" + cam.name +
                  "' (behind the Retro Horror Filter). Save the scene to keep it. Undo: Ctrl+Z or 'Menu: Main Menu UI Back To Overlay'.");
    }

    [MenuItem(OverlayPath, false, 36)]
    private static void BackToOverlay()
    {
        if (!TryGetMenuCanvas(out Scene scene, out Canvas canvas)) return;

        Undo.RecordObject(canvas, "Main Menu UI Back To Overlay");
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = null;
        canvas.planeDistance = 100f;
        EditorUtility.SetDirty(canvas);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = canvas.gameObject;
        Debug.Log("[MainMenuFilterSetup] '" + MenuRootName + "' is back to Screen Space - Overlay. Save the scene to keep it.");
    }

    private static bool TryGetMenuCanvas(out Scene scene, out Canvas canvas)
    {
        canvas = null;
        scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Main Menu UI", "Stop Play mode first.", "OK");
            return false;
        }
        if (scene.path != MainMenuScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
        }
        foreach (GameObject r in scene.GetRootGameObjects())
            if (r.name == MenuRootName && r.TryGetComponent(out canvas)) return true;

        EditorUtility.DisplayDialog("Main Menu UI", "'" + MenuRootName + "' was not found in " + scene.name + ". Nothing was changed.", "OK");
        return false;
    }

    private static Camera FindSceneCamera(Scene scene)
    {
        Camera first = null;
        foreach (GameObject r in scene.GetRootGameObjects())
        foreach (Camera c in r.GetComponentsInChildren<Camera>(true))
        {
            if (c.CompareTag("MainCamera")) return c;
            if (first == null) first = c;
        }
        return first;
    }
}
