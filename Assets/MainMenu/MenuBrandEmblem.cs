using UnityEngine;
using UnityEngine.UI;

// The brand emblem as an object in the showcase stage rather than a line of UI
// text. The sprite is a Blender bake (Tools/bake_emblem.py): bevelled 3D
// lettering wrapped by a real orbit ring that passes behind the upper line and
// comes round in front below the lower one. Every highlight in it is baked, so
// on a phone it costs three unlit quads and five transform writes a frame.
//
// It sizes itself from the gap the menu UI actually leaves between the top row
// of controls and the hero planet, which is what keeps it off both of them on
// every aspect ratio without a per-device table.
[DisallowMultipleComponent]
public sealed class MenuBrandEmblem : MonoBehaviour
{
    // Off (default): legacy framing below, tuned to the Blender ring bake.
    // On: an arbitrary flat logo sprite is the layout authority — its own aspect
    // ratio and full bounds are used, and the ring-riding spark is suppressed
    // since flat art has no baked orbit path for it to follow.
    [SerializeField] bool flatLogoPresentation = false;

    // Mirrors of Tools/bake_emblem.py. The ring is centred on the sprite there,
    // so the live spark can ride the exact ellipse that was rendered into the
    // texture: change one of these and the other file has to move with it.
    const float SpriteAspect = 2.5f;      // RES_X / RES_Y
    const float RingRadiusX = 0.865f;     // RING_RADIUS / (ORTHO * 0.5)
    const float RingRadiusY = 0.870f;     // RING_RADIUS * cos(RING_TILT) / half height

    // Where the bake's ink actually stops inside the quad, as a share of the
    // sprite's half height. Fitting to these instead of to the quad is worth
    // about 15% of emblem on the aspect ratios where the gap is tight.
    const float InkTop = 0.848f;
    const float InkBottom = 0.891f;

    const float WidthFraction = 0.640f;   // of the visible frame width
    const float HeroClearance = 1.35f;    // never wider than this many hero diameters
    const float Margin = 0.020f;          // of screen height, kept off UI and planet

    const int SortingOrder = 8;           // above the planet (0), below the rocket (12)

    // Slow enough that the eye reads it as a still image and only notices the
    // motion if it looks for it.
    const float FloatSpeed = 0.42f;
    const float FloatAmount = 0.010f;     // of the emblem's own height
    const float BreathSpeed = 0.27f;
    const float BreathAmount = 0.0035f;
    const float SparkPeriod = 26f;

    // Below this the band is a failed measurement, not a tight layout.
    const float MinBand = 0.05f;
    const float FlatLogoVerticalOffset = 0.06f; // of the logo's own height
    SpriteRenderer emblem;
    SpriteRenderer halo;
    SpriteRenderer spark;

    Camera stageCamera;
    Canvas canvas;
    Transform hero;
    float heroRadius;

    Vector3 basePosition;
    float baseScale;
    float width;
    float height;
    float haloAlpha;
    float phase;
    bool framed;

    public static Sprite LoadSprite() => Resources.Load<Sprite>("Menu/brand_emblem");

    // Returns false when the bake is missing, which is the caller's cue to leave
    // the plain UI title alone rather than show nothing at all.
    public bool Build(Transform stage, Camera camera, Canvas uiCanvas, Transform heroPlanet,
                      float heroBodyRadius, Color accent)
    {
        Sprite sprite = LoadSprite();
        if (sprite == null) return false;

        stageCamera = camera;
        canvas = uiCanvas;
        hero = heroPlanet;
        heroRadius = heroBodyRadius;
        phase = Random.Range(0f, Mathf.PI * 2f);

        Transform existingEmblem = stage.Find("BrandEmblem");
        if (existingEmblem != null)
        {
            emblem = existingEmblem.GetComponent<SpriteRenderer>();
            halo = stage.Find("BrandEmblemHalo")?.GetComponent<SpriteRenderer>();
            spark = stage.Find("BrandEmblemSpark")?.GetComponent<SpriteRenderer>();
            if (emblem == null || halo == null || spark == null) return false;
            haloAlpha = halo.color.a;
            spark.gameObject.SetActive(!flatLogoPresentation);
            CaptureSerializedFraming();
            Reframe();
            return true;
        }
        if (Application.isPlaying)
        {
            Debug.LogError("MenuBrandEmblem: serialized emblem renderers are missing. Run the Main Menu authoring command.", this);
            return false;
        }

        // A soft wash sitting behind the lettering: the emblem's only tie to the
        // current world's colour. Heavily desaturated and very faint — at full
        // accent it stops being a halo and becomes a coloured smudge behind the
        // wordmark, which is exactly what a premium mark must not have.
        haloAlpha = 0.05f;
        halo = CreateRenderer(stage, "BrandEmblemHalo", VfxSpriteFactory.SoftSprite, SortingOrder - 2);
        Color wash = Color.Lerp(accent, Color.white, 0.45f);
        halo.color = new Color(wash.r, wash.g, wash.b, haloAlpha);

        emblem = CreateRenderer(stage, "BrandEmblem", sprite, SortingOrder);
        emblem.color = ThemeTint(accent);

        spark = CreateRenderer(stage, "BrandEmblemSpark", VfxSpriteFactory.SoftSprite, SortingOrder + 1);
        spark.color = new Color(1f, 0.96f, 0.88f, 0.55f);
        spark.gameObject.SetActive(!flatLogoPresentation);

        Reframe();
        return true;
    }

    // The baked lighting is the emblem's identity, so the theme is allowed to
    // stain it and never to darken it: the accent is normalised to full value
    // first and then mixed in at a fraction white still dominates.
    static Color ThemeTint(Color accent)
    {
        float peak = Mathf.Max(accent.r, Mathf.Max(accent.g, accent.b));
        Color normalised = peak > 0.001f
            ? new Color(accent.r / peak, accent.g / peak, accent.b / peak, 1f)
            : Color.white;
        return Color.Lerp(Color.white, normalised, 0.06f);
    }

    // The prefab already contains an approved, visible framing. Treat that as the last
    // known-good layout before asking the runtime Canvas for measurements: on its first
    // layout frame the top controls can temporarily report no usable band. Without this
    // capture, Reframe's invalid-measurement guard had nothing to preserve and replaced
    // the logo with a near-zero fallback scale for the rest of the menu session.
    void CaptureSerializedFraming()
    {
        if (emblem == null || emblem.sprite == null) return;

        basePosition = emblem.transform.position;
        baseScale = Mathf.Abs(emblem.transform.localScale.x);
        width = emblem.sprite.bounds.size.x * baseScale;
        height = emblem.sprite.bounds.size.y * baseScale;
        framed = width > 0.001f && height > 0.001f;
    }

    SpriteRenderer CreateRenderer(Transform parent, string name, Sprite sprite, int sortingOrder)
    {
        GameObject go = new GameObject(name) { layer = parent.gameObject.layer };
        go.transform.SetParent(parent, false);

        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sharedMaterial = MenuShowcaseAssets.UnlitSpriteMaterial;
        renderer.sortingOrder = sortingOrder;
        return renderer;
    }

    // Re-derived whenever the frame changes. Everything is measured, nothing is
    // a magic offset, so a new UI control in the top row simply pushes the
    // emblem down instead of colliding with it.
    public void Reframe()
    {
        if (emblem == null || stageCamera == null || hero == null) return;

        // Re-read rather than trust the build-time value: the showcase rescales
        // the hero whenever the aspect moves, and a stale radius parks the
        // emblem straight on top of the planet.
        heroRadius = HeroDiscRadius();

        float margin = Screen.height * Margin;
        float ceiling = StageY(TopControlsBottom() - margin);
        float floor = StageY(HeroTopPixel() + margin);
        float band = ceiling - floor;

        // The Canvas re-lays out its top row a frame after the camera's aspect
        // moves, so a Reframe fired on the frame of a resize can measure the
        // controls where they used to be and read no band at all. Clamping that
        // to a minimum baked it in: the emblem shrank to nothing and, with the
        // aspect already recorded as current, nothing ever measured again.
        // Keeping the last good framing until a real one arrives is always
        // better than showing no wordmark.
        if (band <= MinBand)
        {
            if (framed) return;
            band = MinBand;
        }
        framed = true;

        // Flat art has no baked ink margin, so its full sprite bounds are the
        // layout authority and it centres plainly in its band instead of being
        // weighted toward where the old bake's lettering sat.
        float spriteAspect = flatLogoPresentation
            ? emblem.sprite.rect.width / emblem.sprite.rect.height
            : SpriteAspect;
        float inkShare = flatLogoPresentation ? 1f : (InkTop + InkBottom) * 0.5f;

        float frameWidth = stageCamera.orthographicSize * stageCamera.aspect * 2f;
        width = Mathf.Min(frameWidth * WidthFraction, band * spriteAspect / inkShare);
        width = Mathf.Min(width, heroRadius * 2f * HeroClearance);
        height = width / spriteAspect;

        float centre;
        if (flatLogoPresentation)
        {
            centre = floor + band * 0.5f + height * FlatLogoVerticalOffset;
        }
        else
        {
            // Seated slightly above the middle of its band: an emblem hung a little
            // high reads as a crown over the planet, one hung low reads as a caption.
            float inkHeight = height * inkShare;
            float inkCentre = floor + inkHeight * 0.5f + Mathf.Max(0f, band - inkHeight) * 0.55f;
            centre = inkCentre - height * (InkTop - InkBottom) * 0.5f;
        }
        basePosition = new Vector3(hero.position.x, centre, 0f);

        Vector3 spriteSize = emblem.sprite.bounds.size;
        baseScale = width / Mathf.Max(0.0001f, spriteSize.x);

        emblem.transform.position = basePosition;
        emblem.transform.localScale = Vector3.one * baseScale;

        halo.transform.position = basePosition;
        halo.transform.localScale = Vector3.one * (width * 0.78f);

        if (!flatLogoPresentation) spark.transform.localScale = Vector3.one * (height * 0.055f);
    }

    void LateUpdate()
    {
        if (emblem == null) return;

        float time = Time.unscaledTime;

        float drift = Mathf.Sin(time * FloatSpeed + phase) * height * FloatAmount;
        Vector3 position = basePosition + new Vector3(0f, drift, 0f);
        emblem.transform.position = position;
        emblem.transform.localScale =
            Vector3.one * (baseScale * (1f + Mathf.Sin(time * BreathSpeed + phase) * BreathAmount));

        halo.transform.position = position;
        Color glow = halo.color;
        glow.a = haloAlpha * Mathf.Lerp(0.72f, 1.22f, (Mathf.Sin(time * 0.55f + phase) + 1f) * 0.5f);
        halo.color = glow;

        if (!flatLogoPresentation) AnimateSpark(time, position);
    }

    // One mote riding the baked orbit. It disappears behind the lettering on the
    // far half of the path and comes back round in front on the near half, which
    // is the whole reason the ring was modelled in 3D rather than drawn.
    void AnimateSpark(float time, Vector3 centre)
    {
        float angle = time * (Mathf.PI * 2f / SparkPeriod) + phase;
        float sin = Mathf.Sin(angle);

        spark.transform.position = centre + new Vector3(
            Mathf.Cos(angle) * width * 0.5f * RingRadiusX,
            sin * height * 0.5f * RingRadiusY, 0f);

        int order = sin > 0f ? SortingOrder - 1 : SortingOrder + 1;
        if (spark.sortingOrder != order) spark.sortingOrder = order;

        // Near the camera it is bigger and brighter; a slow second beat keeps it
        // from reading as a metronome.
        float near = (1f - sin) * 0.5f;
        float twinkle = 0.78f + 0.22f * Mathf.Sin(time * 0.9f + phase * 1.7f);
        spark.transform.localScale = Vector3.one * (height * Mathf.Lerp(0.038f, 0.062f, near));

        Color colour = spark.color;
        colour.a = Mathf.Lerp(0.22f, 0.62f, near) * twinkle;
        spark.color = colour;
    }

    // Lowest edge of anything the UI keeps in the top band, in screen pixels.
    float TopControlsBottom()
    {
        float limit = Screen.height;
        if (canvas == null) return limit;

        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector3[] corners = new Vector3[4];

        foreach (Graphic graphic in canvas.GetComponentsInChildren<Graphic>(false))
        {
            if (!graphic.isActiveAndEnabled) continue;
            graphic.rectTransform.GetWorldCorners(corners);

            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            for (int i = 0; i < 4; i++)
            {
                float y = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[i]).y;
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }

            // Full-screen dims and panels are not controls, and nothing outside
            // the top band can reach the emblem anyway.
            if (maxY - minY > Screen.height * 0.35f) continue;
            if (minY < Screen.height * 0.6f) continue;

            limit = Mathf.Min(limit, minY);
        }
        return limit;
    }

    // PlanetPresentation.GetBodyRadius reads the renderer's world AABB, which on
    // this stage grows and shrinks by up to 40% as MenuHeroSpin turns the planet.
    // The disc itself never changes size, so measure the sprite instead.
    float HeroDiscRadius()
    {
        SpriteRenderer renderer = hero.GetComponent<SpriteRenderer>();
        if (renderer == null || renderer.sprite == null) return heroRadius;

        Vector3 size = renderer.sprite.bounds.size;
        Vector3 scale = hero.lossyScale;
        return Mathf.Max(size.x * Mathf.Abs(scale.x), size.y * Mathf.Abs(scale.y)) * 0.5f;
    }

    float HeroTopPixel()
    {
        Vector3 top = hero.position + new Vector3(0f, heroRadius, 0f);
        return stageCamera.WorldToScreenPoint(top).y;
    }

    float StageY(float screenY)
    {
        return stageCamera.transform.position.y
             + (screenY / Mathf.Max(1f, Screen.height) - 0.5f) * 2f * stageCamera.orthographicSize;
    }
}
