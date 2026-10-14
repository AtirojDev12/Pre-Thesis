using System;

[Serializable]
public struct PlacedDrinkState
{
    public uint revision;
    public PopcornFlavor flavor;
    public uint fillingActor;
    public bool IsReady => PopcornRecipe.IsDrink(flavor);
}
