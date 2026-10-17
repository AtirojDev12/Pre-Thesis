using UnityEngine;

/// <summary>Measures the bucket body independently of the popcorn piled above its rim.</summary>
[DisallowMultipleComponent]
public sealed class PopcornBucketVisual : MonoBehaviour
{
    public const float BodyHeight = .24f;
    [SerializeField] private float referenceBodyHeight;
    [SerializeField] private Vector3 baseCenter;

    public static bool FitInHand(GameObject visual, Vector3 grip)
    {
        var bucket = visual.GetComponentInChildren<PopcornBucketVisual>();
        if (bucket == null) return false;
        float height = bucket.transform.TransformVector(Vector3.up * bucket.referenceBodyHeight).magnitude;
        if (height > .00001f) bucket.transform.localScale *= BodyHeight / height;
        Vector3 center = bucket.transform.TransformPoint(bucket.baseCenter + Vector3.up * (bucket.referenceBodyHeight * .5f));
        visual.transform.position += grip - center;
        return true;
    }
}
