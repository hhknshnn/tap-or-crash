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
    const float PlanetScaleStart = 0.94f;
    const float IntroHoldDuration = 1.0f;
    const float IntroFadeOutDuration = 0.35f;
    const float IntroFadeInDuration = 0.28f;
    const float SafetyPollInterval = 0.08f;

    static WorldTransitionManager instance;

    public static bool IsPlaying =>
        instance != null && instance.transitionActive;

    GameObject visualRoot;
    CanvasGroup overlayGroup;
    RectTransform cardRect;
    TextMeshProUGUI iconLabel;
    TextMeshProUGUI titleLabel;
    TextMeshProUGUI counterLabel;
    TextMeshProUGUI subtitleLabel;

    GameManager observedManager;
    bool transitionActive;
    bool pendingNaturalIntro;
    bool skipNaturalOnThisRun;
    int lastTransitionScore = -1;
    Coroutine activeTransition;

    readonly int[] announcedWorlds = new int[WorldCatalog.Count];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstall() => SceneInstaller.RunOnEveryScene(Install);

    static void Install()
    {
        if (instance != null) return;

        Canvas canvas = Object.FindAnyObjectByType<Canvas>();
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
        skipNaturalOnThisRun = GameManager.isRestart;
        BuildVisual();
        ResetAnnouncedWorlds();
    }

    void OnDestroy()
    {
        if (instance == this)
        {
            PresentationGate.Release(PresentationGate.Kind.WorldTransition);
            instance = null;
        }
    }

    void Start()
    {
        observedManager = GameManager.instance;
        if (observedManager != null)
            observedManager.ScoreChanged += OnScoreChanged;

        if (!skipNaturalOnThisRun)
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
            && !skipNaturalOnThisRun
            && announcedWorlds[0] == 0
            && activeTransition == null)
        {
            pendingNaturalIntro = false;
            TryBeginTransition(0, isNaturalStart: true);
        }
    }

    void OnScoreChanged(int score)
    {
        if (!WorldCatalog.ShouldTransitionAfterLanding(score)) return;
        if (score == lastTransitionScore) return;

        int worldIndex = WorldCatalog.WorldIndexForNextPlanet(score);
        if (worldIndex <= 0 || worldIndex >= WorldCatalog.Count) return;

        TryBeginTransition(worldIndex, isNaturalStart: false);
    }

    void TryBeginTransition(int worldIndex, bool isNaturalStart)
    {
        if (worldIndex < 0 || worldIndex >= WorldCatalog.Count) return;
        if (announcedWorlds[worldIndex] != 0) return;

        WorldDefinition world = WorldCatalog.GetByIndex(worldIndex);
        if (world == null) return;

        if (activeTransition != null)
            StopCoroutine(activeTransition);

        activeTransition = StartCoroutine(RunTransitionWhenSafe(world, worldIndex, isNaturalStart));
    }

    IEnumerator RunTransitionWhenSafe(WorldDefinition world, int worldIndex, bool isNaturalStart)
    {
        while (!PresentationGate.CanBeginWorldTransition())
            yield return new WaitForSecondsRealtime(SafetyPollInterval);

        if (announcedWorlds[worldIndex] != 0)
        {
            activeTransition = null;
            yield break;
        }

        announcedWorlds[worldIndex] = 1;

        if (!isNaturalStart)
            lastTransitionScore = PlanetSpawner.PlanetsPerLevel * worldIndex;

        yield return PlayTransition(world, isNaturalStart);
        activeTransition = null;
    }

    IEnumerator PlayTransition(WorldDefinition world, bool isNaturalStart)
    {
        transitionActive = true;
        PresentationGate.Acquire(PresentationGate.Kind.WorldTransition);

        CameraFollow camera = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        RocketController rocket = GameManager.instance != null
            ? GameManager.instance.playerRocket
            : null;
        Transform planetTarget = ResolvePlanetTarget(rocket, isNaturalStart);

        Vector3 planetBaseScale = Vector3.one;
        if (planetTarget != null)
            planetBaseScale = planetTarget.localScale;

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
                float eased = 1f - (1f - t) * (1f - t);
                planetTarget.localScale = planetBaseScale * Mathf.Lerp(PlanetScaleStart, 1f, eased);
                yield return null;
            }
            planetTarget.localScale = planetBaseScale;
        }

        if (cameraRoutine != null)
            yield return cameraRoutine;

        PopulateIntro(world);
        yield return FadeIntroIn();

        yield return new WaitForSecondsRealtime(IntroHoldDuration);

        yield return FadeIntroOut();

        if (planetTarget != null)
            planetTarget.localScale = planetBaseScale;

        transitionActive = false;
        PresentationGate.Release(PresentationGate.Kind.WorldTransition);

        if (PauseManager.instance != null)
            PauseManager.instance.ExecuteQueuedPause();
    }

    static Transform ResolvePlanetTarget(RocketController rocket, bool isNaturalStart)
    {
        if (rocket == null || rocket.planets == null || rocket.planets.Count == 0)
            return null;

        if (isNaturalStart)
            return rocket.planets[0];

        return rocket.GetUpcomingPlanetTransform();
    }

    void PopulateIntro(WorldDefinition world)
    {
        Color accent = PlanetAmbience.AccentColorFor(world.BackgroundTheme,
            new Color(0.55f, 0.90f, 0.65f, 1f));

        iconLabel.text = world.IconEmoji;
        titleLabel.text = world.FormattedTitle;
        counterLabel.text = world.FormattedPlanetCounter(1);
        subtitleLabel.text = world.Subtitle;
        subtitleLabel.color = accent;
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

        Image scrim = visualRoot.AddComponent<Image>();
        scrim.color = new Color(0f, 0f, 0f, 0f);
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
        cardRect.sizeDelta = new Vector2(680f, 320f);
        cardRect.localScale = Vector3.one * 0.96f;

        UIKit.MakeGlass(card, UIDesign.RadiusCard, UITinted.Role.GlassDeep, 1f, false);

        iconLabel = UIStyleKit.MakeLabel(card.transform, "🌍",
            72f, UIDesign.TextMain, new Vector2(0f, 108f), new Vector2(120f, 90f),
            FontStyles.Normal);
        iconLabel.alignment = TextAlignmentOptions.Center;

        titleLabel = UIStyleKit.MakeLabel(card.transform, "NATURAL WORLD",
            UIDesign.TypeTitle, UIDesign.TextMain, new Vector2(0f, 36f), new Vector2(640f, 70f),
            FontStyles.Bold);
        UIKit.StyleDisplay(titleLabel, UIDesign.TypeTitle, UIDesign.TrackTitle, UIDesign.TextMain);

        counterLabel = UIStyleKit.MakeLabel(card.transform, "Planet 1 / 10",
            UIDesign.TypeLabel, UIDesign.TextSub, new Vector2(0f, -18f), new Vector2(640f, 40f),
            FontStyles.Bold);
        UIKit.StyleText(counterLabel, UIDesign.TypeLabel, UIDesign.TrackLabel, UIDesign.TextSub,
            FontStyles.Bold);

        subtitleLabel = UIStyleKit.MakeLabel(card.transform, "A peaceful beginning.",
            UIDesign.TypeBody, UIDesign.TextSub, new Vector2(0f, -68f), new Vector2(620f, 50f),
            FontStyles.Italic);
        UIKit.StyleText(subtitleLabel, UIDesign.TypeBody, 0f, UIDesign.TextSub, FontStyles.Italic);

        visualRoot.SetActive(false);
    }

    IEnumerator FadeIntroIn()
    {
        visualRoot.SetActive(true);
        overlayGroup.alpha = 0f;
        cardRect.localScale = Vector3.one * 0.96f;

        float elapsed = 0f;
        while (elapsed < IntroFadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, elapsed / IntroFadeInDuration);
            overlayGroup.alpha = p;
            cardRect.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, p);
            yield return null;
        }

        overlayGroup.alpha = 1f;
        cardRect.localScale = Vector3.one;
    }

    IEnumerator FadeIntroOut()
    {
        float elapsed = 0f;
        while (elapsed < IntroFadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float p = elapsed / IntroFadeOutDuration;
            overlayGroup.alpha = 1f - p;
            cardRect.localScale = Vector3.one * Mathf.Lerp(1f, 1.02f, p);
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
        instance.ResetAnnouncedWorlds();
        instance.lastTransitionScore = -1;
        instance.pendingNaturalIntro = false;
        instance.skipNaturalOnThisRun = true;
    }
}
