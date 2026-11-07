using UnityEngine;

public class BalloonUp : MonoBehaviour
{
    public float floatForce = 12.0f; // ปรับความแรงในการลอยตรงนี้ได้ตามใจชอบ
    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        // ใส่แรงดันขึ้นในแนวแกน Y
        rb.AddForce(Vector3.up * floatForce, ForceMode.Force);
    }
}
