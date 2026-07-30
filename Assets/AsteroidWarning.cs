using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class AsteroidWarning : MonoBehaviour
{
    [Header("Referanslar")]
    public RectTransform arrowRect;
    public Camera mainCamera;
    public RocketController rocket;

    [Header("Ayarlar")]
    public float edgeMargin = 80f;
    public float nearDistance = 8f;
    [SerializeField] private float minimumArrowSize = 88f;
    [SerializeField] private float maximumWarningTime = 4.2f;
    [SerializeField] private float warningTargetRadius = 0.65f;
    [SerializeField] private float threatSwitchAdvantage = 0.18f;

    private Image arrowImage;
    private Image glowImage;
    private Image outerRingImage;
    private Image energySweepImage;
    private Image pointerImage;
    private RectTransform glowRect;
    private RectTransform outerRingRect;
    private RectTransform energySweepRect;
    private AsteroidMover trackedAsteroid;
    private Vector3 lastRocketPosition;
    private Vector2 smoothedRocketVelocity;
    private bool hasRocketSample;
    private static Sprite warningRingSprite;
    private static Sprite warningPointerSprite;

    void Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (rocket == null) rocket = FindAnyObjectByType<RocketController>();

        if (arrowRect == null) return;

        arrowImage = arrowRect.GetComponent<Image>();
        // The root bounds include the glow, not only the pointer. Edge placement
        // can therefore keep every animated layer inside narrow mobile screens.
        float premiumSize = Mathf.Max(174f, minimumArrowSize);
        arrowRect.sizeDelta = new Vector2(premiumSize, premiumSize);

        if (arrowImage != null)
        {
            arrowImage.raycastTarget = false;
            arrowImage.color = Color.clear;
        }

        BuildPremiumIndicator();
        HideArrow();
    }

    void Update()
    {
        if (arrowRect == null || mainCamera == null || rocket == null)
        {
            HideArrow();
            return;
        }

        UpdateRocketVelocity();

        if (!GameManager.isGameStarted || GameManager.isGameOver)
        {
            HideArrow();
            return;
        }

        AsteroidMover best = FindBestThreat(out Vector3 bestScreenPosition, out float bestScore, out float bestDistance);
        if (best == null)
        {
            HideArrow();
            return;
        }

        // Keep the current warning stable unless another asteroid is materially more urgent.
        if (trackedAsteroid != null && trackedAsteroid != best
            && TryGetThreatMetrics(trackedAsteroid, out Vector3 currentScreen, out float currentScore, out float currentDistance)
            && currentScore <= bestScore + threatSwitchAdvantage)
        {
            best = trackedAsteroid;
            bestScreenPosition = currentScreen;
            bestScore = currentScore;
            bestDistance = currentDistance;
        }

        trackedAsteroid = best;
        arrowRect.gameObject.SetActive(true);
        PlaceAtSafeAreaEdge(bestScreenPosition);
        PointAlongScreenVelocity(best);

        float distanceDanger = 1f - Mathf.InverseLerp(nearDistance * 0.28f, nearDistance, bestDistance);
        float timeDanger = 1f - Mathf.InverseLerp(0.45f, maximumWarningTime, bestScore);
        float danger = Mathf.Clamp01(Mathf.Max(distanceDanger, timeDanger));

        Color warning = Color.Lerp(
            new Color(1f, 0.78f, 0.04f, 0.98f),
            new Color(1f, 0.12f, 0.055f, 1f),
            danger);
        if (pointerImage != null) pointerImage.color = warning;
        if (outerRingImage != null)
            outerRingImage.color = new Color(warning.r, warning.g, warning.b, Mathf.Lerp(0.72f, 0.96f, danger));
        if (energySweepImage != null)
            energySweepImage.color = new Color(1f, Mathf.Lerp(0.78f, 0.36f, danger), 0.18f, 0.86f);
        if (glowImage != null)
            glowImage.color = new Color(warning.r, warning.g * 0.65f, warning.b * 0.45f,
                Mathf.Lerp(0.18f, 0.34f, danger));

        float pulseSpeed = Mathf.Lerp(3.8f, 5.8f, danger);
        float pulseAmount = Mathf.Lerp(0.055f, 0.095f, danger);
        float pulse = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount;
        arrowRect.localScale = Vector3.one * pulse;

        float rotationSpeed = Mathf.Lerp(42f, 118f, danger);
        if (outerRingRect != null)
            outerRingRect.localRotation = Quaternion.Euler(0f, 0f, Time.unscaledTime * rotationSpeed);
        if (energySweepRect != null)
            energySweepRect.localRotation = Quaternion.Euler(0f, 0f, -Time.unscaledTime * rotationSpeed * 1.65f);
        if (glowRect != null)
        {
            float glowPulse = 1f + Mathf.Sin(Time.unscaledTime * (pulseSpeed * 0.78f) + 0.7f)
                * Mathf.Lerp(0.07f, 0.15f, danger);
            glowRect.localScale = Vector3.one * glowPulse;
        }
    }

    AsteroidMover FindBestThreat(
        out Vector3 bestScreenPosition,
        out float bestScore,
        out float bestDistance)
    {
        AsteroidMover best = null;
        bestScreenPosition = Vector3.zero;
        bestScore = float.PositiveInfinity;
        bestDistance = float.PositiveInfinity;

        IReadOnlyList<AsteroidMover> asteroids = AsteroidMover.ActiveAsteroids;
        for (int i = 0; i < asteroids.Count; i++)
        {
            AsteroidMover asteroid = asteroids[i];
            if (!TryGetThreatMetrics(asteroid, out Vector3 screenPosition, out float score, out float distance))
                continue;

            bool isBetter = score < bestScore - 0.001f;
            bool isDeterministicTie = Mathf.Abs(score - bestScore) <= 0.001f
                && (best == null || asteroid.SpawnOrder < best.SpawnOrder);
            if (!isBetter && !isDeterministicTie) continue;

            best = asteroid;
            bestScreenPosition = screenPosition;
            bestScore = score;
            bestDistance = distance;
        }

        return best;
    }

    bool TryGetThreatMetrics(
        AsteroidMover asteroid,
        out Vector3 screenPosition,
        out float threatScore,
        out float distance)
    {
        screenPosition = Vector3.zero;
        threatScore = float.PositiveInfinity;
        distance = float.PositiveInfinity;
        if (asteroid == null || !asteroid.IsActiveThreat) return false;

        screenPosition = mainCamera.WorldToScreenPoint(asteroid.transform.position);
        bool offScreen = screenPosition.z > 0f
            && (screenPosition.x < 0f || screenPosition.x > Screen.width
                || screenPosition.y < 0f || screenPosition.y > Screen.height);
        if (!offScreen) return false;

        threatScore = asteroid.EstimateThreatTime(
            rocket.transform.position,
            smoothedRocketVelocity,
            warningTargetRadius,
            out float closestDistance);
        if (float.IsInfinity(threatScore) || threatScore > maximumWarningTime) return false;

        distance = Vector2.Distance(rocket.transform.position, asteroid.transform.position);
        // Put true impact trajectories in a strict priority band ahead of near misses.
        // This prevents an early wide miss from stealing the single warning arrow from
        // a slightly later asteroid that is actually on course to hit the rocket.
        bool impactPath = closestDistance <= warningTargetRadius + asteroid.CollisionRadius;
        if (!impactPath) threatScore += maximumWarningTime;
        threatScore += Mathf.Max(0f, closestDistance - warningTargetRadius) * 0.12f;
        return true;
    }

    void PlaceAtSafeAreaEdge(Vector3 offscreenPosition)
    {
        Rect safeArea = Screen.safeArea;
        Canvas canvas = arrowRect.GetComponentInParent<Canvas>();
        float canvasScale = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
        float halfArrow = Mathf.Max(arrowRect.rect.width, arrowRect.rect.height) * canvasScale * 0.5f;
        float inset = Mathf.Max(edgeMargin, halfArrow + 24f);

        float minX = safeArea.xMin + inset;
        float maxX = safeArea.xMax - inset;
        float minY = safeArea.yMin + inset;
        float maxY = safeArea.yMax - inset;
        if (maxX < minX) minX = maxX = safeArea.center.x;
        if (maxY < minY) minY = maxY = safeArea.center.y;

        Vector2 center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        Vector2 directionToThreat = (Vector2)offscreenPosition - center;
        if (directionToThreat.sqrMagnitude < 0.001f) directionToThreat = Vector2.up;

        float halfWidth = Mathf.Max(0f, (maxX - minX) * 0.5f);
        float halfHeight = Mathf.Max(0f, (maxY - minY) * 0.5f);
        float xScale = Mathf.Abs(directionToThreat.x) > 0.001f
            ? halfWidth / Mathf.Abs(directionToThreat.x)
            : float.PositiveInfinity;
        float yScale = Mathf.Abs(directionToThreat.y) > 0.001f
            ? halfHeight / Mathf.Abs(directionToThreat.y)
            : float.PositiveInfinity;
        float edgeScale = Mathf.Min(xScale, yScale);
        if (float.IsInfinity(edgeScale)) edgeScale = 0f;

        Vector2 edgePosition = center + directionToThreat * edgeScale;
        arrowRect.position = new Vector3(edgePosition.x, edgePosition.y, 0f);
    }

    void PointAlongScreenVelocity(AsteroidMover asteroid)
    {
        Vector3 current = mainCamera.WorldToScreenPoint(asteroid.transform.position);
        Vector3 future = mainCamera.WorldToScreenPoint(
            asteroid.transform.position + (Vector3)(asteroid.WorldVelocity * 0.2f));
        Vector2 screenVelocity = (Vector2)(future - current);
        if (screenVelocity.sqrMagnitude < 0.001f)
            screenVelocity = (Vector2)mainCamera.WorldToScreenPoint(rocket.transform.position) - (Vector2)current;

        // arrow.png naturally points down, so downward motion maps to zero rotation.
        float angle = Mathf.Atan2(screenVelocity.y, screenVelocity.x) * Mathf.Rad2Deg + 90f;
        arrowRect.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    void BuildPremiumIndicator()
    {
        if (arrowRect == null) return;

        glowImage = EnsureVisual("WarningGlow", UIGlass.Glow, 174f,
            new Color(1f, 0.15f, 0.04f, 0.24f));
        glowRect = glowImage != null ? glowImage.rectTransform : null;

        outerRingImage = EnsureVisual("WarningOuterRing", GetWarningRingSprite(), 132f,
            new Color(1f, 0.26f, 0.06f, 0.88f));
        outerRingRect = outerRingImage != null ? outerRingImage.rectTransform : null;

        EnsureVisual("WarningPlate", UIStyleKit.Circle, 82f,
            new Color(0.025f, 0.03f, 0.075f, 0.92f));

        Image shadow = EnsureVisual("WarningPointerShadow", GetWarningPointerSprite(), 76f,
            new Color(0.005f, 0.008f, 0.025f, 0.88f));
        if (shadow != null) shadow.rectTransform.anchoredPosition = new Vector2(3f, -4f);

        pointerImage = EnsureVisual("WarningPointer", GetWarningPointerSprite(), 76f,
            new Color(1f, 0.20f, 0.06f, 1f));

        energySweepImage = EnsureVisual("WarningEnergySweep", GetWarningRingSprite(), 104f,
            new Color(1f, 0.66f, 0.14f, 0.86f));
        energySweepRect = energySweepImage != null ? energySweepImage.rectTransform : null;
    }

    Image EnsureVisual(string visualName, Sprite sprite, float size, Color color)
    {
        Transform existing = arrowRect.Find(visualName);
        GameObject visualObject;
        if (existing != null)
            visualObject = existing.gameObject;
        else
        {
            visualObject = new GameObject(visualName);
            visualObject.transform.SetParent(arrowRect, false);
        }

        RectTransform rect = visualObject.GetComponent<RectTransform>();
        if (rect == null) rect = visualObject.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(size, size);

        Image image = visualObject.GetComponent<Image>();
        if (image == null) image = visualObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    static Sprite GetWarningRingSprite()
    {
        if (warningRingSprite != null) return warningRingSprite;

        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "Runtime Premium Warning Ring";
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.hideFlags = HideFlags.HideAndDontSave;
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = ((x + 0.5f) / size) * 2f - 1f;
                float ny = ((y + 0.5f) / size) * 2f - 1f;
                float radius = Mathf.Sqrt(nx * nx + ny * ny);
                float angle = Mathf.Atan2(ny, nx) * Mathf.Rad2Deg;
                if (angle < 0f) angle += 360f;

                bool ring = radius >= 0.72f && radius <= 0.90f;
                bool segment = Mathf.Repeat(angle + 4f, 36f) < 25f;
                float edge = Mathf.Clamp01(Mathf.Min((radius - 0.70f) / 0.035f, (0.92f - radius) / 0.035f));
                float alpha = ring && segment ? edge : 0f;
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        warningRingSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f);
        warningRingSprite.name = "Runtime Premium Warning Ring";
        warningRingSprite.hideFlags = HideFlags.HideAndDontSave;
        return warningRingSprite;
    }

    static Sprite GetWarningPointerSprite()
    {
        if (warningPointerSprite != null) return warningPointerSprite;

        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "Runtime Premium Warning Pointer";
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.hideFlags = HideFlags.HideAndDontSave;
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = ((x + 0.5f) / size) * 2f - 1f;
                float ny = ((y + 0.5f) / size) * 2f - 1f;
                bool stem = ny >= 0.02f && ny <= 0.70f && Mathf.Abs(nx) <= 0.19f;
                float triangleWidth = Mathf.InverseLerp(-0.86f, 0.16f, ny) * 0.67f;
                bool head = ny >= -0.86f && ny <= 0.16f && Mathf.Abs(nx) <= triangleWidth;
                bool notch = ny > 0.04f && ny < 0.23f && Mathf.Abs(nx) > 0.19f;
                float alpha = (stem || (head && !notch)) ? 1f : 0f;
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        warningPointerSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f);
        warningPointerSprite.name = "Runtime Premium Warning Pointer";
        warningPointerSprite.hideFlags = HideFlags.HideAndDontSave;
        return warningPointerSprite;
    }

    void UpdateRocketVelocity()
    {
        Vector3 current = rocket.transform.position;
        float delta = Time.unscaledDeltaTime;
        if (!hasRocketSample || delta <= 0f || delta > 0.2f)
        {
            lastRocketPosition = current;
            smoothedRocketVelocity = Vector2.zero;
            hasRocketSample = true;
            return;
        }

        Vector2 measured = (current - lastRocketPosition) / delta;
        float blend = 1f - Mathf.Exp(-10f * delta);
        smoothedRocketVelocity = Vector2.Lerp(smoothedRocketVelocity, measured, blend);
        lastRocketPosition = current;
    }

    void HideArrow()
    {
        trackedAsteroid = null;
        if (arrowRect == null) return;
        arrowRect.localScale = Vector3.one;
        arrowRect.gameObject.SetActive(false);
    }
}
