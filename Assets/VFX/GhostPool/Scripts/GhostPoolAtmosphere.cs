using UnityEngine;
using UnityEngine.Rendering;

namespace PreThesis.Vfx
{
    /// <summary>Local smoke silhouette and pool shading only; never samples or modifies the ghost model.</summary>
    public sealed class GhostPoolAtmosphere : MonoBehaviour
    {
        const int PuffCount = 72;
        Mesh mesh;
        Material material;
        MeshRenderer smokeRenderer;
        readonly Vector3[] vertices = new Vector3[PuffCount * 4];
        readonly Color[] colors = new Color[PuffCount * 4];
        readonly Vector2[] uv = new Vector2[PuffCount * 4];
        readonly int[] triangles = new int[PuffCount * 6];
        float age, resolve, opacity;

        public static float ResolveAt(float age, float travel, bool submerging)
        {
            float progress = Mathf.SmoothStep(0, 1,
                Mathf.Clamp01((age - GhostPoolVisual.GrowSeconds) / Mathf.Max(.01f, travel)));
            return submerging ? 1 - progress : progress;
        }

        public static GhostPoolAtmosphere Create(Renderer pool)
        {
            if (pool == null || pool.sharedMaterial == null || !pool.sharedMaterial.HasProperty("_Mist")) return null;
            var go = new GameObject("Resolve smoke (visual only)");
            go.transform.SetParent(pool.transform.parent, false);
            go.transform.position = pool.transform.position;
            var atmosphere = go.AddComponent<GhostPoolAtmosphere>();
            atmosphere.Initialize(pool.sharedMaterial);
            return atmosphere;
        }

        void Initialize(Material water)
        {
            // Reuse the serialized water shader reference so the smoke path survives build stripping.
            material = new Material(water) { name = "Black water resolve mist (instance)" };
            material.SetFloat("_Mist", 1);
            material.renderQueue = 3010;
            mesh = new Mesh { name = "Resolve smoke billboards" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            smokeRenderer = gameObject.AddComponent<MeshRenderer>();
            smokeRenderer.sharedMaterial = material;
            smokeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            smokeRenderer.receiveShadows = false;
            for (int p = 0; p < PuffCount; p++)
            {
                int v = p * 4, t = p * 6;
                uv[v] = new Vector2(0, 0); uv[v + 1] = new Vector2(1, 0);
                uv[v + 2] = new Vector2(1, 1); uv[v + 3] = new Vector2(0, 1);
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles;
        }

        public void Sample(float seconds, float formation, float fade)
        {
            age = Mathf.Max(0, seconds);
            resolve = Mathf.Clamp01(formation);
            opacity = Mathf.Clamp01(fade);
            if (smokeRenderer != null) smokeRenderer.enabled = opacity > .001f;
        }

        void OnEnable() => RenderPipelineManager.beginCameraRendering += RenderSmoke;
        void OnDisable() => RenderPipelineManager.beginCameraRendering -= RenderSmoke;

        void RenderSmoke(ScriptableRenderContext context, Camera camera)
        {
            if (mesh == null || opacity <= .001f || camera == null) return;
            // Per-camera billboards also work with the Scene view and multiplayer split cameras.
            Vector3 right = transform.InverseTransformVector(camera.transform.right);
            Vector3 up = transform.InverseTransformVector(camera.transform.up);
            float gather = Mathf.SmoothStep(0, 1, resolve);
            float freeze = Mathf.SmoothStep(.82f, 1, resolve);
            float time = age * (1 - freeze);
            for (int p = 0; p < PuffCount; p++)
            {
                float seed = p * 2.399963f;
                float height = (p % 24) / 23f;
                // Every sixth ribbon curls against the main current. No global random state is used.
                float angle = seed + time * (p % 6 == 0 ? -.6f : .4f);
                float radius = Mathf.Lerp(1.05f, .28f, gather) * (.6f + .4f * Mathf.Sin(seed * 1.7f));
                Vector3 loose = new Vector3(Mathf.Cos(angle) * radius,
                    .07f + height * 1.6f + .12f * Mathf.Sin(seed + time), Mathf.Sin(angle) * radius);
                // A deliberately ambiguous head / shoulders / taper, rather than a new character mesh.
                float width = height > .78f ? .13f : Mathf.Lerp(.12f, .38f, Mathf.Sin(height * Mathf.PI));
                Vector3 silhouette = new Vector3(Mathf.Cos(seed) * width,
                    .1f + height * 2.15f, Mathf.Sin(seed) * .16f);
                Vector3 center = Vector3.Lerp(loose, silhouette, gather);
                float size = Mathf.Lerp(.62f, .32f, gather) * (.8f + .2f * Mathf.Sin(seed));
                float alpha = opacity * Mathf.Lerp(.17f, .07f, gather);
                Color color = new Color(.045f, .055f, .065f, alpha);
                int v = p * 4;
                vertices[v] = center - right * size - up * size;
                vertices[v + 1] = center + right * size - up * size;
                vertices[v + 2] = center + right * size + up * size;
                vertices[v + 3] = center - right * size + up * size;
                colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = color;
            }
            mesh.vertices = vertices; mesh.colors = colors;
            mesh.RecalculateBounds();
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
