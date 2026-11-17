using UnityEngine;

/// <summary>The shared visual for held and world radios. No inventory or input logic.</summary>
public sealed class WalkieTalkieVisual : MonoBehaviour
{
    [SerializeField] private Renderer ledRenderer;
    [Tooltip("17 Nov: colour of the body (free and paid walkies look different).")]
    [SerializeField] private Color bodyColor = new Color(0.12f, 0.12f, 0.13f);
    private MaterialPropertyBlock properties;

    private void Awake()
    {
        properties = new MaterialPropertyBlock();
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
        {
            if (renderer == ledRenderer) continue;
            properties.SetColor("_BaseColor", bodyColor);
            properties.SetColor("_Color", bodyColor);
            renderer.SetPropertyBlock(properties);
        }
    }

#if UNITY_EDITOR
    public void EditorSetBodyColor(Color color) => bodyColor = color;
#endif

    public void SetPower(bool powered, bool transmitting = false)
    {
        if (ledRenderer == null) return;
        if (properties == null) properties = new MaterialPropertyBlock();
        Color color = !powered ? new Color(0.25f, 0.05f, 0.05f)
            : transmitting ? new Color(1f, 0.2f, 0.15f) : new Color(0.2f, 1f, 0.3f);
        properties.SetColor("_BaseColor", color);
        properties.SetColor("_Color", color);
        ledRenderer.SetPropertyBlock(properties);
    }
}
