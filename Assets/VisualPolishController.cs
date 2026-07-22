using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Existing scene UI is kept intact; this layer applies a consistent mobile presentation at runtime.
public sealed class VisualPolishController : MonoBehaviour
{
    private static VisualPolishController instance;

    private Canvas canvas;
    private RectTransform startEmblem;
    private GameObject gameOverDim;
    private GameObject gameOverPanel;
    private TextMeshProUGUI gameOverCoinsText;
    private bool gameOverWasActive;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstall()
    {
        if (instance != null) return;
        GameObject go = new GameObject("VisualPolishController");
        go.AddComponent<VisualPolishController>();
    }

    void Awake()
    {
        if (instance == null) instance = this;
        else { Destroy(gameObject); return; }

#if UNITY_ANDROID || UNITY_IOS
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
#endif
    }

    IEnumerator Start()
    {
        // Runtime-created HUD elements are added during other Start calls.
        yield return null;

        canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) yield break;

        ConfigureCanvas();
        StyleStartScreen();
        StyleHud();
        StyleGameOver();
        StylePause();
        StyleTutorial();
        StyleCommonButtons();

        SafeAreaFitter safeArea = canvas.GetComponent<SafeAreaFitter>();
        if (safeArea != null) safeArea.Rebaseline();
    }

    void Update()
    {
        if (startEmblem != null && startEmblem.gameObject.activeInHierarchy)
        {
            float time = Time.unscaledTime;
            startEmblem.anchoredPosition = new Vector2(0f, -505f + Mathf.Sin(time * 1.15f) * 9f);
            float pulse = 1f + Mathf.Sin(time * 1.8f) * 0.018f;
            startEmblem.localScale = Vector3.one * pulse;
        }

        if (gameOverPanel != null)
        {
            bool isActive = gameOverPanel.activeInHierarchy;
            if (gameOverDim != null) gameOverDim.SetActive(isActive);

            // Read the earned total when GameManager opens the initially hidden panel.
            if (isActive && (!gameOverWasActive || Time.frameCount % 15 == 0))
                RefreshGameOverCoins();
            gameOverWasActive = isActive;
        }
    }

    void ConfigureCanvas()
    {
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) return;

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    void StyleStartScreen()
    {
        Transform panel = FindDeep(canvas.transform, "StartPanel");
        if (panel == null) return;

        TextMeshProUGUI logo = FindTmp(panel, "LogoText");
        if (logo != null)
        {
            logo.text = "TAP OR CRASH";
            logo.fontSize = 66f;
            logo.fontStyle = FontStyles.Bold;
            logo.color = UIStyleKit.TextMain;
            logo.characterSpacing = 2f;
            logo.outlineWidth = 0.13f;
            logo.outlineColor = new Color32(8, 18, 55, 255);
            SetRect(logo.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -145f), new Vector2(820f, 100f));
        }

        TextMeshProUGUI subtitle = FindTmp(panel, "SubtitleText");
        if (subtitle != null)
        {
            subtitle.text = "ONE TAP  •  ONE ORBIT";
            subtitle.fontSize = 23f;
            subtitle.fontStyle = FontStyles.Bold;
            subtitle.characterSpacing = 9f;
            subtitle.color = new Color(0.32f, 0.86f, 1f, 0.92f);
            SetRect(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -235f), new Vector2(760f, 54f));
        }

        CreateStartEmblem(panel);

        TextMeshProUGUI tap = FindTmp(panel, "TAP TO START");
        if (tap != null)
        {
            CreateLaunchPlate(panel, tap.transform.GetSiblingIndex());
            tap.text = "TAP TO LAUNCH";
            tap.fontSize = 29f;
            tap.fontStyle = FontStyles.Bold;
            tap.characterSpacing = 4f;
            tap.color = new Color(1f, 0.74f, 0.22f, 1f);
            tap.raycastTarget = false;
            SetRect(tap.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 184f), new Vector2(370f, 74f));
            tap.transform.SetAsLastSibling();
        }

        TextMeshProUGUI best = FindTmp(panel, "BestScoreText");
        if (best != null)
        {
            int value = PlayerPrefs.GetInt("HighScore", 0);
            best.text = value > 0 ? "BEST  •  " + value : "FIRST FLIGHT";
            best.fontSize = 21f;
            best.fontStyle = FontStyles.Bold;
            best.characterSpacing = 3f;
            best.color = new Color(0.72f, 0.79f, 0.94f, 0.9f);
            best.gameObject.SetActive(true);
            SetRect(best.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 280f), new Vector2(420f, 44f));
        }

        if (FindDeep(panel, "ControlHint") == null)
        {
            TextMeshProUGUI hint = UIStyleKit.MakeLabel(
                panel, "TAP: LAUNCH     HOLD: REVERSE", 16f,
                new Color(0.67f, 0.74f, 0.88f, 0.82f), new Vector2(0f, 108f), new Vector2(520f, 36f),
                FontStyles.Bold, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            hint.gameObject.name = "ControlHint";
            hint.characterSpacing = 2f;
        }
    }

    void CreateStartEmblem(Transform panel)
    {
        if (FindDeep(panel, "OrbitEmblem") != null) return;

        Texture2D texture = Resources.Load<Texture2D>("Visuals/orbit_emblem");
        if (texture == null) return;

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);

        GameObject go = new GameObject("OrbitEmblem");
        go.transform.SetParent(panel, false);
        startEmblem = go.AddComponent<RectTransform>();
        SetRect(startEmblem, new Vector2(0.5f, 1f), new Vector2(0f, -505f), new Vector2(430f, 430f));

        Image image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = new Color(1f, 1f, 1f, 0.96f);
    }

    void CreateLaunchPlate(Transform panel, int siblingIndex)
    {
        if (FindDeep(panel, "LaunchPlate") != null) return;

        GameObject go = new GameObject("LaunchPlate");
        go.transform.SetParent(panel, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        SetRect(rect, new Vector2(0.5f, 0f), new Vector2(0f, 184f), new Vector2(410f, 82f));

        Image image = go.AddComponent<Image>();
        UIStyleKit.ApplyPanel(image, new Color(0.06f, 0.12f, 0.28f, 0.90f));
        image.raycastTarget = false;
        go.transform.SetSiblingIndex(Mathf.Max(0, siblingIndex));

        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0.20f, 0.77f, 1f, 0.55f);
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = true;
    }

    void StyleHud()
    {
        TextMeshProUGUI score = FindTmp(canvas.transform, "ScoreText");
        if (score != null)
        {
            score.fontSize = 54f;
            score.fontStyle = FontStyles.Bold;
            score.alignment = TextAlignmentOptions.Center;
            score.color = UIStyleKit.TextMain;
            score.outlineWidth = 0.1f;
            score.outlineColor = new Color32(5, 15, 44, 220);
            SetRect(score.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(180f, 66f));
            CreateScorePlate(score.transform.parent, score.transform.GetSiblingIndex());
        }

        foreach (string name in new[] { "PauseButton", "HelpButton", "SoundButton2", "DayNightButton" })
        {
            Transform control = FindDeep(canvas.transform, name);
            RectTransform rect = control != null ? control.GetComponent<RectTransform>() : null;
            if (rect != null)
                rect.sizeDelta = new Vector2(Mathf.Max(64f, rect.sizeDelta.x), Mathf.Max(64f, rect.sizeDelta.y));
        }
    }

    void CreateScorePlate(Transform parent, int siblingIndex)
    {
        if (FindDeep(parent, "ScorePlate") != null) return;

        GameObject go = new GameObject("ScorePlate");
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        SetRect(rect, new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(190f, 100f));

        Image image = go.AddComponent<Image>();
        UIStyleKit.ApplyPanel(image, new Color(0.035f, 0.07f, 0.17f, 0.78f));
        image.raycastTarget = false;

        TextMeshProUGUI caption = UIStyleKit.MakeLabel(go.transform, "ORBIT", 12f,
            new Color(0.31f, 0.84f, 1f, 0.9f), new Vector2(0f, 31f), new Vector2(160f, 22f), FontStyles.Bold);
        caption.characterSpacing = 7f;

        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0.17f, 0.58f, 0.92f, 0.35f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        go.transform.SetSiblingIndex(Mathf.Max(0, siblingIndex));
    }

    void StyleGameOver()
    {
        Transform panel = FindDeep(canvas.transform, "GameOverPanel");
        if (panel == null) return;
        gameOverPanel = panel.gameObject;

        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        if (layout != null) layout.enabled = false;

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        if (panelRect != null)
        {
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(760f, 1120f);
        }

        Image panelImage = panel.GetComponent<Image>();
        if (panelImage != null)
        {
            UIStyleKit.ApplyPanel(panelImage, new Color(0.045f, 0.065f, 0.15f, 0.98f));
            panelImage.raycastTarget = true;
        }
        AddOutline(panel.gameObject, new Color(0.18f, 0.57f, 0.94f, 0.55f), 3f);
        CreatePanelDim(panel, "GameOverDim", new Color(0.008f, 0.012f, 0.035f, 0.78f));

        TextMeshProUGUI title = FindTmp(panel, "GameOverText");
        if (title != null)
        {
            title.text = "GAME OVER";
            title.fontSize = 60f;
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 4f;
            title.color = new Color(1f, 0.39f, 0.20f, 1f);
            title.alignment = TextAlignmentOptions.Center;
            SetLocalRect(title.rectTransform, new Vector2(0f, 420f), new Vector2(660f, 90f));
        }

        EnsurePanelLabel(panel, "FlightReport", "FLIGHT REPORT", new Vector2(0f, 485f), 18f,
            new Color(0.39f, 0.82f, 1f, 0.9f), 6f);
        EnsureDivider(panel, "ReportDivider", new Vector2(0f, 360f), new Vector2(610f, 3f));

        TextMeshProUGUI score = FindTmp(panel, "ScoreResultText");
        if (score != null)
        {
            score.fontSize = 72f;
            score.fontStyle = FontStyles.Bold;
            score.color = UIStyleKit.TextMain;
            score.alignment = TextAlignmentOptions.Center;
            SetLocalRect(score.rectTransform, new Vector2(0f, 280f), new Vector2(650f, 100f));
        }

        TextMeshProUGUI best = FindTmp(panel, "HighScoreText");
        if (best != null)
        {
            best.fontSize = 32f;
            best.fontStyle = FontStyles.Bold;
            best.color = UIStyleKit.CoinColor;
            best.alignment = TextAlignmentOptions.Center;
            SetLocalRect(best.rectTransform, new Vector2(0f, 195f), new Vector2(620f, 56f));
        }

        gameOverCoinsText = EnsurePanelLabel(panel, "RunCoinsEarnedText", "RUN COINS  +0",
            new Vector2(0f, 120f), 30f, UIStyleKit.CoinColor, 2f);
        if (gameOverCoinsText != null)
            SetLocalRect(gameOverCoinsText.rectTransform, new Vector2(0f, 120f), new Vector2(620f, 58f));

        StyleMajorButton(panel, "RestartButton", "FLY AGAIN", new Vector2(0f, -15f), UIStyleKit.BtnAccent, new Vector2(600f, 92f));
        StyleMajorButton(panel, "ShareButton", "SHARE FLIGHT", new Vector2(0f, -135f), UIStyleKit.BtnPrimary, new Vector2(600f, 88f));
        StyleMajorButton(panel, "MainMenuButton_GameOver", "MAIN MENU", new Vector2(0f, -250f), UIStyleKit.BtnNeutral, new Vector2(600f, 84f));
    }

    void StylePause()
    {
        Transform panel = FindDeep(canvas.transform, "PausePanel");
        if (panel == null) return;

        Image overlay = panel.GetComponent<Image>();
        if (overlay != null)
        {
            overlay.color = new Color(0.01f, 0.018f, 0.055f, 0.90f);
            overlay.raycastTarget = true;
        }

        EnsurePauseCard(panel);

        TextMeshProUGUI title = EnsurePanelLabel(panel, "TitleText", "FLIGHT PAUSED",
            new Vector2(0f, 260f), 52f, UIStyleKit.TextMain, 3f);
        if (title != null)
        {
            title.text = "FLIGHT PAUSED";
            title.fontSize = 52f;
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.Center;
            SetLocalRect(title.rectTransform, new Vector2(0f, 260f), new Vector2(660f, 82f));
        }

        TextMeshProUGUI description = EnsurePanelLabel(panel, "PauseDescription",
            "Take a breath. Your orbit is waiting.", new Vector2(0f, 185f), 24f,
            UIStyleKit.TextSub, 0.5f);
        if (description != null)
            SetLocalRect(description.rectTransform, new Vector2(0f, 185f), new Vector2(640f, 52f));

        if (FindDeep(panel, "RestartButton") == null)
        {
            UIStyleKit.MakeButtonAnchored(panel, "RestartButton", "RESTART RUN",
                new Vector2(0f, -65f), new Vector2(600f, 92f), UIStyleKit.BtnAccent,
                RestartFromPause, 30f);
        }

        StyleMajorButton(panel, "ResumeButton", "RESUME", new Vector2(0f, 65f), UIStyleKit.BtnPrimary, new Vector2(600f, 96f));
        StyleMajorButton(panel, "RestartButton", "RESTART RUN", new Vector2(0f, -65f), UIStyleKit.BtnAccent, new Vector2(600f, 92f));
        StyleMajorButton(panel, "MainMenuButton", "MAIN MENU", new Vector2(0f, -195f), UIStyleKit.BtnNeutral, new Vector2(600f, 88f));
    }

    // Called again by TutorialManager every time the panel opens, so the polished
    // layout always wins over ApplyContent()'s plainer fallback values.
    public static void RestyleTutorial()
    {
        if (instance == null) return;
        if (instance.canvas == null) instance.canvas = FindAnyObjectByType<Canvas>();
        instance.StyleTutorial();
    }

    void StyleTutorial()
    {
        if (canvas == null) return;
        Transform panel = FindDeep(canvas.transform, "TutorialPanel");
        if (panel == null) return;

        Image overlay = panel.GetComponent<Image>();
        if (overlay != null)
        {
            overlay.color = new Color(0.008f, 0.015f, 0.045f, 0.92f);
            overlay.raycastTarget = true;
        }

        Transform card = FindDeep(panel, "Card");
        if (card != null)
        {
            RectTransform cardRect = card.GetComponent<RectTransform>();
            if (cardRect != null)
            {
                cardRect.anchorMin = new Vector2(0.055f, 0.05f);
                cardRect.anchorMax = new Vector2(0.945f, 0.95f);
                cardRect.pivot = new Vector2(0.5f, 0.5f);
                cardRect.anchoredPosition = Vector2.zero;
                cardRect.offsetMin = Vector2.zero;
                cardRect.offsetMax = Vector2.zero;
            }
            Image cardImage = card.GetComponent<Image>();
            if (cardImage != null) UIStyleKit.ApplyPanel(cardImage, UIStyleKit.BgPanel);
            AddOutline(card.gameObject, new Color(0.18f, 0.60f, 0.96f, 0.5f), 3f);
        }

        TextMeshProUGUI title = FindTmp(panel, "TitleText");
        if (title != null)
        {
            title.text = "ORBIT TRAINING";
            title.fontSize = 50f;
            title.fontStyle = FontStyles.Bold;
            title.color = new Color(0.36f, 0.86f, 1f, 1f);
            title.alignment = TextAlignmentOptions.Center;
            SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -82f), new Vector2(800f, 86f));
        }

        ScrollRect scroll = panel.GetComponentInChildren<ScrollRect>(true);
        if (scroll != null)
        {
            RectTransform scrollRect = scroll.GetComponent<RectTransform>();
            if (scrollRect != null)
            {
                scrollRect.anchorMin = new Vector2(0.065f, 0.15f);
                scrollRect.anchorMax = new Vector2(0.935f, 0.84f);
                scrollRect.offsetMin = Vector2.zero;
                scrollRect.offsetMax = Vector2.zero;
            }
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.09f;
            scroll.scrollSensitivity = 35f;
        }

        Transform viewport = FindDeep(panel, "Viewport");
        RectTransform viewportRect = viewport != null ? viewport.GetComponent<RectTransform>() : null;
        if (viewportRect != null)
        {
            SetStretch(viewportRect, new Vector2(14f, 14f), new Vector2(-14f, -14f));
            if (scroll != null) scroll.viewport = viewportRect;
        }

        Transform contentRoot = FindDeep(panel, "Content");
        RectTransform contentRect = contentRoot != null ? contentRoot.GetComponent<RectTransform>() : null;
        if (contentRect != null)
        {
            ContentSizeFitter fitter = contentRoot.GetComponent<ContentSizeFitter>();
            if (fitter != null)
            {
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            }
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 1680f);
            if (scroll != null) scroll.content = contentRect;
        }

        TextMeshProUGUI content = FindTmp(panel, "ContentText");
        if (content != null)
        {
            content.text = TutorialManager.InstructionText;
            content.fontSize = 30f;
            content.lineSpacing = 7f;
            content.paragraphSpacing = 10f;
            content.color = UIStyleKit.TextMain;
            content.alignment = TextAlignmentOptions.TopLeft;
            content.textWrappingMode = TextWrappingModes.Normal;
            content.overflowMode = TextOverflowModes.Overflow;
            content.maskable = true;
            content.raycastTarget = false;

            RectTransform contentTextRect = content.rectTransform;
            contentTextRect.anchorMin = Vector2.zero;
            contentTextRect.anchorMax = Vector2.one;
            contentTextRect.pivot = new Vector2(0.5f, 1f);
            contentTextRect.offsetMin = new Vector2(28f, 20f);
            contentTextRect.offsetMax = new Vector2(-28f, -18f);
        }

        StyleMajorButton(panel, "GotItButton", "READY TO FLY", Vector2.zero, UIStyleKit.BtnSuccess, new Vector2(620f, 100f));
        Transform gotIt = FindDeep(panel, "GotItButton");
        RectTransform gotItRect = gotIt != null ? gotIt.GetComponent<RectTransform>() : null;
        if (gotItRect != null)
        {
            gotItRect.anchorMin = gotItRect.anchorMax = new Vector2(0.5f, 0f);
            gotItRect.pivot = new Vector2(0.5f, 0.5f);
            gotItRect.anchoredPosition = new Vector2(0f, 86f);
            gotItRect.sizeDelta = new Vector2(620f, 100f);
        }
    }

    void StyleCommonButtons()
    {
        foreach (Button button in canvas.GetComponentsInChildren<Button>(true))
        {
            string name = button.gameObject.name.Trim();
            if (name == "StartButton" || name == "GotItButton" || name == "LightButton" || name == "DarkButton"
                || name.Contains("Sound") || name.Contains("DayNight") || name == "PauseButton")
                continue;

            if (button.GetComponent<UIButtonPressFeedback>() == null)
                button.gameObject.AddComponent<UIButtonPressFeedback>();
        }
    }

    void StyleMajorButton(Transform root, string name, string label, Vector2 position, Color color, Vector2 size)
    {
        Transform target = FindDeep(root, name);
        if (target == null) return;

        RectTransform rect = target.GetComponent<RectTransform>();
        if (rect != null) SetLocalRect(rect, position, size);

        Button button = target.GetComponent<Button>();
        if (button != null)
        {
            Image image = target.GetComponent<Image>();
            if (image != null)
            {
                UIStyleKit.ApplyPanel(image, color);
                image.raycastTarget = true;
            }

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            button.colors = colors;
        }

        TextMeshProUGUI tmp = target.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null)
        {
            tmp.text = label;
            UIStyleKit.ApplyRuntimeFont(tmp, target);
            tmp.fontSize = 30f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.characterSpacing = 2f;
            tmp.color = UIStyleKit.TextMain;
            tmp.alignment = TextAlignmentOptions.Center;
            UIStyleKit.ConfigureButtonLabel(tmp, 30f);
        }

        Text legacy = target.GetComponentInChildren<Text>(true);
        if (legacy != null)
        {
            legacy.text = label;
            legacy.fontSize = 28;
            legacy.fontStyle = FontStyle.Bold;
            legacy.color = UIStyleKit.TextMain;
        }
    }

    void EnsurePauseCard(Transform panel)
    {
        Transform existing = FindDeep(panel, "PauseCard");
        if (existing != null) return;

        GameObject go = new GameObject("PauseCard");
        go.transform.SetParent(panel, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        SetLocalRect(rect, Vector2.zero, new Vector2(760f, 800f));

        Image image = go.AddComponent<Image>();
        UIStyleKit.ApplyPanel(image, new Color(0.045f, 0.065f, 0.15f, 0.98f));
        image.raycastTarget = false;
        AddOutline(go, new Color(0.18f, 0.57f, 0.94f, 0.55f), 3f);
        go.transform.SetAsFirstSibling();
    }

    void RestartFromPause()
    {
        Time.timeScale = 1f;
        if (GameManager.instance != null) GameManager.instance.RestartGame();
    }

    void RefreshGameOverCoins()
    {
        if (gameOverCoinsText == null && gameOverPanel != null)
            gameOverCoinsText = FindTmp(gameOverPanel.transform, "RunCoinsEarnedText");
        if (gameOverCoinsText == null) return;

        int earned = CoinManager.instance != null ? CoinManager.instance.GetRunCoinsEarned() : 0;
        gameOverCoinsText.text = "RUN COINS  +" + earned;
    }

    void CreatePanelDim(Transform panel, string name, Color color)
    {
        Transform parent = panel.parent;
        Transform existing = FindDirect(parent, name);
        if (existing != null)
        {
            gameOverDim = existing.gameObject;
            return;
        }

        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = true;
        go.transform.SetSiblingIndex(panel.GetSiblingIndex());
        panel.SetSiblingIndex(go.transform.GetSiblingIndex() + 1);
        go.SetActive(panel.gameObject.activeInHierarchy);
        gameOverDim = go;
    }

    TextMeshProUGUI EnsurePanelLabel(Transform parent, string name, string text, Vector2 position, float size, Color color, float spacing)
    {
        TextMeshProUGUI existing = FindTmp(parent, name);
        if (existing != null) return existing;
        TextMeshProUGUI label = UIStyleKit.MakeLabel(parent, text, size, color, position,
            new Vector2(520f, 36f), FontStyles.Bold);
        label.gameObject.name = name;
        label.characterSpacing = spacing;
        return label;
    }

    void EnsureDivider(Transform parent, string name, Vector2 position, Vector2 size)
    {
        if (FindDeep(parent, name) != null) return;
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        SetLocalRect(rect, position, size);
        Image image = go.AddComponent<Image>();
        image.color = new Color(0.23f, 0.63f, 0.95f, 0.38f);
        image.raycastTarget = false;
    }

    static void AddOutline(GameObject target, Color color, float distance)
    {
        Outline outline = target.GetComponent<Outline>();
        if (outline == null) outline = target.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(distance, -distance);
        outline.useGraphicAlpha = true;
    }

    static TextMeshProUGUI FindTmp(Transform root, string name)
    {
        Transform target = FindDeep(root, name);
        TextMeshProUGUI text = target != null ? target.GetComponent<TextMeshProUGUI>() : null;
        if (text != null) UIStyleKit.ApplyRuntimeFont(text, root);
        return text;
    }

    static Transform FindDirect(Transform parent, string name)
    {
        if (parent == null) return null;
        foreach (Transform child in parent)
            if (child.name.Trim() == name) return child;
        return null;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name.Trim() == name) return root;
        foreach (Transform child in root)
        {
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static void SetRect(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    static void SetLocalRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    static void SetStretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}

public sealed class UIButtonPressFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    private Vector3 targetScale = Vector3.one;

    void OnEnable()
    {
        transform.localScale = Vector3.one;
        targetScale = Vector3.one;
    }

    void Update()
    {
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, 22f * Time.unscaledDeltaTime);
    }

    public void OnPointerDown(PointerEventData eventData) => targetScale = Vector3.one * 0.95f;
    public void OnPointerUp(PointerEventData eventData) => targetScale = Vector3.one;
    public void OnPointerExit(PointerEventData eventData) => targetScale = Vector3.one;
}
