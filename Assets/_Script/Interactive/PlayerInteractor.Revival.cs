using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class PlayerInteractor
{
    [Header("Teammate revival")]
    [SerializeField] private float reviveRange = 2.5f;
    [SerializeField] private float reviveHoldSeconds = 5f;
    [SerializeField] private float reviveMarkerSize = 38f;
    [SerializeField, Range(0f, 89f)] private float reviveFacingHalfAngle = 60f;
    private PlayerHealth _localReviveTarget;
    private GUIStyle _reviveInstructionStyle;

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
        bool canAct = _health != null && !_health.IsDead && !_health.IsDowned && !GameplayInput.Blocked;
        bool keyHeld = Keyboard.current != null && Keyboard.current[interactKey].isPressed;
        if (!keyHeld || !canAct) _localReviveTarget = null;
        if (canAct && _localReviveTarget != null)
        {
            if (IsNearbyDowned(_localReviveTarget)) _reviveLookTarget = _localReviveTarget;
        }
        else if (canAct)
        {
            float closest = reviveRange * reviveRange;
            foreach (PlayerHealth candidate in PlayerRegistry.All)
            {
                if (!IsNearbyDowned(candidate)) continue;
                float distance = (candidate.transform.position - transform.position).sqrMagnitude;
                if (distance > closest) continue;
                closest = distance;
                _reviveLookTarget = candidate;
            }
        }
        bool holding = canAct && keyHeld && CanRevive(_reviveLookTarget);
        if (holding) _localReviveTarget = _reviveLookTarget;
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
            !IsNearbyDowned(target) || !IsFacingReviveTarget(target)) return false;
        return HasReviveLineOfSight(target);
    }

    private bool IsNearbyDowned(PlayerHealth target)
    {
        return target != null && target != _health && !target.IsDead && target.IsDowned &&
            Vector3.Distance(transform.position, target.transform.position) <= reviveRange;
    }

    private bool IsFacingReviveTarget(PlayerHealth target)
    {
        Vector3 direction = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        return direction.sqrMagnitude < 0.0001f || Vector3.Angle(forward, direction) <= reviveFacingHalfAngle;
    }

    private bool HasReviveLineOfSight(PlayerHealth target)
    {
        Vector3 origin = transform.position + Vector3.up * 0.45f;
        Vector3 destination = target.transform.position + Vector3.up * 0.45f;
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

    private void OnGUI()
    {
        if (!NetworkMode.IsLocalController(this) || _reviveLookTarget == null || GameplayInput.Blocked) return;
        bool facing = IsFacingReviveTarget(_reviveLookTarget);
        bool clear = HasReviveLineOfSight(_reviveLookTarget);
        if (_reviveInstructionStyle == null)
            _reviveInstructionStyle = new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, fontSize = 22, fontStyle = FontStyle.Bold };
        _reviveInstructionStyle.normal.textColor = Color.white;
        Vector3 direction = _reviveLookTarget.transform.position - transform.position;
        string arrow = Vector3.Dot(transform.right, direction) >= 0f ? ">" : "<";
        string instruction = !facing ? arrow + " Turn toward teammate to revive " + arrow :
            !clear ? "Path to teammate blocked" : "Hold " + interactKey + " to revive teammate";
        GUI.Label(new Rect(0f, Screen.height - 145f, Screen.width, 40f), instruction, _reviveInstructionStyle);
        Vector3 screen = new Vector3(Screen.width * 0.5f, 90f, 1f);
        bool inRange = facing && clear;
        float proximity = 1f;
        float size = reviveMarkerSize;
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
