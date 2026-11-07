using UnityEngine;

public class DrawRope : MonoBehaviour
{
    public Transform anchorPoint; 
    private LineRenderer lineRenderer;

    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = 2;
    }

    void Update()
    {
        if (anchorPoint != null)
        {
            lineRenderer.SetPosition(0, transform.position);
            lineRenderer.SetPosition(1, anchorPoint.position);
        }
    }
}
