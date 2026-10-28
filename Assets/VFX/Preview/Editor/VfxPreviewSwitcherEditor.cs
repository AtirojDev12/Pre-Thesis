using UnityEditor;
using UnityEngine;
using PreThesis.Vfx;

[CustomEditor(typeof(VfxPreviewSwitcher))]
public sealed class VfxPreviewSwitcherEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var preview = (VfxPreviewSwitcher)target;
        if (preview.impactStage != null && GUILayout.Button("Show Item Drop Impact"))
        {
            preview.ShowImpact();
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAt(preview.impactStage.transform.position,
                    Quaternion.Euler(28, -35, 0), 1.3f);
        }
        if (GUILayout.Button("Show Popcorn + Drink"))
        {
            preview.ShowFood();
            FoodVfxScenePreview.Frame(preview.foodStage.transform);
        }
        if (GUILayout.Button("Show Ghost Black Water"))
        {
            preview.ShowGhost();
            FoodVfxScenePreview.Frame(preview.ghostStage.transform);
        }
        if (preview.favorStage != null && GUILayout.Button("Show Ghost Favor (2 seconds)"))
        {
            preview.ShowFavor();
            FoodVfxScenePreview.Frame(preview.favorStage.transform);
        }
    }
}
