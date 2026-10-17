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
        [Tooltip("Optional cinema cup anchor. Follows the filling flavor and the actual cup rim at this machine.")]
        public Transform placementAnchor;
        public Material fantaMaterial;
        public Material pepsiMaterial;
        public Material orangeJuiceMaterial;
        public bool IsDispensing { get; private set; }
        private Material waterMaterial;
        private Material waterHighlight;
        private ParticleSystem.MinMaxGradient waterDroplets;
        private ParticleSystem.MinMaxGradient waterSplashes;
        private bool cachedWater;
        private PopcornFlavor currentFlavor = PopcornFlavor.None;

        public void SetFlavor(PopcornFlavor flavor)
        {
            if (!cachedWater)
            {
                waterMaterial = stream != null ? stream.sharedMaterial : null;
                waterHighlight = highlight != null ? highlight.sharedMaterial : null;
                if (fallingDroplets != null) waterDroplets = fallingDroplets.main.startColor;
                if (rimSplashes != null) waterSplashes = rimSplashes.main.startColor;
                cachedWater = true;
            }
            if (currentFlavor == flavor) return;
            // Clear the previous flavor's particles before changing colors.
            if (IsDispensing) Cancel();
            currentFlavor = flavor;
            Material material = flavor == PopcornFlavor.Fanta ? fantaMaterial :
                flavor == PopcornFlavor.Pepsi ? pepsiMaterial :
                flavor == PopcornFlavor.OrangeJuice ? orangeJuiceMaterial : null;
            if (stream != null) stream.sharedMaterial = material != null ? material : waterMaterial;
            if (highlight != null) highlight.sharedMaterial = material != null ? material : waterHighlight;
            Color tint = material != null && material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
            if (fallingDroplets != null)
            {
                var main = fallingDroplets.main;
                main.startColor = material != null ? new ParticleSystem.MinMaxGradient(tint) : waterDroplets;
            }
            if (rimSplashes != null)
            {
                var main = rimSplashes.main;
                main.startColor = material != null ? new ParticleSystem.MinMaxGradient(tint) : waterSplashes;
            }
        }

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
        private void Update()
        {
            if (placementAnchor != null)
            {
                PopcornFlavor flavor = PopcornFlavor.None;
                bool filling = PopcornPreparation.Instance != null &&
                    PopcornPreparation.Instance.TryGetDispensingFlavorAt(placementAnchor, out flavor);
                if (filling)
                {
                    SetFlavor(flavor);
                    BeginDispensing();
                }
                else if (IsDispensing) Cancel();
            }
            if (IsDispensing) UpdateStream();
        }
        private void UpdateStream()
        {
            if (placementAnchor != null && PopcornPreparation.Instance != null &&
                PopcornPreparation.Instance.TryGetCupRimAt(placementAnchor, out Vector3 rim, out float diameter))
                SetCupTarget(rim, diameter);
            if (fallingDroplets != null) fallingDroplets.transform.localPosition = nozzle;
            if (rimSplashes != null) rimSplashes.transform.localPosition = cupRim;
            const int count = 12;
            if (stream != null) stream.positionCount = count;
            if (highlight != null) highlight.positionCount = count;
            float thickness = stream != null ? stream.widthMultiplier : 1f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / (count - 1);
                Vector3 point = Vector3.Lerp(nozzle, cupRim, t);
                point.x += Mathf.Sin(t * 18f - Time.time * 16f) * .006f * thickness * Mathf.Sin(t * Mathf.PI);
                if (stream != null) stream.SetPosition(i, point);
                if (highlight != null) highlight.SetPosition(i, point + Vector3.back * (.012f * thickness));
            }
        }

        public void SetCupTarget(Vector3 worldRim, float openingDiameter)
        {
            cupRim = transform.InverseTransformPoint(worldRim);
            if (rimSplashes != null)
            {
                var shape = rimSplashes.shape;
                Vector3 scale = rimSplashes.transform.lossyScale;
                // Keep the splash footprint inside the opening, including scaled machines.
                float width = openingDiameter * .5f;
                shape.scale = new Vector3(width / Mathf.Max(.001f, Mathf.Abs(scale.x)),
                    .002f / Mathf.Max(.001f, Mathf.Abs(scale.y)), width / Mathf.Max(.001f, Mathf.Abs(scale.z)));
            }
            if (fallingDroplets != null)
            {
                var velocity = fallingDroplets.velocityOverLifetime;
                Vector3 speed = new Vector3(velocity.x.constant, velocity.y.constant, velocity.z.constant);
                if (velocity.space == ParticleSystemSimulationSpace.Local)
                    speed = fallingDroplets.transform.TransformDirection(speed);
                float fallingSpeed = Mathf.Abs(speed.y);
                if (velocity.enabled && fallingSpeed > .01f)
                {
                    var main = fallingDroplets.main;
                    float gap = Vector3.Distance(transform.TransformPoint(nozzle), worldRim);
                    // End falling particles at the cup instead of continuing below the tray.
                    main.startLifetime = Mathf.Max(.005f, gap / fallingSpeed);
                }
            }
        }
    }
}
