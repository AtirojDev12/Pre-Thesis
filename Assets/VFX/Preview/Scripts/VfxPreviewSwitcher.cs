using UnityEngine;

namespace PreThesis.Vfx
{
    public sealed class VfxPreviewSwitcher : MonoBehaviour
    {
        public GameObject foodStage;
        public GameObject ghostStage;
        public FoodVfxPreview food;
        public GhostVfxPreview ghost;
        public GhostPoolPreview ghostPool;
        public GameObject favorStage;
        public GhostFavorVfxPreview favor;
        public GameObject impactStage;
        public ItemImpactPreview impact;

        [ContextMenu("Show Popcorn and Drink")]
        public void ShowFood()
        {
            if (impactStage != null) impactStage.SetActive(false);
            if (favorStage != null) favorStage.SetActive(false);
            ghostStage.SetActive(false);
            foodStage.SetActive(true);
            if (Application.isPlaying) food.Replay();
        }

        [ContextMenu("Show Ghost Effects")]
        public void ShowGhost()
        {
            if (impactStage != null) impactStage.SetActive(false);
            if (favorStage != null) favorStage.SetActive(false);
            foodStage.SetActive(false);
            ghostStage.SetActive(true);
            if (Application.isPlaying)
            {
                if (ghostPool != null) ghostPool.Replay();
                else if (ghost != null) ghost.Replay();
            }
        }

        [ContextMenu("Show Ghost Favor Disappearance")]
        public void ShowFavor()
        {
            if (favorStage == null) return;
            if (impactStage != null) impactStage.SetActive(false);
            foodStage.SetActive(false);
            ghostStage.SetActive(false);
            favorStage.SetActive(true);
            if (Application.isPlaying && favor != null) favor.Replay();
        }

        [ContextMenu("Show Item Drop Impact")]
        public void ShowImpact()
        {
            if (impactStage == null) return;
            foodStage.SetActive(false);
            ghostStage.SetActive(false);
            if (favorStage != null) favorStage.SetActive(false);
            impactStage.SetActive(true);
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(Screen.width - 206, 20, 186, 192), "VFX PREVIEW STAGES");
            if (GUI.Button(new Rect(Screen.width - 194, 54, 162, 30), "Popcorn + Drink")) ShowFood();
            if (GUI.Button(new Rect(Screen.width - 194, 92, 162, 30), "Ghost black water")) ShowGhost();
            if (favorStage != null && GUI.Button(new Rect(Screen.width - 194, 130, 162, 30), "Ghost Favor (2 sec)")) ShowFavor();
            if (impactStage != null && GUI.Button(new Rect(Screen.width - 194, 168, 162, 30), "Item drop (0.5 sec)")) ShowImpact();
        }
    }
}
