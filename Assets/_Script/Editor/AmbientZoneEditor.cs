using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// 11 Nov 2026: edit AmbientZone boxes directly in the Scene view.
/// Select a zone, then use the normal toolbar tools:
///   Move (W)            -> arrow handle at the centre of each box (main + additional areas)
///   Scale (R) / Rect (T) -> dot handles on each face to resize the box
///   Transform (Y)       -> both
/// Every change goes through Undo and edits the BoxCollider centre/size, so the
/// GameObject pivot never moves and the child areas stay where they are.
/// </summary>
[CustomEditor(typeof(AmbientZone)), CanEditMultipleObjects]
public sealed class AmbientZoneEditor : Editor
{
    private static readonly Color HandleColor = new Color(0.2f, 0.85f, 1f, 1f);
    private readonly BoxBoundsHandle boundsHandle = new BoxBoundsHandle();
    private bool toolsWereHidden;

    private void OnEnable()
    {
        toolsWereHidden = Tools.hidden;
        boundsHandle.handleColor = HandleColor;
        boundsHandle.wireframeColor = Color.clear; // the zone already draws its own outline
    }

    private void OnDisable() => Tools.hidden = toolsWereHidden;

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
            "Scene view: Move (W) drags a box, Scale (R) / Rect (T) resizes it, Transform (Y) does both. " +
            "Works on the main box and every Additional Area.", MessageType.Info);
        DrawDefaultInspector();
    }

    private void OnSceneGUI()
    {
        var zone = (AmbientZone)target;
        Tool tool = Tools.current;
        bool move = tool == Tool.Move || tool == Tool.Transform;
        bool resize = tool == Tool.Scale || tool == Tool.Rect || tool == Tool.Transform;

        // Hide Unity's own transform gizmo (it sits on the pivot, usually far from the box).
        Tools.hidden = move || resize;
        if (!move && !resize) return;

        EditBox(zone.GetComponent<BoxCollider>(), move, resize);
        if (zone.additionalAreas != null)
            foreach (BoxCollider area in zone.additionalAreas)
                EditBox(area, move, resize);
    }

    private void EditBox(BoxCollider box, bool move, bool resize)
    {
        if (box == null) return;
        Transform t = box.transform;

        if (resize)
        {
            using (new Handles.DrawingScope(HandleColor, t.localToWorldMatrix))
            {
                boundsHandle.center = box.center;
                boundsHandle.size = box.size;
                EditorGUI.BeginChangeCheck();
                boundsHandle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(box, "Resize Ambient Area");
                    box.center = boundsHandle.center;
                    box.size = Vector3.Max(boundsHandle.size, Vector3.one * 0.1f);
                }
            }
        }

        if (move)
        {
            Vector3 worldCenter = t.TransformPoint(box.center);
            Quaternion rotation = Tools.pivotRotation == PivotRotation.Local ? t.rotation : Quaternion.identity;
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(worldCenter, rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(box, "Move Ambient Area");
                box.center = t.InverseTransformPoint(moved);
            }
        }
    }
}
