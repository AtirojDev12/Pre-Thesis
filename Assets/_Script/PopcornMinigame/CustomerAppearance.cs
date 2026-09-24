using UnityEngine;

/// <summary>Build-included URP material shared by runtime-created customers.</summary>
public static class CustomerAppearance
{
    private static Material material;

    public static void Apply(Renderer renderer, bool ghost)
    {
        if (material == null) material = Resources.Load<Material>("CustomerBody");
        if (material == null)
        {
            Debug.LogError("[Customers] Missing Resources/CustomerBody material.", renderer);
            return;
        }
        // Property blocks tint each customer without modifying/duplicating the asset.
        renderer.sharedMaterial = material;
        Color color = ghost ? new Color(0.35f, 0.95f, 1f, 0.78f) : new Color(1f, 0.68f, 0.25f, 1f);
        var properties = new MaterialPropertyBlock();
        properties.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(properties);
    }
}
