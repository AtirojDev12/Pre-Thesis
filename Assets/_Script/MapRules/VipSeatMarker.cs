using TMPro;
using UnityEngine;

/// <summary>
/// 23 Nov 2026 (Mr.k): makes the VIP seat easy to see (clip feedback: "where is the VIP seat?").
///   All night : the seat cushion + backrest are gold, a gold "VIP / RESERVED" sign floats above it.
///   VIP here  : (Map Rule 3 running) a gold light shines down on the seat, a gold ring lies on the
///               floor in front of it and the sign pulses, so the escort knows where to go.
/// Put on the VIP CinemaSeat by Tools > Pre-Thesis > One Step > Map Rules. Local look only (no network):
/// every PC reads MapRuleVip.Running (its stage is a SyncVar).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CinemaSeat))]
public sealed class VipSeatMarker : MonoBehaviour
{
    private static readonly Color GoldTint = new Color(1f, 0.78f, 0.28f);
    private static readonly Color GoldLight = new Color(1f, 0.8f, 0.35f);

    [Tooltip("Seat parts painted gold (cushion + backrest; set by the setup tool).")]
    [SerializeField] private Renderer[] paint = new Renderer[0];
    [SerializeField, Min(0.5f)] private float signHeight = 1.45f;

    private CinemaSeat seat;
    private Transform sign;
    private TextMeshPro signText;
    private Light beam;
    private GameObject ring;
    private bool shownRunning;

    private void Start()
    {
        seat = GetComponent<CinemaSeat>();
        PaintGold();
        MakeSign();
        MakeBeam();
        SetRunning(false);
    }

    private void PaintGold()
    {
        foreach (Renderer r in paint)
        {
            if (r == null) continue;
            Material[] list = r.materials; // own copies: only this seat turns gold
            foreach (Material m in list) if (m != null) m.color = GoldTint;
            r.materials = list;
        }
    }

    private Vector3 Floor => seat != null ? seat.FloorPoint : transform.position;

    private void MakeSign()
    {
        var go = new GameObject("VIP Sign", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        go.transform.position = Floor + Vector3.up * signHeight;
        sign = go.transform;
        signText = go.AddComponent<TextMeshPro>();
        signText.rectTransform.sizeDelta = new Vector2(0.9f, 0.45f);
        signText.text = "<size=100%>VIP</size>\n<size=40%>RESERVED</size>";
        signText.enableAutoSizing = true;
        signText.fontSizeMin = 0.1f;
        signText.fontSizeMax = 3f;
        signText.fontStyle = FontStyles.Bold;
        signText.alignment = TextAlignmentOptions.Center;
        signText.color = GoldTint;
        signText.textWrappingMode = TextWrappingModes.NoWrap;
    }

    private void MakeBeam()
    {
        var go = new GameObject("VIP Light");
        go.transform.SetParent(transform, false);
        go.transform.position = Floor + Vector3.up * 3.2f;
        go.transform.rotation = Quaternion.LookRotation(Vector3.down, seat != null ? seat.Facing * Vector3.forward : Vector3.forward);
        beam = go.AddComponent<Light>();
        beam.type = LightType.Spot;
        beam.color = GoldLight;
        beam.range = 5f;
        beam.spotAngle = 38f;
        beam.intensity = 6f;
        beam.shadows = LightShadows.None;

        ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "VIP Floor Ring";
        Collider c = ring.GetComponent<Collider>();
        if (c != null) DestroyImmediate(c);
        ring.transform.SetParent(transform, false);
        Vector3 front = seat != null ? seat.StandPoint : Floor;
        ring.transform.position = front + Vector3.up * 0.02f;
        ring.transform.rotation = Quaternion.identity; // flat on the floor (the seat object is not scaled)
        ring.transform.localScale = new Vector3(0.8f, 0.005f, 0.8f);
        Renderer r = ring.GetComponent<Renderer>();
        if (r != null)
        {
            Material m = MapMockups.NewLitMaterial(); // 28 Nov: the primitive's own material is magenta in a build
            r.sharedMaterial = m;
            m.color = GoldTint;
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", GoldLight * 2f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    private void LateUpdate()
    {
        MapRuleVip vip = MapRuleVip.Instance;
        bool running = vip != null && vip.Running;
        if (running != shownRunning) SetRunning(running);

        if (sign != null)
        {
            Camera cam = RuleFx.LocalCamera();
            if (cam != null) sign.rotation = Quaternion.LookRotation(sign.position - cam.transform.position, Vector3.up);
            float pulse = running ? 0.75f + 0.25f * Mathf.Sin(Time.time * 4f) : 0.6f;
            Color c = GoldTint;
            c.a = pulse;
            signText.color = c;
            sign.position = Floor + Vector3.up * (signHeight + (running ? 0.05f * Mathf.Sin(Time.time * 2f) : 0f));
        }
    }

    private void SetRunning(bool running)
    {
        shownRunning = running;
        if (beam != null) beam.enabled = running;
        if (ring != null) ring.SetActive(running);
    }

    /// <summary>Editor tool.</summary>
    public void EditorSetup(Renderer[] parts) => paint = parts ?? new Renderer[0];
}
