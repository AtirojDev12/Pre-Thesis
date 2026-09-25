using System.Diagnostics;

/// <summary>
/// One remote player's voice, as it arrives from EOS.
///
/// EOS pushes 10 ms blocks of 16-bit PCM (possibly from its own audio thread);
/// Unity's AudioClip reader pulls float samples (from the audio thread). Two
/// small jitter buffers sit in between, one per output:
///   - Proximity: the 3D voice from the player's body. Always fed.
///   - Radio: the Walkie-Talkie copy. Fed only while <see cref="RadioEnabled"/>.
///
/// Plain C# (no Unity API), so it is safe from any thread. Every access to the
/// buffers goes through one lock; the blocks are tiny, so contention is nil.
/// </summary>
public sealed class VoiceStream
{
    /// <summary>Mixed audio (EOS did not split voices). Played 2D, never on the radio.</summary>
    public const string MixedId = "__mixed__";

    public readonly string ParticipantId;
    public bool IsMixed => ParticipantId == MixedId;

    private volatile int sampleRate;
    /// <summary>0 until the first block arrives.</summary>
    public int SampleRate => sampleRate;

    /// <summary>Set by the main thread: route this voice to the radio too.</summary>
    public volatile bool RadioEnabled;

    private readonly object gate = new object();
    private Ring proximity;
    private Ring radio;

    private static readonly Stopwatch clock = Stopwatch.StartNew();
    private long lastWriteMs;
    public float SecondsSinceLastAudio => (clock.ElapsedMilliseconds - System.Threading.Interlocked.Read(ref lastWriteMs)) / 1000f;

    /// <summary>Loudness of the last block, 0..1 (peak). For a "talking" icon or, later, ghosts that hear you.</summary>
    public volatile float Level;

    private long blocks;
    /// <summary>How many audio blocks arrived (voice test overlay).</summary>
    public long Blocks => System.Threading.Interlocked.Read(ref blocks);

    public VoiceStream(string participantId)
    {
        ParticipantId = participantId;
        System.Threading.Interlocked.Exchange(ref lastWriteMs, clock.ElapsedMilliseconds);
    }

    public enum Output { Proximity, Radio }

    /// <summary>EOS side. Interleaved 16-bit frames; downmixed to mono here.</summary>
    public void Write(short[] frames, int rate, int channels)
    {
        if (frames == null || frames.Length == 0 || rate <= 0) return;
        if (channels < 1) channels = 1;

        lock (gate)
        {
            if (sampleRate != rate || proximity == null)
            {
                sampleRate = rate;
                proximity = new Ring(rate);
                radio = new Ring(rate);
            }

            bool toRadio = RadioEnabled;
            if (!toRadio) radio.Clear();

            float peak = 0f;
            int monoCount = frames.Length / channels;
            for (int i = 0; i < monoCount; i++)
            {
                int sum = 0;
                for (int c = 0; c < channels; c++) sum += frames[i * channels + c];
                float sample = sum / (32768f * channels);
                float abs = sample < 0f ? -sample : sample;
                if (abs > peak) peak = abs;

                proximity.Push(sample);
                if (toRadio) radio.Push(sample);
            }

            Level = peak;
        }

        System.Threading.Interlocked.Increment(ref blocks);

        System.Threading.Interlocked.Exchange(ref lastWriteMs, clock.ElapsedMilliseconds);
    }

    /// <summary>Unity audio side. Always fills the whole array (silence on underrun).</summary>
    public void Read(Output output, float[] data)
    {
        lock (gate)
        {
            Ring ring = output == Output.Radio ? radio : proximity;
            if (ring == null)
            {
                System.Array.Clear(data, 0, data.Length);
                return;
            }
            ring.Pop(data);
        }
    }

    /// <summary>Drop anything buffered (e.g. the voice was muted and is coming back).</summary>
    public void Clear()
    {
        lock (gate)
        {
            proximity?.Clear();
            radio?.Clear();
        }
    }

    /// <summary>Tiny jitter buffer: waits for ~60 ms before playing, caps latency at ~250 ms.</summary>
    private sealed class Ring
    {
        private readonly float[] buffer;
        private readonly int startThreshold;
        private readonly int maxLatency;
        private readonly int trimTo;
        private int read, write, count;
        private bool playing;

        public Ring(int rate)
        {
            buffer = new float[rate];              // 1 s
            startThreshold = rate * 60 / 1000;     // 60 ms
            maxLatency = rate / 4;                 // 250 ms
            trimTo = rate / 10;                    // 100 ms
        }

        public void Clear()
        {
            read = write = count = 0;
            playing = false;
        }

        public void Push(float sample)
        {
            if (count == buffer.Length)
            {
                read = (read + 1) % buffer.Length;
                count--;
            }
            buffer[write] = sample;
            write = (write + 1) % buffer.Length;
            count++;

            // Too far behind (a hitch): skip ahead so the voice stays live.
            if (count > maxLatency)
            {
                int drop = count - trimTo;
                read = (read + drop) % buffer.Length;
                count -= drop;
            }
        }

        public void Pop(float[] data)
        {
            if (!playing && count >= startThreshold) playing = true;

            for (int i = 0; i < data.Length; i++)
            {
                if (playing && count > 0)
                {
                    data[i] = buffer[read];
                    read = (read + 1) % buffer.Length;
                    count--;
                }
                else
                {
                    data[i] = 0f;
                    playing = false; // ran dry: re-buffer before playing again
                }
            }
        }
    }
}
