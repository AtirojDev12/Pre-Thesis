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
        public bool resolveAtmosphere = true;
        public bool manualResolve;
        [Range(0, 1)] public float resolve = .6f;
        private float began;
        private bool splashed;
        private MaterialPropertyBlock properties;
        private GhostPoolAtmosphere[] atmospheres;
        private float previewAge;
        private bool wasManual;

        private void Start() => Replay();

        public void Replay()
        {
            began = Time.time;
            previewAge = 0;
            splashed = false;
            foreach (var vfx in splashes) { vfx.Reinit(); vfx.Stop(); }
            smoke.Reinit();
            smoke.Play();
            Sample(0);
        }

        private void Update()
        {
            if (manualResolve && (!wasManual || previewAge < GhostPoolVisual.GrowSeconds))
                previewAge = Mathf.Clamp(previewAge, 1.1f, 1.8f);
            if (wasManual && !manualResolve) began = Time.time - previewAge;
            wasManual = manualResolve;
            foreach (var vfx in splashes) if (vfx != null) vfx.pause = manualResolve;
            if (smoke != null) smoke.pause = manualResolve;
            if (manualResolve) { Sample(previewAge); return; }
            float age = Time.time - began;
            previewAge = age;
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
            if (Application.isPlaying && atmospheres == null)
            {
                atmospheres = new GhostPoolAtmosphere[pools.Length];
                for (int i = 0; i < pools.Length; i++) atmospheres[i] = GhostPoolAtmosphere.Create(pools[i]);
            }
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
                float formation = manualResolve ? resolve : GhostPoolAtmosphere.ResolveAt(age, travel, i == 1);
                properties.SetFloat("_Resolve", formation);
                properties.SetFloat("_Horror", resolveAtmosphere ? 1 : 0);
                float active = manualResolve ? 1 : 1 - Mathf.SmoothStep(0, 1,
                    Mathf.Clamp01((age - GhostPoolVisual.GrowSeconds - travel) / .8f));
                if (atmospheres != null && atmospheres[i] != null)
                    atmospheres[i].Sample(age, formation, resolveAtmosphere ? grow * fade * active : 0);
                properties.SetFloat("_Impact", Mathf.Clamp01(1 - (age - .9f) / 1.6f) * (age >= .9f ? 1 : 0));
                pools[i].SetPropertyBlock(properties);
                float motion = Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - GhostPoolVisual.GrowSeconds) / travel));
                bodies[i].localPosition = new Vector3(0, i == 0 ? Mathf.Lerp(-2.8f, 0, motion) : Mathf.Lerp(0, -2.8f, motion), 0);
                bodies[i].gameObject.SetActive(i == 0 ? age >= GhostPoolVisual.GrowSeconds : age < GhostPoolVisual.GrowSeconds + travel);
            }
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(20, 20, 430, 212), "BLACK WATER / VISUAL EFFECT GRAPH");
            if (GUI.Button(new Rect(34, 54, 140, 30), "Replay")) Replay();
            autoReplay = GUI.Toggle(new Rect(195, 58, 200, 24), autoReplay, "Auto replay");
            GUI.Label(new Rect(34, 94, 400, 25), "LEFT: emerge + splash     RIGHT: smoke + sink");
            resolveAtmosphere = GUI.Toggle(new Rect(34, 122, 380, 24), resolveAtmosphere, "Resolve smoke + inward ripples + frost");
            manualResolve = GUI.Toggle(new Rect(34, 150, 380, 24), manualResolve, "Pause and inspect Resolve (smoke / water only)");
            GUI.Label(new Rect(34, 183, 120, 24), "Resolve " + resolve.ToString("0.00"));
            resolve = GUI.HorizontalSlider(new Rect(154, 190, 240, 20), resolve, 0, 1);
        }

        private void OnDestroy()
        {
            if (atmospheres == null) return;
            foreach (var atmosphere in atmospheres) if (atmosphere != null) Destroy(atmosphere.gameObject);
        }
    }
}
