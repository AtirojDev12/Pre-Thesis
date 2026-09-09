using TMPro;
using UnityEngine;

/// <summary>
/// World-space prompt ("Press E to open") that follows the currently targeted
/// interactable and faces the camera. Purely local and cosmetic -- only the
/// local player's PlayerInteractor ever drives this.
///
/// Setup: a World Space Canvas with a TextMeshProUGUI child, this script on the
/// Canvas, promptText assigned. It can live either on the player prefab or in
/// the scene; PlayerInteractor finds it either way.
/// </summary>
public class InteractionPromptUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI promptText;
    [SerializeField] private Vector3 worldOffset = new Vector3(0, 1.5f, 0);

    [Tooltip("Leave empty to resolve the active camera at runtime.")]
    [SerializeField] private Camera targetCamera;

    private Transform _followTarget;
    private Canvas _canvas;
    private string _currentText;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
        Hide();
    }

    public void Show(string text, Transform followTarget)
    {
        if (string.IsNullOrEmpty(text))
        {
            Hide();
            return;
        }

        _followTarget = followTarget;

        // Only reassign when it actually changed -- setting TMP text forces a
        // mesh rebuild, and PlayerInteractor may call this on consecutive frames.
        if (promptText != null && text != _currentText)
        {
            _currentText = text;
            promptText.text = text;
        }

        if (_canvas != null) _canvas.enabled = true;
    }

    public void Hide()
    {
        _followTarget = null;
        _currentText = null;
        if (_canvas != null) _canvas.enabled = false;
    }

    private void LateUpdate()
    {
        if (_followTarget == null) return;

        // Resolved lazily rather than in Awake: in a networked match the local
        // player's camera doesn't exist yet when this object wakes up, and
        // grabbing Camera.main too early can latch onto the wrong one.
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
            if (targetCamera == null) return;
        }

        transform.position = _followTarget.position + worldOffset;
        transform.forward = targetCamera.transform.forward;
    }
}
