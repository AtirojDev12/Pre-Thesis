using UnityEngine;
using UnityEngine.VFX;

namespace PreThesis.Vfx
{
    /// <summary>Standalone art preview; does not move the network ghost or change gameplay.</summary>
    public sealed class GhostPoolPreview : MonoBehaviour
    {
        public Renderer[] pools;
        public Transform[] bodies;
        public VisualEffect[] splashes;
        public VisualEffect smoke;
        public GameObject[] ghostModels;
        public AnimationClip walk;
        public AnimationClip entry;
        public float cycleSeconds = 13f;
        public bool autoReplay = true;
        private float began;
        private bool splashed;
        private MaterialPropertyBlock properties;

        private void Start() => Replay();

        public void Replay()
        {
            began = Time.time;
            splashed = false;
            foreach (var vfx in splashes) { vfx.Reinit(); vfx.Stop(); }
            smoke.Reinit();
            smoke.Play();
            Sample(0);
        }

        private void Update()
        {
            float age = Time.time - began;
            float minimumCycle = entry != null ? GhostPoolEffect.LifetimeFor(entry.length) + .7f : 9f;
            if (autoReplay && age > Mathf.Max(cycleSeconds, minimumCycle)) { Replay(); return; }
            if (!splashed && age >= .9f)
            {
                splashed = true;
                foreach (var vfx in splashes) vfx.Play();
            }
            Sample(age);
        }

        public void Sample(float age)
        {
            properties ??= new MaterialPropertyBlock();
            float grow = Mathf.SmoothStep(0, 1, Mathf.Clamp01(age / .9f));
            for (int i = 0; i < pools.Length; i++)
            {
                float travel = i == 1 && entry != null ? entry.length : GhostPoolVisual.TravelSeconds;
                float fadeStart = GhostPoolVisual.GrowSeconds + travel + GhostPoolEffect.HoldSeconds;
                float fade = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - fadeStart) / GhostPoolEffect.FadeSeconds));
                if (i == 1 && entry != null)
                    entry.SampleAnimation(ghostModels[i], Mathf.Clamp(entry.length - Mathf.Max(0, age - GhostPoolVisual.GrowSeconds), 0, entry.length * .9999f));
                else if (walk != null) walk.SampleAnimation(ghostModels[i], Mathf.Repeat(age,walk.length));
                pools[i].transform.localScale = GhostPoolEffect.SurfaceSize * Mathf.Max(.005f, grow * Mathf.Lerp(.55f, 1, fade));
                pools[i].GetPropertyBlock(properties);
                properties.SetFloat("_Fade", fade);
                properties.SetFloat("_Phase", age);
                properties.SetFloat("_Impact", Mathf.Clamp01(1 - (age - .9f) / 1.6f) * (age >= .9f ? 1 : 0));
                pools[i].SetPropertyBlock(properties);
                float motion = Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - GhostPoolVisual.GrowSeconds) / travel));
                bodies[i].localPosition = new Vector3(0, i == 0 ? Mathf.Lerp(-2.8f, 0, motion) : Mathf.Lerp(0, -2.8f, motion), 0);
                bodies[i].gameObject.SetActive(i == 0 ? age >= GhostPoolVisual.GrowSeconds : age < GhostPoolVisual.GrowSeconds + travel);
            }
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(20, 20, 430, 112), "BLACK WATER / VISUAL EFFECT GRAPH");
            if (GUI.Button(new Rect(34, 54, 140, 30), "Replay")) Replay();
            autoReplay = GUI.Toggle(new Rect(195, 58, 200, 24), autoReplay, "Auto replay");
            GUI.Label(new Rect(34, 94, 400, 25), "LEFT: emerge + splash     RIGHT: smoke + sink");
        }
    }
}
