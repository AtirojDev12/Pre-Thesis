using UnityEngine;
using UnityEngine.VFX;

namespace PreThesis.Vfx
{
    /// <summary>Scene-only prop preview. Uses the exact gameplay graph and duration.</summary>
    public sealed class GhostFavorVfxPreview : MonoBehaviour
    {
        public VisualEffect smoke;
        public Renderer[] jarRenderers;
        public bool autoReplay = true;
        [Min(3)] public float cycleSeconds = 4f;
        private float began;
        private bool emitted;
        private bool stopped;

        private void OnEnable() { if (Application.isPlaying) Replay(); }

        [ContextMenu("Replay Ghost Favor")]
        public void Replay()
        {
            if (smoke == null) return;
            smoke.gameObject.SetActive(true);
            smoke.Reinit(); smoke.Stop();
            foreach (var renderer in jarRenderers) if (renderer != null) renderer.enabled = true;
            began = Time.time;
            emitted = stopped = false;
        }

        private void Update()
        {
            float age = Time.time - began;
            if (!emitted && age >= .5f)
            {
                foreach (var renderer in jarRenderers) if (renderer != null) renderer.enabled = false;
                smoke.Play(); emitted = true;
            }
            if (!stopped && age >= .5f + GhostFavorDisappearVfx.Lifetime)
            {
                smoke.Stop(); smoke.gameObject.SetActive(false); stopped = true;
            }
            if (autoReplay && age >= cycleSeconds) Replay();
        }

        private void OnDisable()
        {
            if (smoke != null) { smoke.Stop(); smoke.gameObject.SetActive(false); }
            foreach (var renderer in jarRenderers) if (renderer != null) renderer.enabled = true;
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(24, 24, 350, 112), "GHOST FAVOR / RED + BLACK SMOKE");
            if (GUI.Button(new Rect(40, 57, 150, 30), "Replay disappearance")) Replay();
            autoReplay = GUI.Toggle(new Rect(205, 61, 155, 24), autoReplay, "Auto replay");
            GUI.Label(new Rect(40, 100, 320, 25), "Effect duration: 2 seconds including fade");
        }
    }
}
