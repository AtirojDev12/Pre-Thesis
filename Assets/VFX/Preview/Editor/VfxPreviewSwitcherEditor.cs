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
    }
}
