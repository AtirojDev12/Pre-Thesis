using UnityEngine;

/// <summary>
/// Feeds one output of a <see cref="VoiceStream"/> into Unity audio, sample by
/// sample, on the audio thread.
///
/// Why not a streamed AudioClip (the first version): Unity pulls streamed clips
/// in big, irregular chunks (thousands of samples at once), which kept emptying
/// the small voice buffer -> choppy, crackly voice. OnAudioFilterRead is called
/// every DSP block (~20 ms) with exactly what the speakers need next.
///
/// The AudioSource plays a looping clip of constant 1.0 and this filter
/// multiplies it by the voice, so Unity's 3D volume/panning and the radio
/// filters (added AFTER this component) still work normally.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public sealed class VoiceFilter : MonoBehaviour
{
    private VoiceStream stream;
    private VoiceStream.Output output;
    private int outputRate;

    // Linear resampler (voice rate -> speaker rate). Buffers sized once, never reallocated on the audio thread.
    private float[] input = new float[16384];
    private float previous;
    private double phase;

    public void Init(VoiceStream voice, VoiceStream.Output which)
    {
        stream = voice;
        output = which;
        outputRate = AudioSettings.outputSampleRate;
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        VoiceStream voice = stream;
        int inRate = voice != null ? voice.SampleRate : 0;
        if (voice == null || inRate <= 0 || outputRate <= 0)
        {
            System.Array.Clear(data, 0, data.Length);
            return;
        }

        int frames = data.Length / channels;
        double step = (double)inRate / outputRate;

        // How many new voice samples cover these output frames.
        int needed = (int)System.Math.Floor(phase + frames * step);
        if (needed + 1 > input.Length) needed = input.Length - 1;

        // input[0] = last sample of the previous block, input[1..needed] = new samples.
        input[0] = previous;
        voice.Read(output, input, 1, needed);

        for (int f = 0; f < frames; f++)
        {
            double p = phase + f * step;
            int i = (int)p;
            float t = (float)(p - i);
            int j = i + 1 > needed ? needed : i + 1;
            float sample = input[i] + (input[j] - input[i]) * t;

            int offset = f * channels;
            for (int c = 0; c < channels; c++) data[offset + c] *= sample;
        }

        phase = phase + frames * step - needed;
        previous = input[needed];
    }
}
