using UnityEngine;
using UnityEngine.VFX;

namespace PreThesis.Vfx
{
    /// <summary>Shared water presentation for gameplay and both preview stages.</summary>
    public sealed class GhostPoolSurface : MonoBehaviour
    {
        public const float PreludeAt = .18f;
        public const float SplashAt = GhostPoolVisual.GrowSeconds;
        Mesh mesh;
        MeshFilter filter;
        Mesh original;
        VisualEffect prelude;
        VisualEffect evaporation;
        public VisualEffect Evaporation => evaporation;
        bool evaporationPlayed;
        float previousEvaporationAge = -1;
        bool preludePlayed;
        float previousAge = -1;

        public static GhostPoolSurface Create(Renderer surface, VisualEffectAsset droplets, VisualEffectAsset vapor = null)
        {
            var existing = surface.GetComponent<GhostPoolSurface>();
            if (existing != null) return existing;
            var result = surface.gameObject.AddComponent<GhostPoolSurface>();
            result.Initialize(droplets, vapor);
            return result;
        }

        void Initialize(VisualEffectAsset droplets, VisualEffectAsset vapor)
        {
            // A quad has no internal vertices: use a grid so shader displacement has real volume.
            const int resolution = 64;
            filter = GetComponent<MeshFilter>();
            original = filter.sharedMesh;
            var vertices = new Vector3[(resolution + 1) * (resolution + 1)];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[resolution * resolution * 6];
            for (int y = 0; y <= resolution; y++)
                for (int x = 0; x <= resolution; x++)
                {
                    int index = y * (resolution + 1) + x;
                    uv[index] = new Vector2(x / (float)resolution, y / (float)resolution);
                    vertices[index] = new Vector3(uv[index].x - .5f, uv[index].y - .5f, 0);
                    if (x == resolution || y == resolution) continue;
                    int t = (y * resolution + x) * 6;
                    triangles[t] = index; triangles[t + 1] = index + resolution + 1; triangles[t + 2] = index + 1;
                    triangles[t + 3] = index + 1; triangles[t + 4] = index + resolution + 1; triangles[t + 5] = index + resolution + 2;
                }
            mesh = new Mesh { name = "Black water displaced surface" };
            mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles;
            // The local negative Z axis points above the floor after the pool's 90 degree rotation.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1.1f, 1.1f, 2));
            filter.sharedMesh = mesh;
            if (vapor != null)
            {
                var steam = new GameObject("Water evaporation - VFX Graph");
                steam.transform.SetParent(transform.parent, false);
                steam.transform.position = transform.position + transform.TransformDirection(Vector3.back) * .06f;
                evaporation = steam.AddComponent<VisualEffect>();
                evaporation.visualEffectAsset = vapor;
                evaporation.initialEventName = "Idle";
                evaporation.startSeed = 317;
                evaporation.resetSeedOnPlay = false;
                steam.SetActive(false);
            }
            if (droplets == null) return;
            var emitter = new GameObject("Opening pressure splash - VFX Graph");
            emitter.transform.SetParent(transform.parent, false);
            emitter.transform.position = transform.position + transform.TransformDirection(Vector3.back) * .04f;
            prelude = emitter.AddComponent<VisualEffect>();
            prelude.visualEffectAsset = droplets;
            prelude.initialEventName = "Idle";
            prelude.startSeed = 173;
            prelude.resetSeedOnPlay = false;
        }

        public static float SpreadAt(float age) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(age / .7f));

        public void Sample(float age, bool fountain, float strength, bool paused = false)
        {
            if (prelude == null) return;
            if (age < previousAge)
            {
                prelude.Reinit(); prelude.Stop(); preludePlayed = false;
            }
            previousAge = age;
            prelude.pause = paused;
            if (!fountain || strength <= 0) { prelude.Stop(); return; }
            if (preludePlayed || age < PreludeAt) return;
            preludePlayed = true;
            float elapsed = age - PreludeAt;
            if (elapsed >= 1.2f) return;
            prelude.Play();
            if (elapsed > .0167f) prelude.Simulate(1f / 60, (uint)Mathf.FloorToInt(elapsed * 60));
        }

        void OnDisable()
        {
            if (prelude != null) prelude.Stop();
            if (evaporation != null) evaporation.gameObject.SetActive(false);
            evaporationPlayed = false;
        }
        public void SampleEvaporation(float age, float travelSeconds, bool visible = true, bool paused = false)
        {
            if (evaporation == null) return;
            float elapsed = age - GhostPoolEffect.EvaporationStartFor(travelSeconds);
            bool rewind = age < previousEvaporationAge;
            previousEvaporationAge = age;
            if (rewind || !visible || elapsed < 0 || elapsed >= GhostPoolEffect.EvaporationSeconds)
            {
                evaporation.gameObject.SetActive(false);
                evaporationPlayed = false;
            }
            if (!visible || elapsed < 0 || elapsed >= GhostPoolEffect.EvaporationSeconds) return;
            evaporation.gameObject.SetActive(true);
            evaporation.pause = paused;
            if (evaporationPlayed) return;
            evaporationPlayed = true;
            evaporation.Reinit(); evaporation.Stop(); evaporation.Play();
            if (elapsed > .0167f) evaporation.Simulate(1f / 60, (uint)Mathf.FloorToInt(elapsed * 60));
        }
        void OnDestroy()
        {
            if (filter != null && filter.sharedMesh == mesh) filter.sharedMesh = original;
            if (mesh != null) Destroy(mesh);
            if (prelude != null) Destroy(prelude.gameObject);
            if (evaporation != null) Destroy(evaporation.gameObject);
        }
    }
}
