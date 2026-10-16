using Mirror;
using UnityEngine;

/// <summary>
/// 16 Oct 2026 (Mr.k): the light of ONE toilet room + its wall switch.
/// Toilet rules ask "is it dark in here?" (<see cref="IsDark"/> = the switch is off).
///
/// ZONE LIGHT: separate from building power. The L key, the building switches and
/// the ghost's warning flicker never touch it (the lamps carry IndependentLight).
///
/// LOOK (designer, Inspector): dim, warm lamps that flicker now and then for mood.
/// The flicker is only atmosphere: it is not a ghost warning and does not count
/// as "dark" for the rules. Every PC computes the same flicker from the shared
/// network clock, so all players see the same thing with no extra network traffic.
///
/// Lives on the switch (with a <see cref="RuleInteractable"/>: [E] Turn light off/on).
/// The lamps are mockup children (prefab "RM_ToiletLamp") on the ceiling.
/// SERVER AUTHORITY: the switch state is a SyncVar.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RuleInteractable))]
public sealed class ToiletLight : NetworkBehaviour
{
    [Header("Lamps")]
    [Tooltip("The lamp mockups on the ceiling (their Light components are driven from here).")]
    [SerializeField] private GameObject[] lamps;

    [Header("Look (dim, for mood)")]
    [SerializeField] private Color lampColor = new Color(1f, 0.82f, 0.6f);
    [Tooltip("Brightness of ONE lamp. Low = dim and murky.")]
    [SerializeField, Range(0f, 5f)] private float lampIntensity = 1.2f;
    [Tooltip("How far one lamp reaches (metres).")]
    [SerializeField, Range(1f, 15f)] private float lampRange = 5f;

    [Header("Flicker (mood only)")]
    [SerializeField] private bool flicker = true;
    [Tooltip("Chance that a lamp flickers in each 4-second slot.")]
    [SerializeField, Range(0f, 1f)] private float flickerChance = 0.3f;
    [Tooltip("Small constant buzz in brightness (0 = steady).")]
    [SerializeField, Range(0f, 0.4f)] private float buzz = 0.08f;

    [SyncVar] private bool switchOn = true; // Update reads it every frame, no hook needed

    private RuleInteractable button;
    private Light[][] lights;
    private GameObject[] litLooks;
    private uint[] seeds;

    private const float Slot = 4f;      // seconds per flicker roll
    private const float Burst = 0.9f;   // a flicker lasts this long
    private const float Step = 0.07f;   // on/off speed inside a flicker

    /// <summary>Every machine: the switch is ON.</summary>
    public bool SwitchOn => switchOn;
    /// <summary>Every machine: this room's light is off (the switch). Flicker does not count.</summary>
    public bool IsDark => !switchOn;

    /// <summary>SERVER. Someone used the switch (body, new state).</summary>
    public event System.Action<GameObject, bool> ServerSwitched;

    private void Awake()
    {
        button = GetComponent<RuleInteractable>();
        int n = lamps != null ? lamps.Length : 0;
        lights = new Light[n][];
        litLooks = new GameObject[n];
        seeds = new uint[n];
        for (int i = 0; i < n; i++)
        {
            if (lamps[i] == null) { lights[i] = new Light[0]; continue; }
            lights[i] = lamps[i].GetComponentsInChildren<Light>(true);
            Transform lit = lamps[i].transform.Find("Lit");
            litLooks[i] = lit != null ? lit.gameObject : null;
            Vector3 p = lamps[i].transform.position; // same on every PC
            seeds[i] = (uint)(Mathf.RoundToInt(p.x * 10f) * 73856093 ^ Mathf.RoundToInt(p.z * 10f) * 19349663 ^ (i + 1) * 83492791);
        }
    }

    private void Start()
    {
        if (NetworkMode.HasServerAuthority(this))
        {
            button.ServerUsed += OnPressed;
            button.ServerSetLabel(switchOn ? "Turn light off" : "Turn light on");
        }
    }

    private void OnDestroy()
    {
        if (button != null) button.ServerUsed -= OnPressed;
    }

    private void OnPressed(GameObject who) => ServerSet(!switchOn, who);

    /// <summary>SERVER. Turn the toilet light on / off.</summary>
    public void ServerSet(bool on, GameObject who = null)
    {
        if (!NetworkMode.HasServerAuthority(this) || switchOn == on) return;
        switchOn = on;
        button.ServerSetLabel(on ? "Turn light off" : "Turn light on");
        ServerSwitched?.Invoke(who, on);
    }

    // ---- Look (every machine) --------------------------------------------------------------

    private void Update()
    {
        double t = NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;
        for (int i = 0; i < lights.Length; i++)
        {
            float k = switchOn ? Brightness(i, t) : 0f;
            bool on = k > 0.05f;
            foreach (Light l in lights[i])
            {
                if (l == null) continue;
                if (l.enabled != on) l.enabled = on;
                if (!on) continue;
                l.color = lampColor;
                l.range = lampRange;
                l.intensity = lampIntensity * k;
            }
            GameObject look = litLooks[i];
            if (look != null && look.activeSelf != k > 0.3f) look.SetActive(k > 0.3f);
        }
    }

    /// <summary>0..1 brightness of lamp i at shared time t (same result on every PC).</summary>
    private float Brightness(int i, double t)
    {
        float steady = 1f - buzz * (0.5f + 0.5f * Mathf.Sin((float)(t % 1000.0) * 47f + i * 1.7f));
        if (!flicker || flickerChance <= 0f) return steady;

        long slot = (long)System.Math.Floor(t / Slot);
        float inSlot = (float)(t - slot * Slot);
        uint h = Hash((uint)slot, seeds[i]);
        if ((h & 0xFFFF) / 65535f >= flickerChance || inSlot >= Burst) return steady;

        uint h2 = Hash(h, (uint)(inSlot / Step));
        return (h2 & 3) == 0 ? 0f : steady * (0.35f + 0.65f * ((h2 >> 2) & 0xFF) / 255f);
    }

    private static uint Hash(uint a, uint b)
    {
        uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u + (a << 6) + (a >> 2));
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        return h;
    }

    /// <summary>Editor tool.</summary>
    public void SetLamps(GameObject[] lampObjects) => lamps = lampObjects;
}
