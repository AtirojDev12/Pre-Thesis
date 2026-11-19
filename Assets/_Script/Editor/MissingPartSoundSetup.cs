using UnityEditor;
using UnityEngine;

/// <summary>
/// 19 Nov 2026: makes Resources/Tuning/MissingPartSound.asset the first time the project
/// opens (standby knock = GhostFindBodySound.wav, voices = HelpME / I am here / OverHere,
/// finding sounds = the 4 clips in FindingSound), so nobody has to run a tool.
///
///   Tools > Pre-Thesis > Designer Settings > Missing Part Sound   selects it to change the sounds.
/// </summary>
[InitializeOnLoad]
public static class MissingPartSoundSetup
{
    private const string Folder = "Assets/Sound/SFX/Ghost/MIssingPart/";
    private const string DefaultStandbyClip = Folder + "GhostFindBodySound.wav";
    private const string FindingFolder = "Assets/Sound/SFX/Ghost/MIssingPart/FindingSound/";
    private const int DefaultsVersion = 5; // 2 = finding, 3 = HelpME / I am here / OverHere, 4 = attack / success, 5 = hold-E work sound (19 Nov)

    static MissingPartSoundSetup() => EditorApplication.delayCall += () => Ensure(false);

    [MenuItem("Tools/Pre-Thesis/Designer Settings/Missing Part Sound", false, 23)]
    private static void Open() => Ensure(true);

    private static void Ensure(bool select)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        MissingPartSound asset = AssetDatabase.LoadAssetAtPath<MissingPartSound>(MissingPartSound.AssetPath);
        if (asset == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Tuning")) AssetDatabase.CreateFolder("Assets/Resources", "Tuning");
            asset = ScriptableObject.CreateInstance<MissingPartSound>();
            asset.standbyKnockClip = AssetDatabase.LoadAssetAtPath<AudioClip>(DefaultStandbyClip);
            FillFindingClips(asset);
            FillVoiceClips(asset);
            FillEventClips(asset);
            asset.defaultsVersion = DefaultsVersion;
            AssetDatabase.CreateAsset(asset, MissingPartSound.AssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[MissingPartSound] Created " + MissingPartSound.AssetPath + " (standby sound: GhostFindBodySound). Commit it.", asset);
        }
        else if (asset.defaultsVersion < DefaultsVersion)
        {
            // An older asset: give it the new default clips once (a clip cleared later stays cleared).
            if (asset.defaultsVersion < 2) FillFindingClips(asset);
            if (asset.defaultsVersion < 3) FillVoiceClips(asset);
            FillEventClips(asset);
            asset.defaultsVersion = DefaultsVersion;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log("[MissingPartSound] Added the new default sounds (finding / voices / attack / success / hold-E work) to " + MissingPartSound.AssetPath + ". Commit it.", asset);
        }
        if (!select) return;
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    private static void FillFindingClips(MissingPartSound asset)
    {
        Fill(asset.findingNearest, "FindingsoundNearest.wav");
        Fill(asset.findingNear, "FindingsoundNear.wav");
        Fill(asset.findingFar, "FindingsoundFar.wav");
        Fill(asset.findingFarAway, "FindingsoundFarAway.wav");
    }

    private static void FillEventClips(MissingPartSound asset)
    {
        if (asset.attackClip == null) asset.attackClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "MissingPartAttack.wav");
        if (asset.partReturnedClip == null) asset.partReturnedClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "MissingPartSuccess.wav");
        if (asset.workClip == null) asset.workClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "MIssingPartWorkonProcess.wav");
    }

    private static void FillVoiceClips(MissingPartSound asset)
    {
        if (asset.helpMeClip == null) asset.helpMeClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "HelpME.wav");
        if (asset.partVoiceClips == null || asset.partVoiceClips.Length == 0)
            asset.partVoiceClips = new[]
            {
                AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "I am here.wav"),
                AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "OverHere.wav"),
            };
    }

    private static void Fill(MissingPartSound.FindingLevel level, string file)
    {
        if (level != null && level.clip == null) level.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(FindingFolder + file);
    }
}
