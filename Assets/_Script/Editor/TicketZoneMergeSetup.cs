using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 5 Nov 2026 (Mr.k): Tickets and Popcorn &amp; Water are ONE zone, the "Ticket Zone"
/// (Zone Quest Detail). The map had two task boards (zone_ticket + zone_popcorn), so
/// MatchDirector counted them as two zones.
///
///   Tools > Pre-Thesis > Scenes > Map: Merge Ticket + Popcorn Boards (one Ticket Zone)
///       Moves every line of the popcorn board onto the ticket board (zone_ticket,
///       title "TICKET ZONE") and deletes the popcorn board. Undo works. Save the scene.
///
/// Result: the map has 3 zones (Ticket Zone, Screen Rooms, Toilets). Ticket and popcorn
/// sales already report to every board, so no gameplay script changes.
/// </summary>
public static class TicketZoneMergeSetup
{
    private const string TicketID = "zone_ticket";
    private const string PopcornID = "zone_popcorn";
    private const string Title = "TICKET ZONE";

    [MenuItem("Tools/Pre-Thesis/Scenes/Map: Merge Ticket + Popcorn Boards (one Ticket Zone)", false, 61)]
    private static void Merge()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Ticket Zone", "Stop Play mode first.", "OK"); return; }

        ZoneTaskList ticket = null, popcorn = null;
        foreach (ZoneTaskList board in Object.FindObjectsByType<ZoneTaskList>(FindObjectsInactive.Include))
        {
            if (board.ZoneID == TicketID && ticket == null) ticket = board;
            else if (board.ZoneID == PopcornID && popcorn == null) popcorn = board;
        }

        if (ticket == null && popcorn == null)
        {
            EditorUtility.DisplayDialog("Ticket Zone", "No ticket or popcorn task board in the open scene. Open the map (Cinema_GamePlay) first.", "OK");
            return;
        }

        if (popcorn == null)
        {
            SetTitle(ticket);
            EditorSceneManager.MarkSceneDirty(ticket.gameObject.scene);
            Debug.Log("[TicketZoneMergeSetup] Only one board found (zone_ticket). Title set to TICKET ZONE. Save the scene.", ticket);
            return;
        }

        if (ticket == null)
        {
            // Only the popcorn board: it becomes the Ticket Zone board.
            var only = new SerializedObject(popcorn);
            Undo.RecordObject(popcorn, "Ticket Zone board");
            only.FindProperty("zoneID").stringValue = TicketID;
            only.FindProperty("title").stringValue = Title;
            only.ApplyModifiedProperties();
            popcorn.gameObject.name = "Task Board - Ticket Zone";
            EditorSceneManager.MarkSceneDirty(popcorn.gameObject.scene);
            Debug.Log("[TicketZoneMergeSetup] The popcorn board is now the Ticket Zone board (zone_ticket). Add ticket lines in its Inspector, then save the scene.", popcorn);
            return;
        }

        var to = new SerializedObject(ticket);
        var from = new SerializedObject(popcorn);
        SerializedProperty toTasks = to.FindProperty("tasks");
        SerializedProperty fromTasks = from.FindProperty("tasks");
        int moved = 0;
        for (int i = 0; i < fromTasks.arraySize; i++)
        {
            SerializedProperty src = fromTasks.GetArrayElementAtIndex(i);
            int at = toTasks.arraySize;
            toTasks.InsertArrayElementAtIndex(at);
            SerializedProperty dst = toTasks.GetArrayElementAtIndex(at);
            dst.FindPropertyRelative("label").stringValue = src.FindPropertyRelative("label").stringValue;
            dst.FindPropertyRelative("kind").intValue = src.FindPropertyRelative("kind").intValue;
            dst.FindPropertyRelative("flavor").intValue = src.FindPropertyRelative("flavor").intValue;
            dst.FindPropertyRelative("movie").intValue = src.FindPropertyRelative("movie").intValue;
            dst.FindPropertyRelative("customer").intValue = src.FindPropertyRelative("customer").intValue;
            dst.FindPropertyRelative("target").intValue = src.FindPropertyRelative("target").intValue;
            moved++;
        }
        to.FindProperty("title").stringValue = Title;
        to.ApplyModifiedProperties(); // records Undo
        ticket.gameObject.name = "Task Board - Ticket Zone";

        Undo.DestroyObjectImmediate(popcorn.gameObject);
        EditorSceneManager.MarkSceneDirty(ticket.gameObject.scene);
        Selection.activeObject = ticket.gameObject;
        Debug.Log("[TicketZoneMergeSetup] Moved " + moved + " popcorn / drink line(s) onto the Ticket Zone board and removed the popcorn board. " +
                  "The map now has one Ticket Zone. Save the scene.", ticket);
    }

    private static void SetTitle(ZoneTaskList board)
    {
        var so = new SerializedObject(board);
        so.FindProperty("title").stringValue = Title;
        so.ApplyModifiedProperties();
        board.gameObject.name = "Task Board - Ticket Zone";
    }
}
