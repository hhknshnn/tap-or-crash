using UnityEngine;

/// <summary>
/// Shared, safe-area-aware presentation measurements for the live gameplay HUD
/// and camera. All values are Canvas reference pixels unless stated otherwise.
/// </summary>
public static class GameplayPresentationLayout
{
    public enum Lane
    {
        OrbitScore,
        WorldProgress,
        MechanicNotice,
        PerfectFeedback,
    }

    public const float ScoreTop = 36f;
    public const float ScoreHeight = 104f;
    public const float WorldProgressTop = 152f;
    public const float WorldProgressHeight = 96f;
    public const float MechanicNoticeTop = 266f;
    public const float MechanicNoticeHeight = 112f;
    public const float PerfectFeedbackTop = 396f;
    public const float PerfectFeedbackHeight = 80f;

    public static float SafeTopInset(RectTransform canvas)
    {
        if (canvas == null || Screen.height <= 0) return 0f;
        return (Screen.height - Screen.safeArea.yMax) / Screen.height * canvas.rect.height;
    }

    public static void PlaceTopCentre(RectTransform rect, RectTransform canvas, Lane lane)
    {
        if (rect == null) return;

        float top;
        float height;
        switch (lane)
        {
            case Lane.OrbitScore:
                top = ScoreTop;
                height = ScoreHeight;
                rect.pivot = new Vector2(0.5f, 0.5f);
                break;
            case Lane.WorldProgress:
                top = WorldProgressTop;
                height = WorldProgressHeight;
                rect.pivot = new Vector2(0.5f, 1f);
                break;
            case Lane.MechanicNotice:
                top = MechanicNoticeTop;
                height = MechanicNoticeHeight;
                rect.pivot = new Vector2(0.5f, 1f);
                break;
            default:
                top = PerfectFeedbackTop;
                height = PerfectFeedbackHeight;
                rect.pivot = new Vector2(0.5f, 1f);
                break;
        }

        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        float safeTop = SafeTopInset(canvas);
        rect.anchoredPosition = rect.pivot.y > 0.75f
            ? new Vector2(0f, -safeTop - top)
            : new Vector2(0f, -safeTop - top - height * 0.5f);
    }

    /// <summary>
    /// Usable gameplay viewport after safe area, the top presentation stack,
    /// bottom controls, the left Fuel gauge, and a 4% rocket margin are reserved.
    /// </summary>
    public static Rect SafeGameplayViewport()
    {
        Rect safe = Screen.width > 0 && Screen.height > 0
            ? new Rect(
                Screen.safeArea.xMin / Screen.width,
                Screen.safeArea.yMin / Screen.height,
                Screen.safeArea.width / Screen.width,
                Screen.safeArea.height / Screen.height)
            : new Rect(0f, 0f, 1f, 1f);

        float referenceHeight = Screen.width > 0
            ? Screen.height * (1080f / Screen.width)
            : 1920f;
        float topStack = Screen.height > 0
            ? SafeTopInsetPixels() / Screen.height
                + (PerfectFeedbackTop + PerfectFeedbackHeight + 18f) / referenceHeight
            : 0.25f;

        float minX = Mathf.Max(safe.xMin + 0.04f, 0.14f);
        float maxX = Mathf.Min(safe.xMax - 0.04f, 0.96f);
        float minY = Mathf.Max(safe.yMin + 0.04f, 0.12f);
        float maxY = Mathf.Min(safe.yMax - 0.04f, 1f - topStack);
        if (maxX <= minX) { minX = safe.xMin; maxX = safe.xMax; }
        if (maxY <= minY) { minY = safe.yMin; maxY = safe.yMax; }
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private static float SafeTopInsetPixels()
    {
        return Screen.height > 0 ? Screen.height - Screen.safeArea.yMax : 0f;
    }

    public static void ClampHorizontalEnvelope(
        Transform target,
        Camera camera,
        float halfEnvelopeWidth)
    {
        if (target == null || camera == null || !camera.orthographic) return;

        Rect safe = SafeGameplayViewport();
        float worldWidth = camera.orthographicSize * camera.aspect * 2f;
        float min = camera.transform.position.x + (safe.xMin - 0.5f) * worldWidth + halfEnvelopeWidth;
        float max = camera.transform.position.x + (safe.xMax - 0.5f) * worldWidth - halfEnvelopeWidth;
        Vector3 position = target.position;
        position.x = min <= max ? Mathf.Clamp(position.x, min, max) : camera.transform.position.x;
        target.position = position;
    }
}

/// <summary>Single source of truth for the ring/launch-guide assistance progression.</summary>
public static class OrbitAssistanceProgression
{
    public const int FullAssistanceLastPlanet = 10;
    public const int LastAssistedPlanet = 20;
    public static int AssistanceEndingScore => LastAssistedPlanet;

    public static bool IsAvailableForScore(int score) => score + 1 <= LastAssistedPlanet;
    public static bool IsFullForPlanet(int planetNumber) => planetNumber <= FullAssistanceLastPlanet;
}

/// <summary>Single source of truth for moving-orbit activation and ramp.</summary>
public static class MovingOrbitProgression
{
    public const int ActivationPlanet = 31;
    public static int ActivationScore => ActivationPlanet - 1;
    public static bool IsActiveForScore(int score) => score >= ActivationScore;

    public static int TierForPlanet(int planetNumber)
    {
        return planetNumber < ActivationPlanet ? -1 : (planetNumber - ActivationPlanet) / 10;
    }

    public static void Evaluate(int planetNumber, Camera camera,
        out float horizontal, out float vertical, out float period)
    {
        int tier = Mathf.Max(0, TierForPlanet(planetNumber));
        float viewportWidth = camera != null && camera.orthographic
            ? camera.orthographicSize * camera.aspect * 2f
            : 8.5f;
        float fraction = Mathf.Min(0.05f, 0.015f + tier * 0.005f);
        horizontal = viewportWidth * fraction;
        vertical = horizontal * 0.65f;
        period = Mathf.Max(3.5f, 6f - tier * 0.35f);
    }
}
