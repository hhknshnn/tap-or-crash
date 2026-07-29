using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Runtime HUD showing where the player is inside the current world.
//
// A world is a themed set of PlanetSpawner.PlanetsPerLevel planets (Natural, Ice,
// Lava, ...). The readout answers three questions at a glance:
//   1. which world am I in            → "NATURAL WORLD"
//   2. which planet am I flying to    → "PLANET 3 / 10"
//   3. how far into the world am I    → one pip per planet, filled = landed
//
// Pips make the state unambiguous at score 0: nothing is filled yet and the first
// pip pulses as the target, so the HUD never claims progress the player has not made.
// The whole visual is hidden once the themed worlds run out (endless score mode).
public sealed class LevelProgressUI : MonoBehaviour
{
    public static int TotalPlanets => PlanetSpawner.PlanetsPerLevel;

    private static readonly Color DefaultAccent = new Color(0.34f, 0.86f, 1f, 1f);
    private static readonly Color PipEmpty = new Color(0.62f, 0.72f, 0.90f, 0.22f);

    private static LevelProgressUI instance;

    private GameManager observedManager;
    private GameObject visualRoot;
    private CanvasGroup visualGroup;
    private TextMeshProUGUI worldLabel;
    private TextMeshProUGUI counterLabel;
    private Image[] pips;
    private RectTransform[] pipRects;

    private Color accent = DefaultAccent;
    private int currentScore;
    private int shownScore = -1;
    private int landedInWorld;
    private bool inThemedWorld;

    // Punch animation for the pip that was just completed.
    private int poppedPip = -1;
    private float popTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstall() => SceneInstaller.RunOnEveryScene(Install);

    static void Install()
    {
        if (instance != null) return;

        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return;

        Transform gameUi = canvas.transform.Find("GameUI");
        Transform parent = gameUi != null ? gameUi : canvas.transform;

        GameObject host = new GameObject("LevelProgressUI");
        host.transform.SetParent(parent, false);
        RectTransform hostRect = host.AddComponent<RectTransform>();
        hostRect.anchorMin = Vector2.zero;
        hostRect.anchorMax = Vector2.one;
        hostRect.offsetMin = Vector2.zero;
        hostRect.offsetMax = Vector2.zero;
        host.AddComponent<LevelProgressUI>();
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
    }

    void Start()
    {
        // Every scene object's Awake has completed before Start, so the authoritative
        // manager is already selected. Binding synchronously also remains reliable when
        // PauseManager hides the GameUI parent during the first Update.
        BindManager(GameManager.instance);
        RefreshVisibility();
    }

    void Update()
    {
        if (observedManager == null) BindManager(GameManager.instance);

        // Score-change events are the primary trigger, but polling here keeps the HUD
        // correct if an event is ever missed (e.g. GameManager swapping its
        // authoritative instance mid-run).
        RefreshVisibility();

        if (visualRoot != null && visualRoot.activeSelf) AnimatePips();
    }

    void OnDestroy()
    {
        BindManager(null);
        if (instance == this) instance = null;
    }

    void BindManager(GameManager manager)
    {
        if (observedManager == manager) return;

        if (observedManager != null)
            observedManager.ScoreChanged -= HandleScoreChanged;

        observedManager = manager;
        if (observedManager != null)
        {
            observedManager.ScoreChanged += HandleScoreChanged;
            HandleScoreChanged(observedManager.GetScore());
        }
    }

    void HandleScoreChanged(int score)
    {
        currentScore = Mathf.Max(0, score);
        ApplyProgress(currentScore);
        RefreshVisibility();
    }

    // ─── Progress ────────────────────────────────────────────────────────────

    void ApplyProgress(int score)
    {
        int planetsPerWorld = Mathf.Max(1, PlanetSpawner.PlanetsPerLevel);
        int worldIndex = PlanetSpawner.LevelIndexForScore(score);
        string[] worldNames = PlanetSpawner.LevelNames;

        // With no configured worlds the HUD falls back to guiding the first ten planets.
        inThemedWorld = worldNames.Length > 0
            ? worldIndex < worldNames.Length
            : score < planetsPerWorld;

        string worldName = worldIndex < worldNames.Length ? worldNames[worldIndex] : null;
        landedInWorld = Mathf.Clamp(score - worldIndex * planetsPerWorld, 0, planetsPerWorld);
        int targetPlanet = Mathf.Min(landedInWorld + 1, planetsPerWorld);

        bool scoreAdvanced = shownScore >= 0 && score == shownScore + 1;
        shownScore = score;

        accent = PlanetAmbience.AccentColorFor(worldName, DefaultAccent);
        accent.a = 1f;

        if (worldLabel != null)
        {
            worldLabel.text = string.IsNullOrEmpty(worldName)
                ? "PROGRESS"
                : worldName.ToUpperInvariant() + " WORLD";
            worldLabel.color = accent;
        }

        if (counterLabel != null)
            counterLabel.text = $"PLANET {targetPlanet} / {planetsPerWorld}";

        // The panel rim follows the world through UITinted now, so there is no
        // per-world outline to repaint here.

        // Landing on the last planet of a world completes every pip before the next
        // world resets them, so the final step still reads as "world cleared".
        if (scoreAdvanced && landedInWorld > 0) StartPop(landedInWorld - 1);
        else if (!scoreAdvanced) { poppedPip = -1; popTimer = 0f; }

        PaintPips();
    }

    void PaintPips()
    {
        if (pips == null) return;

        for (int i = 0; i < pips.Length; i++)
        {
            bool landed = i < landedInWorld;
            pips[i].color = landed ? accent : PipEmpty;
            if (!landed && pipRects[i] != null) pipRects[i].localScale = Vector3.one;
        }
    }

    void StartPop(int index)
    {
        if (pips == null || index < 0 || index >= pips.Length) return;
        poppedPip = index;
        popTimer = 0f;
    }

    // Current pip breathes so the player sees which planet is next; completed pips
    // get a short punch. Both are pure UI transform/color work.
    void AnimatePips()
    {
        if (pips == null) return;

        int current = Mathf.Clamp(landedInWorld, 0, pips.Length - 1);
        if (landedInWorld < pips.Length)
        {
            float wave = (Mathf.Sin(Time.unscaledTime * 3.4f) + 1f) * 0.5f;
            pips[current].color = Color.Lerp(PipEmpty, accent, 0.25f + wave * 0.45f);
            if (pipRects[current] != null)
                pipRects[current].localScale = Vector3.one * Mathf.Lerp(0.92f, 1.16f, wave);
        }

        if (poppedPip < 0) return;

        popTimer += Time.unscaledDeltaTime;
        const float popDuration = 0.28f;
        if (popTimer >= popDuration)
        {
            if (pipRects[poppedPip] != null) pipRects[poppedPip].localScale = Vector3.one;
            poppedPip = -1;
            return;
        }

        float t = popTimer / popDuration;
        if (pipRects[poppedPip] != null)
            pipRects[poppedPip].localScale = Vector3.one * Mathf.Lerp(1.75f, 1f, Mathf.SmoothStep(0f, 1f, t));
    }

    // ─── Visibility ──────────────────────────────────────────────────────────

    void RefreshVisibility()
    {
        if (visualRoot == null) return;

        if (observedManager != null)
        {
            int score = Mathf.Max(0, observedManager.GetScore());
            if (score != shownScore)
            {
                currentScore = score;
                ApplyProgress(score);
            }
        }

        bool shouldShow = inThemedWorld
            && GameManager.isGameStarted
            && !GameManager.isGameOver;

        if (visualRoot.activeSelf != shouldShow)
            visualRoot.SetActive(shouldShow);
    }

    public static void RefreshState()
    {
        if (instance != null) instance.RefreshVisibility();
    }

    public static void ResetForNewRun()
    {
        if (instance == null) return;

        // A manager can be replaced during scene reload. BindManager guards against
        // duplicate subscriptions while ensuring the authoritative manager is observed.
        instance.BindManager(GameManager.instance);
        instance.ResetVisualState();
    }

    void ResetVisualState()
    {
        shownScore = -1;
        poppedPip = -1;
        popTimer = 0f;

        currentScore = observedManager != null ? Mathf.Max(0, observedManager.GetScore()) : 0;
        ApplyProgress(currentScore);

        if (visualGroup != null)
        {
            visualGroup.alpha = 1f;
            visualGroup.interactable = false;
            visualGroup.blocksRaycasts = false;
        }

        if (visualRoot != null)
            visualRoot.transform.localScale = Vector3.one;

        RefreshVisibility();
    }

    // ─── Build ───────────────────────────────────────────────────────────────

    void BuildVisual()
    {
        const float panelWidth = 560f;
        const float panelHeight = 96f;

        visualRoot = new GameObject("WorldProgress");
        visualRoot.transform.SetParent(transform, false);

        RectTransform rootRect = visualRoot.AddComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 1f);
        rootRect.anchorMax = new Vector2(0.5f, 1f);
        rootRect.pivot = new Vector2(0.5f, 1f);
        rootRect.anchoredPosition = new Vector2(0f, -165f);
        rootRect.sizeDelta = new Vector2(panelWidth, panelHeight);

        // Sits over live gameplay, so it takes the quieter glass alpha and no
        // shadow — the same treatment as the score plate beside it.
        UIDesign.EnsureInitialised();
        UIKit.MakeGlass(visualRoot, UIDesign.RadiusChip, UITinted.Role.Glass, 0.86f, false);

        visualGroup = visualRoot.AddComponent<CanvasGroup>();
        visualGroup.alpha = 1f;
        visualGroup.interactable = false;
        visualGroup.blocksRaycasts = false;

        worldLabel = UIStyleKit.MakeLabel(visualRoot.transform, "PROGRESS", UIDesign.TypeCaption,
            accent, new Vector2(26f, -12f), new Vector2(300f, 32f), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(0f, 1f));
        worldLabel.gameObject.name = "WorldLabel";
        worldLabel.rectTransform.pivot = new Vector2(0f, 1f);
        worldLabel.rectTransform.anchoredPosition = new Vector2(26f, -12f);
        UIKit.StyleText(worldLabel, UIDesign.TypeCaption, UIDesign.TrackCaption, accent,
            FontStyles.Bold, TextAlignmentOptions.Left);

        counterLabel = UIStyleKit.MakeLabel(visualRoot.transform, "PLANET 1 / 10",
            UIDesign.TypeCaption, UIDesign.TextMain, new Vector2(-26f, -12f), new Vector2(260f, 32f),
            FontStyles.Bold, TextAlignmentOptions.Right, new Vector2(1f, 1f), new Vector2(1f, 1f));
        counterLabel.gameObject.name = "CounterLabel";
        counterLabel.rectTransform.pivot = new Vector2(1f, 1f);
        counterLabel.rectTransform.anchoredPosition = new Vector2(-26f, -12f);
        UIKit.StyleText(counterLabel, UIDesign.TypeCaption, UIDesign.TrackLabel, UIDesign.TextSub,
            FontStyles.Bold, TextAlignmentOptions.Right);

        BuildPips(panelWidth);
        ApplyProgress(0);
    }

    void BuildPips(float panelWidth)
    {
        int count = Mathf.Max(1, PlanetSpawner.PlanetsPerLevel);
        const float sidePadding = 26f;
        const float pipSize = 22f;

        GameObject row = new GameObject("PipRow");
        row.transform.SetParent(visualRoot.transform, false);
        RectTransform rowRect = row.AddComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 0f);
        rowRect.anchorMax = new Vector2(0.5f, 0f);
        rowRect.pivot = new Vector2(0.5f, 0f);
        rowRect.anchoredPosition = new Vector2(0f, 18f);
        rowRect.sizeDelta = new Vector2(panelWidth - sidePadding * 2f, pipSize);

        pips = new Image[count];
        pipRects = new RectTransform[count];

        float usableWidth = panelWidth - sidePadding * 2f;
        float step = usableWidth / count;

        for (int i = 0; i < count; i++)
        {
            GameObject pip = new GameObject("Pip" + (i + 1).ToString("00"));
            pip.transform.SetParent(row.transform, false);

            RectTransform pipRect = pip.AddComponent<RectTransform>();
            pipRect.anchorMin = pipRect.anchorMax = new Vector2(0.5f, 0.5f);
            pipRect.pivot = new Vector2(0.5f, 0.5f);
            pipRect.sizeDelta = new Vector2(pipSize, pipSize);
            pipRect.anchoredPosition = new Vector2(-usableWidth * 0.5f + step * (i + 0.5f), 0f);

            Image image = pip.AddComponent<Image>();
            image.sprite = UIStyleKit.Circle;
            image.color = PipEmpty;
            image.raycastTarget = false;

            pips[i] = image;
            pipRects[i] = pipRect;
        }
    }
}
