using System;
using UnityEngine;

/// <summary>
/// 5 Oct (Mr.k). ONE place for the game designer to balance the flashlights.
///
/// HOW TO USE
///   1. Tools > Pre-Thesis > Designer Settings > Flashlight   (creates the file the first time and selects it)
///   2. Change the numbers / sliders in the Inspector.
///   3. Works LIVE in Play mode: the beam, battery and shop update at once.
///   4. Changes made in Play mode STAY (it is an asset). Commit the file:
///         Assets/Resources/Tuning/FlashlightTuning.asset
///
/// No asset (not created yet)? The game uses the default numbers written below.
///
/// ONLINE: every player must have the SAME values (same build / same commit).
/// The host decides battery, charging, prices and carry limits; each player draws
/// the beam with their own copy.
/// </summary>
[CreateAssetMenu(fileName = "FlashlightTuning", menuName = "Pre-Thesis/Flashlight Tuning")]
public sealed class FlashlightTuning : ScriptableObject
{
    public const string ResourcePath = "Tuning/FlashlightTuning";
    public const string AssetPath = "Assets/Resources/Tuning/FlashlightTuning.asset";

    [Serializable]
    public sealed class Torch
    {
        [Tooltip("Name in the shop and hotbar.")]
        public string displayName;
        [TextArea(2, 4), Tooltip("Text in the shop.")]
        public string description;
        [Min(0), Tooltip("Shop price. 0 = FREE (Claim button).")]
        public int price;

        [Header("Beam")]
        [Range(1f, 60f), Tooltip("How far the light reaches (metres). Keep the Darkness fog distance LONGER than this.")]
        public float range;
        [Range(0f, 30f), Tooltip("How bright the light is.")]
        public float brightness;
        [Range(5f, 120f), Tooltip("How wide the cone is (degrees).")]
        public float coneAngle;

        [Header("Battery")]
        [Range(5f, 1200f), Tooltip("Seconds of light from a FULL battery.")]
        public float batterySeconds;
        [Range(0f, 30f), Tooltip("Seconds of light added by ONE Space press. 0 = Space does nothing.")]
        public float secondsPerSpacePress;
        [Tooltip("ON = press R to put in a Battery item.")]
        public bool usesBatteries;
    }

    [Serializable]
    public sealed class BatteryItem
    {
        public string displayName = "Battery";
        [TextArea(2, 4)]
        public string description = "Flashlight in hand + R = full battery. Stack in one slot. Unused ones are kept if you survive.";
        [Min(0), Tooltip("Shop price of ONE battery.")]
        public int price = 25;
        [Range(1, 10), Tooltip("Most batteries a player can own. They all stack in ONE slot.")]
        public int maxCarry = 3;
        [Range(0.05f, 1f), Tooltip("How much ONE battery fills. 1 = full, 0.5 = half.")]
        public float refill = 1f;
    }

    [Serializable]
    public sealed class Look
    {
        [Tooltip("Colour of every flashlight beam.")]
        public Color beamColor = new Color(1f, 0.95f, 0.82f);
        [Range(0f, 1f), Tooltip("Size of the bright centre of the cone. 0 = soft edge, 1 = hard edge.")]
        public float brightCentre = 0.55f;
        [Range(0f, 0.5f), Tooltip("The beam starts to flicker below this battery. 0.15 = 15%. 0 = never flickers.")]
        public float flickerBelow = 0.15f;
        [Range(0f, 1f), Tooltip("How dim the beam gets just before the battery is empty. 0.55 = 55% bright.")]
        public float dimWhenEmpty = 0.55f;
        [Tooltip("YOUR own beam casts shadows (looks better, costs a little). Other players' beams never do.")]
        public bool ownBeamShadows = true;
    }

    [Serializable]
    public sealed class Controls
    {
        [Range(1f, 30f), Tooltip("Fastest Space presses that count (per second). Stops auto-clickers.")]
        public float maxSpacePressesPerSecond = 12f;
        [Range(0f, 3f), Tooltip("Seconds to wait between two R presses.")]
        public float reloadCooldown = 0.5f;
    }

    [Header("Basic Flashlight (free)")]
    public Torch basic = new Torch
    {
        displayName = "Basic Flashlight",
        description = "Free. Weak beam, short battery. Mash SPACE to charge it. Lost if you die (claim a new one).",
        price = 0,
        range = 9f, brightness = 3f, coneAngle = 40f,
        batterySeconds = 45f, secondsPerSpacePress = 1.5f, usesBatteries = false,
    };

    [Header("Flashlight (paid)")]
    public Torch paid = new Torch
    {
        displayName = "Flashlight",
        description = "Bright, long beam, big battery. Cannot be charged by hand: press R to put in a new Battery. Lost if you die.",
        price = 150,
        range = 20f, brightness = 8f, coneAngle = 50f,
        batterySeconds = 240f, secondsPerSpacePress = 0f, usesBatteries = true,
    };

    [Header("Battery item")]
    public BatteryItem battery = new BatteryItem();

    [Header("Look (all flashlights)")]
    public Look look = new Look();

    [Header("Controls")]
    public Controls controls = new Controls();

    // ---- Runtime ----------------------------------------------------------------

    /// <summary>Goes up on every Inspector edit, so ItemCatalog re-reads the values.</summary>
    public static int Changes { get; private set; }

    private static FlashlightTuning current;
    private static bool warned;

    /// <summary>The asset in Resources, or the defaults above if it does not exist yet.</summary>
    public static FlashlightTuning Current
    {
        get
        {
            if (current != null) return current;
            current = Resources.Load<FlashlightTuning>(ResourcePath);
            if (current == null)
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning("[FlashlightTuning] No settings file yet: using the default numbers. " +
                                     "Create it with Tools > Pre-Thesis > Designer Settings > Flashlight.");
                }
                current = CreateInstance<FlashlightTuning>();
                current.hideFlags = HideFlags.HideAndDontSave;
            }
            return current;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Works with "Enter Play Mode Options" (no domain reload) too.
        if (current != null && (current.hideFlags & HideFlags.DontSave) != 0) DestroyImmediate(current);
        current = null;
        warned = false;
        Changes++;
    }

    /// <summary>Editor only: the settings file was just created, use it from now on.</summary>
    public static void Use(FlashlightTuning asset)
    {
        if (asset == null) return;
        current = asset;
        Changes++;
    }

    private void OnValidate()
    {
        // Values that would break the maths.
        basic.batterySeconds = Mathf.Max(1f, basic.batterySeconds);
        paid.batterySeconds = Mathf.Max(1f, paid.batterySeconds);
        battery.maxCarry = Mathf.Max(1, battery.maxCarry);
        Changes++;
    }

    [ContextMenu("Reset to the game's default numbers")]
    private void ResetToDefaults()
    {
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "Reset Flashlight Tuning");
#endif
        FlashlightTuning fresh = CreateInstance<FlashlightTuning>();
        basic = fresh.basic;
        paid = fresh.paid;
        battery = fresh.battery;
        look = fresh.look;
        controls = fresh.controls;
        DestroyImmediate(fresh);
        Changes++;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}
