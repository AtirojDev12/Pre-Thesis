using UnityEngine;

/// <summary>
/// 25 Nov 2026: the COLOURS and post-it behaviour of the Memo Book (WorksheetReader).
/// The book's pictures, places and sizes are in its prefab: Assets/Resources/UI/Memo Book.prefab
/// (Tools > Pre-Thesis > Designer Settings > Memo Book Prefab).
///
/// HOW TO USE
///   1. Tools > Pre-Thesis > Designer Settings > Memo Book Look  (selects this file)
///   2. Change the values in the Inspector. Close and open the book again to see them.
///   3. Commit the file: Assets/Resources/Tuning/MemoBookLook.asset
/// </summary>
[CreateAssetMenu(fileName = "MemoBookLook", menuName = "Pre-Thesis/Memo Book Look")]
public sealed class MemoBookLook : ScriptableObject
{
    public const string ResourcePath = "Tuning/MemoBookLook";
    public const string AssetPath = "Assets/Resources/Tuning/MemoBookLook.asset";

    [Header("Post-its")]
    [Tooltip("The open page's post-it moves this far to the right (screen pixels at 1920 x 1080). 18 = 2 picture pixels.")]
    public float selectedTabOut = 18f;
    [Tooltip("The other post-its are darkened with this colour.")]
    public Color idleTabTint = new Color(0.78f, 0.78f, 0.78f, 1f);

    [Header("Pages")]
    [Tooltip("Colour of each post-it's page (titles, cards), same order as the post-its. Darker than the post-it so it reads on white paper.")]
    public Color[] pageColors =
    {
        new Color(0.72f, 0.52f, 0.05f), new Color(0.78f, 0.24f, 0.30f), new Color(0.20f, 0.45f, 0.78f),
        new Color(0.30f, 0.30f, 0.32f), new Color(0.22f, 0.58f, 0.30f), new Color(0.85f, 0.42f, 0.20f),
    };
    [Tooltip("Colour of the normal words (ink).")]
    public Color ink = new Color(0.20f, 0.13f, 0.10f, 1f);

    private static MemoBookLook current;

    public static MemoBookLook Current
    {
        get
        {
            if (current != null) return current;
            current = Resources.Load<MemoBookLook>(ResourcePath);
            if (current == null)
            {
                current = CreateInstance<MemoBookLook>();
                current.hideFlags = HideFlags.HideAndDontSave;
            }
            return current;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        if (current != null && (current.hideFlags & HideFlags.DontSave) != 0) DestroyImmediate(current);
        current = null;
    }

    public static void Use(MemoBookLook asset)
    {
        if (asset != null) current = asset;
    }
}
