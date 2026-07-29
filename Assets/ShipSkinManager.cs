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
        // The card's lit edge. Was a UnityEngine.UI.Outline — a flat copy of the
        // shape nudged two pixels diagonally, which is not what any other surface
        // in this game uses. UIKit's rim child replaces it, so the state colours
        // below now drive that instead.
        public Image rim;
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

        // The same scrim the pause and tutorial screens dim with, so the shop
        // arrives over the menu the way every other overlay in the game does.
        UIDesign.EnsureInitialised();
        Image overlay = shopPanel.AddComponent<Image>();
        overlay.color = UIDesign.Scrim;
        overlay.raycastTarget = true;
        UITinted.Attach(shopPanel, UITinted.Role.Scrim);

        CanvasGroup group = shopPanel.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = true;
        group.blocksRaycasts = true;

        GameObject cardGo = new GameObject("Card");
        cardGo.transform.SetParent(shopPanel.transform, false);
        shopCard = cardGo.AddComponent<RectTransform>();
        Stretch(shopCard, new Vector2(0.06f, 0.055f), new Vector2(0.94f, 0.945f), Vector2.zero, Vector2.zero);

        // The same glass card as Game Over and the tutorial. No shadow: it very
        // nearly fills the screen, so there is nothing behind it to fall on.
        Image cardBackground = cardGo.AddComponent<Image>();
        cardBackground.raycastTarget = true;
        UIKit.MakeGlass(cardGo, UIDesign.RadiusCard, UITinted.Role.GlassDeep, 1f, false, true);

        TextMeshProUGUI title = UIStyleKit.MakeLabel(cardGo.transform, "ROCKET SHOP",
            UIDesign.TypeTitle, UIDesign.TextMain, new Vector2(0f, -70f), new Vector2(700f, 82f),
            FontStyles.Bold, TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        UIKit.StyleDisplay(title, UIDesign.TypeTitle, UIDesign.TrackTitle, UIDesign.TextMain);

        // Set in the caption style every other small line in the game uses, so
        // the shop stops being the one screen with sentence-case body copy.
        TextMeshProUGUI subtitle = UIStyleKit.MakeLabel(cardGo.transform,
            "CHOOSE A STYLE  •  PURCHASES ARE PERMANENT", UIDesign.TypeCaption, UIDesign.TextSub,
            new Vector2(0f, -132f), new Vector2(680f, 46f), FontStyles.Bold,
            TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        UIKit.StyleText(subtitle, UIDesign.TypeCaption, UIDesign.TrackCaption, UIDesign.TextMuted,
            FontStyles.Bold);

        BuildShopBalance(cardGo.transform);

        // A red square with a typed "X" was the loudest thing on the screen. It
        // becomes the shared glass disc wearing the baked close glyph — the same
        // control as sound, help and pause.
        Button close = UIStyleKit.MakeButtonAnchored(
            parent: cardGo.transform,
            name: "CloseBtn",
            label: string.Empty,
            pos: new Vector2(-28f, -28f),
            size: Vector2.one * (UIDesign.IconButtonSize * 0.78f),
            bgColor: UIDesign.Glass,
            onClick: CloseShop,
            fontSize: UIDesign.TypeButton,
            anchorMin: Vector2.one,
            anchorMax: Vector2.one,
            pivot: Vector2.one);

        if (close != null)
        {
            TextMeshProUGUI closeLabel = close.GetComponentInChildren<TextMeshProUGUI>(true);
            if (closeLabel != null) closeLabel.gameObject.SetActive(false);
            UIKit.StyleIconButton(close.transform, UIIcons.Close, UIDesign.IconButtonSize * 0.78f);
        }

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
        rect.sizeDelta = new Vector2(390f, UIDesign.ChipHeight);

        // Literally the coin counter from the HUD: same chip radius, same glass,
        // same baked coin. The player's balance should not change appearance
        // depending on which screen they read it on.
        Image bg = balanceGo.AddComponent<Image>();
        bg.raycastTarget = false;
        UIKit.MakeGlass(balanceGo, UIDesign.RadiusChip, UITinted.Role.Glass, 0.92f, false);

        GameObject iconGo = new GameObject("CoinIcon");
        iconGo.transform.SetParent(balanceGo.transform, false);
        RectTransform iconRect = iconGo.AddComponent<RectTransform>();
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(24f, 0f);
        iconRect.sizeDelta = new Vector2(44f, 44f);
        Image icon = iconGo.AddComponent<Image>();
        icon.sprite = UIIcons.Get(UIIcons.Coin);
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        shopBalanceText = UIStyleKit.MakeLabel(balanceGo.transform, "0 COINS", UIDesign.TypeHeading,
            UIDesign.TextMain, new Vector2(45f, 0f), new Vector2(270f, 62f), FontStyles.Bold,
            TextAlignmentOptions.MidlineLeft);
        UIKit.StyleText(shopBalanceText, UIDesign.TypeHeading, UIDesign.TrackButton,
            UIDesign.TextMain, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
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
        background.raycastTarget = true;
        // A pill radius rather than the card radius: these are controls inside a
        // card, not cards of their own.
        UIKit.MakeGlass(cardGo, UIDesign.RadiusPill, UITinted.Role.Glass, 0.9f, false, true);

        Transform rimTransform = cardGo.transform.Find("Rim");
        Image rim = rimTransform != null ? rimTransform.GetComponent<Image>() : null;
        // The state colours below own this rim, so it must stop following the
        // world palette or it would be repainted every frame.
        if (rim != null)
        {
            UITinted rimTint = rim.GetComponent<UITinted>();
            if (rimTint != null) { rimTint.enabled = false; Destroy(rimTint); }
        }

        GameObject previewPlate = new GameObject("PreviewPlate");
        previewPlate.transform.SetParent(cardGo.transform, false);
        RectTransform previewPlateRect = previewPlate.AddComponent<RectTransform>();
        previewPlateRect.anchorMin = previewPlateRect.anchorMax = new Vector2(0.5f, 0.5f);
        previewPlateRect.anchoredPosition = new Vector2(0f, 98f);
        previewPlateRect.sizeDelta = new Vector2(168f, 168f);
        Image plate = previewPlate.AddComponent<Image>();
        plate.raycastTarget = false;
        // The same antialiased disc the icon buttons are cut from, at the deep
        // glass value, so the rocket sits in a well rather than on a flat dot.
        UIKit.MakeGlassDisc(previewPlate, UITinted.Role.GlassDeep, 1f, false);

        GameObject previewGo = new GameObject("Preview");
        previewGo.transform.SetParent(previewPlate.transform, false);
        RectTransform previewRect = previewGo.AddComponent<RectTransform>();
        previewRect.anchorMin = previewRect.anchorMax = new Vector2(0.5f, 0.5f);
        previewRect.sizeDelta = new Vector2(116f, 116f);
        Image preview = previewGo.AddComponent<Image>();
        // The gameplay rocket is a 3D model now; its root sprite is an invisible
        // bounds proxy, so shop cards use a pre-rendered snapshot of the model.
        Sprite modelPreview = Resources.Load<Sprite>("RocketPreview");
        preview.sprite = modelPreview != null ? modelPreview : UIStyleKit.Circle;
        preview.color = skin.tint;
        preview.preserveAspect = true;
        preview.raycastTarget = false;

        // Three sizes, three trackings, one falling hierarchy: name, then price,
        // then state. Previously all three were within six points of each other.
        TextMeshProUGUI nameText = UIStyleKit.MakeLabel(cardGo.transform, skin.name,
            UIDesign.TypeButton, UIDesign.TextMain, new Vector2(0f, 4f), new Vector2(340f, 48f),
            FontStyles.Bold);
        UIKit.StyleText(nameText, UIDesign.TypeButton, UIDesign.TrackButton, UIDesign.TextMain,
            FontStyles.Bold);

        TextMeshProUGUI priceText = UIStyleKit.MakeLabel(cardGo.transform, "PRICE",
            UIDesign.TypeLabel, UIDesign.Gold, new Vector2(0f, -45f), new Vector2(330f, 42f),
            FontStyles.Bold);
        UIKit.StyleText(priceText, UIDesign.TypeLabel, UIDesign.TrackLabel, UIDesign.Gold,
            FontStyles.Bold);

        TextMeshProUGUI statusText = UIStyleKit.MakeLabel(cardGo.transform, "AVAILABLE",
            UIDesign.TypeCaption, UIDesign.TextSub, new Vector2(0f, -88f), new Vector2(340f, 38f),
            FontStyles.Bold);
        UIKit.StyleText(statusText, UIDesign.TypeCaption, UIDesign.TrackCaption, UIDesign.TextSub,
            FontStyles.Bold);

        int capturedIndex = index;
        Button actionButton = UIStyleKit.MakeButtonAnchored(
            parent: cardGo.transform,
            name: "ActionButton",
            label: "BUY",
            pos: new Vector2(0f, -145f),
            size: new Vector2(320f, UIDesign.ButtonHeightPill * 0.78f),
            bgColor: UIDesign.Glass,
            onClick: () => OnSkinClicked(capturedIndex),
            fontSize: UIDesign.TypeBody);

        // A glass pill like every other button in the game. Its state is carried
        // by the label and the rim, not by a flat slab of yellow or green.
        UIKit.StylePill(actionButton.transform, "BUY", UIDesign.RadiusChip, UITinted.Role.GlassDeep,
            null, UIDesign.TypeBody);

        cardViews.Add(new SkinCardView
        {
            rect = rect,
            background = background,
            rim = rim,
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

            // One equipped card carries the call-to-action colour; everything
            // else stays on the world palette. That is the same rule the launch
            // pill follows, and it is what makes the current skin obvious.
            if (equipped)
            {
                view.statusText.text = "EQUIPPED";
                view.statusText.color = UIDesign.Accent;
                SetActionState(view, "EQUIPPED", UIDesign.Accent, UIDesign.Accent, false);
                SetRim(view, UIDesign.Accent, 1f);
            }
            else if (owned)
            {
                view.statusText.text = "OWNED";
                view.statusText.color = UIDesign.TextSub;
                SetActionState(view, "EQUIP", UIDesign.Cta, UIDesign.CtaText, true);
                SetRim(view, UIDesign.GlassRim, 1f);
            }
            else
            {
                view.statusText.text = affordable ? "AVAILABLE" : "NEED " + (price - balance) + " MORE";
                view.statusText.color = affordable ? UIDesign.Gold : UIDesign.Danger;
                // It remains clickable so an insufficient-balance tap can explain the problem.
                SetActionState(view, "BUY", affordable ? UIDesign.Gold : UIDesign.GlassRim,
                    affordable ? UIDesign.TextMain : UIDesign.TextMuted, true);
                // A card you cannot afford recedes. At the palette's own rim
                // strength it sat as bright as the equipped card, because both
                // rims carry the world's hue — the only separation left is value.
                SetRim(view, affordable ? UIDesign.Gold : UIDesign.GlassRim, affordable ? 0.7f : 0.42f);
            }
        }
    }

    // The rim is the only thing that changes shape-wise between states, so the
    // grid keeps one silhouette and still reads at a glance.
    static void SetRim(SkinCardView view, Color color, float alpha)
    {
        if (view.rim == null) return;
        view.rim.color = new Color(color.r, color.g, color.b, color.a * alpha);
    }

    static void SetActionState(SkinCardView view, string label, Color rimColor, Color textColor,
        bool interactable)
    {
        view.actionButton.interactable = interactable;
        // The pill's own glass never changes; only its rim and label do. Swapping
        // the fill for a flat colour is what made these read as web buttons.
        UIKit.OverrideRim(view.actionButton.gameObject,
            new Color(rimColor.r, rimColor.g, rimColor.b, interactable ? 0.72f : 0.95f));
        if (view.actionBackground != null) view.actionBackground.raycastTarget = true;
        if (view.actionText != null)
        {
            view.actionText.text = label;
            view.actionText.color = textColor;
        }
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
                ShowFlash("NOT ENOUGH COINS  •  NEED " + missing + " MORE", UIDesign.Danger);
                if (index < cardViews.Count) StartPurchasePulse(cardViews[index].rect, false);
                RefreshShop();
                return;
            }

            PlayerPrefs.SetInt(skin.prefsKey, 1);
            ShowFlash("PURCHASED  •  EQUIPPED", UIDesign.Accent);
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

        // A glass chip carrying the message, with the outcome colour on its rim
        // and its text — not a slab of flat red or green.
        Image bg = flashObject.AddComponent<Image>();
        bg.raycastTarget = false;
        UIKit.MakeGlass(flashObject, UIDesign.RadiusChip, UITinted.Role.GlassDeep, 1f, false);
        UIKit.OverrideRim(flashObject, new Color(color.r, color.g, color.b, 0.8f));

        TextMeshProUGUI label = UIStyleKit.AddLabel(flashObject.transform, message,
            UIDesign.TypeBody, color, FontStyles.Bold);
        UIKit.StyleText(label, UIDesign.TypeBody, UIDesign.TrackLabel, color, FontStyles.Bold);

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
