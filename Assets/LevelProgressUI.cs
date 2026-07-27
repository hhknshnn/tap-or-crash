using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Runtime HUD for the guided first ten successful landings.
// The visual child is disabled completely once endless score mode begins.
public sealed class LevelProgressUI : MonoBehaviour
{
    private const int GuidedLevelCount = 10;
    public static int TotalPlanets => GuidedLevelCount;
    private static LevelProgressUI instance;

    private GameManager observedManager;
    private GameObject visualRoot;
    private CanvasGroup visualGroup;
    private RectTransform fillRect;
    private TextMeshProUGUI levelLabel;
    private int currentScore;
    private bool guidedLevelsComplete;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstall()
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

        // Score-change events are the primary trigger, but polling here guarantees the
        // bar can never get stuck visible past level 10 if an event is ever missed
        // (e.g. GameManager swapping its authoritative instance mid-run).
        RefreshVisibility();
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
        if (currentScore >= GuidedLevelCount)
            guidedLevelsComplete = true;

        float progress = Mathf.Clamp01(currentScore / (float)GuidedLevelCount);
        if (fillRect != null)
            fillRect.anchorMax = new Vector2(progress, 1f);

        if (levelLabel != null)
        {
            int visibleLevel = Mathf.Clamp(currentScore + 1, 1, GuidedLevelCount);
            levelLabel.text = $"LEVEL {visibleLevel} / {GuidedLevelCount}";
        }

        RefreshVisibility();
    }


    void RefreshVisibility()
    {
        if (visualRoot == null) return;

        if (observedManager != null)
        {
            currentScore = Mathf.Max(0, observedManager.GetScore());
            if (currentScore >= GuidedLevelCount)
                guidedLevelsComplete = true;
        }

        bool shouldShow = !guidedLevelsComplete
            && currentScore < GuidedLevelCount
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
        currentScore = observedManager != null ? Mathf.Max(0, observedManager.GetScore()) : 0;
        guidedLevelsComplete = currentScore >= GuidedLevelCount;

        if (fillRect != null)
            fillRect.anchorMax = new Vector2(0f, 1f);

        if (levelLabel != null)
            levelLabel.text = $"LEVEL 1 / {GuidedLevelCount}";

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

    void BuildVisual()
    {
        visualRoot = new GameObject("FirstTenProgress");
        visualRoot.transform.SetParent(transform, false);

        RectTransform rootRect = visualRoot.AddComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 1f);
        rootRect.anchorMax = new Vector2(0.5f, 1f);
        rootRect.pivot = new Vector2(0.5f, 1f);
        rootRect.anchoredPosition = new Vector2(0f, -165f);
        rootRect.sizeDelta = new Vector2(560f, 82f);

        Image panel = visualRoot.AddComponent<Image>();
        panel.color = new Color(0.025f, 0.055f, 0.14f, 0.88f);
        panel.raycastTarget = false;

        visualGroup = visualRoot.AddComponent<CanvasGroup>();
        visualGroup.alpha = 1f;
        visualGroup.interactable = false;
        visualGroup.blocksRaycasts = false;

        Outline outline = visualRoot.AddComponent<Outline>();
        outline.effectColor = new Color(0.18f, 0.68f, 1f, 0.45f);
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = true;

        GameObject labelObject = new GameObject("LevelLabel");
        labelObject.transform.SetParent(visualRoot.transform, false);
        RectTransform labelRect = labelObject.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(0f, -7f);
        labelRect.sizeDelta = new Vector2(0f, 36f);

        levelLabel = labelObject.AddComponent<TextMeshProUGUI>();
        UIStyleKit.ApplyRuntimeFont(levelLabel, visualRoot.transform);
        levelLabel.fontSize = 24f;
        levelLabel.fontStyle = FontStyles.Bold;
        levelLabel.characterSpacing = 2f;
        levelLabel.alignment = TextAlignmentOptions.Center;
        levelLabel.color = new Color(0.82f, 0.92f, 1f, 1f);
        levelLabel.raycastTarget = false;

        GameObject trackObject = new GameObject("Track");
        trackObject.transform.SetParent(visualRoot.transform, false);
        RectTransform trackRect = trackObject.AddComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(0f, 0f);
        trackRect.anchorMax = new Vector2(1f, 0f);
        trackRect.pivot = new Vector2(0.5f, 0f);
        trackRect.anchoredPosition = new Vector2(0f, 12f);
        trackRect.sizeDelta = new Vector2(-42f, 20f);

        Image track = trackObject.AddComponent<Image>();
        track.color = new Color(0.09f, 0.16f, 0.31f, 1f);
        track.raycastTarget = false;

        GameObject fillObject = new GameObject("Fill");
        fillObject.transform.SetParent(trackObject.transform, false);
        fillRect = fillObject.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        Image fill = fillObject.AddComponent<Image>();
        fill.color = new Color(0.18f, 0.82f, 1f, 1f);
        fill.raycastTarget = false;

        HandleScoreChanged(0);
    }
}
