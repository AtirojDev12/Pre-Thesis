/// <summary>
/// Tiny voice codec for the game-network voice (VoiceNetwork).
///
///   48 kHz mono (from VoiceMicCapture) -> 16 kHz (clear speech, like a good
///   phone call) -> IMA ADPCM, 4 bits per sample.
///
/// 16 kHz x 4 bit = 64 kbit/s while talking, 0 while silent (VoiceNetwork only
/// sends when you speak). Raw 48 kHz 16-bit would be 768 kbit/s: 12x more.
///
/// Every packet carries its own decoder start state (4-byte header), so a lost
/// packet (unreliable channel) only costs that 40 ms; nothing after it breaks.
///
/// Pure C#, no allocations after construction. Not thread-safe: one encoder
/// per sender, one decoder per talker, used on the main thread.
/// </summary>
public static class VoiceCodec
{
    public const int NetworkRate = 16000;
    public const int HeaderBytes = 4;

    /// <summary>Bytes needed for <paramref name="samples"/> samples (2 samples per byte + header).</summary>
    public static int EncodedSize(int samples) => HeaderBytes + (samples + 1) / 2;

    /// <summary>Samples contained in an encoded packet of <paramref name="bytes"/> bytes.</summary>
    public static int DecodedSamples(int bytes) => bytes <= HeaderBytes ? 0 : (bytes - HeaderBytes) * 2;

    private static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };

    private static readonly int[] StepTable =
    {
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45,
        50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143, 157, 173, 190, 209, 230,
        253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963,
        1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327,
        3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442,
        11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
    };

    /// <summary>
    /// 48 kHz -> 16 kHz: average of every 3 samples (a simple low-pass that stops
    /// harsh aliasing). Returns how many samples were written to <paramref name="output"/>.
    /// <paramref name="input"/> length must be a multiple of 3.
    /// </summary>
    public static int Downsample48To16(short[] input, int count, short[] output, int outputOffset)
    {
        int written = 0;
        for (int i = 0; i + 2 < count; i += 3)
        {
            output[outputOffset + written++] = (short)((input[i] + input[i + 1] + input[i + 2]) / 3);
        }
        return written;
    }

    /// <summary>Encodes <paramref name="count"/> samples. Returns bytes written (= EncodedSize(count)).</summary>
    public static int Encode(short[] samples, int count, byte[] output)
    {
        // Header: the state the decoder starts from. We start every packet with
        // predictor = first sample, so the packet stands alone.
        int predictor = count > 0 ? samples[0] : 0;
        int index = BestStartIndex(samples, count);

        output[0] = (byte)(predictor & 0xFF);
        output[1] = (byte)((predictor >> 8) & 0xFF);
        output[2] = (byte)index;
        output[3] = 0;

        int outPos = HeaderBytes;
        byte current = 0;
        for (int i = 0; i < count; i++)
        {
            int step = StepTable[index];
            int diff = samples[i] - predictor;
            int nibble = 0;
            if (diff < 0) { nibble = 8; diff = -diff; }

            int delta = step >> 3;
            if (diff >= step) { nibble |= 4; diff -= step; delta += step; }
            step >>= 1;
            if (diff >= step) { nibble |= 2; diff -= step; delta += step; }
            step >>= 1;
            if (diff >= step) { nibble |= 1; delta += step; }

            predictor += (nibble & 8) != 0 ? -delta : delta;
            if (predictor > short.MaxValue) predictor = short.MaxValue;
            else if (predictor < short.MinValue) predictor = short.MinValue;

            index += IndexTable[nibble];
            if (index < 0) index = 0; else if (index > 88) index = 88;

            if ((i & 1) == 0) current = (byte)nibble;
            else output[outPos++] = (byte)(current | (nibble << 4));
        }
        if ((count & 1) == 1) output[outPos++] = current;
        return outPos;
    }

    /// <summary>Decodes a packet. Returns samples written to <paramref name="output"/>.</summary>
    public static int Decode(byte[] data, int offset, int length, short[] output)
    {
        if (length <= HeaderBytes) return 0;

        int predictor = (short)(data[offset] | (data[offset + 1] << 8));
        int index = data[offset + 2];
        if (index > 88) index = 88;

        int samples = DecodedSamples(length);
        if (samples > output.Length) samples = output.Length & ~1;

        int outPos = 0;
        for (int b = offset + HeaderBytes; b < offset + length && outPos < samples; b++)
        {
            for (int half = 0; half < 2 && outPos < samples; half++)
            {
                int nibble = half == 0 ? data[b] & 0x0F : data[b] >> 4;
                int step = StepTable[index];

                int delta = step >> 3;
                if ((nibble & 4) != 0) delta += step;
                if ((nibble & 2) != 0) delta += step >> 1;
                if ((nibble & 1) != 0) delta += step >> 2;

                predictor += (nibble & 8) != 0 ? -delta : delta;
                if (predictor > short.MaxValue) predictor = short.MaxValue;
                else if (predictor < short.MinValue) predictor = short.MinValue;

                index += IndexTable[nibble];
                if (index < 0) index = 0; else if (index > 88) index = 88;

                output[outPos++] = (short)predictor;
            }
        }
        return outPos;
    }

    /// <summary>
    /// A good starting step for this packet, from its first few samples, so a
    /// loud packet does not start with a tiny step (smeared first milliseconds).
    /// </summary>
    private static int BestStartIndex(short[] samples, int count)
    {
        int n = count < 8 ? count : 8;
        int maxDiff = 0;
        for (int i = 1; i < n; i++)
        {
            int d = samples[i] - samples[i - 1];
            if (d < 0) d = -d;
            if (d > maxDiff) maxDiff = d;
        }
        int index = 0;
        while (index < 88 && StepTable[index] < maxDiff) index++;
        return index;
    }
}
