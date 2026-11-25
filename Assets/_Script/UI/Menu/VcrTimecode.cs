using TMPro;
using UnityEngine;

/// <summary>
/// VCR tape counter for the main menu ("Time"): starts at 0:00:00 when the scene
/// starts and counts up every second (H:MM:SS). Uses real time, so it keeps
/// counting even if the game is paused (timeScale 0).
///
/// Added by Tools > Pre-Thesis > Scenes > Menu: Add Timecode + Blink.
/// </summary>
[DisallowMultipleComponent]
public class VcrTimecode : MonoBehaviour
{
    [SerializeField] private TMP_Text label;

    private float startTime;
    private int lastShown = -1;

    private void Awake()
    {
        if (label == null) label = GetComponentInChildren<TMP_Text>(true);
        startTime = Time.realtimeSinceStartup; // counted from here, once per scene load
        Show(0);
    }

    private void Update()
    {
        int seconds = Mathf.FloorToInt(Time.realtimeSinceStartup - startTime);
        if (seconds != lastShown) Show(seconds);
    }

    private void Show(int seconds)
    {
        lastShown = seconds;
        if (label == null) return;
        int h = seconds / 3600, m = seconds / 60 % 60, s = seconds % 60;
        label.SetText("{0}:{1:00}:{2:00}", h, m, s);
    }
}
