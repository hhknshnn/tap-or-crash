using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

public class GameManager : MonoBehaviour
{
    public static GameManager instance;

    public static bool isGameOver    = false;
    public static bool isGameStarted = false;
    public static bool isNearMiss    = false;
    public static bool isRestart     = false;

    /// <summary>True while the single AlmostText presentation is on screen.</summary>
    public static bool IsAlmostFeedbackPlaying { get; private set; }

    // Presentation gate. The menu keeps flying the rocket for a beat after StartGame
    // while the camera pulls back, and hands it to RocketController mid-orbit. Gameplay
    // input stays asleep until then, so the launch tap is never spent on the transition.
    public static bool isIntroPlaying = false;

    private int score      = 0;
    private int highScore  = 0;
    private int comboCount = 0;

    public event System.Action<int> ScoreChanged;

    [Header("Score UI")]
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI almostText;

    [Header("Panels")]
    public GameObject startPanel;
    public GameObject gameOverPanel;

    [Header("Game Over UI")]
    public TextMeshProUGUI highScoreText;
    public TextMeshProUGUI scoreResultText;

    [Header("Referanslar")]
    public RocketController playerRocket;

    [Header("Rewarded Continue")]
    [SerializeField] private AdService adService;
    [SerializeField] private RewardedContinueController rewardedContinue;

    [Header("Animasyon Ayarları")]
    [SerializeField] private float gameOverFadeDuration = 0.4f;
    [SerializeField] private float scoreCountDuration   = 1.2f;

    // Runtime'da oluşturulan combo metin nesnesi
    private TextMeshProUGUI comboText;
    private Coroutine comboAnimation;
    private Coroutine almostAnimation;
    private CanvasGroup comboGroup;
    private CanvasGroup almostGroup;
    private UnityEngine.UI.Outline comboOutline;
    private UnityEngine.UI.Outline almostOutline;

    void Awake()
    {
        if (!BecomeAuthoritativeInstance()) return;

        highScore = PlayerPrefs.GetInt("HighScore", 0);

        if (scoreResultText == null)
        {
            TextMeshProUGUI[] allTexts = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
            foreach (var t in allTexts)
            {
                if (t.gameObject.name == "ScoreResultText") { scoreResultText = t; break; }
            }
        }

        if (playerRocket == null)
            playerRocket = FindAnyObjectByType<RocketController>();

    }

    void OnEnable()
    {
        // Unity invokes OnEnable, but not Awake, after recompiling scripts while
        // Play Mode continues. Re-establish the authoritative runtime singleton
        // before gameplay components resume their Update loops.
        BecomeAuthoritativeInstance();
        if (instance == this) EnsureMenuGameplayFlags();
    }

    /// <summary>
    /// Clears stale game-over flags whenever the authoritative Main Menu is visible.
    /// Does not clear <see cref="isGameStarted"/> — that is reconciled only when a
    /// menu entry point explicitly asks to open the Shop.
    /// </summary>
    internal static void EnsureMenuGameplayFlags()
    {
        if (isIntroPlaying || isRestart || isGameStarted) return;

        if (MainMenuShowcase.ExistsInScene && MainMenuShowcase.IsMenuReady)
            isGameOver = false;
        else if (instance != null && instance.startPanel != null && instance.startPanel.activeInHierarchy)
            isGameOver = false;
    }

    bool BecomeAuthoritativeInstance()
    {
        if (instance == null || instance == this)
        {
            instance = this;
            return true;
        }

        // SampleScene currently also carries a legacy GameManager component on Rocket.
        // Keep that component alive for its serialized Restart UnityEvent, but only the
        // best configured scene manager may run lifecycle/gameplay logic.
        if (ConfigurationPriority() > instance.ConfigurationPriority())
        {
            instance.enabled = false;
            instance = this;
            return true;
        }

        enabled = false;
        return false;
    }

    int ConfigurationPriority()
    {
        int priority = gameObject.name == "GameManager" ? 100 : 0;
        if (startPanel != null) priority += 20;
        if (gameOverPanel != null) priority += 10;
        if (scoreText != null) priority += 5;
        if (scoreResultText != null) priority += 3;
        if (playerRocket != null) priority += 2;
        return priority;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Start()
    {
        if (instance != this) return;

        EnsureRewardedContinue();
        isGameOver  = false;
        isIntroPlaying = false;
        comboCount  = 0;
        if (rewardedContinue != null)
        {
            rewardedContinue.ResetForLevel();
            StartCoroutine(CaptureInitialContinueCheckpoint());
        }

        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        ResolveAlmostTextReference();
        if (almostText   != null) almostText.gameObject.SetActive(false);

        CreateComboTextUI();

        if (isRestart)
        {
            isRestart = false;
            if (startPanel != null) startPanel.SetActive(false);
            StartCoroutine(StartAfterDelay());
        }
        else
        {
            isGameStarted = false;
            if (startPanel != null) startPanel.SetActive(true);
        }

        EnsureMenuGameplayFlags();
        ScoreChanged?.Invoke(score);
    }

    void ResolveAlmostTextReference()
    {
        if (almostText != null) return;

        Canvas canvas = UIRootCanvas.Resolve();
        if (canvas == null) return;

        Transform almostTransform = canvas.transform.Find("GameUI/PerfectFeedbackLane/AlmostText");
        if (almostTransform != null)
            almostText = almostTransform.GetComponent<TextMeshProUGUI>();
    }

    // Combo göstergesi UI elementini kod ile oluşturur
    void CreateComboTextUI()
    {
        Canvas canvas = UIRootCanvas.Resolve();
        if (canvas == null) return;

        Transform gameUI = canvas.transform.Find("GameUI");
        Transform parent = gameUI != null ? gameUI : canvas.transform;

        Transform existingLane = parent.Find("PerfectFeedbackLane");
        GameObject lane = existingLane != null
            ? existingLane.gameObject
            : new GameObject("PerfectFeedbackLane");
        lane.transform.SetParent(parent, false);
        lane.layer = parent.gameObject.layer;
        lane.SetActive(true);

        RectTransform rt = lane.GetComponent<RectTransform>();
        if (rt == null) rt = lane.AddComponent<RectTransform>();
        rt.anchorMin       = new Vector2(0.5f, 1f);
        rt.anchorMax       = new Vector2(0.5f, 1f);
        rt.pivot           = new Vector2(0.5f, 1f);
        rt.sizeDelta       = new Vector2(520f, GameplayPresentationLayout.PerfectFeedbackHeight);
        GameplayPresentationLayout.PlaceTopCentre(rt, canvas.GetComponent<RectTransform>(),
            GameplayPresentationLayout.Lane.PerfectFeedback);

        Transform existingPerfect = lane.transform.Find("PerfectFeedback");
        GameObject perfect = existingPerfect != null
            ? existingPerfect.gameObject
            : new GameObject("PerfectFeedback");
        perfect.transform.SetParent(lane.transform, false);
        perfect.layer = lane.layer;
        RectTransform perfectRect = perfect.GetComponent<RectTransform>();
        if (perfectRect == null) perfectRect = perfect.AddComponent<RectTransform>();
        perfectRect.anchorMin = Vector2.zero;
        perfectRect.anchorMax = Vector2.one;
        perfectRect.offsetMin = Vector2.zero;
        perfectRect.offsetMax = Vector2.zero;

        comboText = perfect.GetComponent<TextMeshProUGUI>();
        if (comboText == null) comboText = perfect.AddComponent<TextMeshProUGUI>();
        comboGroup = perfect.GetComponent<CanvasGroup>();
        if (comboGroup == null) comboGroup = perfect.AddComponent<CanvasGroup>();
        comboGroup.alpha = 0f;
        comboGroup.interactable = false;
        comboGroup.blocksRaycasts = false;
        UIStyleKit.ApplyRuntimeFont(comboText, parent);
        comboText.fontSize  = 34;
        comboText.alignment = TextAlignmentOptions.Center;
        comboText.fontStyle = FontStyles.Bold;
        comboText.color = new Color(1f, 0.89f, 0.62f);
        comboText.characterSpacing = 2f;
        comboText.textWrappingMode = TextWrappingModes.NoWrap;
        comboText.overflowMode = TextOverflowModes.Overflow;
        comboText.raycastTarget = false;
        comboOutline = perfect.GetComponent<UnityEngine.UI.Outline>();
        if (comboOutline == null) comboOutline = perfect.AddComponent<UnityEngine.UI.Outline>();
        comboOutline.effectColor = new Color(0.11f, 0.07f, 0.16f, 0.78f);
        comboOutline.effectDistance = new Vector2(1.5f, -1.5f);
        comboOutline.useGraphicAlpha = true;
        perfect.SetActive(false);

        ConfigureAlmostFeedback(lane.transform, parent);
    }

    void ConfigureAlmostFeedback(Transform lane, Transform fontContext)
    {
        if (almostText == null) return;

        almostText.transform.SetParent(lane, false);
        RectTransform rect = almostText.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;

        UIStyleKit.ApplyRuntimeFont(almostText, fontContext);
        almostText.text = "ALMOST";
        almostText.fontSize = 32f;
        almostText.fontStyle = FontStyles.Bold;
        almostText.alignment = TextAlignmentOptions.Center;
        almostText.color = new Color(1f, 0.76f, 0.48f);
        almostText.characterSpacing = 1.5f;
        almostText.enableVertexGradient = false;
        almostText.textWrappingMode = TextWrappingModes.NoWrap;
        almostText.overflowMode = TextOverflowModes.Overflow;
        almostText.raycastTarget = false;

        almostGroup = almostText.GetComponent<CanvasGroup>();
        if (almostGroup == null) almostGroup = almostText.gameObject.AddComponent<CanvasGroup>();
        almostGroup.alpha = 0f;
        almostGroup.interactable = false;
        almostGroup.blocksRaycasts = false;

        almostOutline = almostText.GetComponent<UnityEngine.UI.Outline>();
        if (almostOutline == null)
            almostOutline = almostText.gameObject.AddComponent<UnityEngine.UI.Outline>();
        almostOutline.effectColor = new Color(0.15f, 0.09f, 0.16f, 0.62f);
        almostOutline.effectDistance = new Vector2(1.25f, -1.25f);
        almostOutline.useGraphicAlpha = true;
        almostText.gameObject.SetActive(false);
    }

    // ─── Skor ────────────────────────────────────────────────────────────────

    public void AddScore()
    {
        if (instance != null && instance != this)
        {
            instance.AddScore();
            return;
        }

        score++;
        if (scoreText != null) scoreText.text = score.ToString();
        ScoreChanged?.Invoke(score);

        GameplayVFX.Ensure().EvaluateMilestoneNotices(score);

        // Milestone kontrolü
        if (CoinManager.instance != null) CoinManager.instance.CheckMilestones(score);
    }

    public int GetScore()  => score;
    public int GetCombo()  => comboCount;

    public bool TryGetCurrentLevelProgress(out int currentPlanetIndex, out int totalPlanets)
    {
        currentPlanetIndex = Mathf.Max(0, score);
        totalPlanets = LevelProgressUI.TotalPlanets;
        return totalPlanets > 0 && currentPlanetIndex < totalPlanets;
    }

    // ─── Combo ───────────────────────────────────────────────────────────────

    public void IncrementCombo()
    {
        comboCount++;
        ShowComboText("PRECISION", new Color(1f, 0.82f, 0.15f));
    }

    public void RegisterLanding(RocketController.LandingQuality quality)
    {
        if (quality == RocketController.LandingQuality.Normal)
        {
            ResetCombo();
            return;
        }

        comboCount++;
        if (quality == RocketController.LandingQuality.Perfect)
            ShowComboText("PERFECT", new Color(1f, 0.82f, 0.15f));
        else
            ShowComboText("EDGE CATCH", new Color(1f, 0.38f, 0.08f));
    }

    public void ResetCombo()
    {
        if (comboCount == 0) return;
        comboCount = 0;
        if (comboAnimation != null)
        {
            StopCoroutine(comboAnimation);
            comboAnimation = null;
        }
        if (comboText != null) comboText.gameObject.SetActive(false);
    }

    void ShowComboText(string eventLabel, Color color)
    {
        if (comboText == null || comboGroup == null) return;
        if (comboAnimation != null)
        {
            StopCoroutine(comboAnimation);
            comboAnimation = null;
        }

        RectTransform rt = comboText.rectTransform;
        comboGroup.alpha = 0f;
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = Vector3.one * 0.76f;
        rt.localRotation = Quaternion.identity;
        comboText.characterSpacing = 2f;
        SetOutlineAlpha(comboOutline, 0.20f);
        comboText.text = comboCount > 1
            ? $"{eventLabel}!  ×{comboCount}"
            : eventLabel + "!";
        comboText.color = eventLabel == "PERFECT"
            ? new Color(1f, 0.86f, 0.48f)
            : color;
        if (!comboText.transform.parent.gameObject.activeSelf)
            comboText.transform.parent.gameObject.SetActive(true);
        comboText.gameObject.SetActive(true);
        comboAnimation = StartCoroutine(ComboTextAnim());
    }

    IEnumerator ComboTextAnim()
    {
        RectTransform rt = comboText.rectTransform;
        Vector2 rest = Vector2.zero;

        float t = 0f;
        while (t < 0.06f)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / 0.06f);
            comboGroup.alpha = Mathf.Lerp(0f, 0.7f, p);
            rt.localScale = Vector3.one * Mathf.Lerp(0.76f, 0.92f, p);
            yield return null;
        }

        comboGroup.alpha = 1f;
        t = 0f;
        while (t < 0.09f)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / 0.09f);
            rt.localScale = Vector3.one * Mathf.Lerp(0.92f, 1.12f, p);
            comboText.characterSpacing = Mathf.Lerp(2f, 3.5f, p);
            SetOutlineAlpha(comboOutline, Mathf.Lerp(0.20f, 0.82f, p));
            yield return null;
        }

        t = 0f;
        while (t < 0.12f)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.12f));
            rt.localScale = Vector3.one * Mathf.Lerp(1.12f, 1f, p);
            comboText.characterSpacing = Mathf.Lerp(3.5f, 2f, p);
            SetOutlineAlpha(comboOutline, Mathf.Lerp(0.82f, 0.55f, p));
            yield return null;
        }

        rt.localScale = Vector3.one;
        rt.anchoredPosition = rest;

        yield return new WaitForSecondsRealtime(0.26f);

        t = 0f;
        while (t < 0.32f)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.32f));
            comboGroup.alpha = 1f - p;
            rt.anchoredPosition = rest + Vector2.up * (22f * p);
            rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.96f, p);
            SetOutlineAlpha(comboOutline, Mathf.Lerp(0.55f, 0f, p));
            yield return null;
        }
        comboGroup.alpha = 0f;
        rt.anchoredPosition = rest;
        rt.localScale = Vector3.one;
        comboText.characterSpacing = 2f;
        comboText.gameObject.SetActive(false);
        comboAnimation = null;
    }

    static void SetOutlineAlpha(UnityEngine.UI.Outline outline, float alpha)
    {
        if (outline == null) return;
        Color color = outline.effectColor;
        color.a = alpha;
        outline.effectColor = color;
    }

    // ─── Game Over ───────────────────────────────────────────────────────────

    public void TriggerGameOver()
    {
        if (instance != null && instance != this)
        {
            instance.TriggerGameOver();
            return;
        }

        if (isGameOver) return;

        isGameOver = true;
        PresentationGate.Acquire(PresentationGate.Kind.GameOver);
        CrashRevealDelay.MarkImpact();
        LevelProgressUI.RefreshState();

        if (AudioManager.instance != null)
        {
            AudioManager.instance.PlayCrash();
            AudioManager.instance.StopLaunch();
        }

        CameraFollow cameraFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cameraFollow != null) cameraFollow.PlayCrashKick();

        if (playerRocket != null) HandleRocketExplosion(playerRocket);

        StartCoroutine(DeathSequence());
    }

    void HandleRocketExplosion(RocketController rocket)
    {
        rocket.CancelHoldInput();
        GameplayVFX.Ensure().PlayCrash(
            rocket.transform.position,
            rocket.transform.rotation,
            rocket.VisualWorldSize);

        rocket.enabled = false;
        rocket.GetComponent<SpriteRenderer>().enabled = false;

        ParticleSystem thruster = rocket.GetComponentInChildren<ParticleSystem>();
        if (thruster != null) thruster.Stop();
    }

    IEnumerator DeathSequence()
    {
        if (comboText != null) comboText.gameObject.SetActive(false);

        bool showingAlmost = almostText != null && isNearMiss;
        if (showingAlmost)
        {
            if (almostAnimation != null) StopCoroutine(almostAnimation);
            almostAnimation = StartCoroutine(AlmostTextAnim());
        }

        // Preserve the original near-miss pause before the crash sequence continues.
        if (showingAlmost) yield return new WaitForSecondsRealtime(0.25f);

        isNearMiss          = false;
        Time.timeScale      = 0.72f;
        yield return new WaitForSecondsRealtime(0.22f);
        Time.timeScale      = 1f;

        if (rewardedContinue != null && rewardedContinue.TryShowOffer())
            yield break;

        ShowNormalGameOver();
    }

    IEnumerator AlmostTextAnim()
    {
        if (almostText == null || almostGroup == null) yield break;

        IsAlmostFeedbackPlaying = true;
        RectTransform rt = almostText.rectTransform;
        Vector2 rest = Vector2.zero;
        EnsureAlmostFeedbackHierarchyActive();
        almostText.gameObject.SetActive(true);
        almostGroup.alpha = 0f;
        rt.anchoredPosition = rest;
        rt.localScale = Vector3.one * 0.84f;
        rt.localRotation = Quaternion.identity;
        SetOutlineAlpha(almostOutline, 0.18f);

        float t = 0f;
        while (t < 0.10f)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / 0.10f);
            almostGroup.alpha = p;
            rt.localScale = Vector3.one * Mathf.Lerp(0.84f, 1.04f, p);
            SetOutlineAlpha(almostOutline, Mathf.Lerp(0.18f, 0.58f, p));
            yield return null;
        }

        t = 0f;
        while (t < 0.14f)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.14f));
            rt.localScale = Vector3.one * Mathf.Lerp(1.04f, 1f, p);
            rt.anchoredPosition = rest + Vector2.right * (Mathf.Sin(p * Mathf.PI) * 5f);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(p * Mathf.PI) * -1.2f);
            SetOutlineAlpha(almostOutline, Mathf.Lerp(0.58f, 0.42f, p));
            yield return null;
        }

        rt.anchoredPosition = rest;
        rt.localRotation = Quaternion.identity;
        yield return new WaitForSecondsRealtime(0.26f);

        t = 0f;
        while (t < 0.30f)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.30f));
            almostGroup.alpha = 1f - p;
            rt.anchoredPosition = rest + Vector2.down * (14f * p);
            rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.96f, p);
            SetOutlineAlpha(almostOutline, Mathf.Lerp(0.42f, 0f, p));
            yield return null;
        }

        almostGroup.alpha = 0f;
        rt.anchoredPosition = rest;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        almostText.gameObject.SetActive(false);
        almostAnimation = null;
        IsAlmostFeedbackPlaying = false;
    }

    void EnsureAlmostFeedbackHierarchyActive()
    {
        if (almostText == null) return;

        Transform node = almostText.transform;
        while (node != null)
        {
            if (!node.gameObject.activeSelf)
                node.gameObject.SetActive(true);
            if (node.name == "GameUI")
                break;
            node = node.parent;
        }
    }

    public void ShowNormalGameOver()
    {
        if (!isGameOver)
            return;

        comboCount = 0;
        if (score > highScore)
        {
            highScore = score;
            PlayerPrefs.SetInt("HighScore", highScore);
            PlayerPrefs.Save();
        }

        if (AdManager.instance != null) AdManager.instance.OnGameOver();
        StartCoroutine(ShowGameOverPanel());
    }

    IEnumerator ShowGameOverPanel()
    {
        if (gameOverPanel == null) yield break;

        // The panel is a full-screen dark Image, so it hides the break-up the
        // instant it fades in. Everything the run itself needs — the result, the
        // ad hook, the gate — is already committed by ShowNormalGameOver; only
        // the entrance waits for the crash to have been read.
        int revealToken = CrashRevealDelay.Token;
        yield return CrashRevealDelay.WaitForReveal(revealToken);
        if (!CrashRevealDelay.IsCurrent(revealToken) || !isGameOver) yield break;

        if (highScoreText  != null) highScoreText.text  = "BEST  "  + highScore;
        if (scoreResultText != null) scoreResultText.text = "SCORE  0";

        // CanvasGroup: yoksa ekle (fade için gerekli)
        CanvasGroup cg = gameOverPanel.GetComponent<CanvasGroup>();
        if (cg == null) cg = gameOverPanel.AddComponent<CanvasGroup>();

        cg.alpha = 0f;
        gameOverPanel.SetActive(true);

        RectTransform panelRt = gameOverPanel.GetComponent<RectTransform>();
        panelRt.localScale    = Vector3.one * 0.85f;

        // Fade + scale-in
        float dur = gameOverFadeDuration;
        float t   = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float p       = Mathf.SmoothStep(0f, 1f, t / dur);
            cg.alpha      = p;
            panelRt.localScale = Vector3.Lerp(Vector3.one * 0.85f, Vector3.one, p);
            yield return null;
        }
        cg.alpha           = 1f;
        panelRt.localScale = Vector3.one;

        // Skor sayacı animasyonu
        if (scoreResultText != null) StartCoroutine(CountUpScore(score));

        // Reklam izle → coin butonu
        if (CoinManager.instance != null) CoinManager.instance.ShowWatchAdButton();
    }

    public void CaptureContinueCheckpoint()
    {
        if (instance != null && instance != this)
        {
            instance.CaptureContinueCheckpoint();
            return;
        }

        if (rewardedContinue != null)
            rewardedContinue.CaptureCheckpoint();
    }

    public void ResumeFromContinue(
        RocketController.ContinueState rocketState,
        CameraFollow.ContinueState cameraState,
        int restoredScore,
        int restoredCombo)
    {
        if (instance != null && instance != this)
        {
            instance.ResumeFromContinue(rocketState, cameraState, restoredScore, restoredCombo);
            return;
        }

        if (playerRocket == null || !playerRocket.RestoreContinueState(rocketState))
        {
            ShowNormalGameOver();
            return;
        }

        ClearCrashPresentation();

        CameraFollow cameraFollow = Camera.main != null
            ? Camera.main.GetComponent<CameraFollow>()
            : null;
        if (cameraFollow != null)
            cameraFollow.RestoreContinueState(cameraState);

        score = Mathf.Max(0, restoredScore);
        comboCount = Mathf.Max(0, restoredCombo);
        if (scoreText != null) scoreText.text = score.ToString();
        ScoreChanged?.Invoke(score);
        GameplayVFX.Ensure().EvaluateMilestoneNotices(score);

        isNearMiss = false;
        isGameStarted = true;
        isGameOver = false;
        PresentationGate.Release(PresentationGate.Kind.GameOver);
        Time.timeScale = 1f;
        LevelProgressUI.RefreshState();
        WorldTransitionManager.IntroduceCurrentWorld();
    }

    IEnumerator CountUpScore(int target)
    {
        float elapsed = 0f;
        while (elapsed < scoreCountDuration)
        {
            elapsed += Time.deltaTime;
            float p = Mathf.SmoothStep(0f, 1f, elapsed / scoreCountDuration);
            scoreResultText.text = "SCORE  " + Mathf.RoundToInt(p * target);
            yield return null;
        }
        scoreResultText.text = "SCORE  " + target;
    }

    // ─── Oyun Akışı ──────────────────────────────────────────────────────────

    public void StartGame()
    {
        if (instance != null && instance != this)
        {
            instance.StartGame();
            return;
        }

        if (isGameStarted || isIntroPlaying) return;
        // Tutorial V2 blocks every Main Menu control behind a full-screen raycast
        // blocker already; this is the defense-in-depth guard for any caller that
        // does not go through the UI event system.
        if (PresentationGate.IsActive(PresentationGate.Kind.Tutorial)) return;
        if (!RocketFuelService.Instance.TryConsumeForNewRun()) return;

        RunSession.Begin();
        isGameStarted = true;
        ResetRunScore();

        // The showcase dissolves the start panel itself, as one move with the camera
        // pull-back. It only declines when there is no menu stage to hand over from.
        if (!MainMenuShowcase.TryBeginLaunch(startPanel))
        {
            if (startPanel != null) startPanel.SetActive(false);
        }

        // Başlangıç ekranında açık kalmışsa shop'u kapat
        if (ShipSkinManager.instance != null) ShipSkinManager.instance.CloseShop();
    }

    public void RestartGame()
    {
        if (instance != null && instance != this)
        {
            instance.RestartGame();
            return;
        }

        if (isRestart) return;
        if (!RocketFuelService.Instance.TryConsumeForNewRun()) return;

        PrepareForRestart();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// Debris and crash particles outlive the crash that spawned them, so both
    /// restore paths clear them before gameplay is handed back. Cancelling the
    /// reveal window with them stops a still-waiting presenter from opening an
    /// obsolete Game Over panel over the resumed run.
    private static void ClearCrashPresentation()
    {
        CrashDebrisPresentation.ClearAll();
        if (GameplayVFX.instance != null) GameplayVFX.instance.ClearActiveBursts();
        CrashRevealDelay.Cancel();
    }

    private void PrepareForRestart()
    {
        ClearCrashPresentation();
        if (GameplayVFX.instance != null) GameplayVFX.instance.CancelMilestoneNotices();

        // The gate set is static and survives the scene load. A restart taken from
        // inside Pause, the Continue offer or Game Over would otherwise carry that
        // presentation's gate into the reloaded scene with nothing left alive to
        // release it. RunSession.Begin does this again once the new run starts;
        // doing it here means the gate is never held while the scene reloads.
        PresentationGate.ReleaseRunScoped();
        isRestart      = true;
        isGameOver     = false;
        isGameStarted  = false;
        isNearMiss     = false;
        IsAlmostFeedbackPlaying = false;
        isIntroPlaying = false;
        if (playerRocket != null) playerRocket.ResetForNewRun();
        ResetRunScore();
        WorldTransitionManager.ResetForNewRun();

        // Last, because the teardown above can restore a hold's remembered scale.
        // The scene has to reload with the clock already running.
        Time.timeScale = 1f;
    }

    private void ResetRunScore()
    {
        score = 0;
        comboCount = 0;
        if (scoreText != null) scoreText.text = "0";
        ScoreChanged?.Invoke(score);
        LevelProgressUI.ResetForNewRun();
    }

    private void EnsureRewardedContinue()
    {
        if (adService == null)
            adService = GetComponent<AdService>();
        if (adService == null)
            adService = gameObject.AddComponent<AdService>();

        if (rewardedContinue == null)
            rewardedContinue = GetComponent<RewardedContinueController>();
        if (rewardedContinue == null)
            rewardedContinue = gameObject.AddComponent<RewardedContinueController>();

        rewardedContinue.Initialize(this, adService);
    }

    private IEnumerator CaptureInitialContinueCheckpoint()
    {
        yield return null;
        if (rewardedContinue != null)
            rewardedContinue.CaptureCheckpoint();
    }

    // The restart path: the scene has reloaded and this is where the new run is
    // handed the game, so it is the restart's RunSession.Begin, exactly as
    // StartGame is the Main Menu launch's.
    IEnumerator StartAfterDelay()
    {
        yield return null; // Tüm objeler yüklensin
        RunSession.Begin();
        isGameStarted = true;
        ResetRunScore();
    }
}
