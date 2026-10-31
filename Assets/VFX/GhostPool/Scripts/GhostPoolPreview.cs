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
        public bool waterEruption = true;
        [Range(0, 2)] public float eruptionStrength = 1;
        public bool waterOnly;
        public bool manualWaterTime;
        [Range(0, 14)] public float waterTime;
        public bool manualResolve;
        [Range(0, 1)] public float resolve = .6f;
        private float began;
        private bool splashed;
        private MaterialPropertyBlock properties;
        private float previewAge;
        private bool wasManual;
        private GhostPoolSurface[] waterSurfaces;
        private bool wasWaterManual;

        private void Start() => Replay();

        public void Replay()
        {
            began = Time.time;
            previewAge = 0;
            splashed = false;
            foreach (var vfx in splashes) { vfx.Reinit(); vfx.Stop(); }
            if (smoke != null) { smoke.Stop(); smoke.gameObject.SetActive(false); }
            Sample(0);
        }

        private void Update()
        {
            if (wasWaterManual && !manualWaterTime) began = Time.time - previewAge;
            wasWaterManual = manualWaterTime;
            if (manualWaterTime)
            {
                previewAge = waterTime;
                foreach (var vfx in splashes) if (vfx != null) vfx.pause = true;
                Sample(previewAge); return;
            }
            if (manualResolve && (!wasManual || previewAge < GhostPoolVisual.GrowSeconds))
                previewAge = Mathf.Clamp(previewAge, 1.1f, 1.8f);
            if (wasManual && !manualResolve) began = Time.time - previewAge;
            wasManual = manualResolve;
            foreach (var vfx in splashes) if (vfx != null) vfx.pause = manualResolve;
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
            if (Application.isPlaying && waterSurfaces == null)
            {
                waterSurfaces = new GhostPoolSurface[pools.Length];
                for (int i = 0; i < pools.Length; i++)
                    waterSurfaces[i] = GhostPoolSurface.Create(pools[i], splashes[i].visualEffectAsset,
                        smoke != null ? smoke.visualEffectAsset : null);
            }
            float grow = waterEruption ? GhostPoolSurface.SpreadAt(age) : Mathf.SmoothStep(0, 1, Mathf.Clamp01(age / .9f));
            for (int i = 0; i < pools.Length; i++)
            {
                float travel = i == 1 && entry != null ? entry.length : GhostPoolVisual.TravelSeconds;
                float fadeStart = GhostPoolVisual.GrowSeconds + travel + GhostPoolEffect.HoldSeconds;
                float fade = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - fadeStart) / GhostPoolEffect.FadeSeconds));
                if (i == 1 && entry != null)
                    entry.SampleAnimation(ghostModels[i], Mathf.Clamp(entry.length - Mathf.Max(0, age - GhostPoolVisual.GrowSeconds), 0, entry.length * .9999f));
                else if (walk != null) walk.SampleAnimation(ghostModels[i], Mathf.Repeat(age,walk.length));
                pools[i].transform.localScale = GhostPoolEffect.SurfaceSize * Mathf.Max(.005f, grow);
                pools[i].GetPropertyBlock(properties);
                properties.SetFloat("_Fade", 1);
                properties.SetFloat("_Absorb", 1 - fade);
                properties.SetFloat("_Phase", age);
                float formation = manualResolve ? resolve : GhostPoolAtmosphere.ResolveAt(age, travel, i == 1);
                properties.SetFloat("_Resolve", formation);
                properties.SetFloat("_Horror", resolveAtmosphere ? 1 : 0);
                properties.SetFloat("_Eruption", waterEruption ? eruptionStrength : 0);
                if (waterSurfaces != null) waterSurfaces[i].Sample(age, waterEruption, eruptionStrength, manualResolve || manualWaterTime);
                if (waterSurfaces != null)
                    waterSurfaces[i].SampleEvaporation(age, travel, !waterOnly, manualResolve || manualWaterTime);
                properties.SetFloat("_Impact", Mathf.Clamp01(1 - (age - .9f) / 1.6f) * (age >= .9f ? 1 : 0));
                pools[i].SetPropertyBlock(properties);
                float motion = Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - GhostPoolVisual.GrowSeconds) / travel));
                bodies[i].localPosition = new Vector3(0, i == 0 ? Mathf.Lerp(-2.8f, 0, motion) : Mathf.Lerp(0, -2.8f, motion), 0);
                bodies[i].gameObject.SetActive(!waterOnly && (i == 0 ? age >= GhostPoolVisual.GrowSeconds : age < GhostPoolVisual.GrowSeconds + travel));
            }
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(20, 20, 430, 362), "BLACK WATER / VISUAL EFFECT GRAPH");
            if (GUI.Button(new Rect(34, 54, 140, 30), "Replay")) Replay();
            autoReplay = GUI.Toggle(new Rect(195, 58, 200, 24), autoReplay, "Auto replay");
            GUI.Label(new Rect(34, 94, 400, 25), "Emerge / sink, then water dries and evaporates");
            resolveAtmosphere = GUI.Toggle(new Rect(34, 122, 380, 24), resolveAtmosphere, "Inward ripples");
            manualResolve = GUI.Toggle(new Rect(34, 150, 380, 24), manualResolve, "Pause and inspect water ripples");
            GUI.Label(new Rect(34, 183, 120, 24), "Resolve " + resolve.ToString("0.00"));
            resolve = GUI.HorizontalSlider(new Rect(154, 190, 240, 20), resolve, 0, 1);
            waterEruption = GUI.Toggle(new Rect(34, 218, 380, 24), waterEruption, "Water eruption + pressure splash");
            GUI.Label(new Rect(34, 246, 150, 24), "Water height " + eruptionStrength.ToString("0.00"));
            eruptionStrength = GUI.HorizontalSlider(new Rect(194, 254, 200, 20), eruptionStrength, 0, 2);
            waterOnly = GUI.Toggle(new Rect(34, 276, 380, 24), waterOnly, "Water only (hide ghost + smoke)");
            manualWaterTime = GUI.Toggle(new Rect(34, 304, 380, 24), manualWaterTime, "Inspect water + soaking (pause droplets)");
            GUI.Label(new Rect(34, 334, 150, 24), "Water time " + waterTime.ToString("0.00") + " s");
            waterTime = GUI.HorizontalSlider(new Rect(194, 342, 200, 20), waterTime, 0,
                Mathf.Max(14, entry != null ? GhostPoolEffect.LifetimeFor(entry.length) : 14));
        }

    }
}
