using UnityEngine;

/// <summary>
/// 2 Nov 2026 (Mr.k): the picture on a cinema screen, like a real cinema.
///
/// The screen is ALWAYS lit (it never turns into a black hole):
///   - Blank  : no film = an empty, softly lit screen (the projector light).
///   - Picture: a film (later the team plays an animation here: pass its texture,
///              e.g. a VideoPlayer RenderTexture, to <see cref="ShowPicture"/>).
///   - Static : TV static (the ghost's "other film" until real ghost footage exists).
/// The picture uses an unlit shader that ignores fog ("Pre-Thesis/Cinema Screen (No Fog)"),
/// so it is seen from the projector and the back rows in the dark, foggy room.
/// A wide light in front of the screen shines over the seats (the light reflected by the
/// screen): steady for Blank / Picture, flickering for Static.
///
/// LOCAL VISUALS ONLY. It sends nothing over the network: whoever owns the state (e.g. the
/// Screen Room rule's SyncVar) calls these methods on every machine.
/// Added at runtime by RuleStaffTime on its "Ghost Movie (on the screen)" object.
/// </summary>
[DisallowMultipleComponent]
public sealed class CinemaScreen : MonoBehaviour
{
    public enum Mode { Blank, Picture, Static }

    private const string ShaderName = "Pre-Thesis/Cinema Screen (No Fog)";
    private const int NoiseWidth = 160;
    private const int NoiseHeight = 90;

    // Set by Setup().
    private float blankBrightness = 0.45f;
    private float pictureBrightness = 1f;
    private float staticBrightness = 1.2f;
    private float lightIntensity = 60f;
    private int staticFps = 24;

    private Renderer picture;
    private Material material;
    private bool unlit;
    private Light lamp;
    private Texture2D noise;
    private Color32[] pixels;
    private uint noiseState = 2463534242u;
    private float nextNoise;

    private Mode mode = Mode.Blank;
    private Texture film;
    private Mode beforeGhost = Mode.Blank;
    private Texture filmBeforeGhost;
    private bool ghostFilm;

    public Mode CurrentMode => mode;

    /// <summary>
    /// Builds the look. <paramref name="screenPicture"/> = the quad on the screen;
    /// <paramref name="intoRoom"/> = the direction from the screen to the seats.
    /// </summary>
    public void Setup(Renderer screenPicture, Vector3 intoRoom, float roomLength, Color lightColor, float lightPower,
        float blank, float pictureLevel, float staticLevel, int fps, bool withLight)
    {
        picture = screenPicture;
        blankBrightness = blank;
        pictureBrightness = pictureLevel;
        staticBrightness = staticLevel;
        lightIntensity = lightPower;
        staticFps = Mathf.Max(5, fps);

        if (picture != null)
        {
            Shader shader = Shader.Find(ShaderName);
            unlit = shader != null && shader.isSupported;
            if (unlit)
            {
                material = new Material(shader) { name = "Cinema Screen (runtime)" };
                picture.sharedMaterial = material;
            }
            else
            {
                // Fallback: the mockup's lit material with emission (fog can still hide it).
                Debug.LogWarning($"[CinemaScreen] Shader '{ShaderName}' not found: using the lit fallback (fog may hide the screen).", this);
                material = picture.material;
                if (material.HasProperty("_EmissionColor")) material.EnableKeyword("_EMISSION");
            }
            picture.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            picture.receiveShadows = false;
        }

        if (withLight)
        {
            var go = new GameObject("Screen Light");
            go.transform.SetParent(transform.parent != null ? transform.parent : transform, true);
            go.transform.SetPositionAndRotation(transform.position + intoRoom.normalized * 0.6f, Quaternion.LookRotation(intoRoom, Vector3.up));
            lamp = go.AddComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.spotAngle = 150f;
            lamp.innerSpotAngle = 60f;
            lamp.range = Mathf.Max(10f, roomLength);
            lamp.color = lightColor;
            lamp.shadows = LightShadows.None;
        }
        Apply();
    }

    // ---- API --------------------------------------------------------------------------

    /// <summary>No film: empty, softly lit screen.</summary>
    public void ShowBlank()
    {
        film = null;
        if (ghostFilm) { filmBeforeGhost = null; beforeGhost = Mode.Blank; return; }
        mode = Mode.Blank;
        Apply();
    }

    /// <summary>A film picture (any texture; a VideoPlayer RenderTexture works).</summary>
    public void ShowPicture(Texture texture)
    {
        if (texture == null) { ShowBlank(); return; }
        film = texture;
        if (ghostFilm) { filmBeforeGhost = texture; beforeGhost = Mode.Picture; return; }
        mode = Mode.Picture;
        Apply();
    }

    /// <summary>The ghost takes over the screen (static for now). Remembers what was playing.</summary>
    public void BeginGhostFilm()
    {
        if (ghostFilm) return;
        ghostFilm = true;
        beforeGhost = mode == Mode.Static ? Mode.Blank : mode;
        filmBeforeGhost = film;
        mode = Mode.Static;
        nextNoise = 0f;
        Apply();
    }

    /// <summary>The ghost is gone: back to the film that was playing, or the blank screen.</summary>
    public void EndGhostFilm()
    {
        if (!ghostFilm) return;
        ghostFilm = false;
        film = filmBeforeGhost;
        mode = beforeGhost == Mode.Picture && film != null ? Mode.Picture : Mode.Blank;
        Apply();
    }

    // ---- Look -------------------------------------------------------------------------

    private void Update()
    {
        if (mode != Mode.Static || Time.unscaledTime < nextNoise) return;
        nextNoise = Time.unscaledTime + 1f / staticFps;
        RefreshNoise();
        if (lamp != null) lamp.intensity = lightIntensity * Random.Range(0.6f, 1f);
    }

    private void Apply()
    {
        if (material != null)
        {
            Texture tex;
            float level;
            switch (mode)
            {
                case Mode.Static: EnsureNoise(); RefreshNoise(); tex = noise; level = staticBrightness; break;
                case Mode.Picture: tex = film; level = pictureBrightness; break;
                default: tex = Texture2D.whiteTexture; level = blankBrightness; break;
            }
            SetPicture(tex, level);
        }
        if (lamp != null)
        {
            float k = mode == Mode.Blank ? 0.35f : mode == Mode.Picture ? 0.7f : 1f;
            lamp.intensity = lightIntensity * k;
        }
    }

    private void SetPicture(Texture tex, float level)
    {
        if (unlit)
        {
            material.SetTexture("_MainTex", tex);
            material.SetColor("_Color", Color.white);
            material.SetFloat("_Brightness", level);
            return;
        }
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", tex);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", tex);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_EmissionMap")) material.SetTexture("_EmissionMap", tex);
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", Color.white * level);
    }

    private void EnsureNoise()
    {
        if (noise != null) return;
        noise = new Texture2D(NoiseWidth, NoiseHeight, TextureFormat.RGBA32, false)
        {
            name = "Screen Static",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        pixels = new Color32[NoiseWidth * NoiseHeight];
    }

    private void RefreshNoise()
    {
        if (noise == null) return;
        uint s = noiseState;
        int band = (int)(Time.unscaledTime * 30f) % NoiseHeight; // a brighter band rolling down
        for (int y = 0; y < NoiseHeight; y++)
        {
            int lift = Mathf.Abs(y - band) < 4 ? 70 : 0;
            int row = y * NoiseWidth;
            for (int x = 0; x < NoiseWidth; x++)
            {
                s ^= s << 13; s ^= s >> 17; s ^= s << 5; // xorshift: fast, no allocation
                int v = (int)(s & 0xFF) + lift;
                byte b = (byte)(v > 255 ? 255 : v);
                pixels[row + x] = new Color32(b, b, b, 255);
            }
        }
        noiseState = s;
        noise.SetPixels32(pixels);
        noise.Apply(false);
    }

    private void OnDestroy()
    {
        if (noise != null) Destroy(noise);
        if (material != null) Destroy(material);
        if (lamp != null) Destroy(lamp.gameObject);
    }
}
