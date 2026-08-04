using UnityEngine;
using UnityEngine.UI;

// The Fuel tank's liquid body, drawn as geometry rather than as a filled sprite.
//
// A vertically filled Image cannot do this job: it scales one sprite, so the fill
// takes the sprite's silhouette with it and a rounded tank ends up with a pointed,
// wedge-shaped level. This builds a full-width quad strip instead — the body always
// spans the tank's whole inner width, and only the top row of vertices moves.
//
// The strip is a fixed 25 vertices. Nothing is allocated per frame: the vertex
// helper is the one the Canvas already owns, and the wave is two sine terms.
// RequireComponent is not inherited from Graphic, so a custom graphic has to ask
// for its own CanvasRenderer. Without one it draws nothing and takes the canvas
// batch down with it.
[AddComponentMenu("")]
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
[RequireComponent(typeof(RectTransform))]
public sealed class FuelLiquidGraphic : MaskableGraphic
{
    // Two soft cycles read as liquid; more reads as water in a glass being shaken.
    // Both are whole numbers of cycles, so the surface displaces exactly as much
    // liquid as it displaces back and the drawn volume matches the real fill.
    private const int Columns = 24;
    private const float PrimaryCycles = 1f;
    private const float SecondaryCycles = 2f;
    private const float SecondaryShare = 0.35f;
    private const float Tau = Mathf.PI * 2f;

    // 45Hz is under the frame rate and above the point where the crest visibly
    // steps. The gauge is small and this is the only thing on its canvas, so the
    // rebuild costs a strip of 48 triangles and nothing else.
    private const float RebuildInterval = 1f / 45f;

    [SerializeField, Range(0f, 1f)] private float fill = 1f;
    [SerializeField] private float cornerRadius = 22f;
    [SerializeField] private float amplitude = 7f;
    [SerializeField] private float primarySpeed = 0.42f;
    [SerializeField] private float secondarySpeed = 0.27f;
    [SerializeField] private Color surfaceColor = Color.white;
    [SerializeField] private Color depthColor = Color.white;

    private float primaryPhase;
    private float secondaryPhase;
    private float rebuildTimer;

    public float Fill
    {
        get => fill;
        set
        {
            float clamped = Mathf.Clamp01(value);
            if (Mathf.Approximately(clamped, fill)) return;
            fill = clamped;
            SetVerticesDirty();
        }
    }

    /// The tank's inner corner radius. The liquid carries the tank's rounded shape
    /// in its own geometry rather than being stencil-masked into it: a UI Mask does
    /// not survive the nested Canvas this gauge uses to keep the wave's rebuilds off
    /// the main HUD canvas, and clipping to a rect would square the tank's corners.
    public float CornerRadius
    {
        get => cornerRadius;
        set
        {
            if (Mathf.Approximately(value, cornerRadius)) return;
            cornerRadius = Mathf.Max(0f, value);
            SetVerticesDirty();
        }
    }

    public void SetColors(Color surface, Color depth)
    {
        if (surface == surfaceColor && depth == depthColor) return;
        surfaceColor = surface;
        depthColor = depth;
        SetVerticesDirty();
        SetMaterialDirty();
    }

    public void SetWavePhase(float primary, float secondary)
    {
        primaryPhase = Mathf.Repeat(primary, Tau);
        secondaryPhase = Mathf.Repeat(secondary, Tau);
        SetVerticesDirty();
    }

    /// The liquid's top edge at a horizontal position across the tank, in this
    /// graphic's local space. The rocket marker rides this, so the marker and the
    /// liquid can never disagree about where the surface is.
    public float SurfaceLocalY(float normalizedX)
    {
        Rect rect = GetPixelAdjustedRect();
        float height = rect.height * Mathf.Clamp01(fill);
        float amp = WaveAmplitude(rect, height);
        return SurfaceBaseY(rect, height, amp)
             + WaveOffset(Mathf.Clamp01(normalizedX), amp);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        SetVerticesDirty();
        SetMaterialDirty();
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        fill = Mathf.Clamp01(fill);
        cornerRadius = Mathf.Max(0f, cornerRadius);
        amplitude = Mathf.Max(0f, amplitude);
        SetVerticesDirty();
        SetMaterialDirty();
    }
#endif

    private void Update()
    {
        if (!Application.isPlaying) return;

        // A tank with no liquid has no surface to animate, and a disabled gauge is
        // not on screen. Either way the strip stops being rebuilt.
        if (fill <= 0.0005f) return;

        primaryPhase = Mathf.Repeat(primaryPhase + Time.unscaledDeltaTime * primarySpeed * Tau, Tau);
        secondaryPhase = Mathf.Repeat(secondaryPhase + Time.unscaledDeltaTime * secondarySpeed * Tau, Tau);

        rebuildTimer += Time.unscaledDeltaTime;
        if (rebuildTimer < RebuildInterval) return;
        rebuildTimer = 0f;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = GetPixelAdjustedRect();
        float height = rect.height * Mathf.Clamp01(fill);
        if (height <= 0.01f || rect.width <= 0f) return;

        float amp = WaveAmplitude(rect, height);
        float surfaceY = SurfaceBaseY(rect, height, amp);
        Color32 surface = surfaceColor;
        Color32 depth = depthColor;

        for (int i = 0; i <= Columns; i++)
        {
            float t = i / (float)Columns;
            float x = rect.xMin + rect.width * t;

            // Each column is cut to the tank's rounded silhouette at its own x, so
            // the body fills the full inner width where the tank is straight and
            // tucks in exactly where the tank curves.
            float halfExtent = RoundedHalfHeight(rect, x);
            float centreY = rect.center.y;
            float bottom = centreY - halfExtent;
            float top = Mathf.Min(surfaceY + WaveOffset(t, amp), centreY + halfExtent);
            if (top < bottom) top = bottom;

            vh.AddVert(new Vector3(x, bottom), depth, new Vector2(t, 0f));
            vh.AddVert(new Vector3(x, top), surface, new Vector2(t, 1f));
        }

        for (int i = 0; i < Columns; i++)
        {
            int left = i * 2;
            vh.AddTriangle(left, left + 1, left + 3);
            vh.AddTriangle(left, left + 3, left + 2);
        }
    }

    /// Half the rounded rectangle's height at a horizontal position: the full half
    /// height along the straight sides, falling off on the corner arc.
    private float RoundedHalfHeight(Rect rect, float x)
    {
        float halfWidth = rect.width * 0.5f;
        float halfHeight = rect.height * 0.5f;
        float radius = Mathf.Min(cornerRadius, Mathf.Min(halfWidth, halfHeight));
        if (radius <= 0f) return halfHeight;

        float straight = halfWidth - radius;
        float dx = Mathf.Abs(x - rect.center.x);
        if (dx <= straight) return halfHeight;

        float into = Mathf.Min(dx - straight, radius);
        return halfHeight - radius + Mathf.Sqrt(Mathf.Max(0f, radius * radius - into * into));
    }

    private float WaveOffset(float t, float amp)
    {
        if (amp <= 0f) return 0f;
        return Mathf.Sin(t * PrimaryCycles * Tau + primaryPhase) * amp
             + Mathf.Sin(t * SecondaryCycles * Tau - secondaryPhase) * amp * SecondaryShare;
    }

    // The crest may never leave the tank. Near the top, the whole wave is shifted
    // inward instead of flattened, so a semantically full tank still has a visible
    // liquid surface. Near empty, its amplitude naturally shrinks with the liquid.
    private float WaveAmplitude(Rect rect, float height)
    {
        float extent = Mathf.Min(amplitude, Mathf.Max(0f, height));
        return extent / (1f + SecondaryShare);
    }

    private float SurfaceBaseY(Rect rect, float height, float amp)
    {
        float waveExtent = amp * (1f + SecondaryShare);
        float headroom = Mathf.Max(0f, rect.height - height);
        return rect.yMin + height - Mathf.Max(0f, waveExtent - headroom);
    }
}
