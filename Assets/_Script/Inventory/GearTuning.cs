using System;
using UnityEngine;

/// <summary>
/// 17 Nov 2026 (Mr.k). ONE place for the game designer to balance the free / paid
/// Walkie-Talkies and the two Syringes (Task 7).
///
/// HOW TO USE
///   1. Tools > Pre-Thesis > Designer Settings > Walkies + Syringes  (creates the file the first time)
///   2. Change the numbers in the Inspector. Works live in Play mode.
///   3. Commit the file: Assets/Resources/Tuning/GearTuning.asset
///
/// No asset yet? The game uses the default numbers written below.
/// ONLINE: every player must have the same values (same build). The host decides.
/// </summary>
[CreateAssetMenu(fileName = "GearTuning", menuName = "Pre-Thesis/Gear Tuning (walkies + syringes)")]
public sealed class GearTuning : ScriptableObject
{
    public const string ResourcePath = "Tuning/GearTuning";
    public const string AssetPath = "Assets/Resources/Tuning/GearTuning.asset";

    [Serializable]
    public sealed class Walkie
    {
        public string displayName;
        [TextArea(2, 4)] public string description;
        [Min(0), Tooltip("Shop price. 0 = FREE (Claim button).")]
        public int price;
    }

    [Serializable]
    public sealed class WalkieBattery
    {
        [Range(30f, 7200f), Tooltip("Seconds a FULL battery lasts while the walkie is ON and nobody talks.")]
        public float secondsWhenIdle = 1200f;
        [Range(1f, 10f), Tooltip("Drains this many times faster while you talk OR hear someone (both at once = still this, not double).")]
        public float talkOrHearMultiplier = 3f;
    }

    [Serializable]
    public sealed class Syringe
    {
        public string displayName;
        [TextArea(2, 4)] public string description;
        [Min(0), Tooltip("Shop price. 0 = FREE (Claim button).")]
        public int price;
        [Range(1, 10), Tooltip("Most a player can own. They stack in ONE slot.")]
        public int maxCarry;
        [Range(0f, 1f), Tooltip("Heals this part of the MISSING health. 0.75 = 75%. Not rounded.")]
        public float healPartOfMissing;
        [Range(0f, 200f), Tooltip("Heals this much health on top (flat).")]
        public float healFlat;
    }

    [Serializable]
    public sealed class SyringeUse
    {
        [Range(0.5f, 10f), Tooltip("Hold the use key (default Left Click) this long to use a syringe.")]
        public float holdSeconds = 2f;
        [Range(1f, 5f), Tooltip("How close a downed friend must be to use a syringe on them (metres).")]
        public float reviveRange = 2.5f;
    }

    [Header("Free Walkie-Talkie (battery)")]
    public Walkie freeWalkie = new Walkie
    {
        displayName = "Free Walkie-Talkie",
        description = "Free. Runs on a battery: about 20 minutes ON, drains 3x faster while you talk or hear someone. Full again in the lobby. Lost if you die.",
        price = 0,
    };
    public WalkieBattery freeWalkieBattery = new WalkieBattery();

    [Header("Walkie-Talkie (paid, no battery)")]
    public Walkie paidWalkie = new Walkie
    {
        displayName = "Walkie-Talkie",
        description = "Talk to every teammate who carries a switched-on walkie, at any distance. No battery. Lost if you die.",
        price = 100,
    };

    [Header("Syringe (paid)")]
    public Syringe paidSyringe = new Syringe
    {
        displayName = "Syringe",
        description = "Hold Left Click: heal 75% of your missing health. Or hold it on a downed friend to get them up.",
        price = 150, maxCarry = 3, healPartOfMissing = 0.75f, healFlat = 0f,
    };

    [Header("Free Syringe")]
    public Syringe freeSyringe = new Syringe
    {
        displayName = "Free Syringe",
        description = "Free, carry 1. Hold Left Click: +15 health. Or hold it on a downed friend to get them up.",
        price = 0, maxCarry = 1, healPartOfMissing = 0f, healFlat = 15f,
    };

    [Header("Using a syringe")]
    public SyringeUse syringeUse = new SyringeUse();

    // ---- Runtime ----------------------------------------------------------------

    /// <summary>Goes up on every Inspector edit, so ItemCatalog re-reads the values.</summary>
    public static int Changes { get; private set; }

    private static GearTuning current;

    /// <summary>The asset in Resources, or the defaults above if it does not exist yet.</summary>
    public static GearTuning Current
    {
        get
        {
            if (current != null) return current;
            current = Resources.Load<GearTuning>(ResourcePath);
            if (current == null)
            {
                current = CreateInstance<GearTuning>();
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
        Changes++;
    }

    /// <summary>Editor only: the settings file was just created, use it from now on.</summary>
    public static void Use(GearTuning asset)
    {
        if (asset == null) return;
        current = asset;
        Changes++;
    }

    private void OnValidate()
    {
        paidSyringe.maxCarry = Mathf.Max(1, paidSyringe.maxCarry);
        freeSyringe.maxCarry = Mathf.Max(1, freeSyringe.maxCarry);
        freeWalkieBattery.secondsWhenIdle = Mathf.Max(1f, freeWalkieBattery.secondsWhenIdle);
        freeWalkieBattery.talkOrHearMultiplier = Mathf.Max(1f, freeWalkieBattery.talkOrHearMultiplier);
        Changes++;
    }
}
