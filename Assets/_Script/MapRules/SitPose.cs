using UnityEngine;

/// <summary>
/// 23 Nov 2026 (Mr.k): a SITTING pose for any humanoid model (players, ghost NPCs) without a
/// sit animation clip. After the Animator, each leg is turned: the thigh points forward
/// (almost flat), the shin points down. Runs before CustomerModelVisual grounds the ghost
/// models, so their feet stay on the floor and the hips end up at seat height by themselves.
/// Players are lowered by the seat code (SeatSystem). LOCAL ONLY, cosmetic.
/// Angles: MapRuleTuning (Seats).
/// </summary>
[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public sealed class SitPose : MonoBehaviour
{
    public bool Sitting { get; set; }

    private Animator animator;
    private Transform[] upper = new Transform[2], lower = new Transform[2], foot = new Transform[2];
    private bool ready, failed;

    /// <summary>The SitPose of the humanoid Animator under <paramref name="root"/> (added once), or null.</summary>
    public static SitPose For(GameObject root)
    {
        if (root == null) return null;
        foreach (Animator a in root.GetComponentsInChildren<Animator>(true))
        {
            if (a == null || a.avatar == null || !a.avatar.isHuman) continue;
            SitPose p = a.GetComponent<SitPose>();
            return p != null ? p : a.gameObject.AddComponent<SitPose>();
        }
        return null;
    }

    private void LateUpdate()
    {
        if (!Sitting || failed) return;
        if (!ready && !Init()) return;
        MapRuleTuning t = MapRuleTuning.Current;
        Vector3 forward = animator.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) return;
        forward.Normalize();
        Vector3 thighWant = Quaternion.AngleAxis(t.sitThighDownDegrees, Vector3.Cross(Vector3.up, forward)) * forward;
        Vector3 shinWant = Quaternion.AngleAxis(-t.sitShinForwardDegrees, Vector3.Cross(Vector3.up, forward)) * Vector3.down;
        for (int i = 0; i < 2; i++)
        {
            Aim(upper[i], lower[i].position - upper[i].position, thighWant);
            Aim(lower[i], foot[i].position - lower[i].position, shinWant);
        }
    }

    private static void Aim(Transform bone, Vector3 now, Vector3 want)
    {
        if (now.sqrMagnitude < 0.0001f) return;
        bone.rotation = Quaternion.FromToRotation(now, want) * bone.rotation;
    }

    private bool Init()
    {
        animator = GetComponent<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman) { failed = true; return false; }
        upper[0] = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        upper[1] = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        lower[0] = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        lower[1] = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        foot[0] = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        foot[1] = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        for (int i = 0; i < 2; i++)
            if (upper[i] == null || lower[i] == null || foot[i] == null) { failed = true; return false; }
        ready = true;
        return true;
    }
}
