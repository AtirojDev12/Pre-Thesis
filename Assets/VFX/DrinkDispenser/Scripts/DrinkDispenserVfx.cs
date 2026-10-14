using UnityEngine;

namespace PreThesis.Vfx
{
    public sealed class DrinkDispenserVfx : MonoBehaviour
    {
        public LineRenderer stream;
        public LineRenderer highlight;
        public ParticleSystem fallingDroplets;
        public ParticleSystem rimSplashes;
        public Vector3 nozzle = new Vector3(0, 1.7f, 0);
        public Vector3 cupRim = new Vector3(0, .78f, 0);
        public bool IsDispensing { get; private set; }

        public void BeginDispensing()
        {
            if (IsDispensing) return;
            IsDispensing = true;
            UpdateStream();
            if (stream != null) stream.enabled = true;
            if (highlight != null) highlight.enabled = true;
            if (fallingDroplets != null) fallingDroplets.Play(true);
            if (rimSplashes != null) rimSplashes.Play(true);
        }

        // Clearing is synchronous: no trailing water remains after a cancelled hold.
        public void Cancel()
        {
            IsDispensing = false;
            if (stream != null) stream.enabled = false;
            if (highlight != null) highlight.enabled = false;
            if (fallingDroplets != null) fallingDroplets.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (rimSplashes != null) rimSplashes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        public void Complete() => Cancel();
        private void OnDisable() => Cancel();
        private void Update() { if (IsDispensing) UpdateStream(); }
        private void UpdateStream()
        {
            const int count = 12;
            if (stream != null) stream.positionCount = count;
            if (highlight != null) highlight.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / (count - 1);
                Vector3 point = Vector3.Lerp(nozzle, cupRim, t);
                point.x += Mathf.Sin(t * 18f - Time.time * 16f) * .006f * Mathf.Sin(t * Mathf.PI);
                if (stream != null) stream.SetPosition(i, point);
                if (highlight != null) highlight.SetPosition(i, point + Vector3.back * .012f);
            }
        }
    }
}
