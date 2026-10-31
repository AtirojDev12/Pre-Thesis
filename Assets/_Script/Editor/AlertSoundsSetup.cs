using UnityEditor;
using UnityEngine;

/// <summary>
/// 31 Oct 2026 (Mr.k): makes Resources/Tuning/AlertSounds.asset the first time the project
/// opens (zone sound = AlarmSiren.wav), so nobody has to run a tool.
///
///   Tools > Pre-Thesis > Designer Settings > Alert Sounds   selects it to change the sounds.
/// </summary>
[InitializeOnLoad]
public static class AlertSoundsSetup
{
    private const string DefaultZoneClip = "Assets/Sound/SFX/AlarmExit/AlarmSiren.wav";

    static AlertSoundsSetup() => EditorApplication.delayCall += () => Ensure(false);

    [MenuItem("Tools/Pre-Thesis/Designer Settings/Alert Sounds", false, 22)]
    private static void Open() => Ensure(true);

    private static void Ensure(bool select)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        AlertSounds asset = AssetDatabase.LoadAssetAtPath<AlertSounds>(AlertSounds.AssetPath);
        if (asset == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Tuning")) AssetDatabase.CreateFolder("Assets/Resources", "Tuning");
            asset = ScriptableObject.CreateInstance<AlertSounds>();
            asset.zoneClip = AssetDatabase.LoadAssetAtPath<AudioClip>(DefaultZoneClip);
            AssetDatabase.CreateAsset(asset, AlertSounds.AssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[AlertSounds] Created " + AlertSounds.AssetPath + " (zone sound: AlarmSiren). Commit it.", asset);
        }
        if (!select) return;
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }
}
