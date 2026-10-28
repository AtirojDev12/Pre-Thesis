using UnityEngine;
using UnityEngine.Rendering;

namespace PreThesis.Vfx
{
    /// <summary>Lit volumetric impact smoke, in metres. Independent of the item after contact.</summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ItemImpactVfx : MonoBehaviour
    {
        public const float Lifetime = .5f;
        private const int CloudCount = 12;
        private const int QuadStart = CloudCount * 8;
        private const int VertexCount = QuadStart + 6 * 4;
        private static readonly Vector3[] Corners = {
            new Vector3(-1,-1,-1), new Vector3(-1,1,-1), new Vector3(1,1,-1), new Vector3(1,-1,-1),
            new Vector3(-1,-1,1), new Vector3(-1,1,1), new Vector3(1,1,1), new Vector3(1,-1,1) };
        private static readonly int[] BoxFaces = { 0,1,2,0,2,3, 4,7,6,4,6,5, 0,4,5,0,5,1,
            3,2,6,3,6,7, 0,3,7,0,7,4, 1,5,6,1,6,2 };
        public const string ResourcePath = "ItemImpact/ItemImpact";
        [Range(.25f, 3f)] public float size = 1f;
        [Range(0f, 2f)] public float dustAmount = 1f;
        [System.NonSerialized] public bool manualTime;
        private Mesh mesh;
        private readonly Vector3[] vertices = new Vector3[VertexCount];
        private readonly Vector2[] uv = new Vector2[VertexCount];
        private readonly Color[] colors = new Color[VertexCount];
        private readonly Vector3[] normals = new Vector3[VertexCount];
        private readonly Vector3[] cloudCenters = new Vector3[VertexCount];
        private readonly Vector3[] cloudRadii = new Vector3[VertexCount];
        private readonly Vector2[] cloudStates = new Vector2[VertexCount];
        private float began, strength = 1f;
        private int seed;
        private bool running;
        private static ItemImpactVfx template;
        private static int liveCount;
        private bool counted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() { template = null; liveCount = 0; }

        public static ItemImpactVfx Spawn(Vector3 point, Vector3 normal, float power, float scale = 1f, float dust = 1f)
        {
            if (liveCount >= 48 || Application.isBatchMode && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            if (template == null)
            {
                var asset = Resources.Load<GameObject>(ResourcePath);
                if (asset != null) template = asset.GetComponent<ItemImpactVfx>();
            }
            if (template == null) return null;
            var effect = Instantiate(template, point + normal.normalized * .003f, Quaternion.FromToRotation(Vector3.up, normal));
            effect.size *= Mathf.Clamp(scale, .25f, 3f);
            effect.dustAmount *= Mathf.Clamp(dust, 0f, 2f);
            effect.counted = true;
            liveCount++;
            effect.Play(power, Random.Range(0, 10000));
            return effect;
        }

        private void Awake()
        {
            mesh = new Mesh { name = "Item contact transient mesh" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            mesh.subMeshCount = 3;
            for (int i = 0; i < 6; i++)
            {
                int v = QuadStart + i * 4;
                uv[v] = new Vector2(0, 0); uv[v + 1] = new Vector2(0, 1);
                uv[v + 2] = new Vector2(1, 1); uv[v + 3] = new Vector2(1, 0);
                for (int j = 0; j < 4; j++) normals[v + j] = Vector3.up;
            }
            mesh.vertices = vertices; mesh.uv = uv; mesh.normals = normals; mesh.colors = colors;
            var volumeTriangles = new int[CloudCount * BoxFaces.Length];
            for (int i = 0; i < CloudCount; i++) for (int j = 0; j < BoxFaces.Length; j++)
                volumeTriangles[i * BoxFaces.Length + j] = i * 8 + BoxFaces[j];
            mesh.SetTriangles(volumeTriangles, 0);
            mesh.SetTriangles(Indices(0, 5), 1);
            mesh.SetTriangles(Indices(5, 1), 2);
            var renderer = GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static int[] Indices(int first, int count)
        {
            var result = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                int v = QuadStart + (first + i) * 4, t = i * 6;
                result[t] = v; result[t + 1] = v + 1; result[t + 2] = v + 2;
                result[t + 3] = v; result[t + 4] = v + 2; result[t + 5] = v + 3;
            }
            return result;
        }

        public void Play(float power, int randomSeed)
        {
            strength = Mathf.Clamp(power, .15f, 1f);
            seed = randomSeed;
            began = Time.time;
            running = true;
            Sample(0);
        }

        private void Update()
        {
            if (!running || manualTime) return;
            float age = Time.time - began;
            if (age >= Lifetime) { Destroy(gameObject); return; }
            Sample(age);
        }

        // Also supports exact-time inspection in the preview and render checks.
        public void Sample(float age)
        {
            if (mesh == null) Awake();
            float scale = 1.65f * size * Mathf.Lerp(.6f, 1f, strength);
            float opacity = Mathf.Lerp(.55f, 1f, strength);
            for (int i = 0; i < CloudCount; i++)
            {
                float r = Noise(i), angle = (i * .7854f + r * .65f + seed) % (Mathf.PI * 2);
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                float start = i == 0 ? 0 : .012f + (i % 8) * .003f;
                float t = Mathf.Clamp01((age - start) / (Lifetime - start));
                float ease = 1 - (1 - t) * (1 - t);
                bool upper = i > 8;
                float radius = Mathf.Lerp(i == 0 ? .025f : .018f, i == 0 ? .12f : .075f + .025f * r, ease) * scale;
                Vector3 radii = new Vector3(radius * (1 + r * .25f), radius * (upper ? .95f : .75f), radius);
                Vector3 center = i == 0 ? Vector3.zero : dir * ease * (upper ? .055f : .12f + .06f * r) * scale;
                center.y = radii.y * .85f + (upper ? .075f : .025f) * t * scale;
                float fadeIn = i == 0 ? 1 : Mathf.SmoothStep(0, 1, (age - start) / .045f);
                float fadeOut = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.15f, Lifetime, age));
                float density = (i == 0 ? .24f : upper ? .13f : .19f) * dustAmount * opacity * fadeIn * fadeOut;
                Cloud(i, center, radii, age < Lifetime ? density : 0, age);
            }
            for (int i = 0; i < 5; i++)
            {
                float r = Noise(i + 5), angle = i * 1.2566f + r * .6f + seed;
                Vector3 dir = new Vector3(Mathf.Cos(angle), i % 3 == 0 ? .3f : .04f, Mathf.Sin(angle)).normalized;
                float t = Mathf.Clamp01((age - .02f) / .10f), ease = 1 - (1 - t) * (1 - t);
                float length = Mathf.Lerp(.03f, .07f, r) * scale;
                Vector3 center = dir * (.015f + ease * .045f) * scale + Vector3.up * .003f;
                Quad(i, center, Vector3.Cross(Vector3.up, dir).normalized, dir,
                    .006f * scale * (1 - t * .7f), length / (.006f * scale * (1 - t * .7f)),
                    age >= .02f && age < .12f ? .07f * opacity * (1 - t) : 0);
            }
            float ringT = Mathf.Clamp01((age - .03f) / .24f);
            float ringRadius = Mathf.Lerp(.03f, .15f + .10f * strength, 1 - (1 - ringT) * (1 - ringT));
            Quad(5, Vector3.up * .002f, Vector3.right, Vector3.forward, ringRadius * 2 * scale, 1,
                age >= .03f && age < .27f ? .035f * opacity * Mathf.Pow(1 - ringT, 1.5f) : 0);
            mesh.vertices = vertices; mesh.colors = colors;
            mesh.SetUVs(1, cloudCenters); mesh.SetUVs(2, cloudRadii); mesh.SetUVs(3, cloudStates);
            mesh.RecalculateBounds();
        }

        private float Noise(int i) => Mathf.Repeat(Mathf.Sin((seed + i * 17) * 12.9898f) * 43758.5453f, 1f);

        private void Cloud(int index, Vector3 center, Vector3 radii, float alpha, float age)
        {
            for (int i = 0; i < 8; i++)
            {
                int v = index * 8 + i;
                vertices[v] = center + Vector3.Scale(Corners[i], radii);
                cloudCenters[v] = center; cloudRadii[v] = radii;
                cloudStates[v] = new Vector2(Noise(index + 30) * 17f, age);
                colors[v] = new Color(.46f, .47f, .48f, alpha);
            }
        }

        private void Quad(int index, Vector3 center, Vector3 right, Vector3 up, float width, float aspect, float alpha)
        {
            int v = QuadStart + index * 4;
            Vector3 x = right * width * .5f, y = up * width * aspect * .5f;
            vertices[v] = center - x - y; vertices[v + 1] = center - x + y;
            vertices[v + 2] = center + x + y; vertices[v + 3] = center + x - y;
            for (int i = 0; i < 4; i++) colors[v + i] = new Color(.5f, .5f, .5f, alpha);
        }

        private void OnDestroy()
        {
            if (counted) liveCount = Mathf.Max(0, liveCount - 1);
            if (mesh != null)
            {
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
        }
    }
}
