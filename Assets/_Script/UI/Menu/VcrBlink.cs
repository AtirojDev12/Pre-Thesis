using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Blinks a UI text / image on and off forever, like a VCR on-screen message
/// ("PRESS PLAY TO CONTINUE"). Half the period on, half off, hard cut (no fade).
/// Uses real time, so it keeps blinking even if the game is paused.
///
/// Added by Tools > Pre-Thesis > Scenes > Menu: Add Timecode + Blink.
/// </summary>
[DisallowMultipleComponent]
public class VcrBlink : MonoBehaviour
{
    [Tooltip("Seconds for one on + off cycle.")]
    [SerializeField] private float period = 1.1f;

    [Tooltip("The text / image that blinks. Empty = the first one on this object or its children.")]
    [SerializeField] private Graphic target;

    private void Awake()
    {
        if (target == null) target = GetComponentInChildren<Graphic>(true);
    }

    private void OnDisable()
    {
        if (target != null) target.enabled = true; // never leave it hidden
    }

    private void Update()
    {
        if (target == null || period <= 0.01f) return;
        bool on = Time.unscaledTime % period < period * 0.5f;
        if (target.enabled != on) target.enabled = on;
    }
}
