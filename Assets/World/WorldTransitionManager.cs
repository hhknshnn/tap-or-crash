using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Coordinates the premium world-entry presentation. Reads metadata from WorldCatalog
// and never mutates progression, score, or save data.
[DisallowMultipleComponent]
public sealed class WorldTransitionManager : MonoBehaviour
{
    const float CameraFocusDuration = 0.5f;
    const float CameraFocusZoom = 0.05f;
    const float BackgroundFadeDuration = 0.6f;
    const float PlanetScaleDuration = 0.35f;
    const float PlanetScaleDip = 0.06f;
    const float IntroHoldDuration = 1.0f;
    const float IntroFadeOutDuration = 0.35f;
    const float IntroFadeInDuration = 0.28f;
    const float SafetyPollInterval = 0.08f;

    // Emoji formatting marks. They carry no glyph of their own, so they are stripped
    // before the icon font is asked whether it can draw a world's emoji.
    const char VariationSelector15 = (char)0xFE0E;
    const char VariationSelector16 = (char)0xFE0F;
    const char ZeroWidthJoiner = (char)0x200D;

    static WorldTransitionManager instance;

    public static bool IsPlaying =>
        instance != null && instance.transitionActive;

    public static bool IsPendingOrPlaying =>
        instance != null && (instance.transitionActive || instance.activeTransition != null);

    GameObject visualRoot;
    CanvasGroup overlayGroup;
    RectTransform cardRect;
    Image iconGlow;
    Image iconDisc;
    Image iconRim;
    TextMeshProUGUI iconLabel;
    TextMeshProUGUI titleLabel;
    TextMeshProUGUI counterLabel;
    TextMeshProUGUI subtitleLabel;

    GameManager observedManager;
    bool gameplayHeld;
    float timeScaleBeforeHold = 1f;
    bool transitionActive;
    bool pendingNaturalIntro;
    int lastTransitionScore = -1;
    Coroutine activeTransition;

    readonly int[] announcedWorlds = new int[WorldCatalog.Count];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstall() => SceneInstaller.RunOnEveryScene(Install);

    static void Install()
    {
        if (instance != null) return;

        Canvas canvas = UIRootCanvas.Resolve();
        if (canvas == null) return;

        GameObject host = new GameObject("WorldTransitionManager");
        host.transform.SetParent(canvas.transform, false);
        RectTransform hostRect = host.AddComponent<RectTransform>();
        hostRect.anchorMin = Vector2.zero;
        hostRect.anchorMax = Vector2.one;
        hostRect.offsetMin = Vector2.zero;
        hostRect.offsetMax = Vector2.zero;
        host.AddComponent<WorldTransitionManager>();
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        BuildVisual();
        ResetAnnouncedWorlds();
    }

    void OnDestroy()
    {
        if (instance == this)
        {
            // Time.timeScale survives a scene load, so a teardown mid-card would hand the
            // next scene a frozen world.
            ReleaseGameplayHold();
            PresentationGate.Release(PresentationGate.Kind.WorldTransition);
            instance = null;
        }
    }

    void Start()
    {
        observedManager = GameManager.instance;
        if (observedManager != null)
            observedManager.ScoreChanged += OnScoreChanged;

        pendingNaturalIntro = true;
    }

    void OnEnable()
    {
        if (observedManager == null)
            observedManager = GameManager.instance;
        if (observedManager != null)
            observedManager.ScoreChanged -= OnScoreChanged;
        if (observedManager != null)
            observedManager.ScoreChanged += OnScoreChanged;
    }

    void OnDisable()
    {
        if (observedManager != null)
            observedManager.ScoreChanged -= OnScoreChanged;
    }

    void Update()
    {
        if (!GameManager.isGameStarted || GameManager.isGameOver) return;

        if (pendingNaturalIntro
            && !GameManager.isIntroPlaying
            && announcedWorlds[0] == 0
            && activeTransition == null)
        {
            pendingNaturalIntro = false;
            TryBeginTransition(0, focusCurrentPlanet: true);
        }
    }

    void OnScoreChanged(int score)
    {
        if (!WorldCatalog.ShouldTransitionAfterLanding(score)) return;
        if (score == lastTransitionScore) return;

        int worldIndex = WorldCatalog.WorldIndexForNextPlanet(score);
        if (worldIndex <= 0 || worldIndex >= WorldCatalog.Count) return;

        TryBeginTransition(worldIndex, focusCurrentPlanet: false);
    }

    void TryBeginTransition(int worldIndex, bool focusCurrentPlanet, bool force = false)
    {
        if (worldIndex < 0 || worldIndex >= WorldCatalog.Count) return;
        if (!force && announcedWorlds[worldIndex] != 0) return;

        WorldDefinition world = WorldCatalog.GetByIndex(worldIndex);
        if (world == null) return;

        // A transition that is already on screen is never cut short. Stopping it would
        // strand the presentation gate and IsPlaying, which blocks every later transition
        // and leaves gameplay input disabled for the rest of the run.
        if (transitionActive) return;

        if (activeTransition != null)
            StopCoroutine(activeTransition);

        activeTransition = StartCoroutine(
            RunTransitionWhenSafe(world, worldIndex, focusCurrentPlanet, force));
    }

    IEnumerator RunTransitionWhenSafe(
        WorldDefinition world,
        int worldIndex,
        bool focusCurrentPlanet,
        bool force)
    {
        while (!PresentationGate.CanBeginWorldTransition())
            yield return new WaitForSecondsRealtime(SafetyPollInterval);

        if (!force && announcedWorlds[worldIndex] != 0)
        {
            activeTransition = null;
            yield break;
        }

        announcedWorlds[worldIndex] = 1;

        if (!focusCurrentPlanet)
            lastTransitionScore = PlanetSpawner.PlanetsPerLevel * worldIndex;

        yield return PlayTransition(world, focusCurrentPlanet);
        activeTransition = null;
    }

    IEnumerator PlayTransition(WorldDefinition world, bool focusCurrentPlanet)
    {
        transitionActive = true;
        PresentationGate.Acquire(PresentationGate.Kind.WorldTransition);

        CameraFollow camera = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        RocketController rocket = GameManager.instance != null
            ? GameManager.instance.playerRocket
            : null;
        Transform planetTarget = ResolvePlanetTarget(rocket, focusCurrentPlanet);

        Vector3 planetBaseScale = planetTarget != null ? planetTarget.localScale : Vector3.one;

        // Every exit goes through the finally: a stopped coroutine, a destroyed manager or
        // a scene teardown all have to give the gate back and put the planet's own scale
        // back, or the run continues with a locked gate and a shrunken planet.
        try
        {
            Coroutine cameraRoutine = null;
            if (camera != null)
                cameraRoutine = StartCoroutine(camera.PlayTransitionFocus(CameraFocusDuration, CameraFocusZoom));

            SpaceEnvironment.CrossfadeToTheme(world.BackgroundTheme, BackgroundFadeDuration);

            float parallelElapsed = 0f;
            float parallelDuration = Mathf.Max(CameraFocusDuration, BackgroundFadeDuration);
            while (parallelElapsed < parallelDuration)
            {
                parallelElapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (planetTarget != null)
            {
                float scaleElapsed = 0f;
                while (scaleElapsed < PlanetScaleDuration)
                {
                    scaleElapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(scaleElapsed / PlanetScaleDuration);
                    float eased = SmootherStep(t);
                    float scaleEnvelope = Mathf.Sin(eased * Mathf.PI);
                    planetTarget.localScale = planetBaseScale * (1f - PlanetScaleDip * scaleEnvelope);
                    yield return null;
                }
            }

            if (cameraRoutine != null)
                yield return cameraRoutine;

            // The world holds still while it introduces itself. The approach above plays
            // over live gameplay — the freeze starts with the card and ends with it, so
            // the run is handed back in exactly the state it was paused in. Every timer
            // in this sequence is unscaled, so the presentation itself keeps running.
            HoldGameplay();

            PopulateIntro(world);
            yield return FadeIntroIn();

            yield return new WaitForSecondsRealtime(IntroHoldDuration);

            yield return FadeIntroOut();
        }
        finally
        {
            ReleaseGameplayHold();

            if (planetTarget != null)
                planetTarget.localScale = planetBaseScale;

            // An interrupted fade would otherwise leave the card — and its raycast
            // blocker — sitting on top of the run.
            if (overlayGroup != null) overlayGroup.alpha = 0f;
            if (visualRoot != null) visualRoot.SetActive(false);

            transitionActive = false;
            PresentationGate.Release(PresentationGate.Kind.WorldTransition);
        }

        if (PauseManager.instance != null)
            PauseManager.instance.ExecuteQueuedPause();
    }

    // The freeze the card is presented over, and the only gameplay state this class
    // touches. It mirrors PauseManager exactly: the world stops, the presentation keeps
    // running on unscaled time, and a finger that was already down is cleared so it
    // cannot fire a launch on the frame the world resumes.
    void HoldGameplay()
    {
        if (gameplayHeld) return;

        gameplayHeld = true;
        timeScaleBeforeHold = Time.timeScale;
        Time.timeScale = 0f;

        RocketController rocket = GameManager.instance != null
            ? GameManager.instance.playerRocket
            : null;
        if (rocket != null) rocket.CancelHoldInput();
    }

    void ReleaseGameplayHold()
    {
        if (!gameplayHeld) return;

        gameplayHeld = false;
        Time.timeScale = timeScaleBeforeHold;
    }

    static Transform ResolvePlanetTarget(RocketController rocket, bool focusCurrentPlanet)
    {
        if (rocket == null || rocket.planets == null || rocket.planets.Count == 0)
            return null;

        if (focusCurrentPlanet
            && rocket.TryCaptureContinueState(out RocketController.ContinueState state)
            && state.planet != null)
            return state.planet;
        if (focusCurrentPlanet)
            return rocket.planets[0];

        return rocket.GetUpcomingPlanetTransform();
    }

    void PopulateIntro(WorldDefinition world)
    {
        Color accent = PlanetAmbience.AccentColorFor(world.BackgroundTheme,
            new Color(0.55f, 0.90f, 0.65f, 1f));

        // The seal is the world's own colour, so a world that has no emoji glyph in the
        // UI font still arrives with its identity on the card.
        iconDisc.color = new Color(accent.r, accent.g, accent.b, 0.30f);
        iconGlow.color = new Color(accent.r, accent.g, accent.b, 0.28f);
        iconRim.color = new Color(accent.r, accent.g, accent.b, 0.85f);

        iconLabel.text = CanRenderGlyphs(world.IconEmoji) ? world.IconEmoji : string.Empty;
        titleLabel.text = world.FormattedTitle;
        counterLabel.text = world.FormattedPlanetCounter(1);
        subtitleLabel.text = world.Subtitle;
        subtitleLabel.color = accent;
    }

    // The UI font family carries no emoji, and TextMeshPro draws a missing glyph as a
    // tofu box. Asking the font first is what keeps a box off the card, and lets the
    // emoji come back on its own the day an emoji fallback is added to the family.
    bool CanRenderGlyphs(string text)
    {
        if (string.IsNullOrEmpty(text) || iconLabel.font == null) return false;

        // Variation selectors and joiners are formatting marks, not drawn glyphs: no font
        // carries them, so they would fail the check for every emoji.
        System.Text.StringBuilder drawn = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == VariationSelector15 || c == VariationSelector16 || c == ZeroWidthJoiner)
                continue;
            drawn.Append(c);
        }

        if (drawn.Length == 0) return false;

        uint[] missing;
        return iconLabel.font.HasCharacters(drawn.ToString(), out missing, true, false);
    }

    void BuildVisual()
    {
        UIDesign.EnsureInitialised();

        visualRoot = new GameObject("WorldTransitionOverlay");
        visualRoot.transform.SetParent(transform, false);

        RectTransform overlayRect = visualRoot.AddComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        // The world stops behind the card, and the scrim is what says so. It fades with
        // the card through the CanvasGroup, so the pause reads as one move rather than as
        // a panel appearing over a live game.
        Image scrim = visualRoot.AddComponent<Image>();
        scrim.color = new Color(0.012f, 0.018f, 0.045f, 0.58f);
        scrim.raycastTarget = true;

        overlayGroup = visualRoot.AddComponent<CanvasGroup>();
        overlayGroup.alpha = 0f;
        overlayGroup.interactable = false;
        overlayGroup.blocksRaycasts = true;

        GameObject card = new GameObject("IntroCard");
        card.transform.SetParent(visualRoot.transform, false);

        cardRect = card.AddComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.anchoredPosition = new Vector2(0f, 40f);
        cardRect.sizeDelta = new Vector2(680f, 360f);
        cardRect.localScale = Vector3.one * 0.96f;

        UIKit.MakeGlass(card, UIDesign.RadiusCard, UITinted.Role.GlassDeep, 1f, false);

        BuildWorldSeal(card.transform, new Vector2(0f, 122f), 96f);

        iconLabel = UIStyleKit.MakeLabel(card.transform, string.Empty,
            52f, UIDesign.TextMain, new Vector2(0f, 122f), new Vector2(120f, 90f),
            FontStyles.Normal);
        iconLabel.alignment = TextAlignmentOptions.Center;

        titleLabel = UIStyleKit.MakeLabel(card.transform, "NATURAL WORLD",
            UIDesign.TypeTitle, UIDesign.TextMain, new Vector2(0f, 28f), new Vector2(640f, 64f),
            FontStyles.Bold);
        UIKit.StyleDisplay(titleLabel, UIDesign.TypeTitle, UIDesign.TrackTitle, UIDesign.TextMain);

        counterLabel = UIStyleKit.MakeLabel(card.transform, "Planet 1 / 10",
            UIDesign.TypeLabel, UIDesign.TextSub, new Vector2(0f, -32f), new Vector2(640f, 40f),
            FontStyles.Bold);
        UIKit.StyleText(counterLabel, UIDesign.TypeLabel, UIDesign.TrackLabel, UIDesign.TextSub,
            FontStyles.Bold);

        subtitleLabel = UIStyleKit.MakeLabel(card.transform, "A peaceful beginning.",
            UIDesign.TypeBody, UIDesign.TextSub, new Vector2(0f, -84f), new Vector2(620f, 50f),
            FontStyles.Italic);
        UIKit.StyleText(subtitleLabel, UIDesign.TypeBody, 0f, UIDesign.TextSub, FontStyles.Italic);

        visualRoot.SetActive(false);
    }

    // The world's mark on the card: a halo, a tinted disc and a lit rim, all recoloured
    // per world in PopulateIntro. Built from the same glass language as every other
    // surface in the game, so the card belongs to the UI it appears over.
    void BuildWorldSeal(Transform parent, Vector2 anchoredPosition, float diameter)
    {
        iconGlow = CreateSealLayer(parent, "SealGlow", UIGlass.Glow,
            anchoredPosition, diameter * 2.1f);
        iconDisc = CreateSealLayer(parent, "SealDisc", UIGlass.Disc,
            anchoredPosition, diameter);
        iconRim = CreateSealLayer(parent, "SealRim", UIGlass.DiscRim,
            anchoredPosition, diameter);
    }

    static Image CreateSealLayer(Transform parent, string name, Sprite sprite,
        Vector2 anchoredPosition, float diameter)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(diameter, diameter);

        Image image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.raycastTarget = false;
        return image;
    }

    IEnumerator FadeIntroIn()
    {
        visualRoot.SetActive(true);
        overlayGroup.alpha = 0f;
        Vector2 restPosition = new Vector2(0f, 40f);
        cardRect.anchoredPosition = restPosition + Vector2.down * 26f;
        cardRect.localScale = Vector3.one * 0.93f;

        float elapsed = 0f;
        while (elapsed < IntroFadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float p = SmootherStep(elapsed / IntroFadeInDuration);
            float settle = 1f - Mathf.Pow(1f - p, 3f);
            overlayGroup.alpha = p;
            cardRect.anchoredPosition = Vector2.LerpUnclamped(
                restPosition + Vector2.down * 26f, restPosition, settle);
            cardRect.localScale = Vector3.one
                * (Mathf.Lerp(0.93f, 1f, settle) + Mathf.Sin(p * Mathf.PI) * 0.008f);
            yield return null;
        }

        overlayGroup.alpha = 1f;
        cardRect.anchoredPosition = restPosition;
        cardRect.localScale = Vector3.one;
    }

    IEnumerator FadeIntroOut()
    {
        float elapsed = 0f;
        Vector2 startPosition = cardRect.anchoredPosition;
        while (elapsed < IntroFadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float p = SmootherStep(elapsed / IntroFadeOutDuration);
            overlayGroup.alpha = 1f - p;
            cardRect.anchoredPosition = Vector2.LerpUnclamped(
                startPosition, startPosition + Vector2.up * 22f, p);
            cardRect.localScale = Vector3.one * Mathf.Lerp(1f, 0.985f, p);
            yield return null;
        }

        overlayGroup.alpha = 0f;
        visualRoot.SetActive(false);
    }

    void ResetAnnouncedWorlds()
    {
        for (int i = 0; i < announcedWorlds.Length; i++)
            announcedWorlds[i] = 0;
    }

    public static void ResetForNewRun()
    {
        if (instance == null) return;

        // A transition belonging to the finished run is stopped here rather than
        // left to expire. While one is still queued IsPendingOrPlaying stays true,
        // and that is the flag asteroids, moving orbits and the milestone notices
        // all wait on — a survivor would hold the new run's mechanics shut.
        // Stopping it disposes the iterator, so PlayTransition's finally still
        // gives back the gate, the gameplay hold and the planet's own scale.
        if (instance.activeTransition != null)
        {
            instance.StopCoroutine(instance.activeTransition);
            instance.activeTransition = null;
        }
        instance.ReleaseGameplayHold();
        instance.transitionActive = false;
        PresentationGate.Release(PresentationGate.Kind.WorldTransition);
        if (instance.overlayGroup != null) instance.overlayGroup.alpha = 0f;
        if (instance.visualRoot != null) instance.visualRoot.SetActive(false);

        instance.ResetAnnouncedWorlds();
        instance.lastTransitionScore = -1;
        instance.pendingNaturalIntro = true;
    }

    public static void IntroduceCurrentWorld()
    {
        if (instance == null || GameManager.instance == null) return;

        int score = Mathf.Max(0, GameManager.instance.GetScore());
        int worldIndex = WorldCatalog.WorldIndexForScore(score);
        instance.TryBeginTransition(worldIndex, focusCurrentPlanet: true, force: true);
    }

    static float SmootherStep(float value)
    {
        float t = Mathf.Clamp01(value);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }
}
