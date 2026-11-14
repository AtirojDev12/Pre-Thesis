using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Body of a HUMAN customer (ticket booth and popcorn counter).
///
/// The customer scripts still move this object's root along the authored route,
/// whose points describe the centre of the old capsule (about 1 m above the floor).
/// The NPC model hangs under a separate "Body" child that is re-grounded every
/// frame AFTER the animation has been applied:
///   1. find the floor under the customer (raycast, fallback: route point - 1 m),
///   2. bake the skinned mesh in its current animated pose and take its lowest vertex,
///   3. shift the Body so that lowest vertex sits exactly on the floor.
/// The lowest point of a standing/walking person is always the planted foot, so the
/// model can neither sink into nor float above the floor, whatever the clip does.
/// The speech/prompt label hangs from a separate anchor at a fixed height above the
/// floor (model height + gap), so it does not bob with the walk cycle.
/// </summary>
[DisallowMultipleComponent]
public sealed class CustomerModelVisual : MonoBehaviour
{
    // The old primitive capsule: height 2 at scale y = 1, so its centre is 1 m above its base.
    private const float CapsuleHalfHeight = 1f;
    // A floor hit must lie within this distance of (route point - 1 m) to count.
    private const float FloorSearchTolerance = 0.75f;
    private const float FloorRayStartAbove = 0.5f;
    private const float FloorRayLength = CapsuleHalfHeight + FloorRayStartAbove + FloorSearchTolerance + 0.5f;

    private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");
    private static readonly int IdleStateHash = Animator.StringToHash("Idle");
    private static readonly int WalkStateHash = Animator.StringToHash("Walk");
    private static readonly RaycastHit[] FloorHits = new RaycastHit[16];

    private Transform body;
    private Transform labelAnchor;
    private Animator animator;
    private SkinnedMeshRenderer[] skinned;
    private Mesh baked;
    private readonly List<Vector3> vertices = new List<Vector3>(4096);
    private CapsuleCollider capsule;
    private float labelGap;
    private HeadRestPose headLock;
    private ArmSpread armSpread;
    private bool warnedNoFloor;

    /// <summary>Model height (feet to top of head), measured once at spawn.</summary>
    public float Height { get; private set; }
    /// <summary>Parent speech bubbles/prompts here. Sits at floor + Height + gap.</summary>
    public Transform LabelAnchor => labelAnchor;
    /// <summary>World Y of the floor under the customer this frame.</summary>
    public float FloorY { get; private set; }

    /// <summary>
    /// Creates a human customer at a route point (capsule-centre convention).
    /// Returns null when no model is configured, so callers can fall back to a capsule.
    /// </summary>
    public static CustomerModelVisual TryCreate(string name, Vector3 position, Quaternion rotation,
        uint seed, bool startWalking, float labelGapAboveHead, bool addInteractionCollider)
    {
        CustomerModelLibrary library = CustomerModelLibrary.Instance;
        if (library == null || !library.TryPick(seed, out CustomerModelLibrary.Entry entry)) return null;

        var root = new GameObject(name);
        root.transform.SetPositionAndRotation(position, rotation);
        var visual = root.AddComponent<CustomerModelVisual>();
        if (!visual.Build(entry, startWalking, labelGapAboveHead, addInteractionCollider))
        {
            Destroy(root);
            return null;
        }
        return visual;
    }

    private bool Build(CustomerModelLibrary.Entry entry, bool startWalking, float gap, bool addCollider)
    {
        labelGap = gap;
        body = new GameObject("Body (grounded)").transform;
        body.SetParent(transform, false);

        GameObject model = Instantiate(entry.model, body, false);
        model.name = entry.model.name;
        // Imported models carry no colliders by default; remove any so they can't block
        // the floor raycast, the player, or the interaction ray.
        foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);

        skinned = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (skinned.Length == 0)
        {
            Debug.LogError($"[Customers] '{entry.model.name}' has no SkinnedMeshRenderer; using a capsule instead.", entry.model);
            return false;
        }
        foreach (SkinnedMeshRenderer r in skinned) r.updateWhenOffscreen = true;
        baked = new Mesh { name = "Customer Grounding Bake" };

        animator = model.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.runtimeAnimatorController = entry.controller;
            animator.applyRootMotion = false; // the customer scripts own the movement
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        else Debug.LogWarning($"[Customers] '{entry.model.name}' has no Animator; it will not animate.", entry.model);

        // Read the rest pose BEFORE the Animator first writes the bones.
        if (entry.lockHeadToRestPose) headLock = HeadRestPose.Capture(animator);
        armSpread = ArmSpread.Capture(animator, entry.armSpreadDegrees);

        labelAnchor = new GameObject("Label Anchor").transform;
        labelAnchor.SetParent(transform, false);

        // Pose the model immediately so the very first rendered frame is already
        // animated (no bind/T-pose flash) and already grounded.
        SetWalking(startWalking, true);

        if (!MeasureVertical(out float minY, out float maxY))
        {
            Debug.LogError($"[Customers] Could not measure '{entry.model.name}'; using a capsule instead.", entry.model);
            return false;
        }
        Height = maxY - minY;
        if (Height < 0.5f || Height > 4f)
            Debug.LogWarning($"[Customers] '{entry.model.name}' measures {Height:0.00} m tall. Check the FBX import scale.", entry.model);

        if (addCollider)
        {
            capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.radius = 0.35f;
            capsule.height = Mathf.Max(Height, capsule.radius * 2f);
        }
        Ground();
        return true;
    }

    public void SetWalking(bool value) => SetWalking(value, false);

    private void SetWalking(bool value, bool immediate)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animator.SetBool(IsWalkingHash, value);
        if (!immediate) return;
        int state = value ? WalkStateHash : IdleStateHash;
        if (animator.HasState(0, state)) animator.Play(state, 0, 0f);
        animator.Update(0f);
        headLock?.Apply();
        armSpread?.Apply();
    }

    // LateUpdate runs after the Animator has posed the bones for this frame and
    // after the customer scripts moved the root in Update.
    private void LateUpdate() => Ground();

    private void Ground()
    {
        if (body == null) return;
        // Pose fixes run after the animation and before measuring, so the mesh we measure is the one drawn.
        headLock?.Apply();
        armSpread?.Apply();
        FloorY = FindFloorY();
        if (MeasureVertical(out float minY, out _))
            body.position += Vector3.up * (FloorY - minY);

        Vector3 root = transform.position;
        labelAnchor.position = new Vector3(root.x, FloorY + Height + labelGap, root.z);
        if (capsule != null) capsule.center = new Vector3(0f, FloorY + Height * 0.5f - root.y, 0f);
    }

    /// <summary>Lowest/highest world Y of the skinned mesh in its current animated pose.</summary>
    private bool MeasureVertical(out float minY, out float maxY)
    {
        minY = float.PositiveInfinity;
        maxY = float.NegativeInfinity;
        foreach (SkinnedMeshRenderer r in skinned)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.sharedMesh == null) continue;
            // useScale: true -> vertices include the renderer's scale and are relative to its
            // position/rotation, so world = position + rotation * vertex.
            r.BakeMesh(baked, true);
            baked.GetVertices(vertices);
            Transform t = r.transform;
            Matrix4x4 rot = Matrix4x4.Rotate(t.rotation);
            float baseY = t.position.y;
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 v = vertices[i];
                float y = baseY + rot.m10 * v.x + rot.m11 * v.y + rot.m12 * v.z;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        return minY <= maxY;
    }

    /// <summary>
    /// Floor under the root. Accepts only a surface near the capsule base
    /// (route point - 1 m) so a counter edge or shelf under the ray is ignored.
    /// </summary>
    private float FindFloorY()
    {
        Vector3 root = transform.position;
        float expected = root.y - CapsuleHalfHeight;
        Vector3 origin = root + Vector3.up * FloorRayStartAbove;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, FloorHits, FloorRayLength,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        float best = float.NaN;
        float bestError = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider c = FloorHits[i].collider;
            if (c == null || c.transform.IsChildOf(transform)) continue;
            if (c.GetComponentInParent<CustomerModelVisual>() != null) continue;
            if (c.GetComponentInParent<PopcornCustomer>() != null) continue;
            if (c.GetComponentInParent<PlayerHealth>() != null) continue;
            float y = FloorHits[i].point.y;
            float error = Mathf.Abs(y - expected);
            if (error > FloorSearchTolerance || error >= bestError) continue;
            best = y;
            bestError = error;
        }

        if (!float.IsNaN(best)) return best;
        if (!warnedNoFloor)
        {
            warnedNoFloor = true;
            Debug.LogWarning($"[Customers] No floor collider under '{name}' at {root}. " +
                "Using route point - 1 m. Give the floor a collider or adjust the route point height.", this);
        }
        return expected;
    }

    /// <summary>Neck + head local rotations from the model's rest pose, re-applied after animation.</summary>
    public sealed class HeadRestPose
    {
        private readonly Transform head, neck;
        private readonly Quaternion headRest, neckRest;

        private HeadRestPose(Transform head, Transform neck)
        {
            this.head = head; this.neck = neck;
            headRest = head.localRotation;
            neckRest = neck != null ? neck.localRotation : Quaternion.identity;
        }

        public static HeadRestPose Capture(Animator animator)
        {
            if (animator == null || !animator.isHuman) return null;
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return null;
            // The neck may be unmapped in the avatar; then it is simply the head's parent bone.
            Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            if (neck == null && head.parent != null &&
                head.parent != animator.GetBoneTransform(HumanBodyBones.UpperChest) &&
                head.parent != animator.GetBoneTransform(HumanBodyBones.Chest)) neck = head.parent;
            return new HeadRestPose(head, neck);
        }

        public void Apply()
        {
            if (neck != null) neck.localRotation = neckRest;
            if (head != null) head.localRotation = headRest;
        }
    }

    /// <summary>
    /// Swings the upper arms outward (away from the body's centre line) by a fixed angle
    /// every frame after the Animator has posed them. The Animator rewrites the bones each
    /// frame, so the offset never accumulates.
    /// </summary>
    public sealed class ArmSpread
    {
        private readonly Transform root, leftUpper, leftLower, rightUpper, rightLower;
        private readonly float degrees;

        private ArmSpread(Animator a, float degrees)
        {
            root = a.transform;
            leftUpper = a.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftLower = a.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            rightUpper = a.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rightLower = a.GetBoneTransform(HumanBodyBones.RightLowerArm);
            this.degrees = degrees;
        }

        public static ArmSpread Capture(Animator animator, float degrees)
        {
            if (animator == null || !animator.isHuman || degrees <= 0.01f) return null;
            var spread = new ArmSpread(animator, degrees);
            return spread.leftUpper != null || spread.rightUpper != null ? spread : null;
        }

        public void Apply()
        {
            Swing(leftUpper, leftLower, -root.right);
            Swing(rightUpper, rightLower, root.right);
        }

        private void Swing(Transform upper, Transform lower, Vector3 outward)
        {
            if (upper == null || lower == null) return;
            Vector3 arm = lower.position - upper.position;
            if (arm.sqrMagnitude < 1e-8f) return;
            arm.Normalize();
            // Already pointing (almost) straight out sideways: nothing to push away from.
            if (Vector3.Dot(arm, outward) > 0.98f) return;
            Vector3 target = Vector3.RotateTowards(arm, outward, degrees * Mathf.Deg2Rad, 0f);
            upper.rotation = Quaternion.FromToRotation(arm, target) * upper.rotation;
        }
    }

    private void OnDestroy()
    {
        if (baked != null) Destroy(baked);
    }
}
