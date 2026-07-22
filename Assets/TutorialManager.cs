using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TutorialManager : MonoBehaviour
{
    public static TutorialManager instance;

    public GameObject tutorialPanel;

    public const string InstructionText =
        "<color=#55DFFF><size=115%><b>TAP TO LAUNCH</b></size></color>\n" +
        "Tap the screen to launch toward the next planet.\n\n" +
        "<color=#FF7040><size=115%><b>HOLD TO REVERSE</b></size></color>\n" +
        "Hold the screen to switch the orbit direction. The new direction stays active after release. Hold again to reverse once more.\n\n" +
        "<color=#B7A8FF><size=115%><b>ORBIT RINGS</b></size></color>\n" +
        "Reach the visible orbit ring to attach safely. Avoid hitting the planet.\n\n" +
        "<color=#FFBE3D><size=115%><b>FIRST 10 LEVELS</b></size></color>\n" +
        "The progress bar tracks the first 10 levels. After Level 10, it disappears and the game continues in endless score mode.\n\n" +
        "<color=#A9BCE8><size=115%><b>ASTEROID WARNING</b></size></color>\n" +
        "Watch the warning arrow and change direction or launch at the right time to avoid incoming asteroids.";

    private bool isFromStartButton;
    private Coroutine panelAnimation;

    void Awake() => instance = this;

    void Start()
    {
        ResolveTutorialPanel();
        EnsureTutorialStructure(tutorialPanel);
        ApplyContent();
        if (tutorialPanel != null) tutorialPanel.SetActive(false);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void ResolveTutorialPanel()
    {
        if (tutorialPanel != null)
        {
            EnsureTutorialStructure(tutorialPanel);
            return;
        }

        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("Orbit Training panel could not be created because no Canvas was found.", this);
            return;
        }

        Transform existing = FindDeep(canvas.transform, "TutorialPanel");
        if (existing != null)
        {
            tutorialPanel = existing.gameObject;
            EnsureTutorialStructure(tutorialPanel);
            return;
        }

        GameObject panelGo = new GameObject("TutorialPanel");
        panelGo.transform.SetParent(canvas.transform, false);
        tutorialPanel = panelGo;

        RectTransform panelRect = panelGo.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image overlay = panelGo.AddComponent<Image>();
        overlay.color = new Color(0.008f, 0.015f, 0.045f, 0.92f);
        overlay.raycastTarget = true;

        CanvasGroup group = panelGo.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        group.blocksRaycasts = true;
        group.interactable = true;

        GameObject cardGo = new GameObject("Card");
        cardGo.transform.SetParent(panelGo.transform, false);
        RectTransform cardRect = cardGo.AddComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.055f, 0.05f);
        cardRect.anchorMax = new Vector2(0.945f, 0.95f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.anchoredPosition = Vector2.zero;
        cardRect.offsetMin = Vector2.zero;
        cardRect.offsetMax = Vector2.zero;

        Image cardImage = cardGo.AddComponent<Image>();
        UIStyleKit.ApplyPanel(cardImage, UIStyleKit.BgPanel);
        cardImage.raycastTarget = true;

        Outline outline = cardGo.AddComponent<Outline>();
        outline.effectColor = new Color(0.18f, 0.60f, 0.96f, 0.5f);
        outline.effectDistance = new Vector2(3f, -3f);

        CreateText(panelGo.transform, "TitleText", "ORBIT TRAINING", 50f,
            new Color(0.36f, 0.86f, 1f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(800f, 86f), FontStyles.Bold, TextAlignmentOptions.Center, new Vector2(0.5f, 1f));

        GameObject scrollGo = new GameObject("ScrollRect");
        scrollGo.transform.SetParent(cardGo.transform, false);
        RectTransform scrollRectTransform = scrollGo.AddComponent<RectTransform>();
        scrollRectTransform.anchorMin = new Vector2(0.065f, 0.15f);
        scrollRectTransform.anchorMax = new Vector2(0.935f, 0.84f);
        scrollRectTransform.offsetMin = Vector2.zero;
        scrollRectTransform.offsetMax = Vector2.zero;

        ScrollRect scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.elasticity = 0.09f;
        scroll.scrollSensitivity = 35f;

        GameObject viewportGo = new GameObject("Viewport");
        viewportGo.transform.SetParent(scrollGo.transform, false);
        RectTransform viewportRect = viewportGo.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(14f, 14f);
        viewportRect.offsetMax = new Vector2(-14f, -14f);

        Image viewportImage = viewportGo.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
        viewportImage.raycastTarget = true;
        viewportGo.AddComponent<RectMask2D>();
        scroll.viewport = viewportRect;

        GameObject contentGo = new GameObject("Content");
        contentGo.transform.SetParent(viewportGo.transform, false);
        RectTransform contentRect = contentGo.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, 1680f);
        scroll.content = contentRect;

        ContentSizeFitter fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        CreateText(contentGo.transform, "ContentText", string.Empty, 30f,
            UIStyleKit.TextMain, Vector2.zero, Vector2.one, new Vector2(-56f, -40f),
            FontStyles.Normal, TextAlignmentOptions.TopLeft, new Vector2(0.5f, 1f));

        Button gotItButton = UIStyleKit.MakeButtonAnchored(
            panelGo.transform, "GotItButton", "READY TO FLY",
            new Vector2(0f, 86f), new Vector2(620f, 100f), UIStyleKit.BtnSuccess,
            () => OnGotItClicked(), 30f, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
        RectTransform gotItRect = gotItButton.GetComponent<RectTransform>();
        gotItRect.anchoredPosition = new Vector2(0f, 86f);
        gotItRect.sizeDelta = new Vector2(620f, 100f);

        panelGo.SetActive(false);
    }

    void EnsureTutorialStructure(GameObject panelGo)
    {
        if (panelGo == null) return;

        Transform card = FindDeep(panelGo.transform, "Card");
        if (card == null)
        {
            card = CreateCard(panelGo.transform).transform;
        }

        if (FindDeep(panelGo.transform, "TitleText") == null)
        {
            CreateText(card, "TitleText", "ORBIT TRAINING", 50f,
                new Color(0.36f, 0.86f, 1f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(800f, 86f), FontStyles.Bold, TextAlignmentOptions.Center, new Vector2(0.5f, 1f));
        }

        // Look up the scroll area by its ScrollRect component rather than by a
        // hard-coded name. The scene authors this object as "ScrollView", so a
        // name-based search for "ScrollRect" used to miss it and build a second,
        // empty scroll area on top of the real one every time the panel opened.
        ScrollRect existingScroll = card.GetComponentInChildren<ScrollRect>(true);
        Transform scrollRect = existingScroll != null ? existingScroll.transform : null;
        if (scrollRect == null)
        {
            scrollRect = CreateScrollArea(card).transform;
        }

        Transform contentText = FindDeep(panelGo.transform, "ContentText");
        if (contentText == null)
        {
            Transform contentRoot = FindDeep(scrollRect, "Content");
            if (contentRoot == null)
            {
                contentRoot = FindDeep(scrollRect, "Viewport");
            }
            if (contentRoot != null)
            {
                CreateText(contentRoot, "ContentText", string.Empty, 30f,
                    UIStyleKit.TextMain, Vector2.zero, Vector2.one, new Vector2(-56f, -40f),
                    FontStyles.Normal, TextAlignmentOptions.TopLeft, new Vector2(0.5f, 1f));
            }
        }

        if (FindDeep(panelGo.transform, "GotItButton") == null)
        {
            Button gotItButton = UIStyleKit.MakeButtonAnchored(
                panelGo.transform, "GotItButton", "READY TO FLY",
                new Vector2(0f, 86f), new Vector2(620f, 100f), UIStyleKit.BtnSuccess,
                () => OnGotItClicked(), 30f, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
            RectTransform gotItRect = gotItButton.GetComponent<RectTransform>();
            gotItRect.anchoredPosition = new Vector2(0f, 86f);
            gotItRect.sizeDelta = new Vector2(620f, 100f);
        }
    }

    GameObject CreateCard(Transform parent)
    {
        GameObject cardGo = new GameObject("Card");
        cardGo.transform.SetParent(parent, false);
        RectTransform cardRect = cardGo.AddComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.055f, 0.05f);
        cardRect.anchorMax = new Vector2(0.945f, 0.95f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.anchoredPosition = Vector2.zero;
        cardRect.offsetMin = Vector2.zero;
        cardRect.offsetMax = Vector2.zero;

        Image cardImage = cardGo.AddComponent<Image>();
        UIStyleKit.ApplyPanel(cardImage, UIStyleKit.BgPanel);
        cardImage.raycastTarget = true;

        Outline outline = cardGo.AddComponent<Outline>();
        outline.effectColor = new Color(0.18f, 0.60f, 0.96f, 0.5f);
        outline.effectDistance = new Vector2(3f, -3f);
        return cardGo;
    }

    GameObject CreateScrollArea(Transform parent)
    {
        GameObject scrollGo = new GameObject("ScrollRect");
        scrollGo.transform.SetParent(parent, false);
        RectTransform scrollRectTransform = scrollGo.AddComponent<RectTransform>();
        scrollRectTransform.anchorMin = new Vector2(0.065f, 0.15f);
        scrollRectTransform.anchorMax = new Vector2(0.935f, 0.84f);
        scrollRectTransform.offsetMin = Vector2.zero;
        scrollRectTransform.offsetMax = Vector2.zero;

        ScrollRect scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.elasticity = 0.09f;
        scroll.scrollSensitivity = 35f;

        GameObject viewportGo = new GameObject("Viewport");
        viewportGo.transform.SetParent(scrollGo.transform, false);
        RectTransform viewportRect = viewportGo.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(14f, 14f);
        viewportRect.offsetMax = new Vector2(-14f, -14f);

        Image viewportImage = viewportGo.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
        viewportImage.raycastTarget = true;
        viewportGo.AddComponent<RectMask2D>();
        scroll.viewport = viewportRect;

        GameObject contentGo = new GameObject("Content");
        contentGo.transform.SetParent(viewportGo.transform, false);
        RectTransform contentRect = contentGo.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, 1680f);
        scroll.content = contentRect;

        ContentSizeFitter fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        return scrollGo;
    }

    TextMeshProUGUI CreateText(Transform parent, string name, string text, float fontSize, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 size, FontStyles style,
        TextAlignmentOptions alignment, Vector2? pivot = null)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        UIStyleKit.ApplyRuntimeFont(tmp, parent);
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = alignment;
        tmp.raycastTarget = false;
        tmp.lineSpacing = 7f;
        tmp.paragraphSpacing = 10f;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.maskable = true;
        return tmp;
    }

    public void OnTapToStart()
    {
        if (PlayerPrefs.GetInt("TutorialShown", 0) == 0)
        {
            isFromStartButton = true;
            ShowTutorial();
        }
        else if (GameManager.instance != null)
        {
            GameManager.instance.StartGame();
        }
    }

    public void OnHelpButtonClicked()
    {
        isFromStartButton = false;
        ShowTutorial();
    }

    public void OnGotItClicked()
    {
        PlayerPrefs.SetInt("TutorialShown", 1);
        PlayerPrefs.Save();

        if (panelAnimation != null) StopCoroutine(panelAnimation);
        panelAnimation = null;
        if (tutorialPanel != null) tutorialPanel.SetActive(false);

        if (isFromStartButton)
        {
            isFromStartButton = false;
            if (GameManager.instance != null) GameManager.instance.StartGame();
        }
    }

    public void ApplyContent()
    {
        ResolveTutorialPanel();
        if (tutorialPanel == null)
        {
            Debug.LogError("Orbit Training content could not be created: TutorialPanel reference is missing.", this);
            return;
        }

        Transform contentObject = FindDeep(tutorialPanel.transform, "ContentText");
        TextMeshProUGUI content = contentObject != null
            ? contentObject.GetComponent<TextMeshProUGUI>()
            : null;
        if (content == null)
        {
            Debug.LogError("Orbit Training content could not be created: ContentText/TextMeshProUGUI is missing.", tutorialPanel);
            return;
        }

        RectTransform contentRoot = content.rectTransform.parent as RectTransform;
        ScrollRect scroll = tutorialPanel.GetComponentInChildren<ScrollRect>(true);
        if (contentRoot == null || scroll == null || scroll.viewport == null)
        {
            Debug.LogError("Orbit Training content could not be laid out: Content root, ScrollRect or Viewport is missing.", tutorialPanel);
            return;
        }

        ContentSizeFitter fitter = contentRoot.GetComponent<ContentSizeFitter>();
        if (fitter != null) fitter.enabled = false;

        contentRoot.anchorMin = new Vector2(0f, 1f);
        contentRoot.anchorMax = new Vector2(1f, 1f);
        contentRoot.pivot = new Vector2(0.5f, 1f);
        contentRoot.anchoredPosition = Vector2.zero;
        contentRoot.sizeDelta = new Vector2(0f, 1380f);

        content.text = InstructionText;
        content.fontSize = 30f;
        content.color = Color.white;
        content.alignment = TextAlignmentOptions.TopLeft;
        content.textWrappingMode = TextWrappingModes.Normal;
        content.overflowMode = TextOverflowModes.Overflow;
        content.maskable = true;
        content.raycastTarget = false;
        content.rectTransform.anchorMin = Vector2.zero;
        content.rectTransform.anchorMax = Vector2.one;
        content.rectTransform.pivot = new Vector2(0.5f, 1f);
        content.rectTransform.anchoredPosition = Vector2.zero;
        content.rectTransform.offsetMin = new Vector2(28f, 22f);
        content.rectTransform.offsetMax = new Vector2(-28f, -22f);

        scroll.content = contentRoot;
        scroll.horizontal = false;
        scroll.vertical = true;
    }

    void ShowTutorial()
    {
        ResolveTutorialPanel();
        if (tutorialPanel == null) return;

        ApplyContent();
        tutorialPanel.SetActive(true);
        tutorialPanel.transform.SetAsLastSibling();

        // VisualPolishController owns the actual visual style (colors, layout, outline).
        // Re-running it here keeps every open consistent instead of falling back to
        // ApplyContent()'s plainer layout, which was overwriting it and leaving the
        // content area blank.
        VisualPolishController.RestyleTutorial();

        Transform contentObject = FindDeep(tutorialPanel.transform, "ContentText");
        TextMeshProUGUI content = contentObject != null ? contentObject.GetComponent<TextMeshProUGUI>() : null;
        if (content != null) content.ForceMeshUpdate(true, true);

        ScrollRect scroll = tutorialPanel.GetComponentInChildren<ScrollRect>(true);
        if (scroll != null)
        {
            Canvas.ForceUpdateCanvases();
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1f;
        }

        if (panelAnimation != null) StopCoroutine(panelAnimation);
        panelAnimation = StartCoroutine(AnimateTutorialOpen());
    }

    IEnumerator AnimateTutorialOpen()
    {
        Transform card = FindDeep(tutorialPanel.transform, "Card");
        CanvasGroup group = tutorialPanel.GetComponent<CanvasGroup>();
        if (group == null) group = tutorialPanel.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = true;
        group.interactable = true;

        if (card != null) card.localScale = Vector3.one * 0.88f;

        float elapsed = 0f;
        while (elapsed < 0.28f)
        {
            elapsed += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 0.28f));
            group.alpha = p;
            if (card != null) card.localScale = Vector3.Lerp(Vector3.one * 0.88f, Vector3.one, p);
            yield return null;
        }

        group.alpha = 1f;
        if (card != null) card.localScale = Vector3.one;

        Transform gotIt = FindDeep(tutorialPanel.transform, "GotItButton");
        while (gotIt != null && tutorialPanel.activeSelf)
        {
            elapsed = 0f;
            while (elapsed < 0.7f && tutorialPanel.activeSelf)
            {
                elapsed += Time.unscaledDeltaTime;
                float scale = 1f + 0.055f * Mathf.Sin(elapsed / 0.7f * Mathf.PI);
                gotIt.localScale = Vector3.one * scale;
                yield return null;
            }
            yield return null;
        }

        if (gotIt != null) gotIt.localScale = Vector3.one;
        panelAnimation = null;
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
}
