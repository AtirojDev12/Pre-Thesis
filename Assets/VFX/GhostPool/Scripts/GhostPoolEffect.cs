using UnityEngine;
using UnityEngine.VFX;

namespace PreThesis.Vfx
{
    /// <summary>Local, detached presentation. No network identity, model, UI or gameplay colliders.</summary>
    public sealed class GhostPoolEffect : MonoBehaviour
    {
        public const float HoldSeconds = 4f;
        public const float FadeStart = GhostPoolVisual.HiddenAt + HoldSeconds;
        public const float FadeSeconds = 1.6f;
        public const float Lifetime = FadeStart + FadeSeconds + .2f;
        public static readonly Vector3 SurfaceSize = new Vector3(3.55f, 3.25f, 1);
        private Renderer pool;
        private VisualEffect splash;
        private VisualEffect smoke;
        private MaterialPropertyBlock properties;
        private float began;
        private bool splashed;
        private Vector3 size;
        private float fadeStart;

        public static float LifetimeFor(float travelSeconds) =>
            GhostPoolVisual.GrowSeconds + travelSeconds + HoldSeconds + FadeSeconds + .2f;

        public void Initialize(Material water, VisualEffectAsset splashAsset, VisualEffectAsset smokeAsset,
            float initialAge, bool submerging, float travelSeconds = GhostPoolVisual.TravelSeconds)
        {
            began = Time.time - initialAge;
            fadeStart = GhostPoolVisual.GrowSeconds + travelSeconds + HoldSeconds;
            properties = new MaterialPropertyBlock();
            // Keep the complete effect in ground space, independent of the moving/destroyed ghost.
            var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = "Glossy Black Water";
            var collider = surface.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            surface.transform.SetParent(transform, false);
            surface.transform.localPosition = new Vector3(0, .012f, -.35f);
            surface.transform.localRotation = Quaternion.Euler(90, 0, 0);
            pool = surface.GetComponent<Renderer>();
            pool.sharedMaterial = water;
            size = SurfaceSize;
            splash = CreateVfx("Black Water Splash", splashAsset, new Vector3(0, .08f, -.35f));
            if (submerging)
            {
                smoke = CreateVfx("Submerge Smoke", smokeAsset, new Vector3(0, .65f, 0));
                Seek(smoke, initialAge, 2.5f);
            }
            Sample(initialAge);
            Destroy(gameObject, Mathf.Max(.01f, LifetimeFor(travelSeconds) - initialAge));
        }

        private VisualEffect CreateVfx(string label, VisualEffectAsset asset, Vector3 offset)
        {
            if (asset == null) return null;
            var emitter = new GameObject(label);
            emitter.transform.SetParent(transform, false);
            emitter.transform.localPosition = offset;
            var effect = emitter.AddComponent<VisualEffect>();
            effect.visualEffectAsset = asset;
            effect.initialEventName = "Idle";
            return effect;
        }

        private static void Seek(VisualEffect effect, float age, float maximumAge)
        {
            if (effect == null || age >= maximumAge) return;
            effect.Play();
            if (age > 0) effect.Simulate(1f / 60, (uint)Mathf.FloorToInt(age * 60));
        }

        private void Update() => Sample(Mathf.Max(0, Time.time - began));

        public void Sample(float age)
        {
            if (pool == null) return;
            float grow = Mathf.SmoothStep(0, 1, Mathf.Clamp01(age / GhostPoolVisual.GrowSeconds));
            float fade = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - fadeStart) / FadeSeconds));
            pool.transform.localScale = size * Mathf.Max(.005f, grow * Mathf.Lerp(.55f, 1, fade));
            properties.SetFloat("_Fade", fade);
            properties.SetFloat("_Phase", age);
            properties.SetFloat("_Impact", age >= GhostPoolVisual.GrowSeconds
                ? Mathf.Clamp01(1 - (age - GhostPoolVisual.GrowSeconds) / 1.6f) : 0);
            pool.SetPropertyBlock(properties);
            if (!splashed && age >= GhostPoolVisual.GrowSeconds)
            {
                splashed = true;
                Seek(splash, age - GhostPoolVisual.GrowSeconds, .85f);
            }
        }
    }
}
