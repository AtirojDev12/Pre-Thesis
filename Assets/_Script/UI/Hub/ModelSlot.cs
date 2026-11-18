using UnityEngine;

/// <summary>
/// 17 Nov 2026 (Mr.k): one 3D MODEL the players see, listed inside the GAME HUD prefab
/// (GAME HUD > Model Slots > one child per model). The UI Hub reads this list and shows every
/// model under "3D models", so a designer finds and changes all of them in one place.
///
/// The slot only POINTS at the model's prefab (an item on the floor, an item in the hand, a
/// rule prop ...). The mesh and material are changed in that prefab (from the hub, or with Edit),
/// so every copy in the game changes. Nothing here runs in the game: it is a list.
///
/// Add a model: drag its prefab into "Add a model" in the hub, or run
/// Tools > Pre-Thesis > UI > GAME HUD: Add Missing Parts (it adds new item / rule prop prefabs).
/// </summary>
[AddComponentMenu("Pre-Thesis/Model Slot (GAME HUD list)")]
[DisallowMultipleComponent]
public sealed class ModelSlot : MonoBehaviour
{
    [Tooltip("Name shown in the UI Hub.")]
    [SerializeField] private string label = "";
    [Tooltip("The prefab that holds this model (its mesh and material are edited there).")]
    [SerializeField] private GameObject modelPrefab;
    [Tooltip("Notes for the team: where the players see it.")]
    [TextArea(1, 3)] [SerializeField] private string note = "";

    public string Label => string.IsNullOrEmpty(label) ? name : label;
    public GameObject ModelPrefab => modelPrefab;
    public string Note => note;

#if UNITY_EDITOR
    public void EditorSetup(string shownName, GameObject prefab, string teamNote)
    {
        label = shownName;
        modelPrefab = prefab;
        note = teamNote;
    }
#endif
}
