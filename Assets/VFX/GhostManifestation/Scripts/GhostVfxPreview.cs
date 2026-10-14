using UnityEngine;

namespace PreThesis.Vfx
{
    /// <summary>Only used by VFX_Preview. No gameplay or networking dependencies.</summary>
    public sealed class GhostVfxPreview : MonoBehaviour
    {
        public ParticleSystem appear;
        public ParticleSystem disappear;
        public GameObject appearReference;
        public GameObject disappearReference;
        public bool loop = true;
        public bool showReference = true;
        [Min(3f)] public float cycleSeconds = 5f;
        private float startedAt;
        private bool playing;

        private void Start() => Replay();

        public void Replay()
        {
            startedAt = Time.time;
            playing = true;
            foreach (var effect in new[] { appear, disappear })
            {
                if (effect == null) continue;
                effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                effect.Play(true);
            }
            UpdateReferences(0f);
        }

        private void Update()
        {
            if (!playing) return;
            float age = Time.time - startedAt;
            UpdateReferences(age);
            if (loop && age >= cycleSeconds) Replay();
        }

        private void UpdateReferences(float age)
        {
            if (appearReference != null) appearReference.SetActive(showReference && age >= .85f);
            if (disappearReference != null) disappearReference.SetActive(showReference && age < .22f);
        }

        private void OnGUI()
        {
            float scale = Mathf.Clamp(Screen.height / 720f, .75f, 2f);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            GUI.Box(new Rect(20, 20, 365, 150), "GHOST MANIFESTATION / VFX PREVIEW");
            if (GUI.Button(new Rect(36, 54, 140, 32), "Replay both")) Replay();
            loop = GUI.Toggle(new Rect(192, 60, 160, 26), loop, "Auto replay (5 sec)");
            showReference = GUI.Toggle(new Rect(36, 96, 310, 24), showReference, "Show size reference (placeholder body)");
            GUI.Label(new Rect(36, 128, 330, 24), "Standalone preview - no gameplay connection");
            var label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20 };
            GUI.Label(new Rect(width * .25f - 150, Screen.height / scale - 74, 300, 35), "01 / APPEAR", label);
            GUI.Label(new Rect(width * .75f - 150, Screen.height / scale - 74, 300, 35), "02 / DISAPPEAR", label);
            GUI.matrix = previous;
        }
    }
}
