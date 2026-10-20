using UnityEngine;

/// <summary>Optional extra on an EXISTING relocation point, e.g. for a bottle on a shelf.</summary>
[DisallowMultipleComponent]
public sealed class GhostFavorTrailDestination : MonoBehaviour
{
    [Tooltip("Optional point on the floor where a player can stand to pick up the bottle. Leave empty for automatic sampling.")]
    public Transform pickupPoint;
}
