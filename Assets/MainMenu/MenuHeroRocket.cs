using UnityEngine;
using UnityEngine.Rendering.Universal;

// Flies the player's actual rocket around the hero planet on the start screen.
//
// This does not build a stand-in. It takes the live Rocket — the one with
// RocketController on it — and drives its transform while gameplay is asleep. The ship
// the player watches orbiting the menu is therefore the exact object that launches when
// they tap: same model, same materials, same skin tint, same LowPolyRocketFlame idling
// at its menu intensity, same RocketModelVisual hover. Nothing is cloned, hidden or
// swapped, so there is no moment where one rocket has to become another.
//
// The orbit is a tilted ellipse drawn entirely outside the planet's silhouette: the ship
// never crosses the face of the world and never slips behind it, so it is readable for
// every frame of the lap. What sells it as an orbit rather than a ring is the pacing, the
// depth scaling and the bank, not an overlap. Everything is deliberately slow — one lap
// takes about a quarter of a minute — so the frame feels calm.
//
// When the player starts the game the ellipse converges onto the circle RocketController
// expects (SetHandOverProgress), so control can be handed over mid-orbit without the
// ship moving a pixel on the frame it changes owner.
[DisallowMultipleComponent]
public sealed class MenuHeroRocket : MonoBehaviour
{
    const float OrbitSpeed = 0.235f;           // radians per second (~27 s per lap)
    const float OrbitTiltDegrees = -12f;
    // Wide enough to carry the ship clear of the planet at the sides, tight enough that it
    // never touches the edge of the frame on a narrow phone. maxRadiusX still caps the
    // horizontal reach, so on a portrait screen the ellipse settles at whatever the frame
    // allows rather than at this ratio.
    const float RadiusXRatio = 1.90f;          // of the planet radius
    // Tall enough to carry the ship over the top and under the bottom of the planet with
    // air to spare rather than across its face. orbitFloor is what actually guarantees
    // that; this ratio is the composition it settles at when the frame allows it.
    const float RadiusYRatio = 1.42f;

    // The ship flies the whole lap clear of the planet, so no point on the ellipse may be
    // closer to the centre than the planet plus the ship's own beam plus a visible gap.
    // This is a floor under the two ratios above, not a replacement for them: it only
    // takes over where a narrow frame would otherwise pull the orbit into the world it is
    // supposed to be flying around.
    // Sized against the other edge the ship has to respect: on a portrait screen the orbit
    // is pressed between the planet and the side of the frame, and a wider gap here is
    // paid for out of the margin at the frame edge.
    const float ClearanceGapRatio = 0.05f;     // of the planet radius

    // The far half of the lap is flown faster than the near half. It is the half where
    // the ship is smallest; uneven pacing spends the menu's time on the half the player is
    // meant to be looking at. It costs nothing else — the hand-over reads the ship's pose,
    // never its angular speed — and eases back to one speed as gameplay takes over.
    const float FarSideSpeedUp = 2.6f;

    // How much smaller the ship is drawn at the far side of the lap than at the near side.
    const float DepthScaleRatio = 0.09f;

    // A slow rise and fall across the path, so the ship is flying rather than riding a
    // rail. Small enough that it never fights the orbit for the eye.
    const float HoverRatio = 0.05f;            // of the vertical orbit radius
    const float HoverSpeed = 0.62f;
    const float BankSmoothing = 1.1f;          // seconds to settle onto a new bank

    // The engine, dressed for a portrait. Gameplay idles the flame at a stub, which is
    // right for a ship the size of a thumbnail and reads as a dead engine on a ship drawn
    // half again larger than life with nothing else in the frame.
    const float MenuFlameIntensity = 0.74f;
    const float MenuExhaustRate = 26f;

    // How long, once the launch hand-over begins, the Retro UFO's Main Menu idle
    // profile (see RetroUfoVisualPivot) takes to blend down to its approved gameplay
    // profile. Deliberately shorter than LaunchDuration: the camera/ellipse settle is
    // a slow one-piece move, but the idle richness should read as "handed off" early
    // rather than linger through the whole pull-back. Heading authority itself is not
    // affected by this timer — it stays with the Main Menu tangent until Release.
    const float PresentationHandOverDuration = 0.32f;

    // The bloom on the nozzle, as a share of the ship's own height.
    const float GlowOuterRatio = 1.5f;
    const float GlowCoreRatio = 0.6f;
    const float GlowOuterAlpha = 0.30f;
    const float GlowCoreAlpha = 0.46f;
    const float EngineLightIntensity = 0.8f;
    static readonly Color GlowWarm = new Color(1f, 0.46f, 0.13f, 1f);
    static readonly Color GlowHot = new Color(1f, 0.83f, 0.47f, 1f);

    Transform planet;
    Transform ship;
    RocketModelVisual shipModelVisual;
    bool presentationHandOverStarted;
    float presentationHandOverStartTime;

    Renderer[] shipRenderers;
    int[] shipBaseSortingOrders;
    Vector3 shipBaseScale;

    // Ceiling on the ellipse's horizontal reach, so the ship cannot swing off the side
    // of a narrow screen. 0 = no clamp. Must be set before Build.
    public float maxRadiusX;

    // How much larger the ship is drawn on the menu than in play. The start screen is a
    // portrait of the ship, not a gameplay frame: at its gameplay size it reads as a
    // sticker on the planet. It eases back to exactly 1 as the hand-over completes, so
    // gameplay always receives the ship at the scale it authored.
    public float menuScale = 1f;

    float radiusX;
    float radiusY;
    float orbitFloor;      // the closest the ship may ever come to the planet's centre
    float angle;
    float phase;

    // Micro course corrections: the ship drifts a few percent off its ideal path and
    // eases back, so the loop never looks mathematically perfect.
    float radiusDrift = 1f;
    float radiusDriftTarget = 1f;
    float bankDrift;
    float bankDriftTarget;
    float bankVelocity;
    float nextCorrection;

    // The engine dressing. All of it is the menu's, parented to the stage rather than to
    // the ship, so the borrowed rocket's own hierarchy is never written to and the whole
    // lot dies with the menu.
    LowPolyRocketFlame flame;
    Vector3 nozzleLocalPosition;
    Transform engineGlow;
    SpriteRenderer glowOuter;
    SpriteRenderer glowCore;
    Light2D engineLight;
    float glowOuterSize;
    float glowCoreSize;
    int glowSortingOrder;

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
        int sortingOffset)
    {
        if (heroPlanet == null || liveRocket == null) return false;

        planet = heroPlanet;
        ship = liveRocket;
        shipModelVisual = ship.GetComponent<RocketModelVisual>();
        shipBaseScale = ship.localScale;

        orbitFloor = planetRadius * (1f + ClearanceGapRatio) + ShipClearanceBeam();

        radiusX = planetRadius * RadiusXRatio;
        if (maxRadiusX > 0f) radiusX = Mathf.Min(radiusX, maxRadiusX);
        radiusX = Mathf.Max(radiusX, orbitFloor);
        radiusY = Mathf.Max(planetRadius * RadiusYRatio, orbitFloor);
        gameplayOrbitRadius = radiusX;

        // Every renderer the ship owns — mesh, flame layers, thruster particles, trail —
        // is lifted in front of the world as one, keeping their relative order.
        shipRenderers = ship.GetComponentsInChildren<Renderer>(true);
        shipBaseSortingOrders = new int[shipRenderers.Length];
        for (int i = 0; i < shipRenderers.Length; i++)
            shipBaseSortingOrders[i] = shipRenderers[i].sortingOrder;

        ApplySorting(sortingOffset);
        BuildEngine();

        angle = Random.Range(0f, Mathf.PI * 2f);
        phase = Random.Range(0f, 10f);
        nextCorrection = Time.unscaledTime + Random.Range(4f, 8f);
        presentationHandOverStarted = false;
        Animate(0f);
        return true;
    }

    // How far the ship reaches out from the path it is flying. Everywhere the ellipse
    // comes closest to the planet the ship is broadside to it, so it is the beam that has
    // to clear the silhouette, not the nose-to-tail length. Measured at the largest the
    // ship is ever drawn: the menu size, at the near side of the lap.
    float ShipClearanceBeam()
    {
        SpriteRenderer proxy = ship.GetComponent<SpriteRenderer>();
        float beam = proxy != null && proxy.sprite != null
            ? proxy.sprite.bounds.extents.y * Mathf.Abs(shipBaseScale.y)
            : 0.25f;
        return beam * menuScale * (1f + DepthScaleRatio);
    }

    // Wakes the engine up for the portrait and hangs the menu's own glow on the nozzle.
    // Everything here is reversed by Release.
    void BuildEngine()
    {
        SpriteRenderer proxy = ship.GetComponent<SpriteRenderer>();

        flame = ship.GetComponent<LowPolyRocketFlame>();
        if (flame != null)
        {
            nozzleLocalPosition = flame.EngineLocalPosition;
            flame.SetPresentationIdle(MenuFlameIntensity, MenuExhaustRate);
        }

        // Sized off the ship at its authored scale; the live scale is applied per frame,
        // so the glow follows the menu size, the depth cue and the hand-over shrink
        // without any of them being spelled out twice.
        float shipHeight = proxy != null && proxy.sprite != null
            ? proxy.sprite.bounds.size.y * Mathf.Abs(shipBaseScale.y)
            : 0.5f;
        glowOuterSize = shipHeight * GlowOuterRatio;
        glowCoreSize = shipHeight * GlowCoreRatio;
        // Behind the ship, and behind the flame layers and thruster sparks it sits under.
        glowSortingOrder = (proxy != null ? proxy.sortingOrder : 0) - 4;

        engineGlow = transform.Find("MenuRocketEngineGlow");
        if (engineGlow != null)
        {
            glowOuter = engineGlow.Find("EngineGlowOuter")?.GetComponent<SpriteRenderer>();
            glowCore = engineGlow.Find("EngineGlowCore")?.GetComponent<SpriteRenderer>();
            engineLight = engineGlow.Find("MenuRocketEngineLight")?.GetComponent<Light2D>();
            if (glowOuter == null || glowCore == null || engineLight == null)
                Debug.LogError("MenuHeroRocket: serialized engine presentation is incomplete.", this);
            return;
        }
        if (Application.isPlaying)
        {
            Debug.LogError("MenuHeroRocket: serialized engine presentation is missing. Run the Main Menu authoring command.", this);
            return;
        }

        GameObject root = new GameObject("MenuRocketEngineGlow") { layer = gameObject.layer };
        root.transform.SetParent(transform, false);
        engineGlow = root.transform;

        glowOuter = CreateGlowLayer("EngineGlowOuter", glowOuterSize, GlowWarm, GlowOuterAlpha, proxy);
        glowCore = CreateGlowLayer("EngineGlowCore", glowCoreSize, GlowHot, GlowCoreAlpha, proxy);

        BuildEngineLight(shipHeight);
    }

    SpriteRenderer CreateGlowLayer(string name, float size, Color tint, float alpha,
        SpriteRenderer proxy)
    {
        SpriteRenderer layer = MenuShowcaseAssets.CreateSprite(engineGlow, name,
            VfxSpriteFactory.SoftSprite, glowSortingOrder, Vector3.zero, size,
            new Color(tint.r, tint.g, tint.b, alpha));
        if (proxy != null) layer.sortingLayerID = proxy.sortingLayerID;
        return layer;
    }

    // A real light, so the engine actually throws something onto the world it is flying
    // over instead of only glowing on its own sprite. Short reach: the ship is the hero
    // of the frame, not a second sun.
    void BuildEngineLight(float shipHeight)
    {
        GameObject go = new GameObject("MenuRocketEngineLight") { layer = gameObject.layer };
        go.transform.SetParent(engineGlow, false);

        engineLight = go.AddComponent<Light2D>();
        engineLight.lightType = Light2D.LightType.Point;
        engineLight.shadowsEnabled = false;
        engineLight.color = GlowWarm;
        engineLight.pointLightInnerRadius = shipHeight * 0.3f;
        engineLight.pointLightOuterRadius = shipHeight * 2.2f;
        engineLight.intensity = EngineLightIntensity;

        SortingLayer[] layers = SortingLayer.layers;
        for (int i = 0; i < layers.Length; i++) engineLight.AddTargetSortingLayer(layers[i].id);
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

        // Hands heading authority back to the gameplay launch-guide direction on the
        // exact frame RocketController resumes driving the ship, so there is no gap
        // where neither authority is writing the pivot's heading.
        RetroUfoVisualPivot retroPresentation = shipModelVisual != null
            ? shipModelVisual.ActiveRetroUfoPresentation : null;
        if (retroPresentation != null) retroPresentation.ClearMenuHeadingOverride();

        if (ship != null) ship.localScale = shipBaseScale;

        // The engine goes back to the idle gameplay authored, and the menu's own glow
        // leaves with the menu rather than hitching a ride into the game.
        if (flame != null) flame.SetPresentationIdle(0f, 0f);
        if (engineGlow != null) Destroy(engineGlow.gameObject);

        if (shipRenderers == null) return;

        for (int i = 0; i < shipRenderers.Length; i++)
            if (shipRenderers[i] != null) shipRenderers[i].sortingOrder = shipBaseSortingOrders[i];
    }

    void OnDestroy() => Release();

    void Update()
    {
        if (released) return;

        float delta = Time.unscaledDeltaTime;

        // Fastest at the top of the ellipse — the far side — and back to one speed as the
        // hand-over converges, so gameplay receives the ship travelling at the rate its
        // own orbit expects.
        float farSide = (Mathf.Sin(angle) + 1f) * 0.5f;
        float pacing = Mathf.Lerp(Mathf.Lerp(1f, FarSideSpeedUp, farSide), 1f, HandOverBlend);
        angle += OrbitSpeed * pacing * delta;

        Animate(delta);
    }

    // Everything the menu adds on top of a plain circular orbit fades out together, so
    // the last frame of the transition is a pose RocketController would produce.
    float HandOverBlend => Mathf.SmoothStep(0f, 1f, handOverProgress);

    void Animate(float delta)
    {
        if (planet == null || ship == null) return;

        float time = Time.unscaledTime;
        UpdateCourseCorrection(time, delta);

        float blend = HandOverBlend;
        // A course correction may breathe the orbit, never far enough to touch the world.
        float menuRadiusX = Mathf.Max(radiusX * radiusDrift, orbitFloor);
        float menuRadiusY = Mathf.Max(radiusY * radiusDrift, orbitFloor);
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

        offset.y += Mathf.Sin(time * HoverSpeed + phase) * radiusY * HoverRatio * (1f - blend);

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

        // Retro UFO owns a separated HeadingPivot/StylePivot hierarchy (see
        // RetroUfoVisualPivot) that writes its own world rotation every frame, so the
        // root rotation above never reaches it. Feed it the same pure ellipse tangent
        // directly — never the root's cosmetic idleBank/bankDrift, which is what the
        // pivot's own richer idle profile is for.
        RetroUfoVisualPivot retroPresentation = shipModelVisual != null
            ? shipModelVisual.ActiveRetroUfoPresentation : null;
        if (retroPresentation != null)
        {
            float headingRad = heading * Mathf.Deg2Rad;
            retroPresentation.SetMenuHeadingOverride(
                new Vector3(Mathf.Cos(headingRad), Mathf.Sin(headingRad), 0f));

            if (blend > 0f && !presentationHandOverStarted)
            {
                presentationHandOverStarted = true;
                presentationHandOverStartTime = time;
            }
            float presentationBlend = presentationHandOverStarted
                ? 1f - Mathf.Clamp01((time - presentationHandOverStartTime) / PresentationHandOverDuration)
                : 1f;
            retroPresentation.SetMenuPresentationBlend(presentationBlend);
        }

        // Top of the ellipse is the far side, so the ship is drawn smaller there. With no
        // overlap left to sell the distance, this and the pacing are what carry it.
        float depth = -sin;                       // +1 nearest, -1 furthest
        float scale = Mathf.Lerp(menuScale, 1f, blend) * (1f + depth * DepthScaleRatio * (1f - blend));
        ship.localScale = shipBaseScale * scale;

        UpdateEngine(time, scale, blend);
    }

    // The glow rides the nozzle in world space rather than being parented to it: the ship
    // belongs to gameplay, and nothing the menu builds is ever added to its hierarchy.
    void UpdateEngine(float time, float shipScale, float blend)
    {
        if (engineGlow == null) return;

        engineGlow.SetPositionAndRotation(ship.TransformPoint(nozzleLocalPosition), ship.rotation);

        // Two incommensurate sines: the engine breathes, and never settles into a beat.
        float flicker = 1f
            + Mathf.Sin(time * 9.3f + phase) * 0.06f
            + Mathf.Sin(time * 3.7f + phase * 1.4f) * 0.035f;
        float fade = 1f - blend;

        SetGlowLayer(glowOuter, glowOuterSize * shipScale * flicker, GlowOuterAlpha * flicker * fade);
        SetGlowLayer(glowCore, glowCoreSize * shipScale * (2f - flicker), GlowCoreAlpha * flicker * fade);

        if (engineLight != null)
            engineLight.intensity = EngineLightIntensity * flicker * fade;
    }

    static void SetGlowLayer(SpriteRenderer layer, float size, float alpha)
    {
        if (layer == null) return;

        layer.transform.localScale = Vector3.one * Mathf.Max(0.0001f, size);
        Color color = layer.color;
        color.a = Mathf.Clamp01(alpha);
        layer.color = color;
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
            // Inward only. The orbit is already as wide as the frame allows, so a
            // correction that pushed outward would carry the ship off the side of the
            // screen; orbitFloor catches it on the way in.
            radiusDriftTarget = Random.Range(0.958f, 1f);
            bankDriftTarget = Random.Range(-4.5f, 4.5f);
        }

        radiusDrift = Mathf.MoveTowards(radiusDrift, radiusDriftTarget, delta * 0.05f);
        // Eased rather than driven at a constant rate: a course correction that arrives
        // and stops dead reads as a servo, not as a pilot.
        bankDrift = Mathf.SmoothDamp(bankDrift, bankDriftTarget, ref bankVelocity,
            BankSmoothing, Mathf.Infinity, delta);
    }
}
