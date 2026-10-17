using UnityEngine;

namespace PreThesis.Vfx
{
    public sealed class PopcornMachineVfx : MonoBehaviour
    {
        public ParticleSystem steam;
        public ParticleSystem kernels;
        public ParticleSystem seasoning;
        [Tooltip("Optional gameplay refill machine. Only steam plays while this machine is making popcorn.")]
        public Transform makerStation;

        private void Awake()
        {
            if (makerStation == null || steam == null) return;
            // The authored maker and steam both use transparent queue 3000.
            // Rising particle bounds change distance sorting and let the glass hide
            // the entire plume. Keep steam after the maker's order-zero glass.
            steam.GetComponent<ParticleSystemRenderer>().sortingOrder = 1;
        }

        private void Update()
        {
            if (makerStation == null) return;
            if (PopcornPreparation.Instance != null && PopcornPreparation.Instance.IsMakingRefillAt(makerStation))
                PlaySteam();
            else if (steam != null && (steam.isPlaying || steam.particleCount > 0))
                Clear(steam);
        }

        public void BeginCooking() { PlaySteam(); Play(kernels); }
        public void BeginSeasoning() => Play(seasoning);
        public void BeginPreparation() { BeginCooking(); BeginSeasoning(); }
        public void Cancel()
        {
            Clear(steam); Clear(kernels); Clear(seasoning);
        }
        public void Complete() => Cancel();
        private void OnDisable() => Cancel();
        private void PlaySteam()
        {
            if (steam == null || steam.isEmitting) return;
            // A system can still be playing its remaining particles after emission stops.
            // Reset that finished cycle so every new hold starts a fresh looping emitter.
            steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = steam.main;
            main.loop = true;
            steam.Play(true);
        }
        private static void Play(ParticleSystem effect)
        {
            if (effect != null && !effect.isPlaying) effect.Play(true);
        }
        private static void Clear(ParticleSystem effect)
        {
            if (effect != null) effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
