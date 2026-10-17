using UnityEngine;

/// <summary>Authored cup-body dimensions exclude the lid and straw.</summary>
[DisallowMultipleComponent]
public sealed class DrinkCupVisual : MonoBehaviour
{
    public const float BodyHeight = .224f;
    [SerializeField] private float referenceBodyHeight = .005f;
    [SerializeField] private Vector3 baseCenter;
    [SerializeField] private Vector3 rimCenter;
    [SerializeField] private float rimDiameter = .0028118f;

    public Vector3 BasePosition => transform.TransformPoint(baseCenter);
    public Vector3 RimPosition => transform.TransformPoint(rimCenter);
    public float RimDiameter => transform.TransformVector(Vector3.right * rimDiameter).magnitude;
    private Vector3 BodyCenter => transform.TransformPoint(baseCenter + Vector3.up * (referenceBodyHeight * .5f));

    private void FitBody()
    {
        float height = transform.TransformVector(Vector3.up * referenceBodyHeight).magnitude;
        if (height > .00001f) transform.localScale *= BodyHeight / height;
    }

    public static bool FitInHand(GameObject visual, Vector3 grip)
    {
        var cup = visual.GetComponentInChildren<DrinkCupVisual>();
        if (cup == null) return false;
        cup.FitBody();
        visual.transform.position += grip - cup.BodyCenter;
        return true;
    }

    public static bool Place(GameObject visual, Transform anchor)
    {
        var cup = visual.GetComponentInChildren<DrinkCupVisual>();
        if (cup == null) return false;
        visual.transform.SetParent(null, true);
        visual.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
        cup.FitBody();
        visual.transform.position += anchor.position - cup.BasePosition;
        return true;
    }
}
