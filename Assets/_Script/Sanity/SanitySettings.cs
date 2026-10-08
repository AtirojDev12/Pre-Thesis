using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 8 Oct (Mr.k, GDD "Sanity (หลอดสติ)"). ONE page for the game designer to balance
/// sanity. Same idea as FlashlightTuning:
///
///   Tools > Pre-Thesis > Designer Settings > Sanity   (creates the file the first time and selects it)
///   Change numbers in the Inspector. Works LIVE in Play mode. Commit:
///      Assets/Resources/Tuning/SanitySettings.asset
///
/// No asset yet? The game uses the default numbers written below.
/// ONLINE: everybody needs the same asset (same commit). The host decides sanity.
/// </summary>
[CreateAssetMenu(fileName = "SanitySettings", menuName = "Pre-Thesis/Sanity Settings")]
public sealed class SanitySettings : ScriptableObject
{
    public const string ResourcePath = "Tuning/SanitySettings";
    public const string AssetPath = "Assets/Resources/Tuning/SanitySettings.asset";

    // ---- Levels --------------------------------------------------------------

    [Header("Levels (0 - 100)")]
    [Range(1f, 100f), Tooltip("At or below this = SHAKEN: vision gets harder, anomalies come more often, tasks get harder.")]
    public float shakenBelow = 60f;
    [Range(0f, 100f), Tooltip("At or below this = BREAKING: friends look like ghosts, and (if ON below) you cannot gain sanity or heal.")]
    public float breakingBelow = 30f;
    [Tooltip("GDD: in the lowest level you cannot gain sanity (chant / snack do nothing).")]
    public bool blockGainWhenBreaking = true;
    [Tooltip("GDD: in the lowest level you cannot heal HP (being revived still works).")]
    public bool blockHealWhenBreaking = true;

    // ---- Drain ---------------------------------------------------------------

    [Header("Drain: seeing a ghost")]
    [Range(0f, 50f), Tooltip("One hit when a ghost comes into view (again after 10 s of not seeing one).")]
    public float ghostFirstSight = 8f;
    [Range(0f, 20f), Tooltip("Per second while you keep looking at a ghost.")]
    public float ghostSightPerSecond = 2f;
    [Range(3f, 60f), Tooltip("A ghost further away than this (metres) does not count.")]
    public float ghostSightRange = 18f;
    [Range(5f, 90f), Tooltip("How far from the centre of your screen a ghost still counts (degrees).")]
    public float ghostSightAngle = 40f;

    [Header("Drain: alone in the dark")]
    [Range(1f, 30f), Tooltip("You are ALONE when no teammate is closer than this (metres).")]
    public float aloneRadius = 8f;
    [Range(0f, 120f), Tooltip("Seconds alone in the dark (lights off) before the drain starts.")]
    public float darkAloneGraceSeconds = 20f;
    [Range(0f, 10f), Tooltip("Per second after that.")]
    public float darkAlonePerSecond = 1f;

    [Header("Drain: called by other systems (rules, anomalies)")]
    [Range(0f, 100f), Tooltip("Default for PlayerSanity.ServerRuleFailed() when the rule does not say an amount.")]
    public float ruleFailedDefault = 10f;
    [Range(0f, 100f), Tooltip("Default for PlayerSanity.ServerAnomaly() when the anomaly does not say an amount.")]
    public float anomalyDefault = 5f;

    // ---- Zero ----------------------------------------------------------------

    [Header("Sanity at 0")]
    [Range(0f, 120f), Tooltip("Seconds at 0 before the first jumpscare.")]
    public float zeroGraceSeconds = 10f;
    [Range(0f, 100f), Tooltip("HP lost at each jumpscare (Mr.k 8 Oct: 5 for now, balance later).")]
    public float zeroDamage = 5f;
    [Range(1f, 120f), Tooltip("Seconds between jumpscares while still at 0.")]
    public float zeroRepeatSeconds = 10f;

    // ---- Chant ---------------------------------------------------------------

    [Header("Gain: chant (hold the key, stand still)")]
    public Key chantKey = Key.H;
    [Range(0f, 10f), Tooltip("Per second, alone and without a holy item. Slow on purpose.")]
    public float chantAlonePerSecond = 0.5f;
    [Range(0f, 10f), Tooltip("Per second when you CARRY a holy item (Holy Book / Amulet / Cross) anywhere in your hotbar.")]
    public float chantWithHolyItemPerSecond = 2f;
    [Range(0f, 10f), Tooltip("Added per second for EACH teammate chanting with you (close by). This is how you chant fast without an item.")]
    public float chantPerFriend = 0.75f;
    [Range(1f, 15f), Tooltip("Teammates closer than this (metres) chant together with you.")]
    public float chantFriendRadius = 4f;
    [Range(0.5f, 20f), Tooltip("The fastest chanting can ever go (per second).")]
    public float chantMaxPerSecond = 3f;
    [Range(0.1f, 3f), Tooltip("Moving faster than this (m/s) stops the chant from counting: stand still to pray.")]
    public float chantMaxMoveSpeed = 0.6f;

    // ---- Items ---------------------------------------------------------------

    [Serializable]
    public sealed class ShopItem
    {
        public string displayName;
        [TextArea(2, 4)] public string description;
        [Min(0)] public int price;
    }

    [Header("Gain: Snack (consumable)")]
    public ShopItem snack = new ShopItem
    {
        displayName = "Snack",
        description = "Hold Left Click to eat (+sanity). Hold Right Click on a friend to feed them, faster. Does nothing while you are breaking.",
        price = 15,
    };
    [Range(1, 10)] public int snackMaxCarry = 3;
    [Range(0f, 100f)] public float snackGain = 25f;
    [Range(0.2f, 10f), Tooltip("Seconds holding Left Click to eat it yourself.")]
    public float snackEatSeconds = 2f;
    [Range(0.1f, 10f), Tooltip("Seconds holding Right Click to feed a friend (GDD: a friend using an item on you is the FAST way).")]
    public float snackFeedSeconds = 0.75f;
    [Range(1f, 5f), Tooltip("How close the friend must be (metres).")]
    public float snackFeedRange = 2.5f;

    [Header("Holy items (carry one = chant faster)")]
    public ShopItem holyBook = new ShopItem { displayName = "Holy Book", description = "Carry it: chanting (hold H) restores sanity much faster. Lost if you die.", price = 120 };
    public ShopItem amulet = new ShopItem { displayName = "Amulet", description = "Carry it: chanting (hold H) restores sanity much faster. Lost if you die.", price = 120 };
    public ShopItem holyCross = new ShopItem { displayName = "Cross", description = "Carry it: chanting (hold H) restores sanity much faster. Lost if you die.", price = 120 };

    // ---- Effects -------------------------------------------------------------

    [Header("Effects (your own screen)")]
    [Range(0f, 1f), Tooltip("Dark edges of the screen when SHAKEN.")]
    public float vignetteShaken = 0.35f;
    [Range(0f, 1f), Tooltip("Dark edges of the screen when BREAKING (pulses like a heartbeat).")]
    public float vignetteBreaking = 0.75f;
    [Tooltip("BREAKING: what your friends look like. Empty = black shadows. A model here must be VISUAL ONLY (scripts are removed anyway).")]
    public GameObject hallucinationPrefab;
    [Tooltip("Optional picture for the sanity-0 jumpscare. Empty = a red flash.")]
    public Sprite jumpscareImage;
    [Tooltip("Optional sound for the sanity-0 jumpscare.")]
    public AudioClip jumpscareSound;
    [Range(0.1f, 3f)] public float jumpscareSeconds = 0.6f;

    [Header("For other programmers (read only by their code)")]
    [Tooltip("PlayerSanity.AnomalyChanceMultiplier when SHAKEN / BREAKING. The anomaly system multiplies its chance by this.")]
    public float anomalyMultiplierShaken = 1.5f;
    public float anomalyMultiplierBreaking = 2.5f;
    [Tooltip("PlayerSanity.TaskDifficultyMultiplier when SHAKEN / BREAKING. Minigames make themselves harder by this.")]
    public float taskDifficultyShaken = 1.25f;
    public float taskDifficultyBreaking = 1.5f;

    [Header("Sanity bar (screen, from bottom-left, 1920x1080 units)")]
    public Vector2 barPosition = new Vector2(24f, 130f);
    public Vector2 barSize = new Vector2(280f, 14f);

    // ---- Runtime ------------------------------------------------------------

    public static int Changes { get; private set; }

    private static SanitySettings current;
    private static bool warned;

    /// <summary>The asset in Resources, or these defaults if it does not exist yet.</summary>
    public static SanitySettings Current
    {
        get
        {
            if (current != null) return current;
            current = Resources.Load<SanitySettings>(ResourcePath);
            if (current == null)
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning("[SanitySettings] No settings file yet: using the default numbers. " +
                                     "Create it with Tools > Pre-Thesis > Designer Settings > Sanity.");
                }
                current = CreateInstance<SanitySettings>();
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
        warned = false;
        Changes++;
    }

    /// <summary>Editor: the settings file was just created, use it from now on.</summary>
    public static void Use(SanitySettings asset)
    {
        if (asset == null) return;
        current = asset;
        Changes++;
    }

    private void OnValidate()
    {
        breakingBelow = Mathf.Min(breakingBelow, shakenBelow);
        snackMaxCarry = Mathf.Max(1, snackMaxCarry);
        Changes++;
    }
}
