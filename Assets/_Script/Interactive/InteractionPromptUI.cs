using UnityEngine;
using TMPro;

/// <summary>
/// Simple world-space prompt (e.g. "Press E to open") that follows the
/// currently targeted interactable and faces the camera.
///
/// Setup: create a World Space Canvas with a TextMeshProUGUI child,
/// attach this script to the Canvas, and assign promptText.
/// </summary>
public class InteractionPromptUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI promptText;
    [SerializeField] private Vector3 worldOffset = new Vector3(0, 1.5f, 0);
    [SerializeField] private Camera targetCamera;

    private Transform _followTarget;
    private Canvas _canvas;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
        if (targetCamera == null) targetCamera = Camera.main;
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
        if (promptText != null) promptText.text = text;
        if (_canvas != null) _canvas.enabled = true;
    }

    public void Hide()
    {
        _followTarget = null;
        if (_canvas != null) _canvas.enabled = false;
    }

    private void LateUpdate()
    {
        if (_followTarget == null) return;

        transform.position = _followTarget.position + worldOffset;

        if (targetCamera != null)
        {
            transform.forward = targetCamera.transform.forward;
        }
    }
}
