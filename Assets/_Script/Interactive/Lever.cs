using UnityEngine;

public class Lever : InteractableBase
{
    protected override void Awake()
    {
        base.Awake();
        interactionPrompt = "Press E to pull lever";
    }

    protected override void OnInteracted(GameObject interactor)
    {
        // your logic here
    }
}