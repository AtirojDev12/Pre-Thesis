using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// 15 Nov 2026 (Mr.k): edit a zone box (RuleZone) in the Scene view.
///   - Move / Rotate the OBJECT as usual: the box and everything under it follow.
///   - Drag the dots on the box faces: resize the box only (children stay where they are).
///   - "Centre the pivot on the box": puts the object's move handle in the middle of the box
///     without moving the box or any child.
/// Every change goes through Undo.
/// </summary>
[CustomEditor(typeof(RuleZone))]
public sealed class RuleZoneEditor : Editor
{
    private static readonly Color HandleColor = new Color(1f, 0.85f, 0.2f, 1f);
    private readonly BoxBoundsHandle boundsHandle = new BoxBoundsHandle();

    private void OnEnable()
    {
        boundsHandle.handleColor = HandleColor;
        boundsHandle.wireframeColor = Color.clear; // the zone draws its own outline
    }

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
            "Move / rotate this object: the box and everything parented under it follow.\n" +
            "Scene view: drag the dots on the box faces to resize the box only.\n" +
            "A player counts as inside only when the feet are in the box.", MessageType.Info);
        DrawDefaultInspector();

        EditorGUILayout.Space(4);
        if (GUILayout.Button(new GUIContent("Centre the pivot on the box",
                "Moves this object's handle to the middle of the main box. The box and every child stay where they are.")))
            foreach (Object o in targets) CentrePivot((RuleZone)o);
    }

    private void OnSceneGUI()
    {
        var zone = (RuleZone)target;
        var so = new SerializedObject(zone);
        SerializedProperty center = so.FindProperty("center");
        SerializedProperty size = so.FindProperty("size");
        SerializedProperty more = so.FindProperty("moreBoxes");

        using (new Handles.DrawingScope(HandleColor, zone.transform.localToWorldMatrix))
        {
            boundsHandle.center = center.vector3Value;
            boundsHandle.size = size.vector3Value;
            EditorGUI.BeginChangeCheck();
            boundsHandle.DrawHandle();
            if (EditorGUI.EndChangeCheck())
            {
                center.vector3Value = boundsHandle.center;
                size.vector3Value = Vector3.Max(boundsHandle.size, Vector3.one * 0.2f);
            }

            for (int i = 0; i < more.arraySize; i++)
            {
                SerializedProperty box = more.GetArrayElementAtIndex(i);
                Bounds b = box.boundsValue;
                boundsHandle.center = b.center;
                boundsHandle.size = b.size;
                EditorGUI.BeginChangeCheck();
                boundsHandle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                    box.boundsValue = new Bounds(boundsHandle.center, Vector3.Max(boundsHandle.size, Vector3.one * 0.2f));
            }
        }
        so.ApplyModifiedProperties(); // records Undo
    }

    /// <summary>Moves the object onto the middle of its main box; the box and all children keep their world place.</summary>
    public static void CentrePivot(RuleZone zone)
    {
        if (zone == null) return;
        Transform t = zone.transform;
        var so = new SerializedObject(zone);
        SerializedProperty center = so.FindProperty("center");
        SerializedProperty more = so.FindProperty("moreBoxes");
        Vector3 offset = center.vector3Value;
        if (offset.sqrMagnitude < 0.000001f) return;

        var kids = new List<Transform>();
        var places = new List<Vector3>();
        var turns = new List<Quaternion>();
        foreach (Transform child in t) { kids.Add(child); places.Add(child.position); turns.Add(child.rotation); }

        var changed = new List<Object> { t };
        changed.AddRange(kids);
        Undo.RecordObjects(changed.ToArray(), "Centre zone pivot");

        t.position = t.TransformPoint(offset);
        for (int i = 0; i < kids.Count; i++) kids[i].SetPositionAndRotation(places[i], turns[i]);

        center.vector3Value = Vector3.zero;
        for (int i = 0; i < more.arraySize; i++)
        {
            SerializedProperty box = more.GetArrayElementAtIndex(i);
            Bounds b = box.boundsValue;
            box.boundsValue = new Bounds(b.center - offset, b.size);
        }
        so.ApplyModifiedProperties();
    }
}
