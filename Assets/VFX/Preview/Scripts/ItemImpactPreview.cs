using UnityEngine;

namespace PreThesis.Vfx
{
    /// <summary>Preview-only physical drop and an exact-time inspection of the gameplay effect.</summary>
    public sealed class ItemImpactPreview : MonoBehaviour
    {
        public GameObject itemPrefab;
        [System.NonSerialized] public Rigidbody item;
        public bool autoReplay = true;
        private float began;
        private bool inspecting;
        private ItemImpactVfx inspection;
        private float timeline = .055f;
        private float power = 1f, size = 1f, dust = 1f;
        private Vector3 originalItemScale;
        private bool largeItem;

        private void Start()
        {
            // Instantiate at runtime: Mirror disables scene identities before offline Play.
            if (!Application.isPlaying || itemPrefab == null) return;
            var world = Instantiate(itemPrefab, transform);
            item = world.GetComponent<Rigidbody>();
            originalItemScale = item.transform.localScale;
            world.GetComponent<WorldInventoryItem>().Initialize(InventorySlot.Of(ItemCatalog.Flashlight));
            Replay();
        }

        private void OnEnable() { if (Application.isPlaying) Replay(); }

        public void Replay()
        {
            if (item == null) return;
            Clear();
            inspecting = false;
            item.gameObject.SetActive(true);
            item.isKinematic = false;
            item.position = transform.TransformPoint(new Vector3(0, .65f, 0));
            item.rotation = Quaternion.Euler(30, 0, 65);
            item.linearVelocity = Vector3.zero;
            item.angularVelocity = new Vector3(0, 0, 1);
            item.WakeUp();
            began = Time.time;
        }

        private void Update()
        {
            if (!inspecting && autoReplay && Time.time - began > 2f) Replay();
        }

        private void Inspect()
        {
            Clear();
            inspecting = true;
            item.gameObject.SetActive(false);
            inspection = ItemImpactVfx.Spawn(transform.position, transform.up, power, size, dust);
            if (inspection != null)
            {
                inspection.manualTime = true;
                inspection.Sample(timeline);
            }
        }

        private void Clear()
        {
            if (inspection != null) Destroy(inspection.gameObject);
            inspection = null;
        }

        private void OnDisable() { if (Application.isPlaying) Clear(); }

        private void OnGUI()
        {
            GUI.Box(new Rect(24, 24, 365, 310), "3D IMPACT SMOKE / 0.5 SECOND");
            if (GUI.Button(new Rect(38, 55, 150, 28), "Replay physical drop")) Replay();
            autoReplay = GUI.Toggle(new Rect(204, 57, 160, 25), autoReplay, "Auto replay");
            GUI.Label(new Rect(38, 92, 320, 24), "Inspect exact time: " + timeline.ToString("0.000") + " s");
            float t = GUI.HorizontalSlider(new Rect(38, 122, 325, 20), timeline, 0, ItemImpactVfx.Lifetime);
            GUI.Label(new Rect(38, 146, 320, 24), "Strength / size / dust (inspection)");
            float p = GUI.HorizontalSlider(new Rect(38, 177, 96, 20), power, .15f, 1f);
            float s = GUI.HorizontalSlider(new Rect(151, 177, 96, 20), size, .25f, 3f);
            float d = GUI.HorizontalSlider(new Rect(265, 177, 96, 20), dust, 0, 2f);
            if (t != timeline || p != power || s != size || d != dust)
            {
                timeline = t; power = p; size = s; dust = d; Inspect();
            }
            if (GUI.Button(new Rect(38, 210, 325, 28), "Freeze impact at selected time")) Inspect();
            bool larger = GUI.Toggle(new Rect(38, 253, 325, 25), largeItem, "Large item (2.5x physical size)");
            if (larger != largeItem && item != null)
            {
                largeItem = larger;
                item.transform.localScale = originalItemScale * (largeItem ? 2.5f : 1f);
                Replay();
            }
            if (GUI.Button(new Rect(38, 291, 100, 28), "Front view")) SetView(new Vector3(.65f, .55f, -.85f));
            if (GUI.Button(new Rect(151, 291, 100, 28), "Side view")) SetView(new Vector3(-.85f, .2f, .4f));
            if (GUI.Button(new Rect(264, 291, 100, 28), "Above view")) SetView(new Vector3(.01f, 1, -.2f));
        }

        private void SetView(Vector3 position)
        {
            var camera = GetComponentInChildren<Camera>();
            camera.transform.position = transform.TransformPoint(position);
            camera.transform.LookAt(transform.TransformPoint(new Vector3(0, .08f, 0)));
        }
    }
}
