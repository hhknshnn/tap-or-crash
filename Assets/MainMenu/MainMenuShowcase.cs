using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Turns the start screen into a showcase stage: a hero planet wearing the player's
// active theme, a rocket orbiting it, layered space behind it and theme-driven light.
//
// The stage is not a diorama standing in for the game — it *is* the game, framed
// closely. The hero planet is the run's first planet and the orbiting ship is the live
// Rocket, both borrowed while gameplay sleeps. The planet is scaled up and the main
// camera is pulled in to ortho 5, which is the exact framing the showcase was composed
// against, so every constant below still means what it always meant.
//
// Because nothing is a copy, starting the game needs no swap: the camera pulls back, the
// planet settles to its real size and the ship is handed to RocketController mid-orbit
// on a frame where the pose it inherits is the pose it already had. See LaunchSequence.
[DisallowMultipleComponent]
public sealed class MainMenuShowcase : MonoBehaviour
{
    public const string StageLayerName = "MenuShowcase";

    const float StageOrthoSize = 5f;
    // The planet still owns the frame, but it now breathes: the logo above it and the
    // launch plate below it get their own air instead of being crowded off the edges.
    const float HeroWidthFraction = 0.545f;   // planet diameter / visible width
    const float HeroMaxHeightFraction = 0.49f;
    const float HeroCenterOffsetY = 0.34f;    // sits above the optical centre, over the UI stack

    // Long enough to read as a camera move rather than a cut, short enough that the
    // player never waits to play. The ship keeps orbiting throughout.
    const float LaunchDuration = 1.45f;

    // The gameplay HUD is switched on the instant the game starts, which would drop a
    // full score bar over a menu that has not left yet. These rise with the camera.
    static readonly string[] HudRootNames = { "ScorePlate", "ScoreText", "GameUI" };

    static readonly Color NightVoid = new Color(0.012f, 0.018f, 0.05f, 1f);
    static readonly Color DayVoid = new Color(0.05f, 0.085f, 0.17f, 1f);
    static readonly Color KeyLightWarm = new Color(1f, 0.94f, 0.80f, 1f);
    static readonly Color FallbackAccent = new Color(0.34f, 0.86f, 1f, 1f);

    static MainMenuShowcase instance;

    Transform stage;
    Camera stageCamera;          // the main camera, borrowed for the length of the menu
    Transform hero;
    Vector3 heroBasePosition;
    Vector3 heroOriginalScale;
    Vector3 heroMenuScale;
    float heroPhase;
    float builtAspect;
    float idleBlend = 1f;        // 1 = full menu idle, 0 = handed over

    Light2D fillLight;
    Light2D keyLight;
    Light2D rimLight;
    SpriteRenderer heroAura;
    float heroAuraAlpha;

    readonly List<Light2D> globalLights = new List<Light2D>();
    readonly List<float> globalLightIntensities = new List<float>();

    GameObject startPanel;
    Canvas menuCanvas;
    Image startPanelDim;
    Color startPanelDimColor;
    GameObject legacyBackground;
    GameObject legacyStarfield;
    GameObject legacyEmblem;
    GameObject legacyLogo;
    GameObject legacyLogoRule;
    GameObject legacySubtitle;

    MenuBrandEmblem brandEmblem;
    int pendingReframes;

    RocketController rocketController;
    Transform rocket;
    Vector3 rocketBaseScale;
    MenuHeroRocket heroRocket;

    CameraFollow cameraFollow;
    float cameraBaseOrthoSize;

    readonly List<GameObject> hiddenPlanets = new List<GameObject>();
    readonly List<Transform> menuOnlyChildren = new List<Transform>();
    MenuFader stageFader;
    MenuFader hiddenPlanetFader;
    MenuFader worldSkyFader;
    readonly List<CanvasGroup> hudGroups = new List<CanvasGroup>();

    bool built;
    bool borrowed;      // the hero, the ship, the camera and the lights are ours to give back
    bool launching;
    bool handedOver;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstall() => SceneInstaller.RunOnEveryScene(Install);

    // Runs after every scene load, which is what makes this the one and only main menu:
    // Game Over → Main Menu reloads the scene, and the showcase is rebuilt with it.
    static void Install()
    {
        // The sky fade is static and outlives a scene load. Clearing it here means a
        // reload can never strand the world's sky at whatever the last menu left it on.
        SpaceEnvironment.PresentationAlpha = 1f;

        if (instance != null) return;
        // A restart skips the menu entirely, so there is nothing to show off.
        // (GameManager clears the flag in its own Start, i.e. after this runs.)
        if (GameManager.isRestart || GameManager.isGameStarted) return;

        GameObject go = new GameObject("MainMenuShowcase");
        go.AddComponent<MainMenuShowcase>();
    }

    // GameManager asks before it hides the start panel. Taking ownership here is what
    // lets the menu dissolve on its own terms instead of blinking out.
    public static bool TryBeginLaunch(GameObject panel)
    {
        if (instance == null || !instance.built || instance.launching) return false;
        if (panel != null && panel != instance.startPanel) return false;

        instance.launching = true;
        GameManager.isIntroPlaying = true;
        PresentationGate.Acquire(PresentationGate.Kind.MenuIntro);
        instance.StartCoroutine(instance.LaunchSequence());
        return true;
    }

    void Awake()
    {
        if (instance == null) instance = this;
        else { Destroy(gameObject); return; }
    }

    void OnDestroy()
    {
        if (instance != this) return;
        Restore();
        instance = null;
    }

    IEnumerator Start()
    {
        // Let the runtime-built UI settle first: VisualPolishController styles the start
        // screen a frame in, ShipSkinManager applies the equipped tint and
        // SplashScreenController fills its own particle parent. The spawner also needs
        // these frames to place the first planets and PlanetPresentation to dress them.
        yield return null;
        yield return null;

        if (!ResolveStartScreen() || !ResolveGameplayObjects()) { Destroy(gameObject); yield break; }

        BuildStage(ResolveAccent());
        HandOverScreen();
        BuildBrandEmblem(ResolveAccent());
        built = true;

        // PlanetAmbience builds its decorative children in its own Start, so the layer
        // has to be re-stamped once they exist.
        yield return null;
        ApplyStageLayer(stage);
        yield return new WaitForSecondsRealtime(1f);
        ApplyStageLayer(stage);
    }

    void Update()
    {
        if (!built) return;

        // While the hand-over plays the panel is already gone and the game is "started";
        // the sequence owns the teardown from that point on.
        if (launching)
        {
            AnimateHero();
            return;
        }

        if (startPanel == null || !startPanel.activeInHierarchy || GameManager.isGameStarted)
        {
            Destroy(gameObject);
            return;
        }

        AnimateHero();
        KeepFraming();

        if (pendingReframes > 0)
        {
            pendingReframes--;
            if (brandEmblem != null) brandEmblem.Reframe();
        }
    }

    // ─── Resolving what the menu borrows ─────────────────────────────────────

    bool ResolveStartScreen()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return false;

        Transform panel = canvas.transform.Find("StartPanel");
        if (panel == null || !panel.gameObject.activeInHierarchy) return false;

        menuCanvas = canvas;
        startPanel = panel.gameObject;
        return true;
    }

    // The hero planet and the hero rocket are the run's own first planet and the live
    // ship. If either is missing the menu simply does not take over, and the plain
    // start screen the scene already carries is shown instead.
    bool ResolveGameplayObjects()
    {
        rocketController = FindAnyObjectByType<RocketController>();
        if (rocketController == null || rocketController.planets.Count == 0) return false;

        hero = rocketController.planets[0];
        if (hero == null || hero.GetComponent<SpriteRenderer>() == null) return false;

        rocket = rocketController.transform;
        rocketBaseScale = rocket.localScale;

        stageCamera = Camera.main;
        if (stageCamera == null || !stageCamera.orthographic) return false;

        cameraFollow = stageCamera.GetComponent<CameraFollow>();
        cameraBaseOrthoSize = stageCamera.orthographicSize;
        return true;
    }

    // The first world's accent, read from the ambience registry exactly as before. The
    // hero is now always a planet of level one, because that is the planet the player
    // is about to fly from.
    static Color ResolveAccent()
    {
        string[] names = PlanetSpawner.LevelNames;
        string levelName = names != null && names.Length > 0 ? names[0] : null;

        Color accent = PlanetAmbience.AccentColorFor(levelName, FallbackAccent);
        accent.a = 1f;
        return accent;
    }

    // The wordmark stops being UI and becomes a lit object on the stage. If the
    // bake is missing the plain title comes straight back, so a failed emblem
    // costs the menu nothing.
    void BuildBrandEmblem(Color accent)
    {
        if (stage == null || stageCamera == null || hero == null) return;

        brandEmblem = stage.gameObject.AddComponent<MenuBrandEmblem>();
        bool ok = brandEmblem.Build(stage, stageCamera, menuCanvas, hero,
            PlanetPresentation.GetBodyRadius(hero), accent);

        if (ok)
        {
            ApplyStageLayer(stage);
            return;
        }

        Destroy(brandEmblem);
        brandEmblem = null;
        SetActive(legacyLogo, true);
        SetActive(legacyLogoRule, true);
        SetActive(legacySubtitle, true);
    }

    // The UI itself is untouched: same buttons, same positions, same navigation. Only
    // the opaque art behind it steps aside so the stage can be seen.
    void HandOverScreen()
    {
        startPanelDim = startPanel.GetComponent<Image>();
        if (startPanelDim != null)
        {
            startPanelDimColor = startPanelDim.color;
            // Still dims the stage enough for the logo and buttons to stay legible, but
            // no longer hides it. Raycasting is unaffected by alpha.
            startPanelDim.color = new Color(0.02f, 0.035f, 0.09f, 0.22f);
        }

        legacyBackground = FindChild("Background");        // the flat space_background image
        legacyStarfield = FindChild("Particle Parent");    // the old UI stars/nebulae/asteroids
        legacyEmblem = FindChild("OrbitEmblem");           // its screen slot is now the planet

        // The flat title, its hairline and the tagline under it are all replaced
        // by the one baked emblem. Hidden before it is built, because the emblem
        // measures the gap the remaining UI leaves and would otherwise duck
        // under the very label it is standing in for.
        legacyLogo = FindDeepChild("LogoText");
        legacyLogoRule = FindDeepChild("LogoRule");
        legacySubtitle = FindDeepChild("SubtitleText");

        SetActive(legacyBackground, false);
        SetActive(legacyStarfield, false);
        SetActive(legacyEmblem, false);
        SetActive(legacyLogo, false);
        SetActive(legacyLogoRule, false);
        SetActive(legacySubtitle, false);
    }

    void Restore()
    {
        if (startPanelDim != null) startPanelDim.color = startPanelDimColor;
        SetActive(legacyBackground, true);
        SetActive(legacyStarfield, true);
        SetActive(legacyEmblem, true);
        SetActive(legacyLogo, true);
        SetActive(legacyLogoRule, true);
        SetActive(legacySubtitle, true);

        // A hand-over has already put every borrowed object back, in its own order and
        // on the exact frame the game took over. This is the path for every other exit —
        // and nothing is given back that was never taken.
        if (borrowed && !handedOver) ReturnBorrowedObjects();

        if (stage != null) Destroy(stage.gameObject);
    }

    // Undoes everything the menu did to objects it does not own.
    void ReturnBorrowedObjects()
    {
        if (heroRocket != null) heroRocket.Release();

        if (hero != null)
        {
            hero.localScale = heroOriginalScale;
            hero.position = heroBasePosition;
        }

        DestroyMenuOnlyChildren();
        ShowWorldSky(1f);
        SetHudAlpha(1f);

        for (int i = 0; i < hiddenPlanets.Count; i++)
            if (hiddenPlanets[i] != null) hiddenPlanets[i].SetActive(true);
        hiddenPlanets.Clear();

        if (hiddenPlanetFader != null)
        {
            hiddenPlanetFader.SetAlpha(1f);
            hiddenPlanetFader.Thaw();
            hiddenPlanetFader = null;
        }

        for (int i = 0; i < globalLights.Count; i++)
            if (globalLights[i] != null) globalLights[i].intensity = globalLightIntensities[i];
        globalLights.Clear();
        globalLightIntensities.Clear();

        if (stageCamera != null) stageCamera.orthographicSize = cameraBaseOrthoSize;
        if (cameraFollow != null) cameraFollow.enabled = true;

        GameManager.isIntroPlaying = false;
        PresentationGate.Release(PresentationGate.Kind.MenuIntro);
    }

    void DestroyMenuOnlyChildren()
    {
        for (int i = 0; i < menuOnlyChildren.Count; i++)
            if (menuOnlyChildren[i] != null) Destroy(menuOnlyChildren[i].gameObject);
        menuOnlyChildren.Clear();

        if (hero == null) return;
        MenuHeroSpin spin = hero.GetComponent<MenuHeroSpin>();
        if (spin != null) Destroy(spin);
        MenuPlanetLife life = hero.GetComponent<MenuPlanetLife>();
        if (life != null) Destroy(life);
    }

    GameObject FindChild(string name)
    {
        Transform child = startPanel != null ? startPanel.transform.Find(name) : null;
        return child != null ? child.gameObject : null;
    }

    // The scene nests some start-screen labels under other controls, so the flat
    // lookup above is not enough for them.
    GameObject FindDeepChild(string name)
    {
        if (startPanel == null) return null;
        foreach (Transform child in startPanel.GetComponentsInChildren<Transform>(true))
            if (child.name.Trim() == name) return child.gameObject;
        return null;
    }

    static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active) target.SetActive(active);
    }

    // ─── Stage ───────────────────────────────────────────────────────────────

    void BuildStage(Color accent)
    {
        int layer = LayerMask.NameToLayer(StageLayerName);
        if (layer < 0) layer = gameObject.layer;

        heroBasePosition = hero.position;
        heroOriginalScale = hero.localScale;
        heroMenuScale = heroOriginalScale;
        // From here on the menu is holding objects it does not own. Anything that goes
        // wrong past this line still has to give them back.
        borrowed = true;

        GameObject root = new GameObject("MenuShowcaseStage") { layer = layer };
        root.transform.position = heroBasePosition - new Vector3(0f, HeroCenterOffsetY, 0f);
        stage = root.transform;

        PrepareCamera();
        BuildLights(accent);
        PrepareHero(accent);

        float halfHeight = StageOrthoSize;
        float halfWidth = StageOrthoSize * stageCamera.aspect;

        MenuSpaceBackdrop backdrop = root.AddComponent<MenuSpaceBackdrop>();
        backdrop.Build(halfWidth, halfHeight, accent);

        heroRocket = root.AddComponent<MenuHeroRocket>();
        heroRocket.Build(hero, PlanetPresentation.GetBodyRadius(hero), rocket, 12, -3);

        HideRemainingPlanets();
        ApplyStageLayer(stage);
    }

    // The game camera is not parked and replaced — it is simply pulled in to the
    // framing the showcase was composed against, and pushed back out again on launch.
    void PrepareCamera()
    {
        if (cameraFollow != null) cameraFollow.enabled = false;

        stageCamera.orthographicSize = StageOrthoSize;
        stageCamera.transform.position = stage.position + new Vector3(0f, 0f, -10f);
        stageCamera.transform.rotation = Quaternion.identity;
        stageCamera.backgroundColor = VoidColor(ResolveAccent());

        builtAspect = stageCamera.aspect;

        // The menu owns the light in the frame; the world's global light would flatten
        // the rig back out. It is eased back in as the camera pulls away.
        foreach (Light2D light in FindObjectsByType<Light2D>(FindObjectsInactive.Include))
        {
            if (light == null || light.lightType != Light2D.LightType.Global) continue;
            globalLights.Add(light);
            globalLightIntensities.Add(light.intensity);
            light.intensity = 0f;
        }

        HideWorldSky();
    }

    // The game's own sky is a full-screen deep void with its own starfields. Behind the
    // showcase it would wash the staged void out, so it is held at zero and raised again
    // as the camera pulls back — the one frame where both skies are on screen is the one
    // where the menu's has already gone.
    void HideWorldSky()
    {
        SpaceEnvironment.PresentationAlpha = 0f;

        worldSkyFader = new MenuFader();
        ParallaxBackground parallax = FindAnyObjectByType<ParallaxBackground>();
        if (parallax != null) worldSkyFader.Add(parallax.transform);
        worldSkyFader.SetAlpha(0f);
    }

    void ShowWorldSky(float alpha)
    {
        SpaceEnvironment.PresentationAlpha = alpha;
        if (worldSkyFader != null) worldSkyFader.SetAlpha(alpha);
    }

    void CaptureHud()
    {
        if (menuCanvas == null) return;

        for (int i = 0; i < HudRootNames.Length; i++)
        {
            Transform child = menuCanvas.transform.Find(HudRootNames[i]);
            if (child == null) continue;

            CanvasGroup group = child.GetComponent<CanvasGroup>();
            if (group == null) group = child.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            hudGroups.Add(group);
        }
    }

    void SetHudAlpha(float alpha)
    {
        for (int i = 0; i < hudGroups.Count; i++)
            if (hudGroups[i] != null) hudGroups[i].alpha = alpha;
    }

    // Deep space, faintly stained by the theme so even the empty corners of the frame
    // belong to the world the planet comes from.
    static Color VoidColor(Color accent)
    {
        Color baseColor = PlayerPrefs.GetInt("DayMode", 0) == 1 ? DayVoid : NightVoid;
        // Only a trace: a stronger stain turns deep space into coloured fog, which is
        // exactly what made the old backdrop read as flat.
        return Color.Lerp(baseColor, accent * 0.5f, 0.06f);
    }

    // Three 2D lights, no shadows and no normal maps: the whole lighting rig costs a
    // handful of blend operations and reacts entirely to the theme's accent colour, so
    // a future theme lights itself correctly the moment it registers.
    void BuildLights(Color accent)
    {
        // Ambient fill — heavily desaturated accent. This is what makes Ice read cold
        // and Lava read hot before any key light lands. Deliberately a very wide point
        // light rather than a global one: URP 2D allows a single global light per
        // sorting layer, and that slot already belongs to the gameplay scene.
        fillLight = CreateLight("FillLight", Light2D.LightType.Point);
        fillLight.color = Color.Lerp(accent, Color.white, 0.46f);
        // Dimmer than the key by a clear margin: that gap is what gives the disc a lit
        // side and a shadow side instead of the evenly-washed ball it used to be.
        fillLight.intensity = 0.46f;
        fillLight.falloffIntensity = 0.28f;

        // Key — the local sun, and a sun is a sun on every planet. Almost pure warm
        // white with a trace of the theme, so Natural reads as sunlight instead of
        // green light while Ice still resolves cold once fill and rim land on it.
        keyLight = CreateLight("KeyLight", Light2D.LightType.Point);
        keyLight.color = Color.Lerp(accent, KeyLightWarm, 0.76f);
        keyLight.intensity = 1.12f;
        keyLight.falloffIntensity = 0.5f;   // wider hot side: the surface detail reads

        // Rim — pure accent from the opposite side. This is what carries the theme's
        // colour and gives the sprite a silhouette instead of a flat disc.
        rimLight = CreateLight("RimLight", Light2D.LightType.Point);
        // Saturated rather than pale, and tight: it should draw a bright edge along the
        // dark side, not add another wash of light to the whole planet.
        rimLight.color = Color.Lerp(accent, Color.white, 0.12f);
        rimLight.intensity = 0.92f;
        rimLight.falloffIntensity = 0.86f;
    }

    Light2D CreateLight(string name, Light2D.LightType type)
    {
        GameObject go = new GameObject(name) { layer = stage.gameObject.layer };
        go.transform.SetParent(stage, false);

        Light2D light = go.AddComponent<Light2D>();
        light.lightType = type;
        light.shadowsEnabled = false;

        // Only one sorting layer exists today, but adding every layer keeps the rig
        // correct if the project ever gains more.
        SortingLayer[] sortingLayers = SortingLayer.layers;
        for (int i = 0; i < sortingLayers.Length; i++) light.AddTargetSortingLayer(sortingLayers[i].id);

        return light;
    }

    // The hero is the run's first planet, borrowed. It is scaled up to the size the
    // showcase was composed for and given the menu's extra life; both are undone,
    // exactly, when the game takes it back.
    void PrepareHero(Color accent)
    {
        ScaleHero();

        heroPhase = Random.Range(0f, Mathf.PI * 2f);
        hero.position = heroBasePosition;

        float radius = PlanetPresentation.GetBodyRadius(hero);
        PlaceLights(radius);
        BuildAura(accent, radius);

        // Everything the hero grows from here is the menu's, and only the menu's.
        HashSet<Transform> before = new HashSet<Transform>();
        foreach (Transform child in hero) before.Add(child);

        // The live ambience system already dressed this planet when it spawned. The menu
        // tops it up so the hero always reads as an inhabited world, whichever planet of
        // the theme the run happened to draw.
        MenuPlanetLife life = hero.gameObject.AddComponent<MenuPlanetLife>();
        life.accent = accent;
        life.living = MenuPlanetLife.IsLivingAccent(accent);

        // Forever, slowly. Direction is stable for the planet, not the session, so the
        // same world always turns the same way.
        MenuHeroSpin spin = hero.gameObject.AddComponent<MenuHeroSpin>();
        spin.Configure((hero.name.Length & 1) == 0);

        StartCoroutine(RecordMenuOnlyChildren(before));
    }

    // MenuPlanetLife builds on its own first frames, so the difference can only be
    // taken once it has finished.
    IEnumerator RecordMenuOnlyChildren(HashSet<Transform> before)
    {
        yield return null;
        yield return null;

        if (hero == null) yield break;
        foreach (Transform child in hero)
            if (!before.Contains(child)) menuOnlyChildren.Add(child);
    }

    void ScaleHero()
    {
        float halfHeight = StageOrthoSize;
        float halfWidth = StageOrthoSize * stageCamera.aspect;

        float target = Mathf.Min(halfWidth * 2f * HeroWidthFraction,
                                 halfHeight * 2f * HeroMaxHeightFraction);

        hero.localScale = heroOriginalScale;
        float natural = Mathf.Max(0.0001f, PlanetPresentation.GetBodyRadius(hero) * 2f);

        heroMenuScale = heroOriginalScale * (target / natural);
        hero.localScale = heroMenuScale;
    }

    // The rest of the route is real and already spawned. It waits off-frame until the
    // camera has pulled back far enough for it to belong there.
    void HideRemainingPlanets()
    {
        hiddenPlanetFader = new MenuFader();

        for (int i = 1; i < rocketController.planets.Count; i++)
        {
            Transform planet = rocketController.planets[i];
            if (planet == null) continue;

            hiddenPlanetFader.Add(planet, typeof(PlanetPresentation), typeof(PlanetAmbience),
                typeof(PlanetAmbienceKit));
            hiddenPlanets.Add(planet.gameObject);
            planet.gameObject.SetActive(false);
        }
    }

    void PlaceLights(float radius)
    {
        if (fillLight != null)
        {
            // Covers the whole frame so every lit sprite in the stage receives it.
            float reach = StageOrthoSize * Mathf.Max(1f, stageCamera.aspect) * 2.6f;
            fillLight.transform.position = heroBasePosition;
            fillLight.pointLightInnerRadius = reach * 0.35f;
            fillLight.pointLightOuterRadius = reach;
        }

        if (keyLight != null)
        {
            keyLight.transform.position = heroBasePosition + new Vector3(-radius * 0.62f, radius * 0.68f, 0f);
            keyLight.pointLightInnerRadius = radius * 0.35f;
            keyLight.pointLightOuterRadius = radius * 3.1f;
        }

        if (rimLight != null)
        {
            // Just outside the rim on the shadow side, so its falloff lands on the edge.
            rimLight.transform.position = heroBasePosition + new Vector3(radius * 0.95f, -radius * 0.72f, 0f);
            rimLight.pointLightInnerRadius = radius * 0.12f;
            rimLight.pointLightOuterRadius = radius * 1.85f;
        }
    }

    // A soft accent halo hugging the planet. Unlit on purpose: it reads as the planet's
    // own atmosphere rather than as something the key light is hitting.
    void BuildAura(Color accent, float radius)
    {
        heroAuraAlpha = 0.10f;
        heroAura = MenuShowcaseAssets.CreateSprite(stage, "HeroAura", VfxSpriteFactory.SoftSprite, -20,
            heroBasePosition - stage.position, radius * 2.5f,
            new Color(accent.r, accent.g, accent.b, heroAuraAlpha));
    }

    // ─── Per-frame ───────────────────────────────────────────────────────────

    void AnimateHero()
    {
        if (hero == null) return;

        float time = Time.unscaledTime;
        // Both channels fade out with the menu, so the planet is standing exactly where
        // the game expects it on the frame gameplay resumes.
        float bob = Mathf.Sin(time * 0.5f + heroPhase) * 0.055f * idleBlend;
        float breath = 1f + Mathf.Sin(time * 0.33f + heroPhase) * 0.006f * idleBlend;

        hero.position = heroBasePosition + new Vector3(0f, bob, 0f);
        hero.localScale = Vector3.Lerp(heroOriginalScale, heroMenuScale, idleBlend) * breath;

        if (heroAura != null)
        {
            heroAura.transform.position = hero.position;
            Color color = heroAura.color;
            color.a = heroAuraAlpha * Mathf.Lerp(0.78f, 1.18f, (Mathf.Sin(time * 0.72f) + 1f) * 0.5f);
            heroAura.color = color;
        }
    }

    // Orientation or resolution changes are rare, but a wrong hero size would be very
    // visible, so the framing is re-derived whenever the aspect actually moves.
    void KeepFraming()
    {
        if (stageCamera == null || hero == null) return;
        if (Mathf.Abs(stageCamera.aspect - builtAspect) < 0.01f) return;

        builtAspect = stageCamera.aspect;
        ScaleHero();
        PlaceLights(PlanetPresentation.GetBodyRadius(hero));
        // The emblem measures the UI, and the Canvas has not re-laid itself out
        // yet on this frame. Reframing for a few frames after the change is what
        // lets it read the top row's real position.
        pendingReframes = 3;
    }

    // ─── Menu → gameplay ─────────────────────────────────────────────────────

    // One continuous move. Nothing is created, destroyed or swapped while the player can
    // see it: the camera pulls back, the planet settles to its real size, the menu's own
    // layers dissolve and the ship — which never stopped orbiting — is handed to
    // RocketController on the last frame, in the pose it is already in.
    IEnumerator LaunchSequence()
    {
        CanvasGroup panelGroup = null;
        RectTransform panelRect = null;
        Vector3 panelBaseScale = Vector3.one;

        if (startPanel != null)
        {
            panelGroup = startPanel.GetComponent<CanvasGroup>();
            if (panelGroup == null) panelGroup = startPanel.AddComponent<CanvasGroup>();
            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;
            panelRect = startPanel.GetComponent<RectTransform>();
            if (panelRect != null) panelBaseScale = panelRect.localScale;
        }

        CaptureHud();
        stageFader = MenuFader.Capture(stage, typeof(MenuSpaceBackdrop));
        // The extra life the menu grew on the hero dissolves with the rest of the stage
        // rather than vanishing when it is destroyed.
        for (int i = 0; i < menuOnlyChildren.Count; i++)
            stageFader.Add(menuOnlyChildren[i], typeof(PlanetAmbienceKit));
        stageFader.Freeze();
        if (hiddenPlanetFader != null) hiddenPlanetFader.Freeze();

        // The route ahead comes back on immediately but invisible, so it can fade up
        // rather than appear.
        for (int i = 0; i < hiddenPlanets.Count; i++)
            if (hiddenPlanets[i] != null) hiddenPlanets[i].SetActive(true);
        if (hiddenPlanetFader != null) hiddenPlanetFader.SetAlpha(0f);

        Vector3 cameraStart = stageCamera.transform.position;
        float rocketHalfHeight = GetRocketOrbitHalfHeight();
        float lookAhead = cameraFollow != null ? cameraFollow.lookAheadY : 2f;

        float elapsed = 0f;
        while (elapsed < LaunchDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / LaunchDuration);

            // The camera leads, everything else settles inside its move.
            float camera = Mathf.SmoothStep(0f, 1f, t);
            float settle = Mathf.SmoothStep(0f, 1f, Curve(t, 0.08f, 0.94f));
            float dissolve = Mathf.SmoothStep(0f, 1f, Curve(t, 0.10f, 0.78f));
            float reveal = Mathf.SmoothStep(0f, 1f, Curve(t, 0.22f, 0.92f));

            idleBlend = 1f - settle;

            // The ellipse becomes the circle RocketController orbits on, tracking the
            // planet as it shrinks so the two never disagree.
            if (heroRocket != null)
            {
                heroRocket.BeginHandOver(
                    PlanetPresentation.GetOrbitRingRadius(hero) + rocketHalfHeight);
                heroRocket.SetHandOverProgress(settle);
            }

            // Camera: ortho 5 back out to the gameplay framing, aiming where CameraFollow
            // will pick it up so the two agree on the final frame.
            stageCamera.orthographicSize = Mathf.Lerp(StageOrthoSize, cameraBaseOrthoSize, camera);
            Vector3 gameplayCamera = new Vector3(0f, rocket.position.y + lookAhead, -10f);
            stageCamera.transform.position = Vector3.Lerp(cameraStart, gameplayCamera, camera);

            if (stageFader != null) stageFader.SetAlpha(1f - dissolve);
            if (hiddenPlanetFader != null) hiddenPlanetFader.SetAlpha(reveal);
            ShowWorldSky(reveal);
            SetHudAlpha(reveal);

            float lightFade = 1f - dissolve;
            if (fillLight != null) fillLight.intensity = 0.46f * lightFade;
            if (keyLight != null) keyLight.intensity = 1.12f * lightFade;
            if (rimLight != null) rimLight.intensity = 0.92f * lightFade;
            for (int i = 0; i < globalLights.Count; i++)
                if (globalLights[i] != null)
                    globalLights[i].intensity = globalLightIntensities[i] * reveal;

            // The panel does not blink out: it lifts and dissolves in the first beat.
            if (panelGroup != null)
            {
                float ui = Mathf.SmoothStep(0f, 1f, Curve(t, 0f, 0.26f));
                panelGroup.alpha = 1f - ui;
                if (panelRect != null)
                {
                    panelRect.localScale = panelBaseScale * (1f - 0.045f * ui);
                    panelRect.anchoredPosition = new Vector2(
                        panelRect.anchoredPosition.x, 26f * ui);
                }
            }

            if (brandEmblem != null) brandEmblem.Reframe();

            yield return null;
        }

        CompleteHandOver();
    }

    // Straight-line remap so each channel can own its own slice of the move.
    static float Curve(float t, float start, float end)
    {
        return Mathf.Clamp01((t - start) / Mathf.Max(0.0001f, end - start));
    }

    float GetRocketOrbitHalfHeight()
    {
        SpriteRenderer renderer = rocket != null ? rocket.GetComponent<SpriteRenderer>() : null;
        if (renderer == null || renderer.sprite == null) return 0.25f;
        return Mathf.Max(0.05f, renderer.sprite.bounds.extents.y * Mathf.Abs(rocketBaseScale.y));
    }

    // The single frame where the ship changes owner. Its pose is read off the menu orbit
    // and written straight into RocketController, so the ship does not move.
    void CompleteHandOver()
    {
        idleBlend = 0f;

        if (hero != null)
        {
            hero.localScale = heroOriginalScale;
            hero.position = heroBasePosition;
        }

        float angleDegrees = heroRocket != null ? heroRocket.AngleDegrees : 0f;
        int direction = heroRocket != null ? heroRocket.OrbitDirection : 1;
        if (heroRocket != null) heroRocket.Release();

        bool adopted = false;
        if (rocketController != null && hero != null && rocket != null)
        {
            adopted = rocketController.RestoreContinueState(new RocketController.ContinueState
            {
                planet = hero,
                planetRelativePosition = rocket.position - hero.position,
                rotation = rocket.rotation,
                angle = angleDegrees,
                orbitRadius = Vector3.Distance(rocket.position, hero.position),
                orbitDirection = direction
            });
        }

        // The camera is already standing exactly where CameraFollow wants it, so handing
        // it back is a no-op on screen.
        if (cameraFollow != null)
        {
            cameraFollow.enabled = true;
            cameraFollow.RestoreContinueState(new CameraFollow.ContinueState
            {
                position = stageCamera.transform.position,
                velocity = Vector3.zero,
                orthographicSize = cameraBaseOrthoSize
            });
        }
        else if (stageCamera != null)
        {
            stageCamera.orthographicSize = cameraBaseOrthoSize;
        }

        DestroyMenuOnlyChildren();
        ShowWorldSky(1f);
        SetHudAlpha(1f);

        for (int i = 0; i < globalLights.Count; i++)
            if (globalLights[i] != null) globalLights[i].intensity = globalLightIntensities[i];
        globalLights.Clear();
        globalLightIntensities.Clear();

        if (hiddenPlanetFader != null)
        {
            hiddenPlanetFader.SetAlpha(1f);
            hiddenPlanetFader.Thaw();
            hiddenPlanetFader = null;
        }
        hiddenPlanets.Clear();

        if (startPanel != null) startPanel.SetActive(false);

        handedOver = true;
        GameManager.isIntroPlaying = false;
        PresentationGate.Release(PresentationGate.Kind.MenuIntro);

        // If the pose could not be adopted the game still has to start; RocketController
        // falls back to its own orbit maths on the next frame.
        if (!adopted && rocketController != null) rocketController.enabled = true;

        Destroy(gameObject);
    }

    static void ApplyStageLayer(Transform root)
    {
        if (root == null) return;

        int layer = root.gameObject.layer;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = layer;
    }
}
