using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Local white silhouette shell. Does not replace authored materials or add colliders.</summary>
public sealed class InteractionOutline : MonoBehaviour
{
    private readonly List<GameObject> shells = new List<GameObject>();
    private Material material;
    private bool built;

    public void SetVisible(bool visible)
    {
        if (visible && !built) Build();
        foreach (GameObject shell in shells) if (shell != null) shell.SetActive(visible);
    }

    private void Build()
    {
        built = true;
        Shader shader = Resources.Load<Shader>("InteractionOutline");
        if (shader == null) return;
        material = new Material(shader);
        foreach (Renderer source in GetComponentsInChildren<Renderer>(true))
        {
            if (!(source is MeshRenderer) && !(source is SkinnedMeshRenderer)) continue;
            if (source.GetComponentInParent<InteractionOutline>() != this) continue;
            MeshFilter filter = source.GetComponent<MeshFilter>();
            SkinnedMeshRenderer skin = source as SkinnedMeshRenderer;
            Mesh mesh = skin != null ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
            if (mesh == null) continue;
            GameObject shell = new GameObject("White Interaction Outline");
            shell.layer = LayerMask.NameToLayer("Ignore Raycast");
            shell.transform.SetParent(source.transform, false);
            Renderer renderer;
            if (skin != null)
            {
                SkinnedMeshRenderer copy = shell.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh = mesh;
                copy.bones = skin.bones;
                copy.rootBone = skin.rootBone;
                copy.localBounds = skin.localBounds;
                renderer = copy;
            }
            else
            {
                shell.AddComponent<MeshFilter>().sharedMesh = mesh;
                renderer = shell.AddComponent<MeshRenderer>();
            }
            Material[] materials = new Material[mesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            shells.Add(shell);
        }
    }

    private void OnDisable() => SetVisible(false);
    private void OnDestroy()
    {
        foreach (GameObject shell in shells) if (shell != null) Destroy(shell);
        if (material != null) Destroy(material);
    }
}
