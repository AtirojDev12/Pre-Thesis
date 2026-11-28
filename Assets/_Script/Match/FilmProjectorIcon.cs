using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 28 Nov 2026 (Atiroj): the film-change ICON. While a projector glows (yellow warning before a film
/// change, red after a missed film in the ghost room), every player sees this icon on screen over
/// the projector, from ANYWHERE in the map: through walls, the same size and look from every angle.
/// When the projector is off screen or behind you, the icon waits at the screen edge on its side.
///
/// Screen UI (not a 3D object), so it never turns or shrinks with distance. The glow on the
/// projector itself is soft and walls hide it (FilmDeadline / SeeThroughGlow).
/// 28 Nov: a player CLOSE to the projector who can SEE it (no wall in between) gets no icon
/// (it fades out); only on that player's screen. Hides under the distance, comes back 1 m
/// further out, so it does not flicker on the line.
/// Picture, size and height: FilmMopTuning (Designer Settings > Film Reels + Mops).
/// LOCAL ONLY: every PC already knows when the projector glows (FilmDeadline SyncVars).
/// </summary>
public sealed class FilmProjectorIcon
{
    private const float EdgeGapPixels = 14f;      // canvas units kept free at the screen edge
    private const float RimExtraPixels = 10f;     // the coloured rim behind the icon
    private const float ShowAgainExtra = 1f;      // metres past the hide distance before it comes back
    private const float FadeSeconds = 0.25f;

    private static Canvas sharedCanvas;

    private readonly RectTransform root;
    private readonly RawImage rim;
    private readonly RawImage picture;
    private readonly Vector3 worldPoint;
    private readonly Vector3 projectorCentre;
    private readonly Transform[] ignoreRoots;      // the projector + its chore box: not "walls"
    private readonly CanvasGroup fade;
    private readonly float size;
    private bool visible;
    private bool hiddenNear;
    private bool snapFade; // first frame after showing: no fade-in flash when already close
    private static readonly RaycastHit[] Hits = new RaycastHit[16];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => sharedCanvas = null;

    private FilmProjectorIcon(RectTransform root, RawImage rim, RawImage picture, Vector3 worldPoint,
        Vector3 projectorCentre, Transform[] ignoreRoots, float size)
    {
        this.root = root;
        this.rim = rim;
        this.picture = picture;
        this.worldPoint = worldPoint;
        this.projectorCentre = projectorCentre;
        this.ignoreRoots = ignoreRoots;
        this.size = size;
        fade = root.GetComponent<CanvasGroup>();
    }

    /// <summary>Null when there is no picture set in FilmMopTuning.</summary>
    public static FilmProjectorIcon Build(Transform[] parts, Texture picture, float size, float heightAbove, Transform owner = null)
    {
        if (picture == null)
        {
            Debug.LogWarning("[FilmProjectorIcon] No Projector Icon set in FilmMopTuning (Designer Settings > Film Reels + Mops).");
            return null;
        }

        // Over the top centre of the projector (it never moves).
        bool any = false;
        Bounds bounds = default;
        if (parts != null)
            foreach (Transform part in parts)
            {
                if (part == null) continue;
                foreach (Renderer r in part.GetComponentsInChildren<Renderer>())
                {
                    if (r == null || !r.enabled) continue;
                    if (!any) { bounds = r.bounds; any = true; }
                    else bounds.Encapsulate(r.bounds);
                }
            }
        Vector3 top = any
            ? new Vector3(bounds.center.x, bounds.max.y, bounds.center.z)
            : (parts != null && parts.Length > 0 && parts[0] != null ? parts[0].position : Vector3.zero);

        Canvas canvas = SharedCanvas();
        var rootObject = new GameObject("Film Projector Icon", typeof(RectTransform), typeof(CanvasGroup));
        CanvasGroup group = rootObject.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        var root = (RectTransform)rootObject.transform;
        root.SetParent(canvas.transform, false);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.sizeDelta = Vector2.one * size;

        RawImage rim = MakeImage("Rim", root, picture, size + RimExtraPixels);
        RawImage image = MakeImage("Icon", root, picture, size);
        rootObject.SetActive(false);
        Vector3 centre = any ? bounds.center : top;
        var ignore = new System.Collections.Generic.List<Transform>();
        if (parts != null) foreach (Transform part in parts) if (part != null) ignore.Add(part);
        if (owner != null) ignore.Add(owner);
        return new FilmProjectorIcon(root, rim, image, top + Vector3.up * heightAbove, centre, ignore.ToArray(), size);
    }

    /// <summary>Same timing as the glow. The rim takes the glow colour (yellow = soon, red = missed).</summary>
    public void SetVisible(bool show, Color glowColor, float pulse)
    {
        if (root == null) return;
        if (show != visible)
        {
            visible = show;
            root.gameObject.SetActive(show);
            if (show)
            {
                hiddenNear = false;
                snapFade = true;
                if (fade != null) fade.alpha = 0f; // placed + faded right in LateUpdate
            }
        }
        if (!show) return;
        Color rimColor = glowColor;
        rimColor.a = Mathf.Lerp(0.45f, 0.95f, Mathf.Clamp01(pulse));
        rim.color = rimColor;
        picture.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.8f, 1f, Mathf.Clamp01(pulse)));
    }

    /// <summary>Call every frame after the cameras moved (LateUpdate). 0 = never hide when close.</summary>
    public void Place(float hideDistance)
    {
        if (!visible || root == null) return;
        Camera view = ViewCamera();
        if (view == null) return;

        // 28 Nov: close AND can see the projector -> no icon on this screen (fades).
        Vector3 eye = view.transform.position;
        float distance = Vector3.Distance(eye, projectorCentre);
        if (hideDistance <= 0f) hiddenNear = false;
        else if (!hiddenNear) hiddenNear = distance < hideDistance && CanSee(eye);
        else hiddenNear = distance <= hideDistance + ShowAgainExtra && CanSee(eye);
        if (fade != null)
        {
            float target = hiddenNear ? 0f : 1f;
            fade.alpha = snapFade ? target : Mathf.MoveTowards(fade.alpha, target, Time.unscaledDeltaTime / FadeSeconds);
            snapFade = false;
        }

        float scale = sharedCanvas != null ? sharedCanvas.scaleFactor : 1f;
        float margin = (size * 0.5f + RimExtraPixels * 0.5f + EdgeGapPixels) * scale;
        Vector2 screen = new Vector2(Screen.width, Screen.height);
        Vector2 centre = screen * 0.5f;

        Vector3 projected = view.WorldToScreenPoint(worldPoint);
        Vector2 point = projected;
        bool behind = projected.z < 0f;
        bool onScreen = !behind &&
                        point.x >= margin && point.x <= screen.x - margin &&
                        point.y >= margin && point.y <= screen.y - margin;
        if (!onScreen)
        {
            // Off screen or behind you: wait at the screen edge, on the side the projector is.
            Vector2 direction = point - centre;
            if (behind) direction = -direction;
            if (direction.sqrMagnitude < 0.01f) direction = Vector2.down;
            float halfX = Mathf.Max(1f, centre.x - margin);
            float halfY = Mathf.Max(1f, centre.y - margin);
            float tx = Mathf.Abs(direction.x) > 0.0001f ? halfX / Mathf.Abs(direction.x) : float.MaxValue;
            float ty = Mathf.Abs(direction.y) > 0.0001f ? halfY / Mathf.Abs(direction.y) : float.MaxValue;
            point = centre + direction * Mathf.Min(tx, ty);
        }
        root.position = new Vector3(point.x, point.y, 0f);
    }

    /// <summary>Nothing but the projector (and its chore box) between the eye and the projector.</summary>
    private bool CanSee(Vector3 eye)
    {
        Vector3 toProjector = projectorCentre - eye;
        float length = toProjector.magnitude;
        if (length < 0.01f) return true;
        int count = Physics.RaycastNonAlloc(eye, toProjector / length, Hits, length,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = Hits[i].collider;
            if (hit == null || IsIgnored(hit.transform)) continue;
            // A player body (yours or a friend's) is not a wall.
            if (hit.GetComponentInParent<PlayerHealth>() != null) continue;
            return false;
        }
        return true;
    }

    private bool IsIgnored(Transform t)
    {
        if (ignoreRoots == null) return false;
        foreach (Transform r in ignoreRoots) if (r != null && t.IsChildOf(r)) return true;
        return false;
    }

    public void Destroy()
    {
        if (root != null) Object.Destroy(root.gameObject);
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private static RawImage MakeImage(string name, RectTransform parent, Texture picture, float size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = Vector2.one * size;
        RawImage image = go.GetComponent<RawImage>();
        image.texture = picture;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>The camera drawing the screen right now (player view, or the spectate camera).</summary>
    private static Camera ViewCamera()
    {
        Camera main = Camera.main;
        if (main != null && main.isActiveAndEnabled && main.targetTexture == null) return main;
        Camera best = null;
        foreach (Camera c in Camera.allCameras)
        {
            if (c == null || c.targetTexture != null || !c.isActiveAndEnabled) continue;
            if (best == null || c.depth > best.depth) best = c;
        }
        return best;
    }

    /// <summary>One screen canvas for every projector icon (under menus, over the world).</summary>
    private static Canvas SharedCanvas()
    {
        if (sharedCanvas != null) return sharedCanvas;
        var go = new GameObject("Film Projector Icons (local)", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Object.DontDestroyOnLoad(go);
        sharedCanvas = go.GetComponent<Canvas>();
        sharedCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        sharedCanvas.sortingOrder = 40; // under the noise meter (49) and the pause menu (50)
        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return sharedCanvas;
    }
}
