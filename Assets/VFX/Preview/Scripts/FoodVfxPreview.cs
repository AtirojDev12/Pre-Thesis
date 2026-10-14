using UnityEngine;

namespace PreThesis.Vfx
{
    /// <summary>Scene-only controls; reusable effects have no automatic playback.</summary>
    public sealed class FoodVfxPreview : MonoBehaviour
    {
        public PopcornMachineVfx popcorn;
        public DrinkDispenserVfx drink;
        public bool autoReplay = true;
        [Tooltip("Animate the preview in Scene view without entering Play mode.")]
        public bool previewInEditMode = true;
        [Min(4)] public float cycleSeconds = 6;
        private float startedAt;
        private bool cycle;
        private string state = "PREPARING";
        private void Start() => Replay();
        public void Replay()
        {
            CancelAll();
            popcorn.BeginPreparation();
            drink.BeginDispensing();
            startedAt = Time.time;
            cycle = true;
            state = "PREPARING";
        }
        public void CancelDrink()
        {
            drink.Cancel();
            autoReplay = false;
            state = "DRINK CANCELLED / water cleared immediately";
        }
        public void CancelAll()
        {
            if (popcorn != null) popcorn.Cancel();
            if (drink != null) drink.Cancel();
            cycle = false;
            state = "STOPPED";
        }
        private void Update()
        {
            if (!cycle) return;
            float age = Time.time - startedAt;
            if (age >= 3f)
            {
                popcorn.Complete();
                drink.Complete();
                if (state == "PREPARING") state = "READY / preparation complete";
            }
            if (autoReplay && age >= cycleSeconds) Replay();
        }
        private void OnDisable() => CancelAll();
        private void OnGUI()
        {
            float scale = Mathf.Clamp(Screen.height / 900f, .65f, 2f);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUI.Box(new Rect(24, 24, 540, 148), "FOOD PREPARATION / VFX PREVIEW");
            if (GUI.Button(new Rect(40, 60, 150, 34), "Prepare both")) Replay();
            if (GUI.Button(new Rect(200, 60, 164, 34), "Cancel drink now")) CancelDrink();
            if (GUI.Button(new Rect(374, 60, 172, 34), "Stop all")) { CancelAll(); autoReplay = false; }
            autoReplay = GUI.Toggle(new Rect(40, 106, 180, 24), autoReplay, "Auto replay (6 sec)");
            GUI.Label(new Rect(40, 136, 505, 26), state);
            GUI.matrix = previous;
        }
    }
}
