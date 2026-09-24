using UnityEngine;

/// <summary>
/// Puts an AudioSource into a sound category (Music / SFX / Ambient) so the
/// matching Settings slider controls it.
///
///   final volume = the volume set on the AudioSource  x  category slider
///   (Master is applied on top by AudioListener.volume, for every sound.)
///
/// HOW TO USE
///   Add this next to an AudioSource and pick the category. Or select objects
///   and use Tools > Pre-Thesis > Audio > Tag Selected As...
///   An AudioSource WITHOUT this component only follows Master.
///
/// IF A SCRIPT CHANGES THE VOLUME AT RUNTIME (fades, distance tricks...)
///   Call SetBaseVolume(v) here instead of setting AudioSource.volume, or the
///   slider and the script will fight over the value.
///
/// Why not an AudioMixer? A mixer needs its groups and exposed parameters set
/// up by hand in the Editor, and every source routed to it. This works with no
/// setup and uses the same GameSettings values, so moving to a mixer later only
/// changes this one file.
/// </summary>
[RequireComponent(typeof(AudioSource))]
[DisallowMultipleComponent]
public class SoundCategoryVolume : MonoBehaviour
{
    [SerializeField] private SoundCategory category = SoundCategory.Sfx;

    private AudioSource source;
    private float baseVolume = 1f;
    private bool captured;

    public SoundCategory Category
    {
        get => category;
        set { category = value; Apply(); }
    }

    private void Awake()
    {
        source = GetComponent<AudioSource>();
        CaptureBase();
    }

    private void OnEnable()
    {
        GameSettings.Changed += Apply;
        Apply();
    }

    private void OnDisable()
    {
        GameSettings.Changed -= Apply;
    }

    /// <summary>Use instead of AudioSource.volume when a script needs to change loudness.</summary>
    public void SetBaseVolume(float volume)
    {
        baseVolume = Mathf.Clamp01(volume);
        captured = true;
        Apply();
    }

    private void CaptureBase()
    {
        if (captured || source == null) return;
        baseVolume = source.volume;
        captured = true;
    }

    private void Apply()
    {
        if (source == null) return;
        CaptureBase();
        source.volume = baseVolume * GameSettings.GetVolume(category);
    }
}
