using UnityEngine;

/// <summary>
/// Plays one <see cref="VoiceStream"/> through Unity audio. Created and moved
/// around by VoiceChatManager; never placed in a scene by hand.
///
///   Proximity source: on this object. In a match it sits at the talker's
///   mouth and is fully 3D, so the voice comes from where they stand, gets
///   quieter with distance and is gone at <see cref="MaxHearingDistance"/>.
///   A louder voice simply stays audible further away, like real life.
///
///   Radio source: a 2D child with a band-pass + a little distortion, so it
///   sounds like a Walkie-Talkie. Only plays while the talker is transmitting
///   and you carry a Walkie-Talkie that is switched on.
/// </summary>
public sealed class VoicePlayback : MonoBehaviour
{
    public const float MaxHearingDistance = 25f;

    private VoiceStream stream;
    private AudioSource proximity;
    private AudioSource radio;
    private AudioClip squelch;
    private AudioSource clickSource; // separate: the voice filter would multiply the click away
    private bool radioOn;

    public VoiceStream Stream => stream;

    public static VoicePlayback Create(VoiceStream stream, Transform parent)
    {
        var go = new GameObject("Voice " + stream.ParticipantId);
        go.transform.SetParent(parent, false);
        var playback = go.AddComponent<VoicePlayback>();
        playback.Init(stream);
        return playback;
    }

    // One shared clip of constant 1.0: VoiceFilter multiplies it by the voice.
    private static AudioClip onesClip;

    private static AudioClip OnesClip()
    {
        if (onesClip != null) return onesClip;
        int rate = AudioSettings.outputSampleRate;
        var ones = new float[rate];
        for (int i = 0; i < ones.Length; i++) ones[i] = 1f;
        onesClip = AudioClip.Create("Voice Carrier (1.0)", rate, 1, rate, false);
        onesClip.SetData(ones, 0);
        return onesClip;
    }

    private void Init(VoiceStream voice)
    {
        stream = voice;
        int rate = AudioSettings.outputSampleRate;

        // ---- Proximity (3D) ----
        proximity = gameObject.AddComponent<AudioSource>();
        proximity.clip = OnesClip();
        gameObject.AddComponent<VoiceFilter>().Init(stream, VoiceStream.Output.Proximity);
        proximity.loop = true;
        proximity.playOnAwake = false;
        proximity.dopplerLevel = 0f;
        proximity.spread = 0f;
        proximity.minDistance = 1f;
        proximity.maxDistance = MaxHearingDistance;
        proximity.rolloffMode = AudioRolloffMode.Custom;
        proximity.SetCustomCurve(AudioSourceCurveType.CustomRolloff, RolloffCurve());
        proximity.Play();

        // ---- Radio (2D, filtered) ----
        var radioGo = new GameObject("Radio");
        radioGo.transform.SetParent(transform, false);
        radio = radioGo.AddComponent<AudioSource>();
        radio.clip = OnesClip();
        // Order matters: the voice filter must come BEFORE the radio filters.
        radioGo.AddComponent<VoiceFilter>().Init(stream, VoiceStream.Output.Radio);
        radio.loop = true;
        radio.playOnAwake = false;
        radio.spatialBlend = 0f;
        radio.volume = 0.9f;
        radioGo.AddComponent<AudioHighPassFilter>().cutoffFrequency = 450f;
        radioGo.AddComponent<AudioLowPassFilter>().cutoffFrequency = 3200f;
        radioGo.AddComponent<AudioDistortionFilter>().distortionLevel = 0.35f;
        radio.Play();

        squelch = BuildSquelch(rate);
        var clickGo = new GameObject("Radio Click");
        clickGo.transform.SetParent(transform, false);
        clickSource = clickGo.AddComponent<AudioSource>();
        clickSource.playOnAwake = false;
        clickSource.spatialBlend = 0f;
        SetSpatial(true);
    }

    /// <summary>3D from the body (match) or flat 2D (waiting lobby: nobody has a body).</summary>
    public void SetSpatial(bool spatial)
    {
        if (proximity != null) proximity.spatialBlend = spatial ? 1f : 0f;
    }

    /// <summary>Route this voice to the Walkie-Talkie too. Plays a click when it opens / closes.</summary>
    public void SetRadio(bool on)
    {
        if (on == radioOn) return;
        radioOn = on;
        stream.RadioEnabled = on;
        if (clickSource != null && squelch != null) clickSource.PlayOneShot(squelch, 0.6f);
    }

    /// <summary>Silence the body voice without destroying anything (e.g. talker not found yet).</summary>
    public void SetProximityMuted(bool muted)
    {
        if (proximity == null || proximity.mute == muted) return;
        proximity.mute = muted;
        if (!muted) stream.Clear(); // do not play a backlog of old words
    }

    // Loud up close, about half at 5 m, faint at 15 m, silent at 25 m.
    private static AnimationCurve RolloffCurve() => new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(0.08f, 1f),   // 2 m
        new Keyframe(0.2f, 0.5f),  // 5 m
        new Keyframe(0.4f, 0.2f),  // 10 m
        new Keyframe(0.6f, 0.08f), // 15 m
        new Keyframe(1f, 0f));     // 25 m

    /// <summary>Short burst of noise: the radio "kshh" when a transmission starts / ends.</summary>
    private static AudioClip BuildSquelch(int rate)
    {
        int length = rate * 12 / 100; // 120 ms
        var samples = new float[length];
        var random = new System.Random(1234);
        for (int i = 0; i < length; i++)
        {
            float fade = 1f - (float)i / length;
            samples[i] = ((float)random.NextDouble() * 2f - 1f) * 0.5f * fade;
        }
        AudioClip clip = AudioClip.Create("Radio Squelch", length, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void OnDestroy()
    {
        if (stream != null) stream.RadioEnabled = false;
        if (squelch != null) Destroy(squelch);
    }
}
