using UnityEngine;

/// <summary>
/// Records the microphone with Unity (not with EOS) and hands out 10 ms blocks
/// of 48 kHz mono 16-bit audio, ready for EOS SendAudio.
///
/// Why Unity and not EOS: with EOS capturing the mic itself, the test showed
/// the mic listed and "sending ON" but ZERO audio blocks — EOS's own capture
/// never started. Unity's Microphone is simple and visible (level meter), and
/// EOS then only has to transport the audio ("manual audio input").
///
/// Main thread only (Unity's Microphone API).
/// </summary>
public sealed class VoiceMicCapture
{
    public const int OutputRate = 48000;
    public const int BlockSamples = OutputRate / 100; // 10 ms

    private AudioClip clip;
    private string device;
    private int deviceRate;
    private int readPosition;
    private float[] readBuffer = new float[4096];

    // Resampler state (device rate -> 48 kHz), linear.
    private double resamplePos;
    private float lastSample;
    private readonly short[] block = new short[BlockSamples];
    private int blockFill;

    public bool IsRecording => clip != null && Microphone.IsRecording(device);
    public string DeviceName => string.IsNullOrEmpty(device) ? "(Windows default)" : device;
    public int DeviceRate => deviceRate;

    /// <summary>Peak of the last block, 0..1.</summary>
    public float Level { get; private set; }

    public string LastError { get; private set; } = "";

    /// <summary>Start (or restart) recording from this device. Null/empty = Windows default.</summary>
    public void Start(string deviceName)
    {
        Stop();
        device = string.IsNullOrEmpty(deviceName) ? null : deviceName;

        if (Microphone.devices.Length == 0)
        {
            LastError = "Unity sees no microphone (Windows privacy settings?)";
            return;
        }

        Microphone.GetDeviceCaps(device, out int minRate, out int maxRate);
        // 0/0 = the device takes any rate.
        deviceRate = (minRate == 0 && maxRate == 0) ? OutputRate : Mathf.Clamp(OutputRate, minRate, maxRate);

        clip = Microphone.Start(device, true, 1, deviceRate);
        if (clip == null)
        {
            LastError = "Microphone.Start failed for " + DeviceName;
            return;
        }

        LastError = "";
        readPosition = 0;
        resamplePos = 0;
        blockFill = 0;
        lastSample = 0f;
    }

    public void Stop()
    {
        if (clip != null) Microphone.End(device);
        if (clip != null) Object.Destroy(clip);
        clip = null;
        Level = 0f;
    }

    /// <summary>
    /// Reads everything recorded since the last call and calls <paramref name="onBlock"/>
    /// for every complete 10 ms block. The array is reused: copy it if you keep it.
    /// </summary>
    public void Pump(System.Action<short[]> onBlock)
    {
        if (clip == null) return;

        int writePosition = Microphone.GetPosition(device);
        if (writePosition < 0 || writePosition == readPosition) return;

        int total = clip.samples;
        int available = writePosition - readPosition;
        if (available < 0) available += total;
        // Stall (e.g. a long hitch): keep only the newest 100 ms so the voice stays live.
        int maxKeep = deviceRate / 10;
        if (available > maxKeep)
        {
            readPosition = (writePosition - maxKeep + total) % total;
            available = maxKeep;
        }

        if (readBuffer.Length < available) readBuffer = new float[Mathf.NextPowerOfTwo(available)];

        // AudioClip.GetData wraps around the end of a looping clip by itself only
        // within the clip length, so read in (up to) two parts.
        int firstPart = Mathf.Min(available, total - readPosition);
        ReadPart(readPosition, firstPart, 0);
        if (available > firstPart) ReadPart(0, available - firstPart, firstPart);
        readPosition = (readPosition + available) % total;

        Resample(available, onBlock);
    }

    private float[] partBuffer = new float[4096];

    private void ReadPart(int offset, int count, int destination)
    {
        if (count <= 0) return;
        if (partBuffer.Length < count) partBuffer = new float[Mathf.NextPowerOfTwo(count)];
        // GetData fills the WHOLE array, so read into an array of exactly 'count'.
        float[] exact = count == partBuffer.Length ? partBuffer : new float[count];
        clip.GetData(exact, offset);
        System.Array.Copy(exact, 0, readBuffer, destination, count);
    }

    private void Resample(int count, System.Action<short[]> onBlock)
    {
        double step = (double)deviceRate / OutputRate; // input samples per output sample
        while (resamplePos < count)
        {
            int i = (int)resamplePos;
            float frac = (float)(resamplePos - i);
            float a = i == 0 ? lastSample : readBuffer[i - 1];
            float b = readBuffer[i];
            float s = step == 1.0 ? b : Mathf.Lerp(a, b, frac);

            float abs = s < 0f ? -s : s;
            if (abs > blockPeak) blockPeak = abs;
            block[blockFill++] = (short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), short.MinValue, short.MaxValue);

            if (blockFill == BlockSamples)
            {
                Level = blockPeak;
                blockPeak = 0f;
                blockFill = 0;
                onBlock(block);
            }
            resamplePos += step;
        }
        resamplePos -= count;
        lastSample = readBuffer[count - 1];
    }

    private float blockPeak;
}
