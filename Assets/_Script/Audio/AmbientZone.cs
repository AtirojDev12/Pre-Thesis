using UnityEngine;

[RequireComponent(typeof(BoxCollider)), DisallowMultipleComponent]
public sealed class AmbientZone : MonoBehaviour
{
    public AudioClip clip;
    [Range(0, 1)] public float volume = 0.5f;
    public int priority;

    public bool Contains(Vector3 position)
    {
        var box = GetComponent<BoxCollider>();
        Vector3 local = transform.InverseTransformPoint(position) - box.center;
        Vector3 half = box.size * 0.5f;
        return isActiveAndEnabled && box.enabled &&
            Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    private void Reset() => GetComponent<BoxCollider>().isTrigger = true;
}
