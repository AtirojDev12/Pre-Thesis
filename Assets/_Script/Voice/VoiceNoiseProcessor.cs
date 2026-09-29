using UnityEngine;

/// <summary>
/// Microphone clean-up for cheap mics / mics with their own noise cancelling
/// switched off. Works on the 10 ms blocks of <see cref="VoiceMicCapture"/>,
/// in place, no allocations after construction. Main thread.
///
/// 1) NOISE REDUCTION (Settings > Sound > Noise reduction)
///    Removes STEADY noise: hiss, fan, PC hum, air-con.
///    - High-pass at 100 Hz (desk thumps, rumble).
///    - Spectral subtraction: every block is split into 513 frequency bands
///      (FFT 1024, 50 % overlap). Each band learns its own noise level (the
///      quietest it has been lately, slowly rising). Bands that are not much
///      louder than their noise are turned down (to at most -18 dB, so voices
///      never sound "underwater").
///    Latency: 10 ms.
///
/// 2) NOISE GATE (Settings > Sound > Noise gate + level)
///    The mic stays SILENT until the sound is above the gate level for 30 ms
///    in a row. A key click is shorter than that, so it never opens the gate.
///    Speech is longer, so it does. The audio is delayed 30 ms ("look-ahead"),
///    so the start of the first word is not cut off. After you stop talking
///    the gate stays open 250 ms (word endings) and then fades out.
///    Latency: 30 ms.
///
/// Both off = the audio passes untouched, no extra latency.
/// </summary>
public sealed class VoiceNoiseProcessor
{
    // ---- Noise reduction tuning ----------------------------------------------
    private const int FftSize = 1024;
    private const int Bins = FftSize / 2 + 1;
    private const float HighPassHz = 100f;
    private const float PowerSmoothing = 0.9f;   // per-band power smoothing between blocks
    private const float NoiseRisePerBlock = 1.006f; // noise estimate may rise ~2.6 dB/s (fan switched on...)
    private const float OverSubtraction = 4.0f;  // how hard noise is removed
    private const float GainFloor = 0.125f;      // -18 dB: never remove more than this
    private const float GainRelease = 0.6f;      // smooth band gains going down (no "musical" chirps)

    // ---- Noise gate tuning -----------------------------------------------------
    private const int OpenBlocks = 3;            // 30 ms above the level to open (key clicks are shorter)
    private const int LookAheadBlocks = 3;       // audio delay so word starts are kept
    private const int HoldBlocks = 25;           // 250 ms open after the last loud block
    private const float CloseHysteresisDb = 6f;  // stays open down to level - 6 dB
    private const int ReleaseBlocks = 12;        // 120 ms fade-out when closing
    private const int AttackBlocks = 1;          // 10 ms fade-in when opening

    private readonly int blockSize;
    private readonly int sampleRate;

    // Settings (changing one resets that part so there is no click / stale state).
    private bool noiseReduction = true;
    private bool noiseGate = true;
    public float GateThresholdDb { get; set; } = GameSettings.DefaultNoiseGateDb;

    public bool NoiseReduction
    {
        get => noiseReduction;
        set { if (value != noiseReduction) { noiseReduction = value; ResetReduction(); } }
    }

    public bool NoiseGate
    {
        get => noiseGate;
        set { if (value != noiseGate) { noiseGate = value; ResetGate(); } }
    }

    /// <summary>False while the gate holds the mic silent. Always true with the gate off.</summary>
    public bool GateOpen => !noiseGate || gateOpen || gateGain > 0f;

    /// <summary>Level of the last block after noise reduction, before the gate (dBFS, RMS).</summary>
    public float LastInputDb { get; private set; } = -120f;

    // ---- Noise reduction state ---------------------------------------------------
    private readonly float[] window;      // sqrt-Hann, 2 blocks long (analysis AND synthesis)
    private readonly float[] previous;    // last input block
    private readonly float[] overlap;     // second half of the last synthesised frame
    private readonly float[] re, im;      // FFT work buffers
    private readonly float[] smoothPower, noisePower, bandGain;
    private readonly float[] cosTable, sinTable;
    private readonly int[] bitReverse;
    private bool noiseKnown;

    // High-pass biquad (Butterworth) state.
    private float hb0, hb1, hb2, ha1, ha2;
    private float hx1, hx2, hy1, hy2;

    // ---- Gate state ------------------------------------------------------------------
    private readonly float[][] delay;     // ring of delayed blocks (look-ahead)
    private int delayIndex;
    private int aboveCount;
    private int holdLeft;
    private bool gateOpen;
    private float gateGain;               // 0 = closed, 1 = open (ramped per sample)

    public VoiceNoiseProcessor(int blockSamples, int rate)
    {
        blockSize = blockSamples;
        sampleRate = rate;
        int frame = blockSize * 2;
        if (frame > FftSize) throw new System.ArgumentException("Block too large for the FFT size");

        // Periodic sqrt-Hann: squared windows with 50 % overlap add up to exactly 1.
        window = new float[frame];
        for (int i = 0; i < frame; i++)
            window[i] = Mathf.Sqrt(0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / frame));

        previous = new float[blockSize];
        overlap = new float[blockSize];
        re = new float[FftSize];
        im = new float[FftSize];
        smoothPower = new float[Bins];
        noisePower = new float[Bins];
        bandGain = new float[Bins];

        cosTable = new float[FftSize / 2];
        sinTable = new float[FftSize / 2];
        for (int i = 0; i < FftSize / 2; i++)
        {
            double a = -2.0 * System.Math.PI * i / FftSize;
            cosTable[i] = (float)System.Math.Cos(a);
            sinTable[i] = (float)System.Math.Sin(a);
        }
        bitReverse = new int[FftSize];
        int bits = 0;
        while ((1 << bits) < FftSize) bits++;
        for (int i = 0; i < FftSize; i++)
        {
            int r = 0;
            for (int b = 0; b < bits; b++) if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
            bitReverse[i] = r;
        }

        SetupHighPass();

        delay = new float[LookAheadBlocks + 1][];
        for (int i = 0; i < delay.Length; i++) delay[i] = new float[blockSize];

        Reset();
    }

    public void Reset()
    {
        ResetReduction();
        ResetGate();
    }

    private void ResetReduction()
    {
        System.Array.Clear(previous, 0, previous.Length);
        System.Array.Clear(overlap, 0, overlap.Length);
        for (int k = 0; k < Bins; k++) bandGain[k] = 1f;
        noiseKnown = false;
        hx1 = hx2 = hy1 = hy2 = 0f;
    }

    private void ResetGate()
    {
        for (int i = 0; i < delay.Length; i++) System.Array.Clear(delay[i], 0, blockSize);
        delayIndex = 0;
        aboveCount = 0;
        holdLeft = 0;
        gateOpen = false;
        gateGain = 0f;
    }

    /// <summary>Cleans one block in place.</summary>
    public void Process(float[] samples)
    {
        if (noiseReduction) Reduce(samples);

        float energy = 0f;
        for (int i = 0; i < blockSize; i++) energy += samples[i] * samples[i];
        LastInputDb = 10f * Mathf.Log10(energy / blockSize + 1e-12f);

        if (noiseGate) Gate(samples);
    }

    // =====================================================================
    //  Noise reduction
    // =====================================================================

    private void SetupHighPass()
    {
        // RBJ cookbook high-pass, Q = 0.707 (Butterworth).
        float w0 = 2f * Mathf.PI * HighPassHz / sampleRate;
        float alpha = Mathf.Sin(w0) / (2f * 0.7071f);
        float cos = Mathf.Cos(w0);
        float a0 = 1f + alpha;
        hb0 = (1f + cos) / 2f / a0;
        hb1 = -(1f + cos) / a0;
        hb2 = (1f + cos) / 2f / a0;
        ha1 = -2f * cos / a0;
        ha2 = (1f - alpha) / a0;
    }

    private void Reduce(float[] samples)
    {
        // High-pass in place.
        for (int i = 0; i < blockSize; i++)
        {
            float x = samples[i];
            float y = hb0 * x + hb1 * hx1 + hb2 * hx2 - ha1 * hy1 - ha2 * hy2;
            hx2 = hx1; hx1 = x;
            hy2 = hy1; hy1 = y;
            samples[i] = y;
        }

        // Frame = previous block + this block, windowed, zero padded to the FFT size.
        for (int i = 0; i < blockSize; i++)
        {
            re[i] = previous[i] * window[i];
            re[blockSize + i] = samples[i] * window[blockSize + i];
        }
        for (int i = blockSize * 2; i < FftSize; i++) re[i] = 0f;
        System.Array.Clear(im, 0, FftSize);
        System.Array.Copy(samples, previous, blockSize);

        Fft(false);

        // Per band: learn the noise, then turn the band down if it is mostly noise.
        for (int k = 0; k < Bins; k++)
        {
            float power = re[k] * re[k] + im[k] * im[k];
            float smooth = noiseKnown ? PowerSmoothing * smoothPower[k] + (1f - PowerSmoothing) * power : power;
            smoothPower[k] = smooth;

            float noise = noiseKnown ? noisePower[k] : smooth;
            noise = smooth < noise ? smooth : noise * NoiseRisePerBlock;
            if (noise < 1e-12f) noise = 1e-12f;
            noisePower[k] = noise;

            float g = power > 1e-20f ? 1f - OverSubtraction * noise / power : 0f;
            if (g < GainFloor) g = GainFloor;
            // Rise at once (keeps speech), fall smoothly (no chirping artefacts).
            float previousGain = bandGain[k];
            if (g < previousGain) g = GainRelease * previousGain + (1f - GainRelease) * g;
            bandGain[k] = g;

            re[k] *= g;
            im[k] *= g;
            if (k > 0 && k < FftSize / 2)
            {
                re[FftSize - k] *= g; // mirror half (real signal)
                im[FftSize - k] *= g;
            }
        }
        noiseKnown = true;

        Fft(true);

        // Overlap-add: first half + the tail of the last frame is this block's output.
        for (int i = 0; i < blockSize; i++)
        {
            samples[i] = overlap[i] + re[i] * window[i];
            overlap[i] = re[blockSize + i] * window[blockSize + i];
        }
    }

    /// <summary>In-place radix-2 FFT on re/im. inverse = true also divides by N.</summary>
    private void Fft(bool inverse)
    {
        int n = FftSize;
        for (int i = 0; i < n; i++)
        {
            int j = bitReverse[i];
            if (j > i)
            {
                float t = re[i]; re[i] = re[j]; re[j] = t;
                t = im[i]; im[i] = im[j]; im[j] = t;
            }
        }

        for (int size = 2; size <= n; size <<= 1)
        {
            int half = size >> 1;
            int step = n / size;
            for (int start = 0; start < n; start += size)
            {
                for (int k = 0; k < half; k++)
                {
                    float wr = cosTable[k * step];
                    float wi = inverse ? -sinTable[k * step] : sinTable[k * step];
                    int a = start + k;
                    int b = a + half;
                    float tr = re[b] * wr - im[b] * wi;
                    float ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
            }
        }

        if (inverse)
        {
            float scale = 1f / n;
            for (int i = 0; i < n; i++) { re[i] *= scale; im[i] *= scale; }
        }
    }

    // =====================================================================
    //  Noise gate
    // =====================================================================

    private void Gate(float[] samples)
    {
        // Decide on the NEWEST block...
        float level = LastInputDb;
        if (!gateOpen)
        {
            aboveCount = level >= GateThresholdDb ? aboveCount + 1 : 0;
            if (aboveCount >= OpenBlocks)
            {
                gateOpen = true;
                holdLeft = HoldBlocks;
            }
        }
        else
        {
            if (level >= GateThresholdDb - CloseHysteresisDb) holdLeft = HoldBlocks;
            else if (--holdLeft <= 0)
            {
                gateOpen = false;
                aboveCount = 0;
            }
        }

        // ...but output the block from LookAheadBlocks ago (so the word start is kept).
        float[] slot = delay[delayIndex];
        System.Array.Copy(samples, slot, blockSize); // store newest (slot = oldest, already read below)
        int oldestIndex = (delayIndex + 1) % delay.Length;
        float[] oldest = delay[oldestIndex];
        delayIndex = oldestIndex;

        float target = gateOpen ? 1f : 0f;
        float stepUp = 1f / (AttackBlocks * blockSize);
        float stepDown = 1f / (ReleaseBlocks * blockSize);
        for (int i = 0; i < blockSize; i++)
        {
            if (gateGain < target) { gateGain += stepUp; if (gateGain > 1f) gateGain = 1f; }
            else if (gateGain > target) { gateGain -= stepDown; if (gateGain < 0f) gateGain = 0f; }
            samples[i] = oldest[i] * gateGain;
        }
    }
}
