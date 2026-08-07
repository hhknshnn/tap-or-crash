using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// The one and only Main Menu. This component sits on the serialized MainMenu root in
// SampleScene, and the Lava hero world and its light rig are serialized children of that
// root — so the approved menu is what the scene shows in Edit Mode and what renders on
// the very first frame of Play Mode. Nothing here reconstructs a menu from scene objects;
// it measures the hero, frames the camera around it and animates what is already there.
//
// PRESENTATION AND GAMEPLAY ARE SEPARATE. The hero world is a dedicated menu-only asset
// (a LavaHero.prefab instance): the Lava sprite the game already ships, with no collider,
// no Planet tag and no gameplay component on it. The menu owns it, dresses it with
// MenuLavaAmbience — a far heavier volcanic pass than any gameplay planet may afford —
// and destroys it on launch. The gameplay planets are only switched off while the menu is
// up and switched back on as the camera pulls away; nothing gameplay owns is scaled,
// moved or re-dressed.
//
//   MainMenu root (serialized) -> Play -> destroy the hero world
//             -> gameplay planets revealed -> RocketController takes the ship
//
// The Rocket is the hero of the frame and the Lava world is what it is flying over: the
// composition is framed for the ship, not for the planet. The ship is still the live
// Rocket, still handed over mid-orbit in the pose it already has, so the launch reads as
// one continuous camera move. See LaunchSequence.
//
// Runs late so the whole stage is built inside the first frame's Start phase — after the
// spawner has placed the planets and after VisualPolishController has styled the UI, and
// still before anything is rendered. That ordering is what removes the first-frame flash;
// it is not a delay, and it must never become one.
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public sealed class MainMenuShowcase : MonoBehaviour
{
    public const string StageLayerName = "MenuShowcase";

    // The world the menu shows off. Drives the light rig, the backdrop stain and the
    // emblem, so the whole stage follows the hero rather than the run's first level.
    const string HeroThemeName = "Lava";

    // The hero world is never scaled — the camera is framed around it instead. This is
    // the share of the visible height its sprite fills. Deliberately under 40%: the
    // Rocket has to stay the loudest thing in the frame, and the logo above and the
    // launch plate below both need air.
    const float HeroHeightFraction = 0.30f;

    // The ship is the hero of the start screen, so it is drawn a touch larger here than in
    // play. Only a touch: a heavily oversized ship covers the hero world instead of flying
    // over it, and it is the wide orbit — not the scale — that keeps the eye on the ship.
    // Read by MenuHeroRocket, and unwound the moment gameplay takes the ship back.
    const float RocketMenuScale = 1.05f;

    // Where the hero world sits in the frame, as a share of the visible height measured
    // down from the optical centre. It sits low: the logo owns the top of the frame and
    // the ship's orbit is drawn wider than the planet, so the world has to give the top
    // of that orbit room rather than sit in the middle of it. A fraction rather than a
    // world distance, so the composition holds at any aspect ratio.
    const float HeroCenterHeightFraction = 0.032f;

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

    [Header("Serialized stage")]
    [Tooltip("The menu's own Lava hero world — a LavaHero.prefab instance parented to this root. " +
             "Never a gameplay planet.")]
    [SerializeField] Transform hero;

    [Tooltip("Ambient fill, the local sun and the accent rim. Positioned and coloured at runtime " +
             "from the hero's measured radius and the theme accent.")]
    [SerializeField] Light2D fillLight;
    [SerializeField] Light2D keyLight;
    [SerializeField] Light2D rimLight;

    [Header("Borrowed gameplay state")]
    [SerializeField] Vector3 gameplayRocketPosition = new Vector3(3f, 0f, 0f);
    [SerializeField] Vector3 gameplayRocketEuler = new Vector3(0f, 0f, 85f);
    [SerializeField] Vector3 gameplayRocketScale = new Vector3(0.4f, 0.4f, 1f);
    [SerializeField] Vector3 gameplayCameraPosition = new Vector3(0f, 0f, -10f);
    [SerializeField] float gameplayCameraOrthoSize = 8f;

    Transform stage;             // this root — the hero and the light rig hang off it
    Camera stageCamera;          // the main camera, borrowed for the length of the menu
    Transform firstPlanet;       // gameplay planet 0, only read for the hand-over radius
    float heroBodyRadius;        // measured once; immune to the spin turning the AABB
    float stageOrtho;            // derived from the Hero Planet, not a fixed constant
    Vector3 heroBasePosition;
    Vector3 heroOriginalScale;
    Vector3 heroMenuScale;
    float heroPhase;
    float builtAspect;
    float idleBlend = 1f;        // 1 = full menu idle, 0 = handed over

    MenuLavaAmbience heroLava;   // the volcanic pass on the hero world
    SpriteRenderer heroAura;
    float heroAuraAlpha;

    readonly List<Light2D> globalLights = new List<Light2D>();
    readonly List<float> globalLightIntensities = new List<float>();

    GameObject startPanel;
    Canvas menuCanvas;
    Image startPanelDim;
    Color startPanelDimColor;

    MenuBrandEmblem brandEmblem;
    int pendingReframes;

    RocketController rocketController;
    Transform rocket;
    Vector3 rocketBaseScale;
    MenuHeroRocket heroRocket;

    CameraFollow cameraFollow;
    float cameraBaseOrthoSize;

    readonly List<GameObject> hiddenPlanets = new List<GameObject>();
    MenuFader stageFader;
    MenuFader hiddenPlanetFader;
    MenuFader worldSkyFader;
    readonly List<CanvasGroup> hudGroups = new List<CanvasGroup>();

    bool built;
    bool borrowed;      // the hero, the ship, the camera and the lights are ours to give back
    bool launching;
    bool handedOver;

    // ─── Menu readiness, for anything that must not present over a half-built menu ──
    //
    // The showcase builds synchronously inside its own Start, so there is exactly one
    // moment where the Main Menu stops being in progress: the end of that Start. This
    // reports that moment as an event plus a current state, so a late subscriber is
    // never left waiting for a signal that has already passed and nothing has to poll.
    //
    // "Settled" is not "ready". A stage that failed to build settles too — the scene's
    // plain start screen is still a complete, interactive Main Menu, and onboarding
    // belongs over that just as much as over the full showcase. What must never happen
    // is presenting while the answer is still unknown.
    //
    // Both flags are reset in Awake, before any subscriber's Start can run, so a scene
    // reload can never hand the new scene the previous one's verdict. Callers that may
    // run in a scene with no showcase at all must ask ExistsInScene first rather than
    // trusting a static nothing was alive to reset.

    static bool menuSettled;
    static bool menuReady;

    /// <summary>Fired once per scene lifecycle, the moment the menu stops building.</summary>
    public static event System.Action MenuSettled;

    /// <summary>True once the menu has either finished building or failed to.</summary>
    public static bool HasMenuSettled => menuSettled;

    /// <summary>True only when the authoritative serialized stage built successfully.</summary>
    public static bool IsMenuReady => menuReady;

    /// <summary>
    /// Whether this scene carries a showcase at all. Resolved live rather than from a
    /// static, so a scene without one is never answered with a stale flag.
    /// </summary>
    public static bool ExistsInScene => FindAnyObjectByType<MainMenuShowcase>() != null;

    static void ResetMenuState()
    {
        menuSettled = false;
        menuReady = false;
        // A subscriber from the previous scene is already destroyed; clearing the list
        // here means the next scene's subscribers are the only ones that can be called.
        MenuSettled = null;
    }

    static void SettleMenu(bool ready)
    {
        if (menuSettled) return;   // exactly once per scene lifecycle
        menuSettled = true;
        menuReady = ready;
        MenuSettled?.Invoke();
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
        // The sky fade is static and outlives a scene load. Clearing it here means a
        // reload can never strand the world's sky at whatever the last menu left it on.
        SpaceEnvironment.PresentationAlpha = 1f;

        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;

        // Before any subscriber's Start can run, so nobody can read the previous
        // scene's verdict.
        ResetMenuState();

        // A restart skips the menu entirely, so there is nothing to show off. Awake runs
        // before the first frame is rendered, so the serialized stage is gone before it
        // could ever be seen. (GameManager clears the flag in its own Start.)
        if (GameManager.isRestart || GameManager.isGameStarted)
        {
            RestoreSerializedGameplayPreview();
            instance = null;
            gameObject.SetActive(false);
            Destroy(gameObject);
            // There will be no menu this scene, and the answer is already final.
            SettleMenu(false);
        }
    }

    void OnDestroy()
    {
        if (instance != this) return;
        Restore();
        menuReady = false;
        instance = null;
    }

    // Everything the first rendered frame needs happens here, synchronously. The late
    // execution order above guarantees the spawner has placed the planets and the UI has
    // been styled by the time this runs, so there is nothing left to wait for.
    void Start()
    {
        if (!ResolveStartScreen() || !ResolveGameplayObjects() || !BuildStage(ResolveAccent()))
        {
            Debug.LogError("MainMenuShowcase: the serialized menu stage could not be built. " +
                           "Check the MainMenu root's hero and light references in SampleScene.", this);
            Destroy(gameObject);
            // The scene's plain start screen is what the player will see. It is a
            // complete menu, so onboarding may still present over it — the answer is
            // simply "not the showcase".
            SettleMenu(false);
            return;
        }

        HandOverScreen();
        BuildBrandEmblem(ResolveAccent());
        built = true;
        SettleMenu(true);

        StartCoroutine(RestampStageLayer());
    }

    // MenuLavaAmbience and PlanetAmbience build their decorative children in their own
    // Start, so the layer has to be re-stamped once those children exist. Purely a
    // culling-mask correction after the fact — the stage is already on screen.
    IEnumerator RestampStageLayer()
    {
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
        Canvas canvas = UIRootCanvas.Resolve();
        if (canvas == null) return false;

        Transform panel = canvas.transform.Find("StartPanel");
        if (panel == null || !panel.gameObject.activeInHierarchy) return false;

        menuCanvas = canvas;
        startPanel = panel.gameObject;
        return true;
    }

    // The ship is the live Rocket; the planet is not borrowed at all. Gameplay is only
    // consulted for two things: where the stage should stand, and the orbit radius the
    // ship has to end on. If anything is missing the menu does not take over, and the
    // plain start screen the scene already carries is shown instead.
    bool ResolveGameplayObjects()
    {
        rocketController = FindAnyObjectByType<RocketController>();
        if (rocketController == null || rocketController.planets.Count == 0) return false;

        firstPlanet = rocketController.planets[0];
        if (firstPlanet == null) return false;
        // The Hero Planet stands exactly where the first real planet does, so the ship's
        // orbit and the camera's pull-back still agree on the final frame.
        heroBasePosition = firstPlanet.position;

        rocket = rocketController.transform;
        rocket.position = gameplayRocketPosition;
        rocket.rotation = Quaternion.Euler(gameplayRocketEuler);
        rocket.localScale = gameplayRocketScale;
        rocketBaseScale = gameplayRocketScale;

        stageCamera = Camera.main;
        if (stageCamera == null || !stageCamera.orthographic) return false;

        cameraFollow = stageCamera.GetComponent<CameraFollow>();
        cameraBaseOrthoSize = gameplayCameraOrthoSize;
        return true;
    }

    void RestoreSerializedGameplayPreview()
    {
        RocketController controller = FindAnyObjectByType<RocketController>();
        if (controller != null)
        {
            controller.transform.position = gameplayRocketPosition;
            controller.transform.rotation = Quaternion.Euler(gameplayRocketEuler);
            controller.transform.localScale = gameplayRocketScale;
        }
        Camera camera = Camera.main;
        if (camera != null)
        {
            camera.transform.position = gameplayCameraPosition;
            camera.transform.rotation = Quaternion.identity;
            camera.orthographicSize = gameplayCameraOrthoSize;
        }
    }

    // The hero world's accent, read from the ambience registry so the stage lighting,
    // the void stain and the emblem all agree with the planet on screen. Lava registers
    // its own aura colour; if that ever stops resolving the menu falls back rather than
    // lighting the frame with a colour nothing in it wears.
    static Color ResolveAccent()
    {
        Color accent = PlanetAmbience.AccentColorFor(HeroThemeName, FallbackAccent);
        accent.a = 1f;
        return accent;
    }

    // The wordmark is not UI: it is a lit object on the stage, and the only title the
    // menu has. There is no flat label behind it to fall back to.
    void BuildBrandEmblem(Color accent)
    {
        if (stageCamera == null || hero == null) return;

        brandEmblem = GetComponent<MenuBrandEmblem>();
        if (brandEmblem == null)
        {
            Debug.LogError("MainMenuShowcase: required serialized MenuBrandEmblem component is missing.", this);
            return;
        }
        if (brandEmblem.Build(stage, stageCamera, menuCanvas, hero, heroBodyRadius, accent))
        {
            ApplyStageLayer(stage);
            return;
        }

        Debug.LogError("MainMenuShowcase: the brand emblem bake could not be loaded, so the menu " +
                       "has no title. Check Resources/Menu/brand_emblem.", this);
        Destroy(brandEmblem);
        brandEmblem = null;
    }

    // The UI itself is untouched: same buttons, same positions, same navigation. The
    // panel's own fill is the only thing that changes — from an opaque plate to a dim
    // over the stage.
    void HandOverScreen()
    {
        startPanelDim = startPanel.GetComponent<Image>();
        if (startPanelDim == null) return;

        startPanelDimColor = startPanelDim.color;
        // Still dims the stage enough for the emblem and buttons to stay legible, but
        // no longer hides it. Raycasting is unaffected by alpha.
        startPanelDim.color = new Color(0.02f, 0.035f, 0.09f, 0.22f);
    }

    void Restore()
    {
        if (startPanelDim != null) startPanelDim.color = startPanelDimColor;

        // A hand-over has already put every borrowed object back, in its own order and
        // on the exact frame the game took over. This is the path for every other exit —
        // and nothing is given back that was never taken.
        if (borrowed && !handedOver) ReturnBorrowedObjects();
    }

    // Undoes everything the menu did to objects it does not own. The Hero Planet is not
    // one of them — it is the menu's, so it is destroyed rather than restored.
    void ReturnBorrowedObjects()
    {
        if (heroRocket != null) heroRocket.Release();

        if (rocket != null)
        {
            rocket.position = gameplayRocketPosition;
            rocket.rotation = Quaternion.Euler(gameplayRocketEuler);
            rocket.localScale = gameplayRocketScale;
        }

        DestroyHeroPlanet();
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

        if (stageCamera != null)
        {
            stageCamera.transform.position = gameplayCameraPosition;
            stageCamera.transform.rotation = Quaternion.identity;
            stageCamera.orthographicSize = cameraBaseOrthoSize;
        }
        if (cameraFollow != null) cameraFollow.enabled = true;

        GameManager.isIntroPlaying = false;
        PresentationGate.Release(PresentationGate.Kind.MenuIntro);
    }

    void DestroyHeroPlanet()
    {
        if (hero == null) return;
        Destroy(hero.gameObject);
        hero = null;
    }

    // ─── Stage ───────────────────────────────────────────────────────────────

    bool BuildStage(Color accent)
    {
        int layer = LayerMask.NameToLayer(StageLayerName);
        if (layer >= 0) gameObject.layer = layer;

        // From here on the menu is holding the camera, the ship and the world's lights.
        // Anything that goes wrong past this line still has to give them back.
        borrowed = true;

        // This root is the stage: the hero world and the light rig are its serialized
        // children. It is parked on the hero world for now — where the stage, and with it
        // the camera, finally sits is a share of the visible height, which is only known
        // once the hero has been measured and framed. See PlaceStage.
        stage = transform;
        stage.position = heroBasePosition;

        if (!MeasureHeroPlanet()) return false;

        PrepareCamera();
        BuildLights(accent);
        PrepareHero(accent);

        float halfHeight = stageOrtho;
        float halfWidth = stageOrtho * stageCamera.aspect;

        MenuSpaceBackdrop backdrop = GetComponent<MenuSpaceBackdrop>();
        if (backdrop == null)
        {
            Debug.LogError("MainMenuShowcase: required serialized MenuSpaceBackdrop component is missing.", this);
            return false;
        }
        backdrop.Build(halfWidth, halfHeight, accent);

        heroRocket = GetComponent<MenuHeroRocket>();
        if (heroRocket == null)
        {
            Debug.LogError("MainMenuShowcase: required serialized MenuHeroRocket component is missing.", this);
            return false;
        }
        // The orbit is capped to the frame so the ship can never swing off the side of a
        // narrow screen. It stays flat in the hero's own z plane: the hero is a sprite,
        // so depth would only push the ship out of the plane gameplay hands it back on.
        heroRocket.menuScale = RocketMenuScale;
        // The margin reserved at each side is the ship's half-width, not its half-length:
        // at the far left and right of the ellipse the ship is flying vertically, so it is
        // its beam that has to fit, not its nose-to-tail. Reserving the longer of the two
        // pulled the orbit in tighter than the planet itself and left the ship permanently
        // on top of the hero world.
        heroRocket.maxRadiusX = Mathf.Max(0.2f,
            halfWidth - GetRocketOrbitHalfHeight() * RocketMenuScale * 1.15f - 0.12f);
        // The ship is in front of the world for the whole lap — it never passes behind it.
        heroRocket.Build(hero, heroBodyRadius, rocket, 12);

        HideGameplayPlanets();
        ApplyStageLayer(stage);
        return true;
    }

    // The hero world is already in the scene — a serialized LavaHero.prefab instance under
    // this root — so there is nothing to load or spawn. It is only measured and moved onto
    // the spot the first gameplay planet took. Nothing gameplay owns is touched here.
    bool MeasureHeroPlanet()
    {
        if (hero == null) return false;
        hero.position = heroBasePosition;

        // Authored at its final size: the camera frames the planet, the planet is never
        // scaled to the camera.
        heroOriginalScale = hero.localScale;
        heroMenuScale = heroOriginalScale;
        // The same measurement gameplay uses for its own planets, so the ship's orbit and
        // the ambience below it are sized off one source of truth. Taken once, because the
        // spin swings the AABB.
        heroBodyRadius = PlanetPresentation.GetBodyRadius(hero);
        return heroBodyRadius > 0.01f;
    }

    // The game camera is not parked and replaced — it is simply pulled in to the
    // framing the showcase was composed against, and pushed back out again on launch.
    void PrepareCamera()
    {
        if (cameraFollow != null) cameraFollow.enabled = false;

        FrameHero();
        PlaceStage();
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

    // Three serialized 2D lights, no shadows and no normal maps: the whole lighting rig
    // costs a handful of blend operations. Only their colour and intensity are set here,
    // and entirely from the theme's accent — so a future theme lights itself correctly the
    // moment it registers, without the scene needing to be re-authored.
    void BuildLights(Color accent)
    {
        // Ambient fill — heavily desaturated accent. This is what makes Ice read cold
        // and Lava read hot before any key light lands. A very wide point light rather
        // than a global one: URP 2D allows a single global light per sorting layer, and
        // that slot already belongs to the gameplay scene.
        if (fillLight != null)
        {
            fillLight.color = Color.Lerp(accent, Color.white, 0.46f);
            // Dimmer than the key by a clear margin: that gap is what gives the disc a lit
            // side and a shadow side instead of an evenly-washed ball.
            fillLight.intensity = 0.46f;
            fillLight.falloffIntensity = 0.28f;
        }

        // Key — the local sun, and a sun is a sun on every planet. Almost pure warm
        // white with a trace of the theme, so Natural reads as sunlight instead of
        // green light while Ice still resolves cold once fill and rim land on it.
        if (keyLight != null)
        {
            keyLight.color = Color.Lerp(accent, KeyLightWarm, 0.76f);
            keyLight.intensity = 1.12f;
            keyLight.falloffIntensity = 0.5f;   // wider hot side: the surface detail reads
        }

        // Rim — pure accent from the opposite side. This is what carries the theme's
        // colour and gives the sprite a silhouette instead of a flat disc. Saturated
        // rather than pale, and tight: it should draw a bright edge along the dark side,
        // not add another wash of light to the whole planet.
        if (rimLight != null)
        {
            rimLight.color = Color.Lerp(accent, Color.white, 0.12f);
            rimLight.intensity = 0.92f;
            rimLight.falloffIntensity = 0.86f;
        }
    }

    // The hero world arrives as bare rock. Everything that makes it read as a live
    // volcano is added here: the light rig around it, its atmosphere, and the volcanic
    // pass on its surface.
    void PrepareHero(Color accent)
    {
        heroPhase = Random.Range(0f, Mathf.PI * 2f);
        hero.position = heroBasePosition;

        PlaceLights(heroBodyRadius);
        BuildAura(accent, heroBodyRadius);

        // The menu's own volcanic pass. MenuPlanetLife is deliberately not added: it
        // dresses living worlds with pollen, petals and birds, none of which belong on
        // molten rock.
        heroLava = hero.GetComponent<MenuLavaAmbience>();
        if (heroLava == null)
            Debug.LogError("MainMenuShowcase: serialized Lava hero is missing MenuLavaAmbience.", hero);

        // One turn every few minutes: the world is alive, and the caldera stays in the
        // corner of the frame it was composed for for as long as the menu is up.
        MenuHeroSpin spin = hero.GetComponent<MenuHeroSpin>();
        if (spin != null) spin.Configure(true);
    }

    // The camera frames the planet; the planet is never resized. This is the only place
    // the stage's ortho size comes from, so every measurement downstream — backdrop,
    // emblem clearance, light reach — follows the Hero Planet's real dimensions.
    void FrameHero()
    {
        stageOrtho = Mathf.Max(0.5f, heroBodyRadius / HeroHeightFraction);
        hero.localScale = heroOriginalScale;
        stageCamera.orthographicSize = stageOrtho;
    }

    // Centres the stage — the camera, the backdrop and the light rig — above the hero
    // world, which is what drops the world below the middle of the frame. The world
    // itself is never moved off its own position: the frame moves around it.
    void PlaceStage()
    {
        float visibleHeight = stageOrtho * 2f;
        stage.position = heroBasePosition + new Vector3(0f, visibleHeight * HeroCenterHeightFraction, 0f);
        // The hero came in as a child of the stage, so it travelled with it.
        hero.position = heroBasePosition;
    }

    // The whole route is real and already spawned, including the first planet the Hero
    // Planet stands in for. It waits off-frame until the camera has pulled back far
    // enough for it to belong there. Deactivating is all that happens to it.
    void HideGameplayPlanets()
    {
        hiddenPlanetFader = new MenuFader();

        for (int i = 0; i < rocketController.planets.Count; i++)
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
            float reach = stageOrtho * Mathf.Max(1f, stageCamera.aspect) * 2.6f;
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
        FrameHero();
        MenuSpaceBackdrop backdrop = GetComponent<MenuSpaceBackdrop>();
        if (backdrop != null)
            backdrop.FitViewport(stageOrtho * stageCamera.aspect, stageOrtho);
        PlaceLights(heroBodyRadius);
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

        if (startPanel != null)
        {
            panelGroup = startPanel.GetComponent<CanvasGroup>();
            if (panelGroup == null) panelGroup = startPanel.AddComponent<CanvasGroup>();
            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;
        }

        CaptureHud();
        // Freezing the volcanic pass with the backdrop is what lets the stage dissolve:
        // a layer that rewrites its own alpha every frame would fight the fade, and the
        // ambience clears its live particles the moment it stops.
        stageFader = MenuFader.Capture(stage, typeof(MenuSpaceBackdrop), typeof(MenuLavaAmbience));
        stageFader.Freeze();
        if (hiddenPlanetFader != null) hiddenPlanetFader.Freeze();

        // The route ahead comes back on immediately but invisible, so it can fade up
        // rather than appear.
        for (int i = 0; i < hiddenPlanets.Count; i++)
            if (hiddenPlanets[i] != null) hiddenPlanets[i].SetActive(true);
        if (hiddenPlanetFader != null) hiddenPlanetFader.SetAlpha(0f);

        MenuSpaceBackdrop launchBackdrop = GetComponent<MenuSpaceBackdrop>();
        FitLaunchViewport(launchBackdrop);

        Vector3 cameraStart = stageCamera.transform.position;
        float gameplayOrbitRadius = rocketController.GetOrbitEnvelopeRadius(firstPlanet);
        float lookAhead = cameraFollow != null ? cameraFollow.lookAheadY : 2f;

        float elapsed = 0f;
        while (elapsed < LaunchDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / LaunchDuration);

            // The camera leads, everything else settles inside its move.
            float camera = SmootherStep(t);
            float settle = SmootherStep(Curve(t, 0.06f, 0.94f));
            float dissolve = SmootherStep(Curve(t, 0.08f, 0.82f));
            float reveal = SmootherStep(Curve(t, 0.18f, 0.95f));

            idleBlend = 1f - settle;

            // The ellipse becomes the circle RocketController orbits on, tracking the
            // planet as it shrinks so the two never disagree.
            // The ellipse becomes the circle RocketController will orbit — measured on the
            // REAL first planet, not on the Hero Planet, because that is the body the ship
            // is being handed to.
            if (heroRocket != null)
            {
                heroRocket.BeginHandOver(gameplayOrbitRadius);
                heroRocket.SetHandOverProgress(settle);
            }

            // Camera: the stage framing back out to the gameplay framing, aiming where
            // CameraFollow will pick it up so the two agree on the final frame.
            stageCamera.orthographicSize = Mathf.Lerp(stageOrtho, cameraBaseOrthoSize, camera);
            Vector3 gameplayCamera = new Vector3(0f, rocket.position.y + lookAhead, -10f);
            stageCamera.transform.position = Vector3.Lerp(cameraStart, gameplayCamera, camera);

            FitLaunchViewport(launchBackdrop);
            ShowWorldSky(reveal);

            // The menu backdrop only dissolves once the gameplay void is refit and
            // rising on the same frame — never ahead of it during the pull-back.
            float menuDissolve = Mathf.Min(dissolve, reveal);
            if (stageFader != null) stageFader.SetAlpha(1f - menuDissolve);
            if (hiddenPlanetFader != null) hiddenPlanetFader.SetAlpha(reveal);
            SetHudAlpha(reveal);

            float lightFade = 1f - dissolve;
            if (heroLava != null) heroLava.SetLightScale(lightFade);
            if (fillLight != null) fillLight.intensity = 0.46f * lightFade;
            if (keyLight != null) keyLight.intensity = 1.12f * lightFade;
            if (rimLight != null) rimLight.intensity = 0.92f * lightFade;
            for (int i = 0; i < globalLights.Count; i++)
                if (globalLights[i] != null)
                    globalLights[i].intensity = globalLightIntensities[i] * reveal;

            // The panel does not blink out: it lifts and dissolves in the first beat.
            if (panelGroup != null)
            {
                float ui = SmootherStep(Curve(t, 0f, 0.30f));
                panelGroup.alpha = 1f - ui;
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

    static float SmootherStep(float value)
    {
        float t = Mathf.Clamp01(value);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    float GetRocketOrbitHalfHeight()
    {
        SpriteRenderer renderer = rocket != null ? rocket.GetComponent<SpriteRenderer>() : null;
        if (renderer == null || renderer.sprite == null) return 0.25f;
        return Mathf.Max(0.05f, renderer.sprite.bounds.extents.y * Mathf.Abs(rocketBaseScale.y));
    }

    void FitLaunchViewport(MenuSpaceBackdrop backdrop)
    {
        if (stageCamera == null) return;

        float halfHeight = stageCamera.orthographicSize;
        float halfWidth = halfHeight * stageCamera.aspect;
        if (backdrop != null) backdrop.FitViewport(halfWidth, halfHeight);
        SpaceEnvironment.SyncCoverToViewport();
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

        // The ship is handed to the REAL first planet. The Hero Planet was only ever
        // standing in the same spot; gameplay never learns it existed.
        bool adopted = false;
        if (rocketController != null && firstPlanet != null && rocket != null)
        {
            adopted = rocketController.RestoreContinueState(new RocketController.ContinueState
            {
                planet = firstPlanet,
                planetRelativePosition = rocket.position - firstPlanet.position,
                rotation = rocket.rotation,
                angle = angleDegrees,
                orbitRadius = Vector3.Distance(rocket.position, firstPlanet.position),
                orbitDirection = direction
            });
        }

        // Presentation ends here: the Hero Planet is destroyed the moment gameplay owns
        // the ship again.
        DestroyHeroPlanet();

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

        // If the pose could not be adopted the game still has to start. RunSession.Begin
        // already attached the ship to a valid planet before the launch sequence ran, so
        // this is a presentation seam rather than a broken run — but it is never silent,
        // because a ship that visibly jumps on the hand-over frame means the first planet
        // is not the one the menu framed itself around.
        if (!adopted && rocketController != null)
        {
            Debug.LogError("MainMenuShowcase: the hand-over pose was refused — the first " +
                           "gameplay planet is not in RocketController's planet list. The run " +
                           "continues from its own orbit state.", this);
            rocketController.enabled = true;
        }

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
