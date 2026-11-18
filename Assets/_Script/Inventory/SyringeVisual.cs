using UnityEngine;

/// <summary>
/// 17 Nov 2026 (Mr.k): the SYRINGE model (held in the hand and lying on the floor).
/// Colours only: the body and the liquid. The paid and the free syringe look different.
/// Swap the model inside the prefab when the art is ready (keep this component).
/// Made by Tools > Pre-Thesis > One Step > Items: Free + Paid Walkies, Flashlights, Syringes.
/// </summary>
public sealed class SyringeVisual : MonoBehaviour
{
    [Tooltip("The liquid inside (drawn in Liquid Colour). Every other renderer uses Body Colour.")]
    [SerializeField] private Renderer liquidRenderer;
    [SerializeField] private Color bodyColor = new Color(0.85f, 0.88f, 0.9f);
    [SerializeField] private Color liquidColor = new Color(0.2f, 0.9f, 0.7f);

    private void Awake()
    {
        var properties = new MaterialPropertyBlock();
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            Color c = r == liquidRenderer ? liquidColor : bodyColor;
            properties.SetColor("_BaseColor", c);
            properties.SetColor("_Color", c);
            r.SetPropertyBlock(properties);
        }
    }

#if UNITY_EDITOR
    public void EditorSetup(Renderer liquid, Color body, Color liquidColour)
    {
        liquidRenderer = liquid;
        bodyColor = body;
        liquidColor = liquidColour;
    }
#endif
}
