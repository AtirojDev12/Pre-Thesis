using UnityEngine;

/// <summary>
/// 22 Oct 2026 (Mr.k): how loud a player's noise is AT a ghost, in 3D.
/// Ghosts hear the way players hear each other, a bit better (rangeBonus).
///
///   MIC  (real voice)    uses the same fall-off as voice chat (VoicePlayback):
///                        full to 2 m, half at 5 m, 20% at 10 m, 8% at 15 m, 0 at 25 m.
///   GAME (steps, sounds) uses the same shape over the footstep range (15 m).
///
/// heard = loudness x fall-off(distance / (range x rangeBonus)).
/// Rough ranges with rangeBonus 1.2 and threshold 0.12:
///   walk (0.3) ~5 m, sprint / light switch (0.6) ~7 m,
///   normal talk (0.5) ~11 m, shouting (1.0) ~16 m, crouch-walk never.
///
/// SERVER: reads PlayerNoise.ServerMic / ServerGame. No walls (Unity audio has no walls either).
/// </summary>
public static class GhostHearing
{
    /// <summary>Same as VoicePlayback.MaxHearingDistance.</summary>
    public const float VoiceRange = 25f;
    /// <summary>Same as the footstep AudioSource max distance.</summary>
    public const float GameRange = 15f;

    /// <summary>How loud (0..1) this player is at <paramref name="listener"/>.</summary>
    public static float HeardAt(PlayerNoise noise, Vector3 listener, float rangeBonus)
    {
        if (noise == null) return 0f;
        float distance = Vector3.Distance(noise.transform.position, listener);
        float bonus = Mathf.Max(0.1f, rangeBonus);
        float mic = noise.ServerMic * Falloff(distance / (VoiceRange * bonus));
        float game = noise.ServerGame * Falloff(distance / (GameRange * bonus));
        return Mathf.Max(mic, game);
    }

    /// <summary>
    /// The voice chat roll-off curve (VoicePlayback.RolloffCurve), as straight lines.
    /// t = distance / max range, 0..1.
    /// </summary>
    public static float Falloff(float t)
    {
        if (t <= 0.08f) return 1f;
        if (t <= 0.2f) return Mathf.Lerp(1f, 0.5f, (t - 0.08f) / 0.12f);
        if (t <= 0.4f) return Mathf.Lerp(0.5f, 0.2f, (t - 0.2f) / 0.2f);
        if (t <= 0.6f) return Mathf.Lerp(0.2f, 0.08f, (t - 0.4f) / 0.2f);
        if (t < 1f) return Mathf.Lerp(0.08f, 0f, (t - 0.6f) / 0.4f);
        return 0f;
    }
}
