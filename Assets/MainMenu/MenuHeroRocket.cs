using UnityEngine;

// Flies the player's actual rocket around the hero planet on the start screen.
//
// This does not build a stand-in. It takes the live Rocket — the one with
// RocketController on it — and drives its transform while gameplay is asleep. The ship
// the player watches orbiting the menu is therefore the exact object that launches when
// they tap: same model, same materials, same skin tint, same LowPolyRocketFlame idling
// at its menu intensity, same RocketModelVisual hover. Nothing is cloned, hidden or
// swapped, so there is no moment where one rocket has to become another.
//
// The orbit is a flattened, tilted ellipse rather than a circle: the ship passes behind
// the planet at the top of the path and in front of it at the bottom, which reads as a
// real orbit around a sphere instead of a sprite sliding around a disc. Everything is
// deliberately slow — one lap takes about half a minute — so the frame feels calm.
//
// When the player starts the game the ellipse converges onto the circle RocketController
// expects (SetHandOverProgress), so control can be handed over mid-orbit without the
// ship moving a pixel on the frame it changes owner.
[DisallowMultipleComponent]
public sealed class MenuHeroRocket : MonoBehaviour
{
    const float OrbitSpeed = 0.235f;           // radians per second (~27 s per lap)
    const float OrbitTiltDegrees = -12f;
    // Wide enough to clear the planet at the sides, tight enough that the ship never
    // touches the edge of the frame on a narrow phone.
    const float RadiusXRatio = 1.58f;          // of the planet radius
    const float RadiusYRatio = 0.64f;

    Transform planet;
    Transform ship;

    Renderer[] shipRenderers;
    int[] shipBaseSortingOrders;
    Vector3 shipBaseScale;

    float radiusX;
    float radiusY;
    float angle;
    float phase;

    // Micro course corrections: the ship drifts a few percent off its ideal path and
    // eases back, so the loop never looks mathematically perfect.
    float radiusDrift = 1f;
    float radiusDriftTarget = 1f;
    float bankDrift;
    float bankDriftTarget;
    float nextCorrection;

    int frontSortingOffset;
    int backSortingOffset;
    int currentSortingOffset = int.MinValue;

    // Hand-over: 0 = menu ellipse, 1 = the exact circle RocketController orbits on.
    float handOverProgress;
    float gameplayOrbitRadius;
    bool released;

    public float AngleDegrees => angle * Mathf.Rad2Deg;

    // The menu always turns the same way RocketController's direction +1 turns, so the
    // hand-over never has to reverse the ship.
    public int OrbitDirection => 1;

    public bool Build(Transform heroPlanet, float planetRadius, Transform liveRocket,
        int sortingOffsetFront, int sortingOffsetBack)
    {
        if (heroPlanet == null || liveRocket == null) return false;

        planet = heroPlanet;
        ship = liveRocket;
        radiusX = planetRadius * RadiusXRatio;
        radiusY = planetRadius * RadiusYRatio;
        gameplayOrbitRadius = radiusX;
        frontSortingOffset = sortingOffsetFront;
        backSortingOffset = sortingOffsetBack;

        shipBaseScale = ship.localScale;

        // Every renderer the ship owns — mesh, flame layers, thruster particles, trail —
        // moves in front of or behind the planet as one, keeping their relative order.
        shipRenderers = ship.GetComponentsInChildren<Renderer>(true);
        shipBaseSortingOrders = new int[shipRenderers.Length];
        for (int i = 0; i < shipRenderers.Length; i++)
            shipBaseSortingOrders[i] = shipRenderers[i].sortingOrder;

        angle = Random.Range(0f, Mathf.PI * 2f);
        phase = Random.Range(0f, 10f);
        nextCorrection = Time.unscaledTime + Random.Range(4f, 8f);
        Animate(0f);
        return true;
    }

    // The circle the ship has to be standing on when RocketController takes over.
    public void BeginHandOver(float orbitRadius)
    {
        gameplayOrbitRadius = Mathf.Max(0.01f, orbitRadius);
    }

    public void SetHandOverProgress(float progress)
    {
        handOverProgress = Mathf.Clamp01(progress);
    }

    // Gives the ship back exactly as it was found. Called once control passes to
    // RocketController, and again if the menu is torn down without ever starting.
    public void Release()
    {
        if (released) return;
        released = true;

        if (ship != null) ship.localScale = shipBaseScale;
        if (shipRenderers == null) return;

        for (int i = 0; i < shipRenderers.Length; i++)
            if (shipRenderers[i] != null) shipRenderers[i].sortingOrder = shipBaseSortingOrders[i];
    }

    void OnDestroy() => Release();

    void Update()
    {
        if (released) return;

        float delta = Time.unscaledDeltaTime;
        angle += OrbitSpeed * delta;
        Animate(delta);
    }

    void Animate(float delta)
    {
        if (planet == null || ship == null) return;

        float time = Time.unscaledTime;
        UpdateCourseCorrection(time, delta);

        // Everything the menu adds on top of a plain circular orbit fades out together,
        // so the last frame of the transition is a pose RocketController would produce.
        float blend = Mathf.SmoothStep(0f, 1f, handOverProgress);
        float menuRadiusX = radiusX * radiusDrift;
        float menuRadiusY = radiusY * radiusDrift;
        float currentRadiusX = Mathf.Lerp(menuRadiusX, gameplayOrbitRadius, blend);
        float currentRadiusY = Mathf.Lerp(menuRadiusY, gameplayOrbitRadius, blend);

        float sin = Mathf.Sin(angle);
        float cos = Mathf.Cos(angle);
        float tilt = Mathf.Lerp(OrbitTiltDegrees, 0f, blend) * Mathf.Deg2Rad;
        float tiltSin = Mathf.Sin(tilt);
        float tiltCos = Mathf.Cos(tilt);

        // Position on the tilted ellipse, plus a slight breathing of the orbit radius.
        float localX = cos * currentRadiusX;
        float localY = sin * currentRadiusY;
        Vector3 offset = new Vector3(
            localX * tiltCos - localY * tiltSin,
            localX * tiltSin + localY * tiltCos,
            0f);

        ship.position = planet.position + offset;

        // Facing follows the tangent of the same ellipse, so the nose always leads. At
        // full hand-over the ellipse is a circle and this resolves to exactly the
        // rotation RocketController computes for the same angle.
        float tangentX = -sin * currentRadiusX;
        float tangentY = cos * currentRadiusY;
        float heading = Mathf.Atan2(
            tangentX * tiltSin + tangentY * tiltCos,
            tangentX * tiltCos - tangentY * tiltSin) * Mathf.Rad2Deg;

        float idleBank = Mathf.Sin(time * 0.9f + phase) * 3.5f * (1f - blend);
        ship.rotation = Quaternion.Euler(0f, 0f, heading + idleBank + bankDrift * (1f - blend));

        // Top of the ellipse is the far side: smaller, and drawn behind the planet.
        float depth = -sin;                       // +1 in front, -1 behind
        ship.localScale = shipBaseScale * (1f + depth * 0.09f * (1f - blend));

        ApplySorting(depth >= 0f ? frontSortingOffset : backSortingOffset);
    }

    void ApplySorting(int offset)
    {
        if (currentSortingOffset == offset || shipRenderers == null) return;
        currentSortingOffset = offset;

        for (int i = 0; i < shipRenderers.Length; i++)
            if (shipRenderers[i] != null)
                shipRenderers[i].sortingOrder = shipBaseSortingOrders[i] + offset;
    }

    void UpdateCourseCorrection(float time, float delta)
    {
        if (time >= nextCorrection)
        {
            nextCorrection = time + Random.Range(6f, 11f);
            radiusDriftTarget = Random.Range(0.968f, 1.032f);
            bankDriftTarget = Random.Range(-4.5f, 4.5f);
        }

        radiusDrift = Mathf.MoveTowards(radiusDrift, radiusDriftTarget, delta * 0.05f);
        bankDrift = Mathf.MoveTowards(bankDrift, bankDriftTarget, delta * 6f);
    }
}
