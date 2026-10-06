using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LightSwitchSetup
{
    private const string Menu = "Tools/Pre-Thesis/Lighting/Set Up Selected Light Switch";

    [MenuItem(Menu)]
    public static void SetUpSelected()
    {
        GameObject target = Selection.activeGameObject;
        if (target == null || !target.scene.IsValid() || EditorApplication.isPlaying) return;
        if (target.GetComponentInParent<Mirror.NetworkIdentity>() != null && target.GetComponent<Mirror.NetworkIdentity>() == null)
        {
            Debug.LogError("Choose the root object with the NetworkIdentity. Mirror does not support nested identities.", target);
            return;
        }

        Undo.SetCurrentGroupName("Set up light switch");
        int group = Undo.GetCurrentGroup();
        if (target.GetComponent<Mirror.NetworkIdentity>() == null)
            Undo.AddComponent<Mirror.NetworkIdentity>(target);
        if (target.GetComponent<LightSwitchInteractable>() == null)
            Undo.AddComponent<LightSwitchInteractable>(target);

        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        bool hasSolidCollider = false;
        foreach (Collider collider in colliders)
            if (collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy) hasSolidCollider = true;
        if (!hasSolidCollider)
        {
            BoxCollider collider = Undo.AddComponent<BoxCollider>(target);
            // Unity fits this automatically when the mesh is on the same object.
            // Otherwise fit the child renderers in the switch's local space.
            if (target.GetComponent<MeshFilter>() == null)
            {
                bool hasBounds = false;
                Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
                foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
                {
                    Bounds local = renderer.localBounds;
                    for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3(x, y, z));
                        Vector3 point = target.transform.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                        if (!hasBounds) { bounds = new Bounds(point, Vector3.zero); hasBounds = true; }
                        else bounds.Encapsulate(point);
                    }
                }
                if (hasBounds)
                {
                    collider.center = bounds.center;
                    collider.size = Vector3.Max(bounds.size, Vector3.one * 0.01f);
                }
            }
        }
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(target.scene);
        Debug.Log("Light switch ready: aim at it within 3 metres and press E. Save the scene to keep the setup.", target);
    }

    [MenuItem(Menu, true)]
    private static bool CanSetUp() => !EditorApplication.isPlaying && Selection.activeGameObject != null
        && Selection.activeGameObject.scene.IsValid();
}
