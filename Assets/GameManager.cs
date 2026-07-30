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

        ScoreChanged?.Invoke(score);
    }

    // Combo göstergesi UI elementini kod ile oluşturur
    void CreateComboTextUI()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return;

        Transform gameUI = canvas.transform.Find("GameUI");
        Transform parent = gameUI != null ? gameUI : canvas.transform;

        GameObject go = new GameObject("ComboText");
        go.transform.SetParent(parent, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin       = new Vector2(0.5f, 1f);
        rt.anchorMax       = new Vector2(0.5f, 1f);
        rt.pivot           = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -120f);
        rt.sizeDelta       = new Vector2(320f, 60f);

        comboText           = go.AddComponent<TextMeshProUGUI>();
        comboText.fontSize  = 36;
        comboText.alignment = TextAlignmentOptions.Center;
        comboText.color     = new Color(1f, 0.85f, 0.1f); // altın sarısı
        comboText.fontStyle = FontStyles.Bold;
        go.SetActive(false);
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

        GameplayVFX.Ensure().PlayMilestone(score);

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
        if (comboText == null) return;
        comboText.text = comboCount > 1
            ? $"{eventLabel}  ·  x{comboCount}"
            : eventLabel + "!";
        comboText.color = color;
        comboText.gameObject.SetActive(true);

        if (comboAnimation != null) StopCoroutine(comboAnimation);
        comboAnimation = StartCoroutine(ComboTextAnim());
    }

    IEnumerator ComboTextAnim()
    {
        RectTransform rt = comboText.GetComponent<RectTransform>();
        CanvasGroup   cg = comboText.GetComponent<CanvasGroup>();
        if (cg == null) cg = comboText.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 1f;
        rt.localScale = Vector3.one * 1.4f;

        // Pop-in
        float t = 0f;
        while (t < 0.2f)
        {
            t += Time.deltaTime;
            rt.localScale = Vector3.Lerp(Vector3.one * 1.4f, Vector3.one, t / 0.2f);
            yield return null;
        }
        rt.localScale = Vector3.one;

        yield return new WaitForSeconds(0.8f);

        // Fade-out
        t = 0f;
        while (t < 0.25f)
        {
            t += Time.deltaTime;
            cg.alpha = 1f - (t / 0.25f);
            yield return null;
        }
        cg.alpha = 1f;
        comboText.gameObject.SetActive(false);
        comboAnimation = null;
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
        GameplayVFX.Ensure().PlayCrash(rocket.transform.position);

        rocket.enabled = false;
        rocket.GetComponent<SpriteRenderer>().enabled = false;

        ParticleSystem thruster = rocket.GetComponentInChildren<ParticleSystem>();
        if (thruster != null) thruster.Stop();
    }

    IEnumerator DeathSequence()
    {
        if (comboText != null) comboText.gameObject.SetActive(false);

        // "ALMOST!" animasyonu
        if (almostText != null && isNearMiss)
        {
            almostText.gameObject.SetActive(true);
            RectTransform rt  = almostText.GetComponent<RectTransform>();
            float elapsed = 0f;
            while (elapsed < 0.25f)
            {
                elapsed += Time.unscaledDeltaTime;
                rt.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, elapsed / 0.25f);
                yield return null;
            }
        }

        isNearMiss          = false;
        Time.timeScale      = 0.72f;
        yield return new WaitForSecondsRealtime(0.22f);
        Time.timeScale      = 1f;

        if (almostText != null) almostText.gameObject.SetActive(false);

        if (rewardedContinue != null && rewardedContinue.TryShowOffer())
            yield break;

        ShowNormalGameOver();
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
            t += Time.deltaTime;
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

        CameraFollow cameraFollow = Camera.main != null
            ? Camera.main.GetComponent<CameraFollow>()
            : null;
        if (cameraFollow != null)
            cameraFollow.RestoreContinueState(cameraState);

        score = Mathf.Max(0, restoredScore);
        comboCount = Mathf.Max(0, restoredCombo);
        if (scoreText != null) scoreText.text = score.ToString();
        ScoreChanged?.Invoke(score);

        isNearMiss = false;
        isGameStarted = true;
        isGameOver = false;
        Time.timeScale = 1f;
        LevelProgressUI.RefreshState();
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

        PrepareForRestart();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void PrepareForRestart()
    {
        Time.timeScale = 1f;
        isRestart      = true;
        isGameOver     = false;
        isGameStarted  = false;
        isNearMiss     = false;
        isIntroPlaying = false;
        if (playerRocket != null) playerRocket.ResetForNewRun();
        ResetRunScore();
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

    IEnumerator StartAfterDelay()
    {
        yield return null; // Tüm objeler yüklensin
        isGameStarted = true;
        ResetRunScore();
    }
}
