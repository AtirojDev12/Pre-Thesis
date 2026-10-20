using UnityEngine;

/// <summary>
/// 20 Oct 2026 (Mr.k) bug: pressing Start showed "Display 1 - No cameras rendering".
/// The lobby bodies (with their cameras) are removed first and the map loads after
/// (RoHRoomManager, Editor-freeze fix), and the lobby scene camera was switched off
/// when your body spawned. For those frames no camera existed.
///
/// This invisible camera turns itself on ONLY while no other camera is on, and draws
/// plain black (UI overlays still draw on top). Created by itself, no scene change.
/// </summary>
[DisallowMultipleComponent]
public sealed class NoCameraFallback : MonoBehaviour
{
    private Camera cam;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (FindAnyObjectByType<NoCameraFallback>() != null) return;
        var go = new GameObject("No Camera Fallback");
        DontDestroyOnLoad(go);
        go.AddComponent<NoCameraFallback>();
    }

    private void Awake()
    {
        cam = gameObject.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.cullingMask = 0;     // draws nothing but black
        cam.depth = -100f;
        cam.enabled = false;
        // No AudioListener on purpose: never a second listener next to the player's.
    }

    private void LateUpdate()
    {
        int others = Camera.allCamerasCount - (cam.enabled ? 1 : 0);
        bool needed = others == 0;
        if (cam.enabled != needed) cam.enabled = needed;
    }
}
