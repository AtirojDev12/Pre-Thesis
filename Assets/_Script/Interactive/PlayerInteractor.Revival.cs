using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class PlayerInteractor
{
    [Header("Teammate revival")]
    [SerializeField] private float reviveRange = 2.5f;
    [SerializeField] private float reviveMarkerDistance = 18f;
    [SerializeField] private float reviveHoldSeconds = 5f;
    [SerializeField] private float reviveMarkerSize = 38f;

    private PlayerHealth _reviveLookTarget;
    private PlayerHealth _serverReviveTarget;
    private float _reviveHeartbeat;
    private float _nextReviveHeartbeat;
    private bool _wasReviveHolding;
    [SyncVar] private float _reviveProgress;
    private Texture2D _reviveCircle;
    private GUIStyle _reviveKeyStyle;

    private void UpdateRevival()
    {
        if (NetworkMode.HasServerAuthority(this))
        {
            if (_serverReviveTarget != null)
            {
                if (Time.time - _reviveHeartbeat > 0.4f || !CanRevive(_serverReviveTarget))
                    ResetRevival();
                else
                {
                    _reviveProgress += Time.deltaTime / Mathf.Max(0.1f, reviveHoldSeconds);
                    if (_reviveProgress >= 1f)
                    {
                        _serverReviveTarget.Revive();
                        ResetRevival();
                    }
                }
            }
        }
        if (!NetworkMode.IsLocalController(this)) return;

        _reviveLookTarget = null;
        if (_health == null || _health.IsDead || _health.IsDowned || GameplayInput.Blocked) return;
        RaycastHit[] hits = Physics.RaycastAll(rayOrigin.position, rayOrigin.forward,
            reviveMarkerDistance, interactableLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.transform.IsChildOf(transform)) continue;
            PlayerHealth candidate = hit.collider.GetComponentInParent<PlayerHealth>();
            if (candidate != null && candidate != _health && candidate.IsDowned && !candidate.IsDead)
                _reviveLookTarget = candidate;
            break; // Walls and other characters occlude the marker.
        }

        bool holding = _reviveLookTarget != null && Keyboard.current != null &&
            Keyboard.current[interactKey].isPressed &&
            Vector3.Distance(transform.position, _reviveLookTarget.transform.position) <= reviveRange;
        if (!holding && !_wasReviveHolding) return;
        if (!holding)
        {
            _wasReviveHolding = false;
            if (NetworkMode.IsOffline) ResetRevival();
            else CmdReviveHeartbeat(null);
            return;
        }
        if (Time.time < _nextReviveHeartbeat) return;
        _wasReviveHolding = true;
        _nextReviveHeartbeat = Time.time + 0.1f;
        if (NetworkMode.IsOffline) ReceiveReviveHeartbeat(holding ? _reviveLookTarget : null);
        else CmdReviveHeartbeat(holding ? _reviveLookTarget.netIdentity : null);
    }

    [Command]
    private void CmdReviveHeartbeat(NetworkIdentity target)
    {
        ReceiveReviveHeartbeat(target != null ? target.GetComponent<PlayerHealth>() : null);
    }

    private void ReceiveReviveHeartbeat(PlayerHealth target)
    {
        if (!CanRevive(target)) { ResetRevival(); return; }
        if (_serverReviveTarget != target) _reviveProgress = 0f;
        _serverReviveTarget = target;
        _reviveHeartbeat = Time.time;
    }

    private bool CanRevive(PlayerHealth target)
    {
        if (_health == null || _health.IsDead || _health.IsDowned || target == null ||
            target == _health || target.IsDead || !target.IsDowned ||
            Vector3.Distance(transform.position, target.transform.position) > reviveRange) return false;
        Vector3 origin = rayOrigin.position;
        Vector3 destination = ReviveAnchor(target);
        foreach (RaycastHit hit in Physics.RaycastAll(origin, destination - origin,
            Vector3.Distance(origin, destination), interactableLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(target.transform)) continue;
            return false;
        }
        return true;
    }

    private void ResetRevival()
    {
        _serverReviveTarget = null;
        _reviveProgress = 0f;
    }

    private static Vector3 ReviveAnchor(PlayerHealth target)
    {
        Animator animator = target.GetComponentInChildren<Animator>();
        Transform hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        return hips != null ? hips.position + Vector3.up * 0.2f : target.transform.position + Vector3.up * 0.45f;
    }

    private void OnGUI()
    {
        if (!NetworkMode.IsLocalController(this) || _reviveLookTarget == null || GameplayInput.Blocked) return;
        Camera camera = rayOrigin.GetComponent<Camera>();
        if (camera == null) camera = GetComponentInChildren<Camera>();
        if (camera == null) return;
        Vector3 screen = camera.WorldToScreenPoint(ReviveAnchor(_reviveLookTarget));
        if (screen.z <= 0f) return;
        float distance = Vector3.Distance(transform.position, _reviveLookTarget.transform.position);
        if (distance >= reviveMarkerDistance) return;
        bool inRange = distance <= reviveRange;
        float proximity = Mathf.InverseLerp(reviveMarkerDistance, reviveRange, distance);
        float size = Mathf.Lerp(12f, reviveMarkerSize, proximity);
        if (_reviveCircle == null)
        {
            _reviveCircle = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            _reviveCircle.hideFlags = HideFlags.HideAndDontSave;
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float radius = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f));
                    _reviveCircle.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(31f - radius)));
                }
            _reviveCircle.Apply();
        }
        Color previous = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, Mathf.Lerp(0f, 0.9f, proximity));
        Rect circle = new Rect(screen.x - size / 2f, Screen.height - screen.y - size / 2f, size, size);
        GUI.DrawTexture(circle, _reviveCircle);
        if (inRange)
        {
            if (_reviveKeyStyle == null)
                _reviveKeyStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 23, fontStyle = FontStyle.Bold };
            _reviveKeyStyle.normal.textColor = Color.black;
            GUI.color = Color.white;
            GUI.Label(circle, interactKey.ToString(), _reviveKeyStyle);
            if (_reviveProgress > 0f)
            {
                Rect bar = new Rect(screen.x - 45f, circle.yMax + 8f, 90f, 6f);
                GUI.color = new Color(0f, 0f, 0f, 0.7f);
                GUI.DrawTexture(bar, Texture2D.whiteTexture);
                GUI.color = new Color(0.4f, 1f, 0.7f);
                bar.width *= Mathf.Clamp01(_reviveProgress);
                GUI.DrawTexture(bar, Texture2D.whiteTexture);
            }
        }
        GUI.color = previous;
    }

    private void OnDestroy()
    {
        if (_reviveCircle != null) Destroy(_reviveCircle);
    }
}
