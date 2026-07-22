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
    private AsteroidMover trackedAsteroid;
    private Vector3 lastRocketPosition;
    private Vector2 smoothedRocketVelocity;
    private bool hasRocketSample;

    void Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (rocket == null) rocket = FindAnyObjectByType<RocketController>();

        if (arrowRect == null) return;

        arrowImage = arrowRect.GetComponent<Image>();
        arrowRect.sizeDelta = new Vector2(
            Mathf.Max(minimumArrowSize, arrowRect.sizeDelta.x),
            Mathf.Max(minimumArrowSize, arrowRect.sizeDelta.y));

        if (arrowImage != null)
        {
            arrowImage.raycastTarget = false;
            Outline outline = arrowRect.GetComponent<Outline>();
            if (outline == null) outline = arrowRect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.015f, 0.02f, 0.055f, 0.92f);
            outline.effectDistance = new Vector2(3f, -3f);
            outline.useGraphicAlpha = true;
        }

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

        if (arrowImage != null)
        {
            Color warning = Color.Lerp(
                new Color(1f, 0.78f, 0.04f, 0.96f),
                new Color(1f, 0.16f, 0.10f, 0.98f),
                danger);
            arrowImage.color = warning;
        }

        float pulseSpeed = Mathf.Lerp(3.8f, 5.8f, danger);
        float pulseAmount = Mathf.Lerp(0.055f, 0.095f, danger);
        float pulse = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount;
        arrowRect.localScale = Vector3.one * pulse;
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
        float inset = Mathf.Max(edgeMargin, halfArrow + 8f);

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
