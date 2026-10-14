using UnityEngine;

namespace PreThesis.Vfx
{
    public sealed class PopcornMachineVfx : MonoBehaviour
    {
        public ParticleSystem steam;
        public ParticleSystem kernels;
        public ParticleSystem seasoning;

        public void BeginCooking() { Play(steam); Play(kernels); }
        public void BeginSeasoning() => Play(seasoning);
        public void BeginPreparation() { BeginCooking(); BeginSeasoning(); }
        public void Cancel()
        {
            Clear(steam); Clear(kernels); Clear(seasoning);
        }
        public void Complete() => Cancel();
        private void OnDisable() => Cancel();
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
