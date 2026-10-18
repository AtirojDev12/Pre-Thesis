using System.Collections.Generic;
using UnityEngine;

namespace PreThesis.Vfx
{
    /// <summary>Moves only the model. The NavMesh agent and network transform stay on the original root.</summary>
    public sealed class GhostPoolVisual : MonoBehaviour
    {
        public const float GrowSeconds = .9f;
        public const float TravelSeconds = 1.25f;
        public const float HiddenAt = GrowSeconds + TravelSeconds;
        public Animator Animator { get; private set; }
        public Transform Pivot { get; private set; }
        public float DisappearTravelSeconds { get; private set; } = TravelSeconds;
        private Renderer[] renderers;
        private bool[] enabledRenderers;
        private float depth;
        private Vector3 sinkFrom;
        private bool reverseEntry;
        private float reverseStart;

        public void Initialize()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            enabledRenderers = new bool[renderers.Length];
            float top = transform.position.y + 3.2f;
            for (int i = 0; i < renderers.Length; i++)
            {
                enabledRenderers[i] = renderers[i].enabled;
                top = Mathf.Max(top, renderers[i].bounds.max.y + .5f);
            }
            depth = (top - transform.position.y) / Mathf.Max(.001f, Mathf.Abs(transform.lossyScale.y));
            var children = new List<Transform>();
            foreach (Transform child in transform) children.Add(child);
            Pivot = new GameObject("Ghost Visual Presentation").transform;
            Pivot.SetParent(transform, false);
            var original = GetComponent<Animator>();
            if (original != null) original.enabled = false;
            foreach (Transform child in children) child.SetParent(Pivot, false);
            if (original != null)
            {
                // Preserve the animation binding paths under the new presentation root.
                Animator = Pivot.gameObject.AddComponent<Animator>();
                Animator.avatar = original.avatar;
                Animator.runtimeAnimatorController = original.runtimeAnimatorController;
                Animator.applyRootMotion = false;
                Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Animator.updateMode = original.updateMode;
            }
            SampleAppear(0);
        }

        public void SampleAppear(float age)
        {
            float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - GrowSeconds) / TravelSeconds));
            Pivot.localPosition = Vector3.down * (depth * (1 - t));
            SetVisible(age >= GrowSeconds);
        }

        public void BeginDisappear(float entryProgress = 1f)
        {
            sinkFrom = Pivot.localPosition;
            if (Animator == null) return;
            Animator.speed = 0;
            reverseEntry = false;
            DisappearTravelSeconds = TravelSeconds;
            if (Animator.runtimeAnimatorController == null) return;
            foreach (var clip in Animator.runtimeAnimatorController.animationClips)
            {
                if (clip.name != "Entry" || clip.length <= 0) continue;
                reverseStart = Mathf.Clamp01(entryProgress);
                DisappearTravelSeconds = Mathf.Max(.01f, clip.length * reverseStart);
                reverseEntry = true;
                break;
            }
        }

        public void SampleDisappear(float age)
        {
            float progress = Mathf.Clamp01((age - GrowSeconds) / DisappearTravelSeconds);
            if (reverseEntry)
            {
                // Sample the controller backwards explicitly: deterministic for late observers,
                // and does not require Animator's recording mode for negative playback speed.
                Animator.Play("Entry", 0, Mathf.Min(.9999f, reverseStart * (1 - progress)));
                Animator.Update(0);
            }
            float t = Mathf.SmoothStep(0, 1, progress);
            Pivot.localPosition = Vector3.Lerp(sinkFrom, Vector3.down * depth, t);
            SetVisible(age < GrowSeconds + DisappearTravelSeconds);
        }

        private void SetVisible(bool visible)
        {
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = visible && enabledRenderers[i];
        }
    }
}
