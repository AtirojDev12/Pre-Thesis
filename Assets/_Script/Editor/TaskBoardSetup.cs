using System.Collections.Generic;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 15 Nov 2026 (Mr.k): task board tools for the map.
///
///   Tools > Pre-Thesis > Scenes > Map: Task Boards - Split Ticket Zone + Staff Room Copies
///     1. Splits the one Ticket Zone board into TWO boards:
///          "Task Board - Tickets" (zone_ticket)  = the ticket lines
///          "Task Board - Food"    (zone_popcorn) = popcorn + drink lines
///        Both still count as ONE zone (ZoneTaskList.CountedZoneOf): the Ticket Zone is done
///        when both boards are done. Targets are kept. The Food board appears next to the
///        Tickets board: move it.
///     2. Makes ONE display-only copy of every task board (ZoneTaskBoardMirror) in front of the
///        Scene view camera, in a row: look at the Staff Room wall first, then move them.
///   Safe to run again: it only adds what is missing. Undo works. Save the scene after.
/// </summary>
public static class TaskBoardSetup
{
    private const string TicketID = ZoneTaskList.TicketZoneID;
    private const string FoodID = "zone_popcorn";
    private const string CopiesRoot = "Task Board Copies (Staff Room)";

    [MenuItem("Tools/Pre-Thesis/Scenes/Map: Task Boards - Split Ticket Zone + Staff Room Copies", false, 61)]
    private static void Run()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Task Boards", "Stop Play mode first.", "OK"); return; }

        var boards = new List<ZoneTaskList>();
        foreach (ZoneTaskList b in Object.FindObjectsByType<ZoneTaskList>(FindObjectsInactive.Include))
            if (b.gameObject.scene.IsValid()) boards.Add(b);
        if (boards.Count == 0)
        {
            EditorUtility.DisplayDialog("Task Boards", "No task board in the open scene. Open the map (Cinema_GamePlay) first.", "OK");
            return;
        }

        var log = new System.Text.StringBuilder();
        ZoneTaskList food = Split(boards, log);
        if (food != null) boards.Add(food);
        int copies = MakeCopies(boards, log);

        EditorSceneManager.MarkSceneDirty(boards[0].gameObject.scene);
        string text = log.Length > 0 ? log.ToString() : "Everything is already in the scene. Nothing changed.";
        Debug.Log("[TaskBoardSetup]\n" + text);
        EditorUtility.DisplayDialog("Task Boards", text + "\nMove the new boards into place, then save the scene (Ctrl+S).", "OK");
    }

    // ---- 1. Split ---------------------------------------------------------------------------

    private static bool IsTicketLine(SerializedProperty task) =>
        task.FindPropertyRelative("kind").intValue == (int)ZoneTaskKind.Ticket;

    /// <summary>Returns the new Food board, or null when there was nothing to split.</summary>
    private static ZoneTaskList Split(List<ZoneTaskList> boards, System.Text.StringBuilder log)
    {
        ZoneTaskList ticket = null;
        foreach (ZoneTaskList b in boards)
        {
            if (b.ZoneID == FoodID) { log.AppendLine("- The Ticket Zone already has a separate Food board. Not split again."); return null; }
            if (b.ZoneID == TicketID) ticket = b;
        }
        if (ticket == null) { log.AppendLine("- No Ticket Zone board (zone_ticket) found. Nothing to split."); return null; }

        var from = new SerializedObject(ticket);
        SerializedProperty tasks = from.FindProperty("tasks");
        int ticketLines = 0, foodLines = 0;
        for (int i = 0; i < tasks.arraySize; i++)
            if (IsTicketLine(tasks.GetArrayElementAtIndex(i))) ticketLines++; else foodLines++;
        if (foodLines == 0 || ticketLines == 0)
        {
            log.AppendLine("- The Ticket Zone board has only one kind of line. Nothing to split.");
            return null;
        }

        // The Food board: a new scene object (its own NetworkIdentity), same settings as the ticket board.
        var go = new GameObject("Task Board - Food");
        Undo.RegisterCreatedObjectUndo(go, "Split Ticket Zone board");
        go.layer = ticket.gameObject.layer;
        go.transform.SetParent(ticket.transform.parent, false);
        float width = from.FindProperty("boardWidth").floatValue;
        go.transform.SetPositionAndRotation(ticket.transform.position - ticket.transform.right * (width + 0.25f), ticket.transform.rotation);
        go.AddComponent<NetworkIdentity>();
        ZoneTaskList food = go.AddComponent<ZoneTaskList>();
        EditorUtility.CopySerialized(ticket, food);

        var to = new SerializedObject(food);
        to.FindProperty("zoneID").stringValue = FoodID;
        to.FindProperty("title").stringValue = "FOOD & DRINKS";
        SerializedProperty foodTasks = to.FindProperty("tasks");
        for (int i = foodTasks.arraySize - 1; i >= 0; i--)
            if (IsTicketLine(foodTasks.GetArrayElementAtIndex(i))) foodTasks.DeleteArrayElementAtIndex(i);
        to.ApplyModifiedPropertiesWithoutUndo();

        Undo.RecordObject(ticket, "Split Ticket Zone board");
        from.FindProperty("title").stringValue = "TICKETS";
        for (int i = tasks.arraySize - 1; i >= 0; i--)
            if (!IsTicketLine(tasks.GetArrayElementAtIndex(i))) tasks.DeleteArrayElementAtIndex(i);
        from.ApplyModifiedProperties();
        Undo.RecordObject(ticket.gameObject, "Split Ticket Zone board");
        ticket.gameObject.name = "Task Board - Tickets";

        log.AppendLine($"- Split the Ticket Zone board: Tickets ({ticketLines} lines) + Food ({foodLines} lines). Both count as ONE zone.");
        log.AppendLine("  The Food board stands next to the Tickets board.");
        Selection.activeGameObject = go;
        return food;
    }

    // ---- 2. Staff Room copies -------------------------------------------------------------------

    private static int MakeCopies(List<ZoneTaskList> boards, System.Text.StringBuilder log)
    {
        var have = new HashSet<ZoneTaskList>();
        foreach (ZoneTaskBoardMirror m in Object.FindObjectsByType<ZoneTaskBoardMirror>(FindObjectsInactive.Include))
            if (m.Source != null) have.Add(m.Source);

        var missing = new List<ZoneTaskList>();
        foreach (ZoneTaskList b in boards) if (!have.Contains(b)) missing.Add(b);
        if (missing.Count == 0) return 0;
        missing.Sort((first, second) => string.CompareOrdinal(first.name, second.name));

        GameObject root = GameObject.Find(CopiesRoot);
        if (root == null)
        {
            root = new GameObject(CopiesRoot);
            Undo.RegisterCreatedObjectUndo(root, "Task board copies");
        }

        // A row in front of the Scene view camera, facing it (same as the other board tool).
        Vector3 origin = Vector3.zero;
        Quaternion facing = Quaternion.identity;
        SceneView view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            origin = view.pivot;
            Vector3 flat = view.camera != null ? view.camera.transform.forward : Vector3.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.001f) facing = Quaternion.LookRotation(-flat.normalized, Vector3.up);
        }

        float total = 0f;
        foreach (ZoneTaskList b in missing) total += b.ApproxBoardSize.x + 0.3f;
        float x = -total * 0.5f;
        foreach (ZoneTaskList b in missing)
        {
            float w = b.ApproxBoardSize.x;
            var go = new GameObject("Task Board Copy - " + b.Title);
            Undo.RegisterCreatedObjectUndo(go, "Task board copies");
            go.layer = b.gameObject.layer;
            go.transform.SetParent(root.transform, false);
            go.transform.SetPositionAndRotation(origin + facing * new Vector3(x + w * 0.5f, 0f, 0f), facing);
            go.AddComponent<ZoneTaskBoardMirror>().EditorSetSource(b);
            x += w + 0.3f;
        }

        log.AppendLine($"- Made {missing.Count} board cop{(missing.Count == 1 ? "y" : "ies")} in front of the Scene view (under \"{CopiesRoot}\").");
        log.AppendLine("  They only show the real boards' progress. Read them from the side the cyan line points to.");
        return missing.Count;
    }
}
