using UnityEngine;

namespace PreThesis.Vfx
{
    public sealed class VfxPreviewSwitcher : MonoBehaviour
    {
        public GameObject foodStage;
        public GameObject ghostStage;
        public FoodVfxPreview food;
        public GhostVfxPreview ghost;

        [ContextMenu("Show Popcorn and Drink")]
        public void ShowFood()
        {
            ghostStage.SetActive(false);
            foodStage.SetActive(true);
            if (Application.isPlaying) food.Replay();
        }

        [ContextMenu("Show Ghost Effects")]
        public void ShowGhost()
        {
            foodStage.SetActive(false);
            ghostStage.SetActive(true);
            if (Application.isPlaying) ghost.Replay();
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(Screen.width - 206, 20, 186, 116), "VFX PREVIEW STAGES");
            if (GUI.Button(new Rect(Screen.width - 194, 54, 162, 30), "Popcorn + Drink")) ShowFood();
            if (GUI.Button(new Rect(Screen.width - 194, 92, 162, 30), "Ghost effects")) ShowGhost();
        }
    }
}
