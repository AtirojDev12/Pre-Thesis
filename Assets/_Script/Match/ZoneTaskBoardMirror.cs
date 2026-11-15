using UnityEngine;

/// <summary>
/// 15 Nov 2026 (Mr.k): a COPY of a task board, e.g. for the Staff Room.
///
/// It shows the same lines, numbers and strike-outs as its source board
/// (<see cref="ZoneTaskList"/>), but it is display only: it counts nothing, so a sale
/// is never counted twice and the zone is still finished by the real board.
///
///   - Plain scene object (no NetworkIdentity): every PC draws it from the source
///     board's already-synced values. 0 extra network traffic.
///   - The text faces the object's FORWARD (blue arrow), like the real boards.
///   - Its look follows the same "Task Board Canvas" screen in the UI Hub.
///
/// Made by Tools > Pre-Thesis > Scenes > Map: Task Boards - Split Ticket Zone + Staff Room Copies.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZoneTaskBoardMirror : MonoBehaviour
{
    [Tooltip("The real task board this copy shows.")]
    [SerializeField] private ZoneTaskList source;

    private ZoneTaskList.BoardView view;

    public ZoneTaskList Source => source;

    private void Start()
    {
        if (source == null)
        {
            Debug.LogWarning("[ZoneTaskBoardMirror] '" + name + "' has no Source board: nothing to show.", this);
            return;
        }
        view = source.BuildBoardOn(transform);
    }

    private void LateUpdate()
    {
        if (view != null && source != null) view.Tick();
    }

#if UNITY_EDITOR
    /// <summary>Editor tool: which board this copy shows.</summary>
    public void EditorSetSource(ZoneTaskList board) => source = board;

    // Shows where the copy will be and which way it reads, before pressing Play.
    private void OnDrawGizmos()
    {
        Vector2 size = source != null ? source.ApproxBoardSize : new Vector2(1.6f, 1f);
        Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0.02f));
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.6f);
    }
#endif
}
