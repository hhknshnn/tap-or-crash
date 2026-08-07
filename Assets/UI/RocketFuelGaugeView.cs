using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One Fuel gauge, built in code and reused wherever Fuel is shown.
//
// It knows nothing about where Fuel comes from, how it refills, or who is allowed
// to spend it: it is handed a normalised level and draws it. The Main Menu and the
// in-run HUD each own an instance of this same component, which is why the two can
// never drift apart visually.
[DisallowMultipleComponent]
public sealed class RocketFuelGaugeView : MonoBehaviour
{
    // SafeAreaFitter recognises HUD elements by name; the gauge joins that list
    // under this one so both instances inherit the same left-edge inset rule.
    public const string RootName = "RocketFuelHud";

    // ── Geometry, in canvas reference pixels (1080x1920) ─────────────────────
    private const float TrackWidth = 76f;
    private const float FrameThickness = 9f;
    private const float FrameRadius = 30f;
    private const float InnerRadius = 22f;
    private const float LabelBlockHeight = 128f;
    private const float RootWidth = 148f;
    private const float MarkerWidth = 56f;
    private const float MarkerHeight = 44f;

    // The unit sits just below the true midline: at dead centre it reads as
    // dividing the screen, and a few percent down settles it against the stage.
    private const float VerticalCentreOfUsable = 0.515f;
    private const float FillEaseDuration = 0.30f;
    private const float LowFuelThreshold = 0.20f;

    // ── Palette ──────────────────────────────────────────────────────────────
    // Deeper and more saturated than the original: at low alpha the old values
    // read as flat grey plastic rather than navy/purple glass.
    private static readonly Color FrameFill = new Color(0.100f, 0.080f, 0.190f, 0.82f);
    private static readonly Color FrameRim = new Color(0.620f, 0.520f, 0.980f, 0.70f);
    private static readonly Color TrackFill = new Color(0.035f, 0.028f, 0.075f, 0.94f);
    private static readonly Color GlossColor = new Color(1f, 1f, 1f, 0.075f);
    private static readonly Color MarkerBacking = new Color(0.085f, 0.070f, 0.150f, 0.95f);

    // One gradient for the whole Fuel language: liquid, percentage and halo all
    // read the same curve, so nothing can disagree about what "low" looks like.
    private static readonly Gradient FuelGradient = BuildGradient();

    private RectTransform rootRect;
    private RectTransform frameRect;
    private RectTransform markerRect;
    private RectTransform glowRect;
    private FuelLiquidGraphic liquid;
    private Image glow;
    private TextMeshProUGUI percentage;
    private TextMeshProUGUI fuelLabel;

    private const float FuelLabelSize = 24f;
    private const float PercentageSize = 30f;

    private float targetNormalized;
    private float displayedNormalized;
    private float easeFrom;
    private float easeStartTime = -1f;
    private float trackHeight;
    private Vector2 lastCanvasSize;
    private RectTransform rootCanvasRect;
    private Canvas isolationCanvas;
    private bool isolationChecked;
    private int lastShownPercent = -1;

    public RectTransform Root => rootRect;

    /// The Fuel colour for a level, so anything else that accents itself with Fuel
    /// state — the empty-tank popup, for one — reads the same curve as the gauge.
    public static Color ColourFor(float normalized) => FuelGradient.Evaluate(Mathf.Clamp01(normalized));

    /// Builds a gauge under the given parent and returns it ready to be driven.
    /// The canvas rect is passed in rather than looked up: the in-run gauge is
    /// built under a GameUI that is switched off until the run starts, and a
    /// parent lookup from inside an inactive hierarchy finds nothing.
    public static RocketFuelGaugeView Create(Transform parent, RectTransform canvasRect)
    {
        Transform existing = parent != null ? parent.Find(RootName) : null;
        RocketFuelGaugeView existingView = existing != null
            ? existing.GetComponent<RocketFuelGaugeView>() : null;
        if (existingView != null)
        {
            existingView.BindExisting(canvasRect);
            return existingView;
        }

        GameObject root = new GameObject(RootName, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        RocketFuelGaugeView view = root.AddComponent<RocketFuelGaugeView>();
        view.rootCanvasRect = canvasRect;
        view.Build();
        return view;
    }

    private void Awake() => BindExisting(GetComponentInParent<Canvas>()?.GetComponent<RectTransform>());

    public bool BindExisting(RectTransform canvasRect)
    {
        rootRect = GetComponent<RectTransform>();
        rootCanvasRect = canvasRect != null ? canvasRect : rootCanvasRect;
        Transform frame = transform.Find("FuelFrame");
        Transform marker = frame != null ? frame.Find("FuelRocketMarker") : null;
        Transform tank = frame != null ? frame.Find("FuelTank") : null;
        Transform glowTransform = transform.Find("FuelGlow");
        rootRect = GetComponent<RectTransform>();
        frameRect = frame as RectTransform;
        markerRect = marker as RectTransform;
        glowRect = glowTransform as RectTransform;
        glow = glowTransform != null ? glowTransform.GetComponent<Image>() : null;
        liquid = tank != null ? tank.GetComponentInChildren<FuelLiquidGraphic>(true) : null;
        Transform percentageTransform = transform.Find("FuelPercentage");
        Transform fuelLabelTransform = transform.Find("FuelLabel");
        fuelLabel = fuelLabelTransform != null
            ? fuelLabelTransform.GetComponent<TextMeshProUGUI>() : null;
        percentage = percentageTransform != null
            ? percentageTransform.GetComponent<TextMeshProUGUI>() : null;
        if (rootRect == null || frameRect == null || markerRect == null || liquid == null || percentage == null)
            return false;
        ApplyReadableText();
        if (rootCanvasRect != null) Layout(rootCanvasRect.rect.size);
        return true;
    }

    /// The authoritative level, 0..1. The number on screen changes at once; the
    /// liquid eases toward it, because a tank that snaps does not read as liquid.
    public void SetNormalized(float normalized, bool animate)
    {
        float clamped = Mathf.Clamp01(normalized);
        targetNormalized = clamped;

        if (!animate || !isActiveAndEnabled)
        {
            displayedNormalized = clamped;
            easeStartTime = -1f;
            ApplyLevel(clamped, false);
            return;
        }

        easeFrom = displayedNormalized;
        easeStartTime = Time.unscaledTime;
        ApplyReadout(clamped);
    }

    private void OnEnable()
    {
        // A gauge whose parent was switched off holds no stale animation: it
        // reappears already showing the value the presenter last pushed.
        displayedNormalized = targetNormalized;
        easeStartTime = -1f;
        ApplyLevel(targetNormalized, false);
    }

    // A nested Canvas keeps the wave's 45Hz geometry rebuilds off the main HUD
    // canvas, where they would re-batch the score, coins and progress every time.
    //
    // It is attached from Update and nowhere else. Adding a Canvas from inside a
    // component-add callback, or inside a switched-off hierarchy, leaves Unity
    // unable to resolve the parent canvas: the component silently becomes a
    // WorldSpace root canvas, which stops the whole overlay canvas from drawing.
    // The check below refuses that outcome — losing the batching optimisation is
    // survivable, losing the HUD is not.
    private void EnsureIsolationCanvas()
    {
        if (isolationChecked) return;
        isolationChecked = true;

        Transform parent = transform.parent;
        if (parent == null || parent.GetComponentInParent<Canvas>() == null) return;

        // The Main Menu instance's tap hit area (RocketFuelHud.WireMenuGaugeTap)
        // adds a GraphicRaycaster to this same GameObject, and GraphicRaycaster
        // requires a Canvas — Unity may have already attached one as that
        // dependency by the time this first runs, so this must adopt it rather
        // than assume it is always the one adding it.
        Canvas nested = GetComponent<Canvas>();
        if (nested == null) nested = gameObject.AddComponent<Canvas>();
        if (nested.isRootCanvas)
        {
            Destroy(nested);
            return;
        }

        nested.overrideSorting = false;
        isolationCanvas = nested;
    }

    private void Update()
    {
        EnsureIsolationCanvas();

        if (rootCanvasRect != null && rootCanvasRect.rect.size != lastCanvasSize)
            Layout(rootCanvasRect.rect.size);

        if (easeStartTime >= 0f)
        {
            float progress = Mathf.Clamp01((Time.unscaledTime - easeStartTime) / FillEaseDuration);
            displayedNormalized = Mathf.Lerp(easeFrom, targetNormalized, Mathf.SmoothStep(0f, 1f, progress));
            if (progress >= 1f) easeStartTime = -1f;
        }

        ApplyLevel(displayedNormalized, false);
    }

    // ── Presentation ─────────────────────────────────────────────────────────

    // Deterministic authoring state. This uses the same visual application as
    // runtime, but pins the wave and marker instead of sampling the editor clock.
    public bool ApplyAuthoringPreview(RectTransform canvasRect)
    {
        if (!BindExisting(canvasRect)) return false;
        targetNormalized = 1f;
        displayedNormalized = 1f;
        easeStartTime = -1f;
        liquid.SetWavePhase(0f, 0f);
        ApplyLevel(1f, true);
        return true;
    }

    private void ApplyLevel(float normalized, bool deterministicPreview)
    {
        if (liquid == null) return;

        liquid.Fill = normalized;

        // Colour rides the eased level, not the authoritative one: a grant that
        // takes a tank from orange to green should travel through yellow with the
        // liquid rather than change under it. Only the number itself is immediate.
        Color colour = FuelGradient.Evaluate(normalized);
        Color depth = colour * 0.62f;
        depth.a = 1f;
        liquid.SetColors(colour, depth);

        if (percentage != null) percentage.color = colour;

        if (glow != null)
        {
            // Below a fifth of a tank the halo breathes once every couple of
            // seconds. It is a reminder, not an alarm: no flashing, no strobe.
            float baseAlpha = 0.22f;
            if (normalized <= LowFuelThreshold)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI / 1.1f);
                baseAlpha = Mathf.Lerp(0.14f, 0.34f, pulse);
            }
            glow.color = new Color(colour.r, colour.g, colour.b, baseAlpha);
        }

        PositionMarker(normalized, deterministicPreview);
        ApplyReadout(targetNormalized);
    }

    // Called every frame, so the string is built only when the rounded number
    // actually moves — otherwise this would allocate 60 strings a second.
    private void ApplyReadout(float normalized)
    {
        if (percentage == null) return;
        int shown = Mathf.RoundToInt(normalized * 100f);
        if (shown == lastShownPercent) return;
        lastShownPercent = shown;
        percentage.text = shown + "%";
    }

    private void PositionMarker(float normalized, bool deterministicPreview)
    {
        if (markerRect == null || liquid == null) return;

        float innerHalf = Mathf.Max(0f, trackHeight * 0.5f - FrameThickness);
        float surfaceY = liquid.SurfaceLocalY(0.5f);

        // A small hover keeps the marker alive at a level that is not moving.
        if (!deterministicPreview)
            surfaceY += Mathf.Sin(Time.unscaledTime * 2.1f) * 2.5f;

        float limit = Mathf.Max(0f, innerHalf - MarkerHeight * 0.5f);
        markerRect.anchoredPosition = new Vector2(0f, Mathf.Clamp(surfaceY, -limit, limit));
    }

    // ── Construction ─────────────────────────────────────────────────────────

    private void Build()
    {
        UIDesign.EnsureInitialised();

        rootRect = GetComponent<RectTransform>();
        if (rootRect == null) rootRect = gameObject.AddComponent<RectTransform>();

        GameObject glowObject = new GameObject("FuelGlow");
        glowObject.transform.SetParent(transform, false);
        glowRect = glowObject.AddComponent<RectTransform>();
        glow = glowObject.AddComponent<Image>();
        glow.sprite = UIGlass.Glow;
        glow.raycastTarget = false;

        GameObject frameObject = new GameObject("FuelFrame");
        frameObject.transform.SetParent(transform, false);
        frameRect = frameObject.AddComponent<RectTransform>();
        Image frame = frameObject.AddComponent<Image>();
        frame.sprite = UIGlass.Panel(FrameRadius);
        frame.type = Image.Type.Sliced;
        frame.color = FrameFill;
        frame.raycastTarget = false;

        GameObject tankObject = new GameObject("FuelTank");
        tankObject.transform.SetParent(frameObject.transform, false);
        RectTransform tankRect = tankObject.AddComponent<RectTransform>();
        Stretch(tankRect, FrameThickness);
        Image tank = tankObject.AddComponent<Image>();
        tank.sprite = UIGlass.Panel(InnerRadius);
        tank.type = Image.Type.Sliced;
        tank.color = TrackFill;
        tank.raycastTarget = false;

        GameObject liquidObject = new GameObject("FuelLiquid");
        liquidObject.transform.SetParent(tankObject.transform, false);
        Stretch(liquidObject.AddComponent<RectTransform>(), 0f);
        liquid = liquidObject.AddComponent<FuelLiquidGraphic>();
        liquid.raycastTarget = false;
        // The liquid carries the tank's rounded profile itself. A stencil Mask
        // would have to sit between this gauge's nested Canvas and its content,
        // where Unity drops the mask entirely.
        liquid.CornerRadius = InnerRadius;

        GameObject glossObject = new GameObject("FuelGloss");
        glossObject.transform.SetParent(tankObject.transform, false);
        RectTransform glossRect = glossObject.AddComponent<RectTransform>();
        glossRect.anchorMin = new Vector2(0f, 0f);
        glossRect.anchorMax = new Vector2(0f, 1f);
        glossRect.pivot = new Vector2(0f, 0.5f);
        glossRect.anchoredPosition = new Vector2(7f, 0f);
        glossRect.sizeDelta = new Vector2(11f, -18f);
        Image gloss = glossObject.AddComponent<Image>();
        gloss.sprite = UIGlass.Panel(6f);
        gloss.type = Image.Type.Sliced;
        gloss.color = GlossColor;
        gloss.raycastTarget = false;

        GameObject rimObject = new GameObject("FuelRim");
        rimObject.transform.SetParent(frameObject.transform, false);
        Stretch(rimObject.AddComponent<RectTransform>(), 0f);
        Image rim = rimObject.AddComponent<Image>();
        rim.sprite = UIGlass.Rim(FrameRadius, 2.5f);
        rim.type = Image.Type.Sliced;
        rim.color = FrameRim;
        rim.raycastTarget = false;

        GameObject markerObject = new GameObject("FuelRocketMarker");
        markerObject.transform.SetParent(frameObject.transform, false);
        markerRect = markerObject.AddComponent<RectTransform>();
        markerRect.anchorMin = markerRect.anchorMax = new Vector2(0.5f, 0.5f);
        markerRect.pivot = new Vector2(0.5f, 0.5f);
        markerRect.sizeDelta = new Vector2(MarkerWidth, MarkerHeight);
        Image markerDisc = markerObject.AddComponent<Image>();
        markerDisc.sprite = UIGlass.Disc;
        markerDisc.color = MarkerBacking;
        markerDisc.raycastTarget = false;
        BuildProceduralRocket(markerObject.transform);

        fuelLabel = CreateLabel(transform, "FuelLabel", "FUEL", FuelLabelSize, UIDesign.TextSub);
        percentage = CreateLabel(transform, "FuelPercentage", "100%", PercentageSize, Color.white);
        ApplyReadableText();

        if (rootCanvasRect != null) Layout(rootCanvasRect.rect.size);
    }

    private void Layout(Vector2 canvasSize)
    {
        lastCanvasSize = canvasSize;
        trackHeight = Mathf.Clamp(canvasSize.y * 0.24f, 320f, 440f);
        float unitHeight = trackHeight + LabelBlockHeight;

        // The vertical placement is worked out here rather than left to
        // SafeAreaFitter: a rect anchored at the vertical middle gets no inset
        // from it, and the tank is tall enough to reach a notch on a short screen.
        float safeTop = 0f;
        float safeBottom = 0f;
        if (Screen.width > 0 && Screen.height > 0)
        {
            Rect safe = Screen.safeArea;
            safeTop = (Screen.height - safe.yMax) / Screen.height * canvasSize.y;
            safeBottom = safe.yMin / Screen.height * canvasSize.y;
        }

        float usableTop = canvasSize.y * 0.5f - safeTop;
        float usableBottom = -canvasSize.y * 0.5f + safeBottom;
        float centre = Mathf.Lerp(usableTop, usableBottom, VerticalCentreOfUsable);

        float half = unitHeight * 0.5f;
        centre = Mathf.Clamp(centre, usableBottom + half, usableTop - half);

        rootRect.anchorMin = rootRect.anchorMax = new Vector2(0f, 0.5f);
        rootRect.pivot = new Vector2(0f, 0.5f);
        rootRect.sizeDelta = new Vector2(RootWidth, unitHeight);
        rootRect.anchoredPosition = new Vector2(24f, centre);

        // Local X zero: the track's own left boundary is the screen's left edge,
        // with only the device's safe-area inset between them.
        frameRect.anchorMin = frameRect.anchorMax = new Vector2(0f, 1f);
        frameRect.pivot = new Vector2(0f, 1f);
        frameRect.anchoredPosition = Vector2.zero;
        frameRect.sizeDelta = new Vector2(TrackWidth, trackHeight);

        glowRect.anchorMin = glowRect.anchorMax = new Vector2(0f, 1f);
        glowRect.pivot = new Vector2(0f, 1f);
        glowRect.anchoredPosition = new Vector2(-34f, 34f);
        glowRect.sizeDelta = new Vector2(TrackWidth + 68f, trackHeight + 68f);

        // Left-aligned under the tank. Centring them on a track that starts at the
        // screen edge would push their first characters off it.
        LayoutLabel("FuelLabel", -trackHeight - 34f, 34f);
        LayoutLabel("FuelPercentage", -trackHeight - 82f, 44f);
    }

    private void LayoutLabel(string childName, float y, float height)
    {
        Transform child = transform.Find(childName);
        if (child == null) return;

        RectTransform rect = (RectTransform)child;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(TrackWidth * 0.5f, y);
        rect.sizeDelta = new Vector2(112f, height);
    }

    private void ApplyReadableText()
    {
        ConfigureReadableLabel(fuelLabel, FuelLabelSize, UIDesign.TextSub);
        ConfigureReadableLabel(percentage, PercentageSize, percentage != null ? percentage.color : Color.white);
    }

    private static void ConfigureReadableLabel(TextMeshProUGUI label, float size, Color color)
    {
        if (label == null) return;
        label.enableAutoSizing = false;
        label.fontSize = size;
        label.fontSizeMin = size;
        label.fontSizeMax = size;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
    }

    private static TextMeshProUGUI CreateLabel(Transform parent, string name, string text,
        float size, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        UIStyleKit.ApplyRuntimeFont(label, parent);
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.characterSpacing = UIDesign.TrackCaption;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        return label;
    }

    private static void BuildProceduralRocket(Transform parent)
    {
        AddPart(parent, "RocketBody", UIStyleKit.RoundedRect, Color.white,
            new Vector2(0f, 0f), new Vector2(13f, 28f), 0f);
        AddPart(parent, "RocketNose", UIStyleKit.Circle, Color.white,
            new Vector2(0f, 11f), new Vector2(13f, 13f), 0f);
        AddPart(parent, "RocketFinLeft", UIStyleKit.RoundedRect, UIDesign.Cta,
            new Vector2(-7f, -7f), new Vector2(7f, 15f), -24f);
        AddPart(parent, "RocketFinRight", UIStyleKit.RoundedRect, UIDesign.Cta,
            new Vector2(7f, -7f), new Vector2(7f, 15f), 24f);
        AddPart(parent, "RocketFlame", UIStyleKit.RoundedRect, UIDesign.CtaText,
            new Vector2(0f, -17f), new Vector2(6f, 11f), 0f);
    }

    private static void AddPart(Transform parent, string name, Sprite sprite, Color color,
        Vector2 position, Vector2 size, float rotation)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
        Image image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static Gradient BuildGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.920f, 0.170f, 0.150f), 0.00f),
                new GradientColorKey(new Color(0.980f, 0.280f, 0.130f), 0.10f),
                new GradientColorKey(new Color(1.000f, 0.520f, 0.100f), 0.24f),
                new GradientColorKey(new Color(1.000f, 0.720f, 0.120f), 0.40f),
                new GradientColorKey(new Color(0.990f, 0.870f, 0.160f), 0.52f),
                new GradientColorKey(new Color(0.740f, 0.960f, 0.200f), 0.60f),
                new GradientColorKey(new Color(0.680f, 1.000f, 0.150f), 0.80f),
                new GradientColorKey(new Color(0.722f, 1.000f, 0.102f), 1.00f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return gradient;
    }
}
