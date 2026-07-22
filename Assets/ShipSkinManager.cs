using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShipSkinManager : MonoBehaviour
{
    public static ShipSkinManager instance;

    private struct SkinData
    {
        public string name;
        public Color tint;
        public string prefsKey;
    }

    private sealed class SkinCardView
    {
        public RectTransform rect;
        public Image background;
        public Outline outline;
        public Image preview;
        public TextMeshProUGUI priceText;
        public TextMeshProUGUI statusText;
        public Button actionButton;
        public Image actionBackground;
        public TextMeshProUGUI actionText;
    }

    private static readonly SkinData[] Skins =
    {
        new SkinData { name = "DEFAULT", tint = Color.white, prefsKey = "skin_0" },
        new SkinData { name = "FIRE", tint = new Color(1f, 0.35f, 0.1f), prefsKey = "skin_1" },
        new SkinData { name = "ICE", tint = new Color(0.4f, 0.85f, 1f), prefsKey = "skin_2" },
        new SkinData { name = "GOLD", tint = new Color(1f, 0.82f, 0.1f), prefsKey = "skin_3" },
    };

    private const string SelectedSkinKey = "SelectedSkin";

    private readonly List<SkinCardView> cardViews = new List<SkinCardView>();
    private int selectedSkin;
    private SpriteRenderer rocketRenderer;
    private GameObject shopPanel;
    private RectTransform shopCard;
    private ScrollRect shopScroll;
    private RectTransform shopContent;
    private TextMeshProUGUI shopBalanceText;
    private bool shopOpen;
    private Coroutine panelAnimation;
    private Coroutine flashAnimation;
    private Coroutine purchaseAnimation;
    private RectTransform purchasePulseTarget;
    private GameObject flashObject;

    void Awake()
    {
        if (instance == null) instance = this;
        else { Destroy(gameObject); return; }

        PlayerPrefs.SetInt(Skins[0].prefsKey, 1);
        selectedSkin = PlayerPrefs.GetInt(SelectedSkinKey, 0);

        if (selectedSkin < 0 || selectedSkin >= Skins.Length
            || PlayerPrefs.GetInt(Skins[selectedSkin].prefsKey, 0) != 1)
        {
            selectedSkin = 0;
            PlayerPrefs.SetInt(SelectedSkinKey, selectedSkin);
        }

        PlayerPrefs.Save();
    }

    void Start()
    {
        rocketRenderer = FindRocketRenderer();
        ApplySkin(selectedSkin);
        CreateShopButton();

        if (CoinManager.instance != null)
            CoinManager.instance.BalanceChanged += OnBalanceChanged;
    }

    void OnDestroy()
    {
        if (CoinManager.instance != null)
            CoinManager.instance.BalanceChanged -= OnBalanceChanged;
        if (instance == this) instance = null;
    }

    SpriteRenderer FindRocketRenderer()
    {
        RocketController controller = FindAnyObjectByType<RocketController>();
        return controller != null ? controller.GetComponent<SpriteRenderer>() : null;
    }

    public void ApplySkin(int index)
    {
        if (index < 0 || index >= Skins.Length) return;
        selectedSkin = index;
        if (rocketRenderer == null) rocketRenderer = FindRocketRenderer();
        if (rocketRenderer != null) rocketRenderer.color = Skins[index].tint;
    }

    public void ReapplyCurrent() => ApplySkin(selectedSkin);

    void CreateShopButton()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return;

        Transform startPanel = canvas.transform.Find("StartPanel");
        Transform parent = startPanel != null ? startPanel : canvas.transform;
        if (parent.Find("ShopButton") != null) return;

        UIStyleKit.MakeButtonAnchored(
            parent: parent,
            name: "ShopButton",
            label: "SHOP",
            pos: new Vector2(32f, 32f),
            size: new Vector2(210f, 72f),
            bgColor: UIStyleKit.BtnNeutral,
            onClick: ToggleShop,
            fontSize: 30f,
            anchorMin: Vector2.zero,
            anchorMax: Vector2.zero,
            pivot: Vector2.zero);
    }

    public void ToggleShop()
    {
        if (shopOpen) CloseShop();
        else OpenShop();
    }

    public void OpenShop()
    {
        if (shopPanel == null) BuildShopPanel();
        if (shopPanel == null)
        {
            Debug.LogError("Rocket Shop could not open because its runtime panel was not created.", this);
            return;
        }

        if (!EnsureShopContentReady(out string error))
        {
            Debug.LogError("Rocket Shop content is invalid: " + error
                + " The panel will not be opened empty.", this);
            return;
        }

        shopPanel.SetActive(true);
        shopPanel.transform.SetAsLastSibling();
        shopOpen = true;
        RefreshShop();

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(shopContent);
        shopScroll.verticalNormalizedPosition = 1f;

        CanvasGroup group = shopPanel.GetComponent<CanvasGroup>();
        if (group != null) group.blocksRaycasts = group.interactable = true;

        if (panelAnimation != null) StopCoroutine(panelAnimation);
        panelAnimation = StartCoroutine(AnimatePanel(true));
    }

    public void CloseShop()
    {
        if (!shopOpen) return;
        shopOpen = false;
        if (shopPanel == null) return;

        if (panelAnimation != null) StopCoroutine(panelAnimation);
        panelAnimation = StartCoroutine(AnimatePanel(false));
    }

    void BuildShopPanel()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("Rocket Shop could not be created because no active Canvas was found.", this);
            return;
        }

        shopPanel = new GameObject("ShopPanel");
        shopPanel.transform.SetParent(canvas.transform, false);
        RectTransform panelRect = shopPanel.AddComponent<RectTransform>();
        Stretch(panelRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        Image overlay = shopPanel.AddComponent<Image>();
        overlay.color = new Color(0.005f, 0.01f, 0.035f, 0.92f);
        overlay.raycastTarget = true;

        CanvasGroup group = shopPanel.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = true;
        group.blocksRaycasts = true;

        GameObject cardGo = new GameObject("Card");
        cardGo.transform.SetParent(shopPanel.transform, false);
        shopCard = cardGo.AddComponent<RectTransform>();
        Stretch(shopCard, new Vector2(0.06f, 0.055f), new Vector2(0.94f, 0.945f), Vector2.zero, Vector2.zero);

        Image cardBackground = cardGo.AddComponent<Image>();
        UIStyleKit.ApplyPanel(cardBackground, UIStyleKit.BgPanel);
        cardBackground.raycastTarget = true;

        Outline cardOutline = cardGo.AddComponent<Outline>();
        cardOutline.effectColor = new Color(0.19f, 0.61f, 1f, 0.55f);
        cardOutline.effectDistance = new Vector2(3f, -3f);

        TextMeshProUGUI title = UIStyleKit.MakeLabel(cardGo.transform, "ROCKET SHOP", 46f,
            UIStyleKit.TextMain, new Vector2(0f, -70f), new Vector2(700f, 82f), FontStyles.Bold,
            TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        title.characterSpacing = 4f;

        TextMeshProUGUI subtitle = UIStyleKit.MakeLabel(cardGo.transform,
            "Choose a style. Every purchase is permanent.", 23f, UIStyleKit.TextSub,
            new Vector2(0f, -132f), new Vector2(680f, 46f), FontStyles.Normal,
            TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        subtitle.enableAutoSizing = true;
        subtitle.fontSizeMin = 18f;
        subtitle.fontSizeMax = 23f;

        BuildShopBalance(cardGo.transform);

        UIStyleKit.MakeButtonAnchored(
            parent: cardGo.transform,
            name: "CloseBtn",
            label: "X",
            pos: new Vector2(-28f, -28f),
            size: new Vector2(72f, 72f),
            bgColor: UIStyleKit.BtnDanger,
            onClick: CloseShop,
            fontSize: 30f,
            anchorMin: Vector2.one,
            anchorMax: Vector2.one,
            pivot: Vector2.one);

        BuildShopScroll(cardGo.transform);
    }

    void BuildShopBalance(Transform parent)
    {
        GameObject balanceGo = new GameObject("ShopBalance");
        balanceGo.transform.SetParent(parent, false);
        RectTransform rect = balanceGo.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -178f);
        rect.sizeDelta = new Vector2(390f, 76f);

        Image bg = balanceGo.AddComponent<Image>();
        UIStyleKit.ApplyPanel(bg, new Color(0.10f, 0.13f, 0.25f, 1f));
        bg.raycastTarget = false;

        GameObject iconGo = new GameObject("CoinIcon");
        iconGo.transform.SetParent(balanceGo.transform, false);
        RectTransform iconRect = iconGo.AddComponent<RectTransform>();
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(24f, 0f);
        iconRect.sizeDelta = new Vector2(44f, 44f);
        Image icon = iconGo.AddComponent<Image>();
        icon.sprite = UIStyleKit.Circle;
        icon.color = UIStyleKit.CoinColor;
        icon.raycastTarget = false;

        shopBalanceText = UIStyleKit.MakeLabel(balanceGo.transform, "0 COINS", 32f,
            UIStyleKit.TextMain, new Vector2(45f, 0f), new Vector2(270f, 62f), FontStyles.Bold,
            TextAlignmentOptions.MidlineLeft);
        shopBalanceText.enableAutoSizing = true;
        shopBalanceText.fontSizeMin = 23f;
        shopBalanceText.fontSizeMax = 32f;
        shopBalanceText.textWrappingMode = TextWrappingModes.NoWrap;
    }

    void BuildShopScroll(Transform parent)
    {
        GameObject scrollGo = new GameObject("ShopScroll");
        scrollGo.transform.SetParent(parent, false);
        RectTransform scrollRectTransform = scrollGo.AddComponent<RectTransform>();
        Stretch(scrollRectTransform, new Vector2(0.035f, 0.055f), new Vector2(0.965f, 0.76f), Vector2.zero, Vector2.zero);

        Image scrollHitArea = scrollGo.AddComponent<Image>();
        scrollHitArea.color = new Color(0f, 0f, 0f, 0.001f);
        scrollHitArea.raycastTarget = true;

        shopScroll = scrollGo.AddComponent<ScrollRect>();
        shopScroll.horizontal = false;
        shopScroll.vertical = true;
        shopScroll.movementType = ScrollRect.MovementType.Elastic;
        shopScroll.elasticity = 0.08f;
        shopScroll.inertia = true;
        shopScroll.decelerationRate = 0.12f;
        shopScroll.scrollSensitivity = 35f;

        GameObject viewportGo = new GameObject("Viewport");
        viewportGo.transform.SetParent(scrollGo.transform, false);
        RectTransform viewport = viewportGo.AddComponent<RectTransform>();
        Stretch(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image viewportImage = viewportGo.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
        viewportImage.raycastTarget = true;
        viewportGo.AddComponent<RectMask2D>();

        GameObject contentGo = new GameObject("Content");
        contentGo.transform.SetParent(viewportGo.transform, false);
        shopContent = contentGo.AddComponent<RectTransform>();
        shopContent.anchorMin = new Vector2(0f, 1f);
        shopContent.anchorMax = new Vector2(1f, 1f);
        shopContent.pivot = new Vector2(0.5f, 1f);
        shopContent.anchoredPosition = Vector2.zero;
        shopContent.sizeDelta = Vector2.zero;

        GridLayoutGroup grid = contentGo.AddComponent<GridLayoutGroup>();
        grid.padding = new RectOffset(18, 18, 18, 18);
        grid.cellSize = new Vector2(350f, 390f);
        grid.spacing = new Vector2(24f, 28f);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;

        ContentSizeFitter fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        shopScroll.viewport = viewport;
        shopScroll.content = shopContent;

        BuildShopCards();
    }

    bool BuildShopCards()
    {
        cardViews.Clear();

        if (shopContent == null)
        {
            Debug.LogError("Rocket Shop cards could not be created because Content is missing.", this);
            return false;
        }

        try
        {
            for (int i = 0; i < Skins.Length; i++) BuildSkinCard(shopContent, i);
        }
        catch (Exception exception)
        {
            Debug.LogError("Rocket Shop card creation failed. Expected Default, Fire, Ice and Gold.\n"
                + exception, this);
            return false;
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(shopContent);
        return cardViews.Count == Skins.Length;
    }

    bool EnsureShopContentReady(out string error)
    {
        if (shopScroll == null)
        {
            error = "ScrollRect is missing.";
            return false;
        }
        if (shopScroll.viewport == null)
        {
            error = "ScrollRect viewport is missing.";
            return false;
        }
        if (shopContent == null || shopScroll.content != shopContent)
        {
            error = "ScrollRect Content is missing or assigned to the wrong RectTransform.";
            return false;
        }

        if (cardViews.Count == 0 && shopContent.childCount == 0 && !BuildShopCards())
        {
            error = "the four product cards could not be created.";
            return false;
        }
        if (cardViews.Count != Skins.Length)
        {
            error = "expected 4 product cards but found " + cardViews.Count + ".";
            return false;
        }

        for (int i = 0; i < cardViews.Count; i++)
        {
            SkinCardView view = cardViews[i];
            if (view == null || view.rect == null || view.rect.parent != shopContent)
            {
                error = "product card " + i + " is missing or has the wrong parent.";
                return false;
            }
            view.rect.localScale = Vector3.one;
            view.rect.gameObject.SetActive(true);
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(shopContent);
        if (shopContent.rect.width <= 1f || shopContent.rect.height <= 1f)
        {
            error = "Content has a zero-sized layout ("
                + shopContent.rect.width + " x " + shopContent.rect.height + ").";
            return false;
        }

        error = null;
        return true;
    }

    void BuildSkinCard(Transform parent, int index)
    {
        SkinData skin = Skins[index];
        GameObject cardGo = new GameObject("SkinCard_" + index);
        cardGo.transform.SetParent(parent, false);
        RectTransform rect = cardGo.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(350f, 390f);
        rect.localScale = Vector3.one;

        Image background = cardGo.AddComponent<Image>();
        UIStyleKit.ApplyPanel(background, UIStyleKit.BgCard);
        background.raycastTarget = true;

        Outline outline = cardGo.AddComponent<Outline>();
        outline.effectColor = new Color(0.25f, 0.35f, 0.58f, 0.55f);
        outline.effectDistance = new Vector2(2f, -2f);

        GameObject previewPlate = new GameObject("PreviewPlate");
        previewPlate.transform.SetParent(cardGo.transform, false);
        RectTransform previewPlateRect = previewPlate.AddComponent<RectTransform>();
        previewPlateRect.anchorMin = previewPlateRect.anchorMax = new Vector2(0.5f, 0.5f);
        previewPlateRect.anchoredPosition = new Vector2(0f, 98f);
        previewPlateRect.sizeDelta = new Vector2(168f, 168f);
        Image plate = previewPlate.AddComponent<Image>();
        plate.sprite = UIStyleKit.Circle;
        plate.color = new Color(0.035f, 0.055f, 0.13f, 0.94f);
        plate.raycastTarget = false;

        GameObject previewGo = new GameObject("Preview");
        previewGo.transform.SetParent(previewPlate.transform, false);
        RectTransform previewRect = previewGo.AddComponent<RectTransform>();
        previewRect.anchorMin = previewRect.anchorMax = new Vector2(0.5f, 0.5f);
        previewRect.sizeDelta = new Vector2(116f, 116f);
        Image preview = previewGo.AddComponent<Image>();
        preview.sprite = rocketRenderer != null && rocketRenderer.sprite != null
            ? rocketRenderer.sprite
            : UIStyleKit.Circle;
        preview.color = skin.tint;
        preview.preserveAspect = true;
        preview.raycastTarget = false;

        TextMeshProUGUI nameText = UIStyleKit.MakeLabel(cardGo.transform, skin.name, 30f,
            UIStyleKit.TextMain, new Vector2(0f, 4f), new Vector2(340f, 48f), FontStyles.Bold);
        nameText.characterSpacing = 2f;

        TextMeshProUGUI priceText = UIStyleKit.MakeLabel(cardGo.transform, "PRICE", 24f,
            UIStyleKit.CoinColor, new Vector2(0f, -45f), new Vector2(330f, 42f), FontStyles.Bold);

        TextMeshProUGUI statusText = UIStyleKit.MakeLabel(cardGo.transform, "AVAILABLE", 20f,
            UIStyleKit.TextSub, new Vector2(0f, -88f), new Vector2(340f, 38f), FontStyles.Bold);
        statusText.enableAutoSizing = true;
        statusText.fontSizeMin = 15f;
        statusText.fontSizeMax = 20f;
        statusText.textWrappingMode = TextWrappingModes.NoWrap;

        int capturedIndex = index;
        Button actionButton = UIStyleKit.MakeButtonAnchored(
            parent: cardGo.transform,
            name: "ActionButton",
            label: "BUY",
            pos: new Vector2(0f, -145f),
            size: new Vector2(320f, 68f),
            bgColor: UIStyleKit.BtnGold,
            onClick: () => OnSkinClicked(capturedIndex),
            fontSize: 25f);

        cardViews.Add(new SkinCardView
        {
            rect = rect,
            background = background,
            outline = outline,
            preview = preview,
            priceText = priceText,
            statusText = statusText,
            actionButton = actionButton,
            actionBackground = actionButton.GetComponent<Image>(),
            actionText = actionButton.GetComponentInChildren<TextMeshProUGUI>(),
        });
    }

    void RefreshShop()
    {
        int balance = CoinManager.instance != null ? CoinManager.instance.GetCoins() : 0;
        if (shopBalanceText != null) shopBalanceText.text = balance + " COINS";

        for (int i = 0; i < cardViews.Count && i < Skins.Length; i++)
        {
            SkinCardView view = cardViews[i];
            int price = GameEconomyConfig.Current.GetSkinPrice(i);
            bool owned = PlayerPrefs.GetInt(Skins[i].prefsKey, i == 0 ? 1 : 0) == 1;
            bool equipped = selectedSkin == i;
            bool affordable = balance >= price;

            view.priceText.text = price == 0 ? "PRICE  •  FREE" : "PRICE  •  " + price + " COINS";
            view.preview.color = Skins[i].tint;

            if (equipped)
            {
                view.statusText.text = "EQUIPPED";
                view.statusText.color = new Color(0.45f, 1f, 0.68f, 1f);
                SetActionState(view, "EQUIPPED", UIStyleKit.BtnSelected, false);
                view.background.color = new Color(0.08f, 0.32f, 0.24f, 0.98f);
                view.outline.effectColor = new Color(0.35f, 1f, 0.67f, 0.95f);
                view.outline.effectDistance = new Vector2(4f, -4f);
            }
            else if (owned)
            {
                view.statusText.text = "OWNED";
                view.statusText.color = new Color(0.45f, 0.83f, 1f, 1f);
                SetActionState(view, "EQUIP", UIStyleKit.BtnPrimary, true);
                view.background.color = UIStyleKit.BgCard;
                view.outline.effectColor = new Color(0.25f, 0.55f, 0.92f, 0.72f);
                view.outline.effectDistance = new Vector2(2f, -2f);
            }
            else
            {
                view.statusText.text = affordable ? "AVAILABLE" : "NEED " + (price - balance) + " MORE";
                view.statusText.color = affordable ? UIStyleKit.CoinColor : new Color(0.92f, 0.47f, 0.38f, 1f);
                // It remains clickable so an insufficient-balance tap can explain the problem.
                SetActionState(view, "BUY", affordable ? UIStyleKit.BtnGold : UIStyleKit.BtnNeutral, true);
                view.background.color = UIStyleKit.BgCard;
                view.outline.effectColor = affordable
                    ? new Color(1f, 0.72f, 0.16f, 0.72f)
                    : new Color(0.24f, 0.29f, 0.44f, 0.7f);
                view.outline.effectDistance = new Vector2(2f, -2f);
            }
        }
    }

    static void SetActionState(SkinCardView view, string label, Color color, bool interactable)
    {
        view.actionButton.interactable = interactable;
        if (view.actionBackground != null)
        {
            UIStyleKit.ApplyPanel(view.actionBackground, color);
            view.actionBackground.raycastTarget = true;
        }
        if (view.actionText != null) view.actionText.text = label;
    }

    void OnSkinClicked(int index)
    {
        if (index < 0 || index >= Skins.Length) return;

        SkinData skin = Skins[index];
        int price = GameEconomyConfig.Current.GetSkinPrice(index);
        bool owned = PlayerPrefs.GetInt(skin.prefsKey, index == 0 ? 1 : 0) == 1;

        if (!owned)
        {
            int balance = CoinManager.instance != null ? CoinManager.instance.GetCoins() : 0;
            if (CoinManager.instance == null || balance < price || !CoinManager.instance.SpendCoins(price))
            {
                int missing = Mathf.Max(0, price - balance);
                ShowFlash("NOT ENOUGH COINS  •  NEED " + missing + " MORE", UIStyleKit.BtnDanger);
                if (index < cardViews.Count) StartPurchasePulse(cardViews[index].rect, false);
                RefreshShop();
                return;
            }

            PlayerPrefs.SetInt(skin.prefsKey, 1);
            ShowFlash("PURCHASED  •  EQUIPPED", UIStyleKit.BtnSuccess);
            if (index < cardViews.Count) StartPurchasePulse(cardViews[index].rect, true);
        }

        selectedSkin = index;
        PlayerPrefs.SetInt(SelectedSkinKey, selectedSkin);
        PlayerPrefs.Save();
        ApplySkin(selectedSkin);
        RefreshShop();
    }

    void OnBalanceChanged(int _) => RefreshShop();

    void StartPurchasePulse(RectTransform target, bool success)
    {
        if (target == null) return;
        if (purchaseAnimation != null)
        {
            StopCoroutine(purchaseAnimation);
            if (purchasePulseTarget != null) purchasePulseTarget.localScale = Vector3.one;
        }
        purchasePulseTarget = target;
        purchaseAnimation = StartCoroutine(PurchasePulse(target, success));
    }

    IEnumerator PurchasePulse(RectTransform target, bool success)
    {
        Vector3 start = Vector3.one;
        Vector3 peak = Vector3.one * (success ? 1.07f : 0.96f);
        float elapsed = 0f;
        while (elapsed < 0.14f)
        {
            elapsed += Time.unscaledDeltaTime;
            target.localScale = Vector3.Lerp(start, peak, Mathf.Clamp01(elapsed / 0.14f));
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < 0.18f)
        {
            elapsed += Time.unscaledDeltaTime;
            target.localScale = Vector3.Lerp(peak, Vector3.one, Mathf.Clamp01(elapsed / 0.18f));
            yield return null;
        }

        target.localScale = Vector3.one;
        if (purchasePulseTarget == target) purchasePulseTarget = null;
        purchaseAnimation = null;
    }

    IEnumerator AnimatePanel(bool opening)
    {
        CanvasGroup group = shopPanel != null ? shopPanel.GetComponent<CanvasGroup>() : null;
        if (group == null) yield break;

        float startAlpha = group.alpha;
        float endAlpha = opening ? 1f : 0f;
        Vector3 startScale = shopCard != null ? shopCard.localScale : Vector3.one;
        Vector3 endScale = opening ? Vector3.one : Vector3.one * 0.97f;
        if (opening && shopCard != null)
        {
            startScale = Vector3.one * 0.94f;
            shopCard.localScale = startScale;
        }

        float duration = opening ? 0.24f : 0.18f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            group.alpha = Mathf.Lerp(startAlpha, endAlpha, p);
            if (shopCard != null) shopCard.localScale = Vector3.Lerp(startScale, endScale, p);
            yield return null;
        }

        group.alpha = endAlpha;
        if (shopCard != null) shopCard.localScale = endScale;

        if (!opening)
        {
            group.interactable = false;
            group.blocksRaycasts = false;
            shopPanel.SetActive(false);
            if (shopCard != null) shopCard.localScale = Vector3.one;
        }

        panelAnimation = null;
    }

    void ShowFlash(string message, Color color)
    {
        if (flashAnimation != null) StopCoroutine(flashAnimation);
        if (flashObject != null) Destroy(flashObject);
        flashAnimation = StartCoroutine(FlashRoutine(message, color));
    }

    IEnumerator FlashRoutine(string message, Color color)
    {
        if (shopCard == null) yield break;

        flashObject = new GameObject("ShopNotice");
        flashObject.transform.SetParent(shopCard, false);
        RectTransform rect = flashObject.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 42f);
        rect.sizeDelta = new Vector2(700f, 76f);

        Image bg = flashObject.AddComponent<Image>();
        UIStyleKit.ApplyPanel(bg, color);
        bg.raycastTarget = false;
        TextMeshProUGUI label = UIStyleKit.AddLabel(flashObject.transform, message, 25f,
            UIStyleKit.TextMain, FontStyles.Bold);
        UIStyleKit.ConfigureButtonLabel(label, 25f);

        CanvasGroup group = flashObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        group.alpha = 0f;

        float elapsed = 0f;
        while (elapsed < 0.18f)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Clamp01(elapsed / 0.18f);
            rect.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, Mathf.Clamp01(elapsed / 0.18f));
            yield return null;
        }

        yield return new WaitForSecondsRealtime(1.15f);

        elapsed = 0f;
        while (elapsed < 0.2f)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = 1f - Mathf.Clamp01(elapsed / 0.2f);
            yield return null;
        }

        Destroy(flashObject);
        flashObject = null;
        flashAnimation = null;
    }

    static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}
