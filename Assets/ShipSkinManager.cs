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
        public GameObject modelPrefab;
        // Full root euler for the shop preview render. Most models are authored
        // facing sideways and need the shared (0,0,90) spin to face the icon
        // camera; a model with its own visual pivot (e.g. Retro UFO) already
        // orients itself in OnEnable and specifies Vector3.zero here so the
        // preview doesn't stack a second, conflicting rotation on top.
        public Vector3 shopPreviewEuler;
        // Set only for the three IAP rockets. Ownership then comes from
        // MonetizationManager instead of a coin-purchase PlayerPrefs key.
        public string iapProductId;
        public LowPolyRocketFlame.FlameProfile? flameProfile;
    }

    private sealed class SkinCardView
    {
        public RectTransform rect;
        public Image visual;
        public RectTransform previewRect;
        public Image preview;
        public RawImage modelPreview;
        public Texture2D previewTexture;
        public TextMeshProUGUI nameText;
        public TextMeshProUGUI statusText;
        public Image priceIcon;
    }

    // Bump when shop visuals change so cached runtime panels rebuild once.
    const int ShopPanelPolishVersion = 36;

    private const string SelectedSkinKey = "SelectedSkin";
    private const int ShopPreviewLayer = 31;

    // Three-column rocket grid aligned to the 978px Watch Ad / tab width.
    private const int GridColumns = 3;
    const float GridColumnGap = 14f;
    const float GridRowGap = 16f;
    const float GridVerticalContentPadding = 8f;
    // Visible card content ratio from authored artwork (857×881 visible region).
    const float RocketCardVisibleAspectHeightOverWidth = 881f / 857f;
    // Shared preview authority — circle center and size within the card shell.
    const float GridPreviewDiameterNorm = 0.76f;
    const float GridPreviewCenterFromTopNorm = 0.34f;
    const float GridNameFromTopNorm = 0.66f;
    const float GridActionFromTopNorm = 0.80f;
    const float GridTextWidthInset = 16f;
    // Dedicated price/state row — one alignment authority for icon + amount.
    const float GridPriceIconSize = 38f;
    const float GridPriceRowGap = 8f;
    const float GridPriceRowHeight = 34f;
    // Inner rocket art shrinks a bit further inside the preview circle so
    // there is visible breathing room between the rim and the artwork.
    const float GridPreviewFillNorm = 0.83f;
    static float GridCellWidth =>
        (WatchAdBannerWidth - GridColumnGap * (GridColumns - 1)) / GridColumns;
    static float GridCellHeight => GridCellWidth * ResolveGridCardAspect();
    // Bottom margin below the grid/scroll area — the only reserved space
    // beneath the tabs now that there is no detail panel to size around.
    const float ShopGridBottomPad = 36f;

    private const float TypeCardName = 27f;
    private const float TypeCardStatus = 28f;

    // Premium dark presentation plates — never pure white Glass palette.
    static readonly Color PreviewPlateFillMuted = new Color(0.038f, 0.032f, 0.072f, 0.94f);
    static readonly Color BalanceChipFill = new Color(0.048f, 0.036f, 0.110f, 0.98f);
    static readonly Color BalanceChipRim = new Color(0.72f, 0.52f, 0.98f, 0.55f);

    // Above the Main Menu's screen-filling StartButton and above the fuel gauge's
    // own nested Canvas. Below the Fuel popup, which is the only thing allowed to
    // sit on top of the shop.
    private const int ShopSortingOrder = 380;

    private static readonly Color CoinIdentity = new Color(0.340f, 0.860f, 1.000f);
    private static readonly Color PremiumIdentity = new Color(1.000f, 0.760f, 0.240f);
    private static readonly Color PremiumGlowColor = new Color(0.520f, 0.280f, 0.880f, 0.16f);
    private static readonly Color CoinCardSurface = new Color(0.045f, 0.058f, 0.125f, 0.98f);
    private static readonly Color PremiumCardSurface = new Color(0.075f, 0.048f, 0.115f, 0.98f);
    // Same lime target Stage 3 tuned the Fuel gauge to (#B8FF1A) — one lime
    // across the whole game, not a second green invented for this screen.
    private static readonly Color OwnedLime = new Color(0.722f, 1.000f, 0.102f);
    private static readonly Color LockedLavender = new Color(0.667f, 0.659f, 1.000f);

    const string WatchAdAssetRoot = "Shop/UI/WatchAdButton/";
    const string ShopTabAssetRoot = "Shop/UI/Tabs/";
    const string ShopTabCroppedRoot = "Shop/UI/Tabs/Cropped/";
    const float ShopTabGap = 8f;
    const string ShopBackgroundAsset = "Shop/UI/Background/ShopBackground";
    internal const string ShopHeaderAssetRoot = "Shop/UI/Header/";
    internal const string ShopHeaderCroppedRoot = "Shop/UI/Header/Cropped/";
    // Header layout tuned for 1080×1920 using alpha-cropped sprites.
    const float ShopHeaderEdgeInset = 26f;
    // Cropped ShopTitleCluster visible aspect (625×647).
    const float ShopTitleClusterWidth = 278f;
    const float ShopTitleClusterTopInset = 14f;
    const float ShopTitleClusterAspectWidthOverHeight = 625f / 647f;
    const float ShopTitleClusterGapBeforeContent = 25f;
    const float ShopHeaderControlsTopInset = 14f;
    const float ShopHeaderControlsRowHeight = 124f;
    const float ShopCloseHitSize = 124f;
    const float ShopCloseVisualSize = 100f;
    static readonly Color ShopTabInactiveImageColor = new Color(0.62f, 0.58f, 0.78f, 0.94f);
    // Dynamic flat balance chip — low-profile horizontal glass pill.
    internal const float ShopBalanceChipHeight = 78f;
    const float ShopBalanceDiamondLeftInset = 20f;
    const float ShopBalancePlusRightInset = 18f;
    const float ShopBalanceInnerGap = 8f;
    internal const float ShopBalanceChipMinWidth = 278f;
    internal const float ShopBalanceChipMaxWidth = 400f;
    internal const float ShopBalanceDiamondVisualSize = 50f;
    internal const float ShopBalancePlusVisualSize = 54f;
    internal const float ShopBalancePlusHitSize = 64f;
    const float ShopBalanceChipCapSourceWidth = 160f;
    internal const float ShopBalanceAmountFontSize = 43f;
    internal const float ShopBalanceAmountVerticalLift = 3.5f;
    internal static readonly Color ShopBalanceAmountColor = new Color(0.94f, 0.90f, 0.80f, 1f);
    static Sprite diamondIconSprite;

    // Loaded once and reused everywhere the Shop shows its currency — the
    // header strip, the Watch Ad reward cluster and each grid card's price
    // row all read the same sprite, so "diamond" cannot mean several slightly
    // different icons. Prefers the alpha-cropped source — the raw PNG has
    // roughly 50% transparent padding, which preserveAspect renders as a
    // near-invisible dot at grid-card icon sizes.
    internal static Sprite DiamondIcon()
    {
        if (diamondIconSprite == null)
        {
            diamondIconSprite = Resources.Load<Sprite>(ShopHeaderCroppedRoot + "DiamondIcon_Cropped");
            if (diamondIconSprite == null)
                diamondIconSprite = Resources.Load<Sprite>(WatchAdAssetRoot + "DiamondIcon");
        }
        return diamondIconSprite;
    }

    [Header("Optional Model Skins")]
    [SerializeField] private GameObject catRocketPrefab;
    [SerializeField] private GameObject dogRocketPrefab;
    [SerializeField] private GameObject retroUfoRocketPrefab;

    private enum ShopTab { Rockets, Premium }

    private readonly List<SkinCardView> cardViews = new List<SkinCardView>();
    // Reusable, non-interactive row-filler cards — built lazily (at most
    // GridColumns - 1 are ever needed for one incomplete row) and shared
    // between tabs, so switching tabs never creates or destroys objects.
    private readonly List<RectTransform> placeholderCards = new List<RectTransform>();
    private SkinData[] skins;
    private int selectedSkin;

    private ShopTab activeTab = ShopTab.Rockets;

    private RectTransform rocketsTabButton;
    private RectTransform premiumTabButton;
    private Image rocketsTabImage;
    private Image premiumTabImage;
    private Sprite rocketsTabActiveSprite;
    private Sprite rocketsTabInactiveSprite;
    private Sprite premiumTabActiveSprite;
    private Sprite premiumTabInactiveSprite;

    private RectTransform shopScrollRect;
    private int activeVisibleRows = 1;
    private SpriteRenderer rocketRenderer;
    private RocketModelVisual rocketVisual;
    private GameObject shopPanel;
    private RectTransform shopCard;
    private ScrollRect shopScroll;
    private RectTransform shopContent;
    private TextMeshProUGUI shopBalanceText;
    private RectTransform shopBalanceRect;
    private RectTransform shopBalanceAmountRect;
    private float shopBalanceChipAppliedWidth = -1f;
    private Button shopAdButton;
    private Image shopAdButtonImage;
    private WatchAdButtonPulse shopAdButtonPulse;
    private bool shopAdInProgress;
    private bool shopOpen;
    private Coroutine panelAnimation;
    private Coroutine flashAnimation;
    private readonly Dictionary<RectTransform, Coroutine> activePulseCoroutines = new Dictionary<RectTransform, Coroutine>();
    private GameObject flashObject;

    // Main Menu foreground suppression while the Shop is open. State is captured
    // once per open cycle and restored exactly on close.
    private bool menuForegroundHidden;
    private bool menuForegroundStateCaptured;

    private CanvasGroup capturedStartPanelGroup;
    private float capturedStartPanelAlpha = 1f;
    private bool capturedStartPanelInteractable = true;
    private bool capturedStartPanelBlocksRaycasts = true;

    private MenuFader capturedShowcaseFader;
    private MenuFader capturedBrandFader;
    private bool capturedShowcaseFadersFrozen;

    private readonly List<Renderer> capturedHeroRenderers = new List<Renderer>();
    private readonly List<bool> capturedHeroRendererEnabled = new List<bool>();
    private readonly List<ParticleSystem> capturedHeroParticles = new List<ParticleSystem>();
    private readonly List<bool> capturedHeroParticlePlaying = new List<bool>();
    private readonly HashSet<int> capturedRendererIds = new HashSet<int>();

    public static bool CanOpenFromMainMenu
    {
        get
        {
            if (GameManager.isIntroPlaying) return false;
            if (PresentationGate.IsAnyFullScreenPresentationActive) return false;
            if (!IsMainMenuShopEntryAllowed()) return false;

            // A stale isGameStarted can survive a script recompile while the settled
            // menu is still on screen. Reconcile only at this explicit entry gate —
            // never during an active run where the showcase is already gone.
            if (GameManager.isGameStarted
                && MainMenuShowcase.ExistsInScene
                && MainMenuShowcase.IsMenuReady)
                GameManager.isGameStarted = false;

            return !GameManager.isGameStarted;
        }
    }

    static bool IsMainMenuShopEntryAllowed()
    {
        if (MainMenuShowcase.ExistsInScene)
            return MainMenuShowcase.HasMenuSettled && MainMenuShowcase.IsMenuReady;

        Canvas canvas = FindMainCanvas();
        Transform startPanel = canvas != null ? canvas.transform.Find("StartPanel") : null;
        return startPanel != null && startPanel.gameObject.activeInHierarchy;
    }

    // TEMP DIAGNOSTIC — remove once the Android Shop-block regression is
    // root-caused. Logs the exact reason CanOpenFromMainMenu refused, once
    // per blocked tap, tagged by which entry point was pressed.
    internal static void LogShopBlocked(string source)
    {
        if (CanOpenFromMainMenu) return;

        Canvas canvas = FindMainCanvas();
        Transform startPanel = canvas != null ? canvas.transform.Find("StartPanel") : null;

        string activeKinds = "";
        foreach (PresentationGate.Kind kind in System.Enum.GetValues(typeof(PresentationGate.Kind)))
            if (PresentationGate.IsActive(kind)) activeKinds += kind + " ";
        if (activeKinds.Length == 0) activeKinds = "none";

        Debug.LogWarning("[ShopBlocked] source=" + source
            + " isGameStarted=" + GameManager.isGameStarted
            + " isIntroPlaying=" + GameManager.isIntroPlaying
            + " startPanelActive=" + (startPanel != null && startPanel.gameObject.activeInHierarchy)
            + " menuReady=" + MainMenuShowcase.IsMenuReady
            + " menuSettled=" + MainMenuShowcase.HasMenuSettled
            + " gateActive=" + PresentationGate.IsAnyFullScreenPresentationActive
            + " activeKinds=" + activeKinds);
    }

    void Awake()
    {
        if (instance == null) instance = this;
        else { Destroy(gameObject); return; }

        skins = new[]
        {
            new SkinData { name = "DEFAULT", tint = Color.white, prefsKey = "skin_0" },
            new SkinData { name = "FIRE", tint = new Color(1f, 0.35f, 0.1f), prefsKey = "skin_1" },
            new SkinData { name = "ICE", tint = new Color(0.4f, 0.85f, 1f), prefsKey = "skin_2" },
            new SkinData { name = "GOLD", tint = new Color(1f, 0.82f, 0.1f), prefsKey = "skin_3" },
            new SkinData
            {
                name = "CAT ROCKET",
                tint = Color.white,
                modelPrefab = catRocketPrefab,
                shopPreviewEuler = new Vector3(0f, 0f, 90f),
                iapProductId = MonetizationProducts.RocketCat
            },
            new SkinData
            {
                name = "DOG ROCKET",
                tint = Color.white,
                modelPrefab = dogRocketPrefab,
                shopPreviewEuler = new Vector3(0f, 0f, 90f),
                iapProductId = MonetizationProducts.RocketDog
            },
            new SkinData
            {
                name = "RETRO UFO",
                tint = Color.white,
                modelPrefab = retroUfoRocketPrefab,
                // RetroUfoVisualPivot already orients the saucer correctly in its own
                // OnEnable; stacking the shared 90 degree spin on top of that pushed the
                // preview edge-on, so this skin supplies no extra root rotation.
                shopPreviewEuler = Vector3.zero,
                iapProductId = MonetizationProducts.RocketRetroUfo,
                flameProfile = new LowPolyRocketFlame.FlameProfile
                {
                    outer = new Color(0.55f, 0.18f, 0.85f, 0.88f),
                    inner = new Color(0.35f, 0.95f, 1f, 0.98f),
                    particleStart = new Color(0.6f, 0.95f, 1f),
                    particleMid = new Color(0.55f, 0.3f, 0.95f),
                    particleEnd = new Color(0.3f, 0.08f, 0.55f),
                    lengthMultiplier = 0.65f,
                }
            },
        };

        if (catRocketPrefab == null)
            Debug.LogError("ShipSkinManager: Cat Rocket prefab is not assigned.", this);
        if (dogRocketPrefab == null)
            Debug.LogError("ShipSkinManager: Dog Rocket prefab is not assigned.", this);
        if (retroUfoRocketPrefab == null)
            Debug.LogError("ShipSkinManager: Retro UFO Rocket prefab is not assigned.", this);

        PlayerPrefs.SetInt(skins[0].prefsKey, 1);
        selectedSkin = PlayerPrefs.GetInt(SelectedSkinKey, 0);

        if (selectedSkin < 0 || selectedSkin >= skins.Length || !IsOwned(selectedSkin))
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
        RecoverOrphanedShopGate();
        RecoverOrphanedMenuSuppression();

        if (CoinManager.instance != null)
            CoinManager.instance.BalanceChanged += OnBalanceChanged;

        if (MonetizationManager.instance != null)
        {
            MonetizationManager.instance.ProductsUpdated += OnMonetizationProductsUpdated;
            MonetizationManager.instance.PurchaseSucceeded += OnIapPurchaseSucceeded;
            MonetizationManager.instance.PurchaseFailed += OnIapPurchaseFailed;
        }
    }

    void Update()
    {
        RecoverOrphanedShopGate();
    }

    void OnDestroy()
    {
        if (shopOpen || PresentationGate.IsActive(PresentationGate.Kind.Shop))
        {
            shopOpen = false;
            PresentationGate.Release(PresentationGate.Kind.Shop);
        }
        RestoreMainMenuForeground();

        if (CoinManager.instance != null)
            CoinManager.instance.BalanceChanged -= OnBalanceChanged;

        if (MonetizationManager.instance != null)
        {
            MonetizationManager.instance.ProductsUpdated -= OnMonetizationProductsUpdated;
            MonetizationManager.instance.PurchaseSucceeded -= OnIapPurchaseSucceeded;
            MonetizationManager.instance.PurchaseFailed -= OnIapPurchaseFailed;
        }

        for (int i = 0; i < cardViews.Count; i++)
        {
            Texture2D texture = cardViews[i] != null ? cardViews[i].previewTexture : null;
            if (texture == null) continue;
            Destroy(texture);
        }

        if (instance == this) instance = null;
    }

    SpriteRenderer FindRocketRenderer()
    {
        RocketController controller = FindAnyObjectByType<RocketController>();
        if (controller == null) return null;
        rocketVisual = controller.GetComponent<RocketModelVisual>();
        return controller.GetComponent<SpriteRenderer>();
    }

    public void ApplySkin(int index)
    {
        if (index < 0 || index >= skins.Length) return;
        selectedSkin = index;
        if (rocketRenderer == null) rocketRenderer = FindRocketRenderer();
        if (rocketRenderer != null) rocketRenderer.color = skins[index].tint;
        if (rocketVisual == null && rocketRenderer != null)
            rocketVisual = rocketRenderer.GetComponent<RocketModelVisual>();
        if (rocketVisual != null)
            rocketVisual.SetReplacementModel(skins[index].modelPrefab);

        LowPolyRocketFlame flame = rocketRenderer != null
            ? rocketRenderer.GetComponent<LowPolyRocketFlame>()
            : null;
        if (flame != null)
            flame.SetFlameProfile(skins[index].flameProfile ?? LowPolyRocketFlame.FlameProfile.Classic);
    }

    public void ReapplyCurrent() => ApplySkin(selectedSkin);

    static Canvas FindMainCanvas()
    {
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas candidate = canvases[i];
            if (candidate != null && candidate.isRootCanvas
                && candidate.transform.Find("StartPanel") != null)
                return candidate;
        }

        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas candidate = canvases[i];
            if (candidate != null && candidate.isRootCanvas)
                return candidate;
        }

        return canvases.Length > 0 ? canvases[0] : null;
    }

    void CreateShopButton()
    {
        Canvas canvas = FindMainCanvas();
        if (canvas == null) return;

        Transform startPanel = canvas.transform.Find("StartPanel");
        Transform parent = startPanel != null ? startPanel : canvas.transform;
        Transform existingShopButton = parent.Find("ShopButton");
        if (existingShopButton != null)
        {
            Button button = existingShopButton.GetComponent<Button>();
            if (button == null)
            {
                Debug.LogError("ShipSkinManager: ShopButton has no Button component.", this);
                return;
            }

            button.onClick.RemoveListener(ToggleShop);
            button.onClick.AddListener(ToggleShop);
            return;
        }

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
        LogShopBlocked("OpenShop");
        if (!CanOpenFromMainMenu) return;
        EnsureShopPanelPolishVersion();
        if (shopPanel != null && !IsShopPanelStructureComplete())
            DestroyShopPanelRuntime();
        if (shopPanel == null) BuildShopPanel();
        else ReconcileShopPanelLayout();

        RestoreShopScrollRefs();

        // Grid layout must run before the zero-size guard — content height comes
        // from LayoutVisibleCards, not from Unity's automatic layout pass.
        ShowRocketsTab();

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

        // Canvas.overrideSorting is only writable while the GameObject is active,
        // so it is re-asserted on every open rather than trusted from build time.
        Canvas shopSorting = shopPanel.GetComponent<Canvas>();
        if (shopSorting != null)
        {
            shopSorting.overrideSorting = true;
            shopSorting.sortingOrder = ShopSortingOrder;
        }
        shopOpen = true;
        PresentationGate.Acquire(PresentationGate.Kind.Shop);
        HideMainMenuForeground();
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
        if (shopPanel == null)
        {
            RestoreMainMenuForeground();
            return;
        }

        // The Main Menu behind the shop is one screen-filling StartButton, so the
        // release of the press that closed the shop must not also launch a run.
        CanvasGroup closingGroup = shopPanel.GetComponent<CanvasGroup>();
        if (closingGroup != null) closingGroup.interactable = closingGroup.blocksRaycasts = false;
        MenuInputGuard.SuppressLaunchUntilPointerReleased();

        if (panelAnimation != null) StopCoroutine(panelAnimation);
        panelAnimation = StartCoroutine(AnimatePanel(false));
    }

    void HideMainMenuForeground()
    {
        if (menuForegroundHidden) return;
        CaptureMenuForegroundStateIfNeeded();
        ApplyMenuForegroundSuppression();
        menuForegroundHidden = true;
    }

    void CaptureMenuForegroundStateIfNeeded()
    {
        if (menuForegroundStateCaptured) return;
        menuForegroundStateCaptured = true;
        capturedRendererIds.Clear();
        capturedHeroRenderers.Clear();
        capturedHeroRendererEnabled.Clear();
        capturedHeroParticles.Clear();
        capturedHeroParticlePlaying.Clear();

        Canvas canvas = FindMainCanvas();
        Transform startPanel = canvas != null ? canvas.transform.Find("StartPanel") : null;
        if (startPanel != null)
        {
            capturedStartPanelGroup = startPanel.GetComponent<CanvasGroup>();
            if (capturedStartPanelGroup == null)
                capturedStartPanelGroup = startPanel.gameObject.AddComponent<CanvasGroup>();
            capturedStartPanelAlpha = capturedStartPanelGroup.alpha;
            capturedStartPanelInteractable = capturedStartPanelGroup.interactable;
            capturedStartPanelBlocksRaycasts = capturedStartPanelGroup.blocksRaycasts;
        }

        MainMenuShowcase showcase = FindAnyObjectByType<MainMenuShowcase>();
        if (showcase != null)
        {
            capturedShowcaseFader = MenuFader.Capture(
                showcase.transform, typeof(MenuSpaceBackdrop), typeof(MenuLavaAmbience));
        }

        MenuBrandEmblem brand = FindAnyObjectByType<MenuBrandEmblem>();
        if (brand != null && (showcase == null || brand.transform != showcase.transform))
            capturedBrandFader = MenuFader.Capture(brand.transform);

        MenuHeroRocket heroRocket = FindAnyObjectByType<MenuHeroRocket>();
        if (heroRocket != null)
        {
            CaptureRendererStatesUnder(heroRocket.transform);
            CaptureParticleStatesUnder(heroRocket.transform);
        }
    }

    void ApplyMenuForegroundSuppression()
    {
        if (capturedStartPanelGroup != null)
        {
            capturedStartPanelGroup.alpha = 0f;
            capturedStartPanelGroup.interactable = false;
            capturedStartPanelGroup.blocksRaycasts = false;
        }

        if (capturedShowcaseFader != null)
        {
            capturedShowcaseFader.Freeze();
            capturedShowcaseFader.SetAlpha(0f);
            capturedShowcaseFadersFrozen = true;
        }

        if (capturedBrandFader != null)
        {
            capturedBrandFader.Freeze();
            capturedBrandFader.SetAlpha(0f);
        }

        for (int i = 0; i < capturedHeroRenderers.Count; i++)
        {
            Renderer renderer = capturedHeroRenderers[i];
            if (renderer != null) renderer.enabled = false;
        }

        for (int i = 0; i < capturedHeroParticles.Count; i++)
        {
            ParticleSystem ps = capturedHeroParticles[i];
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    void CaptureRendererStatesUnder(Transform root)
    {
        if (root == null) return;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            int id = renderer.GetInstanceID();
            if (!capturedRendererIds.Add(id)) continue;
            capturedHeroRenderers.Add(renderer);
            capturedHeroRendererEnabled.Add(renderer.enabled);
        }
    }

    void CaptureParticleStatesUnder(Transform root)
    {
        if (root == null) return;
        ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem ps = systems[i];
            if (ps == null) continue;
            capturedHeroParticles.Add(ps);
            capturedHeroParticlePlaying.Add(ps.isPlaying);
        }
    }

    void RestoreMainMenuForeground()
    {
        if (!menuForegroundHidden && !menuForegroundStateCaptured) return;

        if (capturedStartPanelGroup != null)
        {
            capturedStartPanelGroup.alpha = capturedStartPanelAlpha;
            capturedStartPanelGroup.interactable = capturedStartPanelInteractable;
            capturedStartPanelGroup.blocksRaycasts = capturedStartPanelBlocksRaycasts;
        }

        if (capturedShowcaseFader != null)
        {
            capturedShowcaseFader.SetAlpha(1f);
            if (capturedShowcaseFadersFrozen)
            {
                capturedShowcaseFader.Thaw();
                capturedShowcaseFadersFrozen = false;
            }
        }

        if (capturedBrandFader != null)
        {
            capturedBrandFader.SetAlpha(1f);
            capturedBrandFader.Thaw();
        }

        for (int i = 0; i < capturedHeroRenderers.Count; i++)
        {
            Renderer renderer = capturedHeroRenderers[i];
            if (renderer == null) continue;
            renderer.enabled = i < capturedHeroRendererEnabled.Count && capturedHeroRendererEnabled[i];
        }

        for (int i = 0; i < capturedHeroParticles.Count; i++)
        {
            ParticleSystem ps = capturedHeroParticles[i];
            if (ps == null) continue;
            if (i < capturedHeroParticlePlaying.Count && capturedHeroParticlePlaying[i])
                ps.Play(true);
        }

        ClearMenuForegroundCapture();
        menuForegroundHidden = false;
    }

    void ClearMenuForegroundCapture()
    {
        menuForegroundStateCaptured = false;
        capturedStartPanelGroup = null;
        capturedShowcaseFader = null;
        capturedBrandFader = null;
        capturedShowcaseFadersFrozen = false;
        capturedRendererIds.Clear();
        capturedHeroRenderers.Clear();
        capturedHeroRendererEnabled.Clear();
        capturedHeroParticles.Clear();
        capturedHeroParticlePlaying.Clear();
    }

    void RecoverOrphanedShopGate()
    {
        if (!PresentationGate.IsActive(PresentationGate.Kind.Shop)) return;
        if (shopOpen) return;

        bool panelVisible = shopPanel != null && shopPanel.activeInHierarchy;
        if (panelVisible)
        {
            CanvasGroup group = shopPanel.GetComponent<CanvasGroup>();
            if (group != null && group.alpha > 0.05f) return;
        }

        if (panelAnimation != null)
        {
            StopCoroutine(panelAnimation);
            panelAnimation = null;
        }

        if (shopPanel != null)
        {
            CanvasGroup group = shopPanel.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }
            shopPanel.SetActive(false);
        }

        PresentationGate.Release(PresentationGate.Kind.Shop);
        RestoreMainMenuForeground();
    }

    void RecoverOrphanedMenuSuppression()
    {
        if (shopOpen || PresentationGate.IsActive(PresentationGate.Kind.Shop)) return;
        if (!MainMenuShowcase.ExistsInScene || !MainMenuShowcase.IsMenuReady) return;

        bool panelSuppressed = false;
        Canvas canvas = FindMainCanvas();
        Transform startPanel = canvas != null ? canvas.transform.Find("StartPanel") : null;
        if (startPanel != null)
        {
            CanvasGroup cg = startPanel.GetComponent<CanvasGroup>();
            panelSuppressed = cg != null && cg.alpha < 0.05f;
        }

        bool renderersSuppressed = false;
        MainMenuShowcase showcase = FindAnyObjectByType<MainMenuShowcase>();
        if (showcase != null)
        {
            SpriteRenderer[] sprites = showcase.GetComponentsInChildren<SpriteRenderer>(true);
            int visibleCount = 0;
            for (int i = 0; i < sprites.Length; i++)
            {
                SpriteRenderer sprite = sprites[i];
                if (sprite == null) continue;
                if (sprite.enabled && sprite.color.a > 0.05f) visibleCount++;
            }
            renderersSuppressed = sprites.Length > 0 && visibleCount == 0;
        }

        if (!panelSuppressed && !renderersSuppressed) return;

        EmergencyRestoreMainMenuPresentation(startPanel);
    }

    void EmergencyRestoreMainMenuPresentation(Transform startPanel)
    {
        if (startPanel != null)
        {
            CanvasGroup cg = startPanel.GetComponent<CanvasGroup>();
            if (cg == null) cg = startPanel.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }

        MainMenuShowcase showcase = FindAnyObjectByType<MainMenuShowcase>();
        if (showcase != null)
        {
            MenuFader fader = MenuFader.Capture(
                showcase.transform, typeof(MenuSpaceBackdrop), typeof(MenuLavaAmbience));
            fader.SetAlpha(1f);
            fader.Thaw();

            Renderer[] renderers = showcase.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = true;
        }

        MenuHeroRocket heroRocket = FindAnyObjectByType<MenuHeroRocket>();
        if (heroRocket != null)
        {
            Renderer[] renderers = heroRocket.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = true;

            ParticleSystem[] systems = heroRocket.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
                if (systems[i] != null && !systems[i].isPlaying) systems[i].Play(true);
        }

        ThawMenuAnimatorsUnder<MenuSpaceBackdrop>();
        ThawMenuAnimatorsUnder<MenuLavaAmbience>();

        ClearMenuForegroundCapture();
        menuForegroundHidden = false;
    }

    static void ThawMenuAnimatorsUnder<T>() where T : MonoBehaviour
    {
        T[] behaviours = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < behaviours.Length; i++)
            if (behaviours[i] != null) behaviours[i].enabled = true;
    }

    void EnsureShopPanelPolishVersion()
    {
        if (shopPanel == null) return;
        Transform marker = shopPanel.transform.Find("PolishMarker");
        if (marker != null && marker.GetComponent<ShopPolishMarker>() != null
            && marker.GetComponent<ShopPolishMarker>().version >= ShopPanelPolishVersion)
            return;

        DestroyShopPanelRuntime();
    }

    void DestroyShopPanelRuntime()
    {
        if (shopPanel != null) Destroy(shopPanel);

        shopPanel = null;
        shopCard = null;
        shopScroll = null;
        shopScrollRect = null;
        shopContent = null;
        shopBalanceText = null;
        shopBalanceRect = null;
        shopBalanceAmountRect = null;
        shopBalanceChipAppliedWidth = -1f;
        shopAdButton = null;
        shopAdButtonImage = null;
        shopAdButtonPulse = null;
        rocketsTabButton = null;
        premiumTabButton = null;
        rocketsTabImage = null;
        premiumTabImage = null;
        rocketsTabActiveSprite = null;
        rocketsTabInactiveSprite = null;
        premiumTabActiveSprite = null;
        premiumTabInactiveSprite = null;
        cardViews.Clear();
        placeholderCards.Clear();
    }

    sealed class ShopPolishMarker : MonoBehaviour
    {
        public int version = ShopPanelPolishVersion;
    }

    static Image CreateAtmosphereGlow(Transform parent, string name, Vector2 anchor, Vector2 position,
        Vector2 size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Image image = go.AddComponent<Image>();
        image.sprite = UIGlass.Glow;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    internal static Sprite LoadRequiredShopSprite(string resourcePath)
    {
        Sprite sprite = Resources.Load<Sprite>(resourcePath);
        if (sprite == null)
            Debug.LogError("Shop visual blocker: missing Resources sprite at '" + resourcePath + "'.", instance);
        return sprite;
    }

    // Prefer alpha-cropped header derivatives; fall back to authored originals.
    internal static Sprite LoadShopHeaderSprite(string baseName)
    {
        Sprite cropped = Resources.Load<Sprite>(ShopHeaderCroppedRoot + baseName + "_Cropped");
        if (cropped != null) return cropped;
        return LoadRequiredShopSprite(ShopHeaderAssetRoot + baseName);
    }

    static Sprite LoadShopTabSprite(string baseName)
    {
        Sprite cropped = Resources.Load<Sprite>(ShopTabCroppedRoot + baseName + "_Cropped");
        if (cropped != null) return cropped;
        return LoadRequiredShopSprite(ShopTabAssetRoot + baseName);
    }

    static float ShopTitleClusterHeight => ShopTitleClusterWidth / ShopTitleClusterAspectWidthOverHeight;

    // Authoritative top edge for the content chain below the title cluster.
    static float ShopContentTop =>
        ShopTitleClusterTopInset + ShopTitleClusterHeight + ShopTitleClusterGapBeforeContent;

    const float WatchAdBannerHeight = 200f;
    const float ShopWatchAdBelowTitleExtraGap = 10f;
    const float ShopBannerToTabsGap = 20f;
    const float ShopTabsRowHeight = 96f;
    const float ShopTabsToGridGap = 18f;

    static float ShopTabWidth => (WatchAdBannerWidth - ShopTabGap) * 0.5f;

    static float ShopWatchAdBannerTop => ShopContentTop + ShopWatchAdBelowTitleExtraGap;
    static float ShopTabsTop => ShopWatchAdBannerTop + WatchAdBannerHeight + ShopBannerToTabsGap;
    static float ShopGridTop => ShopTabsTop + ShopTabsRowHeight + ShopTabsToGridGap;

    internal static float ShopBalanceDiamondCenterX =>
        ShopBalanceDiamondLeftInset + ShopBalanceDiamondVisualSize * 0.5f;

    internal static float ShopBalancePlusCenterX =>
        ShopBalancePlusRightInset + ShopBalancePlusVisualSize * 0.5f;

    internal static float ShopBalanceAmountLeftInset =>
        ShopBalanceDiamondLeftInset + ShopBalanceDiamondVisualSize + ShopBalanceInnerGap;

    internal static float ShopBalanceAmountRightInset =>
        ShopBalancePlusRightInset + ShopBalancePlusVisualSize + ShopBalanceInnerGap;

    static float ShopBalanceChipCapDisplayWidth(Sprite chipSprite)
    {
        if (chipSprite == null) return ShopBalanceChipCapSourceWidth;
        return ShopBalanceChipCapSourceWidth / chipSprite.rect.height * ShopBalanceChipHeight;
    }

    static Sprite CreateShopBalanceChipSlice(Sprite source, Rect sliceInTexture)
    {
        return Sprite.Create(
            source.texture,
            sliceInTexture,
            new Vector2(0.5f, 0.5f),
            source.pixelsPerUnit,
            0,
            SpriteMeshType.FullRect);
    }

    internal static void BuildShopBalanceChipVisual(Transform chipRoot, Sprite chipSprite)
    {
        Rect spriteRect = chipSprite.textureRect;
        float capW = ShopBalanceChipCapSourceWidth;
        float centerW = Mathf.Max(1f, spriteRect.width - capW * 2f);

        Sprite leftSprite = CreateShopBalanceChipSlice(
            chipSprite, new Rect(spriteRect.x, spriteRect.y, capW, spriteRect.height));
        Sprite rightSprite = CreateShopBalanceChipSlice(
            chipSprite, new Rect(spriteRect.xMax - capW, spriteRect.y, capW, spriteRect.height));
        Sprite centerSprite = CreateShopBalanceChipSlice(
            chipSprite, new Rect(spriteRect.x + capW, spriteRect.y, centerW, spriteRect.height));

        float capDisplay = ShopBalanceChipCapDisplayWidth(chipSprite);

        GameObject leftGo = new GameObject("LeftCap", typeof(RectTransform));
        leftGo.transform.SetParent(chipRoot, false);
        RectTransform leftRect = leftGo.GetComponent<RectTransform>();
        leftRect.anchorMin = new Vector2(0f, 0f);
        leftRect.anchorMax = new Vector2(0f, 1f);
        leftRect.pivot = new Vector2(0f, 0.5f);
        leftRect.anchoredPosition = Vector2.zero;
        leftRect.sizeDelta = new Vector2(capDisplay, 0f);
        Image leftImage = leftGo.AddComponent<Image>();
        leftImage.sprite = leftSprite;
        leftImage.type = Image.Type.Simple;
        leftImage.preserveAspect = false;
        leftImage.color = Color.white;
        leftImage.raycastTarget = false;
        KillShopTint(leftGo);

        GameObject centerGo = new GameObject("CenterFill", typeof(RectTransform));
        centerGo.transform.SetParent(chipRoot, false);
        RectTransform centerRect = centerGo.GetComponent<RectTransform>();
        centerRect.anchorMin = Vector2.zero;
        centerRect.anchorMax = Vector2.one;
        centerRect.offsetMin = new Vector2(capDisplay, 0f);
        centerRect.offsetMax = new Vector2(-capDisplay, 0f);
        Image centerImage = centerGo.AddComponent<Image>();
        centerImage.sprite = centerSprite;
        centerImage.type = Image.Type.Simple;
        centerImage.preserveAspect = false;
        centerImage.color = Color.white;
        centerImage.raycastTarget = false;
        KillShopTint(centerGo);

        GameObject rightGo = new GameObject("RightCap", typeof(RectTransform));
        rightGo.transform.SetParent(chipRoot, false);
        RectTransform rightRect = rightGo.GetComponent<RectTransform>();
        rightRect.anchorMin = new Vector2(1f, 0f);
        rightRect.anchorMax = new Vector2(1f, 1f);
        rightRect.pivot = new Vector2(1f, 0.5f);
        rightRect.anchoredPosition = Vector2.zero;
        rightRect.sizeDelta = new Vector2(capDisplay, 0f);
        Image rightImage = rightGo.AddComponent<Image>();
        rightImage.sprite = rightSprite;
        rightImage.type = Image.Type.Simple;
        rightImage.preserveAspect = false;
        rightImage.color = Color.white;
        rightImage.raycastTarget = false;
        KillShopTint(rightGo);
    }

    internal static Sprite LoadShopBalanceChipSprite()
    {
        Sprite cropped = Resources.Load<Sprite>(ShopHeaderCroppedRoot + "BalanceChipBaseFlat_Cropped");
        if (cropped != null) return cropped;
        return LoadRequiredShopSprite(ShopHeaderAssetRoot + "BalanceChipBaseFlat");
    }

    float ComputeShopBalanceChipWidth()
    {
        if (shopBalanceText == null) return ShopBalanceChipMinWidth;
        shopBalanceText.ForceMeshUpdate(true);
        float textWidth = shopBalanceText.preferredWidth;
        return Mathf.Clamp(
            ShopBalanceAmountLeftInset + textWidth + ShopBalanceAmountRightInset,
            ShopBalanceChipMinWidth,
            ShopBalanceChipMaxWidth);
    }

    void ApplyShopBalanceChipLayout()
    {
        if (shopBalanceRect == null || shopBalanceText == null) return;

        float width = ComputeShopBalanceChipWidth();
        if (Mathf.Abs(width - shopBalanceChipAppliedWidth) > 0.5f)
        {
            shopBalanceRect.sizeDelta = new Vector2(width, ShopBalanceChipHeight);
            shopBalanceChipAppliedWidth = width;
        }

        Transform diamond = shopBalanceRect.Find("DiamondIcon");
        if (diamond is RectTransform diamondRect)
            diamondRect.anchoredPosition = new Vector2(ShopBalanceDiamondCenterX, 0f);

        Transform plus = shopBalanceRect.Find("PlusButton");
        if (plus is RectTransform plusRect)
            plusRect.anchoredPosition = new Vector2(-ShopBalancePlusCenterX, 0f);

        if (shopBalanceAmountRect != null)
        {
            shopBalanceAmountRect.offsetMin = new Vector2(ShopBalanceAmountLeftInset, 0f);
            shopBalanceAmountRect.offsetMax = new Vector2(-ShopBalanceAmountRightInset, 0f);
            shopBalanceAmountRect.anchoredPosition = new Vector2(0f, ShopBalanceAmountVerticalLift);
        }

        Transform chipBase = shopBalanceRect.Find("BalanceChipBase");
        if (chipBase != null)
        {
            Sprite chipSprite = LoadShopBalanceChipSprite();
            float capDisplay = ShopBalanceChipCapDisplayWidth(chipSprite);
            Transform left = chipBase.Find("LeftCap");
            Transform center = chipBase.Find("CenterFill");
            Transform right = chipBase.Find("RightCap");
            if (left is RectTransform leftRect)
                leftRect.sizeDelta = new Vector2(capDisplay, 0f);
            if (right is RectTransform rightRect)
                rightRect.sizeDelta = new Vector2(capDisplay, 0f);
            if (center is RectTransform centerRect)
            {
                centerRect.offsetMin = new Vector2(capDisplay, 0f);
                centerRect.offsetMax = new Vector2(-capDisplay, 0f);
            }
        }

        if (shopBalanceText != null)
        {
            shopBalanceText.fontSize = ShopBalanceAmountFontSize;
            shopBalanceText.color = ShopBalanceAmountColor;
            shopBalanceText.alignment = TextAlignmentOptions.Center;
            shopBalanceText.margin = Vector4.zero;
        }
    }

    static bool ValidateShopPresentationSprites()
    {
        string[] paths =
        {
            ShopBackgroundAsset,
            ShopHeaderAssetRoot + "ShopTitleCluster",
            ShopHeaderAssetRoot + "CloseButtonShell",
            ShopHeaderAssetRoot + "BalanceChipBaseFlat",
            ShopHeaderAssetRoot + "DiamondIcon",
            ShopHeaderAssetRoot + "PlusButton"
        };

        bool ok = true;
        for (int i = 0; i < paths.Length; i++)
        {
            if (Resources.Load<Sprite>(paths[i]) != null) continue;
            Debug.LogError("Shop visual blocker: missing Resources sprite at '" + paths[i] + "'.", instance);
            ok = false;
        }
        return ok;
    }

    internal static void ConfigureShopSpriteButtonColors(Button button)
    {
        if (button == null) return;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.pressedColor = Color.white;
        colors.selectedColor = Color.white;
        colors.disabledColor = Color.white;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0f;
        button.colors = colors;
    }

    void BuildShopBackground(Transform parent)
    {
        Sprite sprite = LoadRequiredShopSprite(ShopBackgroundAsset);
        if (sprite == null) return;

        GameObject bgGo = new GameObject("ShopBackground", typeof(RectTransform));
        bgGo.transform.SetParent(parent, false);
        bgGo.transform.SetAsFirstSibling();
        RectTransform bgRect = bgGo.GetComponent<RectTransform>();
        Stretch(bgRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        Image bgImage = bgGo.AddComponent<Image>();
        bgImage.sprite = sprite;
        bgImage.type = Image.Type.Simple;
        bgImage.color = Color.white;
        bgImage.preserveAspect = false;
        bgImage.raycastTarget = false;
        KillShopTint(bgGo);

        AspectRatioFitter fitter = bgGo.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
    }

    void BuildShopHeaderControlsRow(Transform parent)
    {
        GameObject rowGo = new GameObject("HeaderControlsRow", typeof(RectTransform));
        rowGo.transform.SetParent(parent, false);
        RectTransform rowRect = rowGo.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = new Vector2(0f, -ShopHeaderControlsTopInset);
        rowRect.sizeDelta = new Vector2(0f, ShopHeaderControlsRowHeight);

        BuildShopCloseButton(rowGo.transform);
        BuildShopBalance(rowGo.transform);
    }

    void BuildShopTitleCluster(Transform parent)
    {
        Sprite sprite = LoadShopHeaderSprite("ShopTitleCluster");
        if (sprite == null) return;

        float aspect = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
        float renderHeight = ShopTitleClusterWidth / aspect;

        GameObject clusterGo = new GameObject("ShopTitleCluster", typeof(RectTransform));
        clusterGo.transform.SetParent(parent, false);
        RectTransform clusterRect = clusterGo.GetComponent<RectTransform>();
        clusterRect.anchorMin = clusterRect.anchorMax = new Vector2(0.5f, 1f);
        clusterRect.pivot = new Vector2(0.5f, 1f);
        clusterRect.anchoredPosition = new Vector2(0f, -ShopTitleClusterTopInset);
        clusterRect.sizeDelta = new Vector2(ShopTitleClusterWidth, renderHeight);

        Image clusterImage = clusterGo.AddComponent<Image>();
        clusterImage.sprite = sprite;
        clusterImage.type = Image.Type.Simple;
        clusterImage.preserveAspect = true;
        clusterImage.color = Color.white;
        clusterImage.raycastTarget = false;
        KillShopTint(clusterGo);
    }

    Button BuildShopCloseButton(Transform parent)
    {
        Sprite shellSprite = LoadShopHeaderSprite("CloseButtonShell");
        if (shellSprite == null) return null;

        GameObject backGo = new GameObject("BackButton", typeof(RectTransform));
        backGo.transform.SetParent(parent, false);
        RectTransform backRect = backGo.GetComponent<RectTransform>();
        backRect.anchorMin = backRect.anchorMax = new Vector2(0f, 0.5f);
        backRect.pivot = new Vector2(0f, 0.5f);
        backRect.anchoredPosition = new Vector2(ShopHeaderEdgeInset, 0f);
        backRect.sizeDelta = new Vector2(ShopCloseHitSize, ShopCloseHitSize);

        Image hitImage = backGo.AddComponent<Image>();
        hitImage.color = Color.clear;
        hitImage.raycastTarget = true;

        Button backButton = backGo.AddComponent<Button>();
        backButton.targetGraphic = hitImage;
        backButton.transition = Selectable.Transition.None;
        ConfigureShopSpriteButtonColors(backButton);
        backButton.onClick.RemoveListener(CloseShop);
        backButton.onClick.AddListener(CloseShop);
        if (backGo.GetComponent<UIButtonPressFeedback>() == null)
            backGo.AddComponent<UIButtonPressFeedback>();

        GameObject visualGo = new GameObject("Visual", typeof(RectTransform));
        visualGo.transform.SetParent(backGo.transform, false);
        RectTransform visualRect = visualGo.GetComponent<RectTransform>();
        visualRect.anchorMin = visualRect.anchorMax = new Vector2(0.5f, 0.5f);
        visualRect.pivot = new Vector2(0.5f, 0.5f);
        visualRect.anchoredPosition = Vector2.zero;
        visualRect.sizeDelta = new Vector2(ShopCloseVisualSize, ShopCloseVisualSize);

        Image visualImage = visualGo.AddComponent<Image>();
        visualImage.sprite = shellSprite;
        visualImage.type = Image.Type.Simple;
        visualImage.preserveAspect = true;
        visualImage.color = Color.white;
        visualImage.raycastTarget = false;
        KillShopTint(visualGo);
        return backButton;
    }

    static void StripObsoleteShopPresentation(Transform shopPanelRoot, Transform cardRoot)
    {
        if (shopPanelRoot != null)
        {
            string[] panelObsolete =
            {
                "TopNebula", "MidNebula", "BottomNebula", "Vignette", "StarField"
            };
            for (int i = 0; i < panelObsolete.Length; i++)
            {
                Transform child = shopPanelRoot.Find(panelObsolete[i]);
                if (child != null) DestroyImmediate(child.gameObject);
            }
        }

        if (cardRoot == null) return;
        string[] cardObsolete =
        {
            "CompactLogo", "CardBottomGlow", "DetailBandGlow", "DiamondIconGlow"
        };
        for (int i = 0; i < cardObsolete.Length; i++)
        {
            Transform child = cardRoot.Find(cardObsolete[i]);
            if (child != null) DestroyImmediate(child.gameObject);
        }

        Transform balance = cardRoot.Find("ShopBalance");
        if (balance != null)
        {
            Transform legacyFlat = balance.Find("BalanceChipBaseFlat");
            if (legacyFlat != null) DestroyImmediate(legacyFlat.gameObject);
        }

        for (int i = cardRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = cardRoot.GetChild(i);
            if (child == null) continue;
            if (child.name == "SHOP" || child.name == "UPGRADE YOUR ROCKET")
                DestroyImmediate(child.gameObject);
        }

        Transform titleCluster = cardRoot.Find("ShopTitleCluster/Visual");
        if (titleCluster != null) DestroyImmediate(titleCluster.gameObject);
    }

    bool IsShopPanelStructureComplete()
    {
        if (shopPanel == null || shopCard == null) return false;
        return shopPanel.transform.Find("ShopBackground") != null
            && shopCard.Find("HeaderControlsRow") != null
            && shopCard.Find("HeaderControlsRow/BackButton") != null
            && shopCard.Find("ShopTitleCluster") != null
            && shopCard.Find("HeaderControlsRow/ShopBalance/BalanceChipBase") != null
            && shopCard.Find("HeaderControlsRow/ShopBalance/PlusButton") != null
            && shopCard.Find("CategoryTabs") != null
            && shopCard.Find("ShopScroll") != null
            && shopCard.Find("ShopWatchAdRow") != null;
    }

    void BuildShopPanel()
    {
        Canvas canvas = FindMainCanvas();
        if (canvas == null)
        {
            Debug.LogError("Rocket Shop could not be created because no active Canvas was found.", this);
            return;
        }

        if (!ValidateShopPresentationSprites())
        {
            Debug.LogError("Rocket Shop could not be created because required presentation sprites are missing.", this);
            return;
        }

        GameObject panelGo = new GameObject("ShopPanel");
        panelGo.transform.SetParent(canvas.transform, false);
        RectTransform panelRect = panelGo.AddComponent<RectTransform>();
        Stretch(panelRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // Transparent modal blocker — input stays on the Shop root while the
        // supplied artwork provides the visible background.
        Image overlay = panelGo.AddComponent<Image>();
        overlay.color = new Color(0f, 0f, 0f, 0f);
        overlay.raycastTarget = true;
        overlay.canvasRenderer.cullTransparentMesh = false;

        BuildShopBackground(panelGo.transform);

        CanvasGroup group = panelGo.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = true;
        group.blocksRaycasts = true;

        Canvas shopSorting = panelGo.AddComponent<Canvas>();
        shopSorting.overrideSorting = true;
        shopSorting.sortingOrder = ShopSortingOrder;
        panelGo.AddComponent<GraphicRaycaster>();

        GameObject cardGo = new GameObject("Card");
        cardGo.transform.SetParent(panelGo.transform, false);
        shopCard = cardGo.AddComponent<RectTransform>();
        Stretch(shopCard, new Vector2(0.008f, 0.006f), new Vector2(0.992f, 0.994f), Vector2.zero, Vector2.zero);

        Image cardBackground = cardGo.AddComponent<Image>();
        cardBackground.raycastTarget = true;
        KillShopTint(cardGo);
        cardBackground.color = new Color(0.026f, 0.024f, 0.082f, 0.10f);

        BuildShopRewardedButton(cardGo.transform);
        BuildShopHeaderControlsRow(cardGo.transform);
        BuildShopTitleCluster(cardGo.transform);
        EnsureShopHeaderDrawOrder();
        BuildCategoryTabs(cardGo.transform);
        BuildShopScroll(cardGo.transform);
        ApplyShopPanelTextOverflow(shopCard);

        GameObject markerGo = new GameObject("PolishMarker", typeof(RectTransform));
        markerGo.transform.SetParent(panelGo.transform, false);
        markerGo.AddComponent<ShopPolishMarker>();

        shopPanel = panelGo;
    }

    // [Diamond icon] [dynamic amount] [+] — the same underlying CoinManager
    // balance and economy, just presented as the Shop's currency rather than
    // the HUD's coin. The "+" reuses the Shop's own real reward action
    // (RequestShopCoinsReward, the same listener the Watch Ad control uses)
    // rather than inventing a second currency-purchase entry point.
    void BuildShopBalance(Transform parent)
    {
        Sprite chipSprite = LoadShopBalanceChipSprite();
        Sprite iconSprite = LoadShopHeaderSprite("DiamondIcon");
        Sprite plusSprite = LoadShopHeaderSprite("PlusButton");
        if (chipSprite == null || iconSprite == null || plusSprite == null) return;

        GameObject balanceGo = new GameObject("ShopBalance");
        balanceGo.transform.SetParent(parent, false);
        shopBalanceRect = balanceGo.AddComponent<RectTransform>();
        shopBalanceRect.anchorMin = shopBalanceRect.anchorMax = new Vector2(1f, 0.5f);
        shopBalanceRect.pivot = new Vector2(1f, 0.5f);
        shopBalanceRect.anchoredPosition = new Vector2(-ShopHeaderEdgeInset, 0f);
        shopBalanceRect.sizeDelta = new Vector2(ShopBalanceChipMinWidth, ShopBalanceChipHeight);
        shopBalanceChipAppliedWidth = -1f;
        KillShopTint(balanceGo);

        GameObject chipGo = new GameObject("BalanceChipBase", typeof(RectTransform));
        chipGo.transform.SetParent(balanceGo.transform, false);
        chipGo.transform.SetAsFirstSibling();
        RectTransform chipRect = chipGo.GetComponent<RectTransform>();
        Stretch(chipRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        BuildShopBalanceChipVisual(chipGo.transform, chipSprite);

        GameObject iconGo = new GameObject("DiamondIcon", typeof(RectTransform));
        iconGo.transform.SetParent(balanceGo.transform, false);
        RectTransform iconRect = iconGo.GetComponent<RectTransform>();
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = new Vector2(ShopBalanceDiamondCenterX, 0f);
        iconRect.sizeDelta = new Vector2(ShopBalanceDiamondVisualSize, ShopBalanceDiamondVisualSize);
        Image iconImage = iconGo.AddComponent<Image>();
        iconImage.sprite = iconSprite;
        iconImage.type = Image.Type.Simple;
        iconImage.preserveAspect = true;
        iconImage.color = Color.white;
        iconImage.raycastTarget = false;
        KillShopTint(iconGo);

        GameObject amountGo = new GameObject("BalanceAmount", typeof(RectTransform));
        amountGo.transform.SetParent(balanceGo.transform, false);
        shopBalanceAmountRect = amountGo.GetComponent<RectTransform>();
        shopBalanceAmountRect.anchorMin = new Vector2(0f, 0f);
        shopBalanceAmountRect.anchorMax = new Vector2(1f, 1f);
        shopBalanceAmountRect.pivot = new Vector2(0.5f, 0.5f);
        shopBalanceAmountRect.offsetMin = new Vector2(ShopBalanceAmountLeftInset, 0f);
        shopBalanceAmountRect.offsetMax = new Vector2(-ShopBalanceAmountRightInset, 0f);
        shopBalanceAmountRect.anchoredPosition = new Vector2(0f, ShopBalanceAmountVerticalLift);

        shopBalanceText = amountGo.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset balanceFont = Resources.Load<TMP_FontAsset>("Fonts/Montserrat-ExtraBold SDF");
        if (balanceFont == null) balanceFont = TMP_Settings.defaultFontAsset;
        if (balanceFont != null) shopBalanceText.font = balanceFont;
        shopBalanceText.text = "0";
        shopBalanceText.fontSize = ShopBalanceAmountFontSize;
        shopBalanceText.fontStyle = FontStyles.Bold;
        shopBalanceText.alignment = TextAlignmentOptions.Center;
        shopBalanceText.color = ShopBalanceAmountColor;
        shopBalanceText.margin = Vector4.zero;
        shopBalanceText.overflowMode = TextOverflowModes.Overflow;
        shopBalanceText.raycastTarget = false;

        GameObject plusGo = new GameObject("PlusButton", typeof(RectTransform));
        plusGo.transform.SetParent(balanceGo.transform, false);
        RectTransform plusRect = plusGo.GetComponent<RectTransform>();
        plusRect.anchorMin = plusRect.anchorMax = new Vector2(1f, 0.5f);
        plusRect.pivot = new Vector2(0.5f, 0.5f);
        plusRect.anchoredPosition = new Vector2(-ShopBalancePlusCenterX, 0f);
        plusRect.sizeDelta = new Vector2(ShopBalancePlusHitSize, ShopBalancePlusHitSize);

        Image plusHit = plusGo.AddComponent<Image>();
        plusHit.color = Color.clear;
        plusHit.raycastTarget = true;

        Button plusButton = plusGo.AddComponent<Button>();
        plusButton.targetGraphic = plusHit;
        plusButton.transition = Selectable.Transition.None;
        ConfigureShopSpriteButtonColors(plusButton);
        plusButton.onClick.RemoveListener(RequestShopCoinsReward);
        plusButton.onClick.AddListener(RequestShopCoinsReward);
        if (plusGo.GetComponent<UIButtonPressFeedback>() == null)
            plusGo.AddComponent<UIButtonPressFeedback>();

        GameObject plusVisualGo = new GameObject("Visual", typeof(RectTransform));
        plusVisualGo.transform.SetParent(plusGo.transform, false);
        RectTransform plusVisualRect = plusVisualGo.GetComponent<RectTransform>();
        plusVisualRect.anchorMin = plusVisualRect.anchorMax = new Vector2(0.5f, 0.5f);
        plusVisualRect.pivot = new Vector2(0.5f, 0.5f);
        plusVisualRect.anchoredPosition = Vector2.zero;
        plusVisualRect.sizeDelta = new Vector2(ShopBalancePlusVisualSize, ShopBalancePlusVisualSize);

        Image plusImage = plusVisualGo.AddComponent<Image>();
        plusImage.sprite = plusSprite;
        plusImage.type = Image.Type.Simple;
        plusImage.preserveAspect = true;
        plusImage.color = Color.white;
        plusImage.raycastTarget = false;
        KillShopTint(plusVisualGo);

        int balance = CoinManager.instance != null ? CoinManager.instance.GetCoins() : 0;
        shopBalanceText.text = balance.ToString();
        ApplyShopBalanceChipLayout();
    }

    void ReconcileShopHeaderPresentation()
    {
        if (shopCard == null) return;

        Transform row = shopCard.Find("HeaderControlsRow");
        if (row != null) DestroyImmediate(row.gameObject);

        Transform back = shopCard.Find("BackButton");
        if (back != null) DestroyImmediate(back.gameObject);
        Transform title = shopCard.Find("ShopTitleCluster");
        if (title != null) DestroyImmediate(title.gameObject);
        Transform balance = shopCard.Find("ShopBalance");
        if (balance != null) DestroyImmediate(balance.gameObject);
        shopBalanceText = null;
        shopBalanceRect = null;
        shopBalanceAmountRect = null;
        shopBalanceChipAppliedWidth = -1f;

        BuildShopHeaderControlsRow(shopCard.transform);
        BuildShopTitleCluster(shopCard.transform);
        EnsureShopHeaderDrawOrder();
    }

    void EnsureShopHeaderDrawOrder()
    {
        if (shopCard == null) return;

        Transform row = shopCard.Find("HeaderControlsRow");
        Transform title = shopCard.Find("ShopTitleCluster");
        if (row != null) row.SetAsLastSibling();
        if (title != null) title.SetAsLastSibling();
    }

    // Disable palette-driven UITinted on Shop surfaces so dark navy/purple
    // fills are never overwritten by the shared Glass/GlassDeep HUD colours.
    internal static void KillShopTint(GameObject host)
    {
        if (host == null) return;
        UITinted tint = host.GetComponent<UITinted>();
        if (tint != null)
        {
            tint.enabled = false;
            UnityEngine.Object.Destroy(tint);
        }
    }

    // Watch Ad banner dimensions — top position derived from ShopContentTop.
    const float WatchAdBannerWidth = 978f;

    const string RocketCardAssetRoot = "Shop/UI/Cards/";
    const string RocketCardCroppedRoot = "Shop/UI/Cards/Cropped/";
    static Sprite rocketCardNormalSprite;
    static Sprite rocketCardSelectedSprite;
    static float gridCardAspectHeightOverWidth = RocketCardVisibleAspectHeightOverWidth;

    static Sprite LoadRocketCardSprite(string baseName)
    {
        Sprite cropped = Resources.Load<Sprite>(RocketCardCroppedRoot + baseName + "_Cropped");
        if (cropped != null) return cropped;
        return Resources.Load<Sprite>(RocketCardAssetRoot + baseName);
    }

    static void EnsureRocketCardSprites()
    {
        if (rocketCardNormalSprite == null)
        {
            rocketCardNormalSprite = LoadRocketCardSprite("RocketCard_Normal");
            if (rocketCardNormalSprite != null && rocketCardNormalSprite.rect.width > 1f)
            {
                float aspect = rocketCardNormalSprite.rect.height / rocketCardNormalSprite.rect.width;
                gridCardAspectHeightOverWidth = aspect < 1.05f
                    ? RocketCardVisibleAspectHeightOverWidth
                    : aspect;
            }
        }

        if (rocketCardSelectedSprite == null)
            rocketCardSelectedSprite = LoadRocketCardSprite("RocketCard_Selected");
    }

    static float ResolveGridCardAspect()
    {
        EnsureRocketCardSprites();
        return gridCardAspectHeightOverWidth;
    }

    static void ApplyGridCardPreviewLayout(RectTransform previewRect)
    {
        float diameter = GridCellWidth * GridPreviewDiameterNorm;
        float centerFromTop = GridCellHeight * GridPreviewCenterFromTopNorm;
        previewRect.anchorMin = previewRect.anchorMax = new Vector2(0.5f, 1f);
        previewRect.pivot = new Vector2(0.5f, 0.5f);
        previewRect.anchoredPosition = new Vector2(0f, -centerFromTop);
        previewRect.sizeDelta = new Vector2(diameter, diameter);
    }

    static void ApplyGridCardNameLayout(TextMeshProUGUI nameText)
    {
        if (nameText == null) return;
        float textWidth = GridCellWidth - GridTextWidthInset * 2f;
        RectTransform nameRect = nameText.rectTransform;
        nameRect.anchorMin = nameRect.anchorMax = new Vector2(0.5f, 1f);
        nameRect.pivot = new Vector2(0.5f, 1f);
        nameRect.anchoredPosition = new Vector2(0f, -GridCellHeight * GridNameFromTopNorm);
        nameRect.sizeDelta = new Vector2(textWidth, 34f);
    }

    // Full-width centered state text — used only by the non-interactive
    // Coming Soon placeholder, which never shows a diamond price.
    static void ApplyGridCardPlaceholderStateLayout(TextMeshProUGUI stateText)
    {
        if (stateText == null) return;
        float textWidth = GridCellWidth - GridTextWidthInset * 2f;
        RectTransform stateRect = stateText.rectTransform;
        stateRect.anchorMin = stateRect.anchorMax = new Vector2(0.5f, 1f);
        stateRect.pivot = new Vector2(0.5f, 1f);
        stateRect.anchoredPosition = new Vector2(0f, -GridCellHeight * GridActionFromTopNorm);
        stateRect.sizeDelta = new Vector2(textWidth, 24f);
    }

    // Single alignment authority for the diamond icon + amount (or bare state
    // word). Both children are re-centered as one unit every refresh so the
    // icon never drifts from its number — see ApplyGridPriceRowContent.
    static void ApplyGridCardPriceRowLayout(TextMeshProUGUI priceText, Image priceIcon)
    {
        RectTransform rowRect = priceText.transform.parent as RectTransform;
        float textWidth = GridCellWidth - GridTextWidthInset * 2f;
        rowRect.anchorMin = rowRect.anchorMax = new Vector2(0.5f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = new Vector2(0f, -GridCellHeight * GridActionFromTopNorm);
        rowRect.sizeDelta = new Vector2(textWidth, GridPriceRowHeight);

        RectTransform textRect = priceText.rectTransform;
        textRect.anchorMin = textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0f, 0.5f);
        textRect.sizeDelta = new Vector2(textWidth, GridPriceRowHeight);

        RectTransform iconRect = priceIcon.rectTransform;
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.sizeDelta = new Vector2(GridPriceIconSize, GridPriceIconSize);
    }

    // Re-centers the icon + amount as one group so the diamond never floats
    // away from the number it prices. Runs every RefreshShop, after the
    // amount/state text is set, since the group width depends on the text.
    void ApplyGridPriceRowContent(SkinCardView view, bool showIcon)
    {
        if (view?.statusText == null) return;

        TextMeshProUGUI text = view.statusText;
        text.ForceMeshUpdate(true);
        float iconSpan = showIcon ? GridPriceIconSize + GridPriceRowGap : 0f;
        float maxTextWidth = Mathf.Max(10f, GridCellWidth - GridTextWidthInset * 2f - iconSpan);
        float textWidth = Mathf.Clamp(text.preferredWidth, 10f, maxTextWidth);
        float groupWidth = iconSpan + textWidth;
        float startX = -groupWidth * 0.5f;

        text.rectTransform.sizeDelta = new Vector2(textWidth, GridPriceRowHeight);

        if (view.priceIcon != null) view.priceIcon.gameObject.SetActive(showIcon);
        if (showIcon && view.priceIcon != null)
        {
            view.priceIcon.rectTransform.anchoredPosition = new Vector2(startX + GridPriceIconSize * 0.5f, 0f);
            text.rectTransform.anchoredPosition = new Vector2(startX + iconSpan, 0f);
        }
        else
        {
            text.rectTransform.anchoredPosition = new Vector2(startX, 0f);
        }
    }

    static void ConfigureShopScrollAuthority(RectTransform scrollRect)
    {
        if (scrollRect == null) return;
        scrollRect.anchorMin = scrollRect.anchorMax = new Vector2(0.5f, 1f);
        scrollRect.pivot = new Vector2(0.5f, 1f);
        scrollRect.sizeDelta = new Vector2(WatchAdBannerWidth, scrollRect.sizeDelta.y);
    }

    const float WatchAdButtonWidth = 216f;
    const float WatchAdButtonHeight = 96f;
    const float WatchAdButtonInset = 31f;
    // Single mild dim when the ad is unavailable in builds — ~85% brightness, full alpha.
    static readonly Color WatchAdButtonUnavailableDim = new Color(0.85f, 0.85f, 0.85f, 1f);

    static bool WatchAdEditorPlayPreview =>
        Application.isEditor && Application.isPlaying;

    static Color WatchAdButtonVisualColor(bool adReady, bool adLoading)
    {
        if (adReady) return Color.white;
        if (WatchAdEditorPlayPreview && !adLoading) return Color.white;
        return WatchAdButtonUnavailableDim;
    }

    static bool WatchAdShouldPulseVisual(bool adReady, bool adLoading)
    {
        if (adLoading) return false;
        if (adReady) return true;
        return WatchAdEditorPlayPreview;
    }

    static void ConfigureWatchAdButtonColors(Button button) => ConfigureShopSpriteButtonColors(button);

    // Final baked banner: BannerBase artwork + one interactive WatchButton sprite.
    void BuildShopRewardedButton(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (child == null) continue;
            string n = child.name;
            if (n == "ShopWatchAdRow" || n == "WatchAdModule" || n == "WatchAdButton"
                || n == "WatchAdRow" || n == "RewardedAdButton" || n == "ShopFooterStrip")
                DestroyImmediate(child.gameObject);
        }

        GameObject rowGo = new GameObject("ShopWatchAdRow", typeof(RectTransform));
        rowGo.transform.SetParent(parent, false);
        RectTransform rowRect = rowGo.GetComponent<RectTransform>();
        rowRect.anchorMin = rowRect.anchorMax = new Vector2(0.5f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = new Vector2(0f, -ShopWatchAdBannerTop);
        rowRect.sizeDelta = new Vector2(WatchAdBannerWidth, WatchAdBannerHeight);

        GameObject bannerGo = new GameObject("BannerBase", typeof(RectTransform));
        bannerGo.transform.SetParent(rowGo.transform, false);
        RectTransform bannerRect = bannerGo.GetComponent<RectTransform>();
        Stretch(bannerRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image bannerImage = bannerGo.AddComponent<Image>();
        bannerImage.sprite = Resources.Load<Sprite>(WatchAdAssetRoot + "BannerBase");
        bannerImage.type = Image.Type.Simple;
        bannerImage.preserveAspect = false;
        bannerImage.raycastTarget = false;

        GameObject watchBtnGo = new GameObject("WatchButton", typeof(RectTransform));
        watchBtnGo.transform.SetParent(bannerGo.transform, false);
        RectTransform watchBtnRect = watchBtnGo.GetComponent<RectTransform>();
        watchBtnRect.anchorMin = watchBtnRect.anchorMax = new Vector2(1f, 0.5f);
        watchBtnRect.pivot = new Vector2(0.5f, 0.5f);
        watchBtnRect.anchoredPosition = new Vector2(-(WatchAdButtonInset + WatchAdButtonWidth * 0.5f), 0f);
        watchBtnRect.sizeDelta = new Vector2(WatchAdButtonWidth, WatchAdButtonHeight);

        shopAdButtonImage = watchBtnGo.AddComponent<Image>();
        shopAdButtonImage.sprite = Resources.Load<Sprite>(WatchAdAssetRoot + "WatchButton");
        shopAdButtonImage.type = Image.Type.Simple;
        shopAdButtonImage.preserveAspect = true;
        shopAdButtonImage.raycastTarget = true;

        shopAdButton = watchBtnGo.AddComponent<Button>();
        shopAdButton.targetGraphic = shopAdButtonImage;
        shopAdButton.transition = Selectable.Transition.None;
        ConfigureWatchAdButtonColors(shopAdButton);
        shopAdButton.onClick.RemoveListener(RequestShopCoinsReward);
        shopAdButton.onClick.AddListener(RequestShopCoinsReward);

        shopAdButtonPulse = watchBtnGo.GetComponent<WatchAdButtonPulse>();
        if (shopAdButtonPulse == null) shopAdButtonPulse = watchBtnGo.AddComponent<WatchAdButtonPulse>();
        RefreshShopRewardedButton();
    }

    static void StyleShopText(TMP_Text text, float size, float tracking, Color color,
        FontStyles style = FontStyles.Normal,
        TextAlignmentOptions align = TextAlignmentOptions.Center,
        bool changeFont = true)
    {
        UIKit.StyleText(text, size, tracking, color, style, align, changeFont);
        if (text != null) text.overflowMode = TextOverflowModes.Truncate;
    }

    static void StyleShopDisplay(TMP_Text text, float size, float tracking, Color color)
    {
        UIKit.StyleDisplay(text, size, tracking, color);
        if (text != null) text.overflowMode = TextOverflowModes.Truncate;
    }

    static void ApplyShopTextTruncate(TMP_Text text)
    {
        if (text != null) text.overflowMode = TextOverflowModes.Truncate;
    }

    static void ApplyShopPanelTextOverflow(Transform root)
    {
        if (root == null) return;
        TextMeshProUGUI[] labels = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] != null && labels[i].name == "BalanceAmount")
            {
                labels[i].overflowMode = TextOverflowModes.Overflow;
                continue;
            }
            ApplyShopTextTruncate(labels[i]);
        }
    }

    void RequestShopCoinsReward()
    {
        if (shopAdInProgress || PresentationGate.IsActive(PresentationGate.Kind.IapPurchase)
            || AdManager.instance == null || !AdManager.instance.IsRewardedAdReady()) return;

        shopAdInProgress = true;
        RefreshShopRewardedButton();
        AdManager.instance.ShowRewardedAdForCoins(GameEconomyConfig.Current.rewardedAdCoins, OnShopCoinsRewardGranted);
    }

    void OnShopCoinsRewardGranted(int amount)
    {
        shopAdInProgress = false;
        if (amount > 0) ShowFlash("+" + amount + " DIAMONDS", UIDesign.Accent);
        RefreshShopRewardedButton();
    }

    void RefreshShopRewardedButton()
    {
        if (shopAdButton == null) return;

        bool ready = !shopAdInProgress && !PresentationGate.IsActive(PresentationGate.Kind.IapPurchase)
            && AdManager.instance != null && AdManager.instance.IsRewardedAdReady();
        bool loading = shopAdInProgress;

        shopAdButton.interactable = ready;
        if (shopAdButtonImage != null)
            shopAdButtonImage.color = WatchAdButtonVisualColor(ready, loading);
        if (shopAdButtonPulse != null)
        {
            shopAdButtonPulse.SetPulsing(WatchAdShouldPulseVisual(ready, loading));
            shopAdButtonPulse.SetPressFeedbackEnabled(ready);
        }
    }

    // Cached shop panels from earlier sessions may predate layout fixes. Force
    // a clean Watch Ad rebuild and strip obsolete footer/atmosphere leftovers.
    void ReconcileShopPanelLayout()
    {
        if (shopCard == null) return;

        StripObsoleteShopPresentation(shopPanel != null ? shopPanel.transform : null, shopCard);

        ReconcileShopHeaderPresentation();

        // Always remove the obsolete marketing footer strip.
        Transform footer = shopCard.Find("ShopFooterStrip");
        if (footer != null) DestroyImmediate(footer.gameObject);

        // Rebuild Watch Ad from a clean slate — prevents stale dual headings,
        // baked-bg + runtime text collisions, and duplicate listeners.
        if (shopAdButton != null)
        {
            shopAdButton.onClick.RemoveListener(RequestShopCoinsReward);
            shopAdButton = null;
            shopAdButtonImage = null;
            shopAdButtonPulse = null;
        }

        BuildShopRewardedButton(shopCard);
        EnsureShopHeaderDrawOrder();
        ApplyShopContentLayout();

        if (shopScrollRect == null)
        {
            Transform scroll = shopCard.Find("ShopScroll");
            if (scroll != null) shopScrollRect = scroll as RectTransform;
        }

        RestoreShopScrollRefs();
        EnsureShopContentSized();
        ApplyShopPanelTextOverflow(shopCard);
    }

    void RestoreShopScrollRefs()
    {
        if (shopCard == null) return;

        Transform scrollTransform = shopCard.Find("ShopScroll");
        if (scrollTransform == null) return;

        if (shopScrollRect == null)
            shopScrollRect = scrollTransform as RectTransform;

        if (shopScroll == null)
            shopScroll = scrollTransform.GetComponent<ScrollRect>();

        if (shopScroll == null) return;

        if (shopScroll.viewport == null)
        {
            Transform viewportTransform = scrollTransform.Find("Viewport");
            if (viewportTransform != null)
                shopScroll.viewport = viewportTransform as RectTransform;
        }

        if (shopContent == null && shopScroll.viewport != null)
        {
            Transform contentTransform = shopScroll.viewport.Find("Content");
            if (contentTransform != null)
                shopContent = contentTransform as RectTransform;
        }

        if (shopContent != null && shopScroll.content != shopContent)
            shopScroll.content = shopContent;
    }

    int ComputeVisibleRowsForTab(ShopTab tab)
    {
        if (skins == null) return MaxVisibleGridRows();

        int visible = 0;
        for (int i = 0; i < skins.Length; i++)
            if (IsPremiumSkin(skins[i]) == (tab == ShopTab.Premium)) visible++;

        int remainder = visible % GridColumns;
        int placeholdersNeeded = visible == 0 ? 0
            : (remainder == 0 ? 0 : GridColumns - remainder);
        int totalSlots = visible + placeholdersNeeded;
        return totalSlots == 0 ? 1 : Mathf.CeilToInt(totalSlots / (float)GridColumns);
    }

    void ApplyShopContentHeight(int rows)
    {
        if (shopContent == null) return;

        activeVisibleRows = Mathf.Max(1, rows);
        float contentHeight = GridViewportHeightForRows(activeVisibleRows);
        shopContent.sizeDelta = new Vector2(WatchAdBannerWidth, contentHeight);
        UpdateShopLayoutForActiveTab();
    }

    void EnsureShopContentSized()
    {
        if (shopContent == null) return;

        if (skins != null && cardViews.Count == skins.Length)
            LayoutVisibleCards();
        else
            ApplyShopContentHeight(ComputeVisibleRowsForTab(activeTab));

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(shopContent);
    }

    float GridViewportHeightForRows(int rows)
    {
        if (rows <= 0) rows = 1;
        return GridVerticalContentPadding * 2f
            + rows * GridCellHeight + Mathf.Max(0, rows - 1) * GridRowGap;
    }

    // Rows visible before the grid needs to scroll — computed from the space
    // actually available beneath the tabs, down to the card's bottom edge.
    // With no detail panel reserving a fixed slice of that space, the grid
    // claims all of it.
    int MaxVisibleGridRows()
    {
        float available = EstimateShopCardHeight() - ShopGridTop - ShopGridBottomPad;
        if (available < GridCellHeight) return 1;
        int rows = 1 + Mathf.FloorToInt((available - GridCellHeight) / (GridCellHeight + GridRowGap));
        return Mathf.Max(1, rows);
    }

    void UpdateShopLayoutForActiveTab()
    {
        int rows = Mathf.Max(1, activeVisibleRows);
        float viewportHeight = GridViewportHeightForRows(Mathf.Min(rows, MaxVisibleGridRows()));

        if (shopScrollRect != null)
            shopScrollRect.sizeDelta = new Vector2(WatchAdBannerWidth, viewportHeight);
    }

    float EstimateShopCardHeight()
    {
        if (shopCard != null && shopCard.rect.height > 1f)
            return shopCard.rect.height;

        Canvas canvas = FindMainCanvas();
        if (canvas != null)
        {
            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            if (canvasRect != null && canvasRect.rect.height > 1f)
                return canvasRect.rect.height * 0.988f;
        }

        return 1920f;
    }

    void ApplyShopContentLayout()
    {
        if (shopCard == null) return;

        Transform watch = shopCard.Find("ShopWatchAdRow");
        if (watch is RectTransform watchRect)
            watchRect.anchoredPosition = new Vector2(0f, -ShopWatchAdBannerTop);

        Transform tabs = shopCard.Find("CategoryTabs");
        if (tabs is RectTransform tabsRect)
        {
            tabsRect.anchoredPosition = new Vector2(0f, -ShopTabsTop);
            tabsRect.sizeDelta = new Vector2(WatchAdBannerWidth, ShopTabsRowHeight);
        }

        if (shopScrollRect != null)
        {
            shopScrollRect.anchoredPosition = new Vector2(0f, -ShopGridTop);
            ConfigureShopScrollAuthority(shopScrollRect);
        }

        UpdateShopLayoutForActiveTab();
    }

    void BuildShopScroll(Transform parent)
    {
        EnsureRocketCardSprites();

        GameObject scrollGo = new GameObject("ShopScroll");
        scrollGo.transform.SetParent(parent, false);
        shopScrollRect = scrollGo.AddComponent<RectTransform>();
        ConfigureShopScrollAuthority(shopScrollRect);
        shopScrollRect.anchoredPosition = new Vector2(0f, -ShopGridTop);
        shopScrollRect.sizeDelta = new Vector2(WatchAdBannerWidth, GridViewportHeightForRows(MaxVisibleGridRows()));

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
        shopContent.anchorMin = shopContent.anchorMax = new Vector2(0f, 1f);
        shopContent.pivot = new Vector2(0f, 1f);
        shopContent.anchoredPosition = Vector2.zero;
        shopContent.sizeDelta = new Vector2(WatchAdBannerWidth, 0f);

        // Three-column grid aligned to Watch Ad width. Positioning is manual
        // (LayoutVisibleCards) so incomplete rows stay left-aligned in order.
        shopScroll.viewport = viewport;
        shopScroll.content = shopContent;

        BuildShopCards();

        if (skins != null)
        {
            activeTab = ShopTab.Rockets;
            LayoutVisibleCards();
        }
        else
            ApplyShopContentHeight(MaxVisibleGridRows());
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
            // The grid is one uniform surface — payment type still drives each
            // card's badge, price and purchase behaviour, it just no longer
            // splits the showroom into two separately-headed lists.
            for (int i = 0; i < skins.Length; i++)
                BuildSkinCard(shopContent, i);
        }
        catch (Exception exception)
        {
            Debug.LogError("Rocket Shop card creation failed. Expected Default, Fire, Ice, Gold and Cat Rocket.\n"
                + exception, this);
            return false;
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(shopContent);
        return cardViews.Count == skins.Length;
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
            error = "the rocket cards could not be created.";
            return false;
        }
        if (cardViews.Count != skins.Length)
        {
            error = "expected " + skins.Length + " rocket cards but found " + cardViews.Count + ".";
            return false;
        }

        EnsureShopContentSized();

        for (int i = 0; i < cardViews.Count; i++)
        {
            SkinCardView view = cardViews[i];
            if (view == null || view.rect == null || view.rect.parent != shopContent)
            {
                error = "product card " + i + " is missing or has the wrong parent.";
                return false;
            }
            if (Mathf.Abs(view.rect.localScale.x - 1f) > 0.01f)
                view.rect.localScale = Vector3.one;
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

    // One compact, wholly-tappable card: approved PNG shell, shared preview
    // circle, rocket name and one compact action/state line.
    void BuildSkinCard(Transform parent, int index)
    {
        EnsureRocketCardSprites();
        SkinData skin = skins[index];

        GameObject cardGo = new GameObject("SkinCard_" + index);
        cardGo.transform.SetParent(parent, false);
        RectTransform rect = cardGo.AddComponent<RectTransform>();
        rect.localScale = Vector3.one;
        rect.sizeDelta = new Vector2(GridCellWidth, GridCellHeight);

        Image hitImage = cardGo.AddComponent<Image>();
        hitImage.color = Color.clear;
        hitImage.raycastTarget = true;

        int capturedIndex = index;
        Button cardButton = cardGo.AddComponent<Button>();
        cardButton.targetGraphic = hitImage;
        cardButton.transition = Selectable.Transition.None;
        cardButton.onClick.AddListener(() => OnSkinClicked(capturedIndex));
        cardGo.AddComponent<UIButtonPressFeedback>();

        GameObject visualGo = new GameObject("CardVisual", typeof(RectTransform));
        visualGo.transform.SetParent(cardGo.transform, false);
        RectTransform visualRect = visualGo.GetComponent<RectTransform>();
        Stretch(visualRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image visual = visualGo.AddComponent<Image>();
        visual.sprite = rocketCardNormalSprite;
        visual.type = Image.Type.Simple;
        visual.preserveAspect = false;
        visual.color = Color.white;
        visual.raycastTarget = false;
        KillShopTint(visualGo);

        GameObject previewGo = new GameObject("Preview", typeof(RectTransform));
        previewGo.transform.SetParent(cardGo.transform, false);
        RectTransform previewRect = previewGo.GetComponent<RectTransform>();
        ApplyGridCardPreviewLayout(previewRect);

        Image preview = null;
        RawImage prefabPreview = null;
        Texture2D previewTexture = skin.modelPrefab != null
            ? RenderModelPreview(skin.modelPrefab, skin.shopPreviewEuler)
            : null;
        if (previewTexture != null)
        {
            prefabPreview = previewGo.AddComponent<RawImage>();
            prefabPreview.texture = previewTexture;
            prefabPreview.color = Color.white;
            prefabPreview.raycastTarget = false;
        }
        else
        {
            preview = previewGo.AddComponent<Image>();
            Sprite modelPreview = Resources.Load<Sprite>("RocketPreview");
            preview.sprite = modelPreview != null ? modelPreview : UIStyleKit.Circle;
            preview.color = skin.tint;
            preview.preserveAspect = true;
            preview.raycastTarget = false;
        }

        // Shrink the art within the circle established by ApplyGridCardPreviewLayout
        // above — do not touch anchor/pivot/position here, or the rocket falls back
        // to the card's dead center instead of sitting inside the upper circle.
        float previewFill = GridCellWidth * GridPreviewDiameterNorm * GridPreviewFillNorm;
        previewRect.sizeDelta = new Vector2(previewFill, previewFill);

        TextMeshProUGUI nameText = UIStyleKit.MakeLabel(cardGo.transform, skin.name,
            TypeCardName, UIDesign.TextMain, Vector2.zero,
            new Vector2(GridCellWidth - GridTextWidthInset * 2f, 28f), FontStyles.Bold,
            TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        StyleShopText(nameText, TypeCardName, UIDesign.TrackButton, UIDesign.TextMain,
            FontStyles.Bold, TextAlignmentOptions.Center);
        UIStyleKit.ApplySoftShadow(nameText, new Color(0.01f, 0.01f, 0.03f, 0.65f),
            new Vector2(0.4f, -0.6f), 0.28f);
        ApplyGridCardNameLayout(nameText);

        GameObject priceRowGo = new GameObject("PriceRow", typeof(RectTransform));
        priceRowGo.transform.SetParent(cardGo.transform, false);

        TextMeshProUGUI statusText = UIStyleKit.MakeLabel(priceRowGo.transform, "AVAILABLE",
            TypeCardStatus, UIDesign.TextSub, Vector2.zero,
            new Vector2(GridCellWidth - GridTextWidthInset * 2f, GridPriceRowHeight), FontStyles.Bold,
            TextAlignmentOptions.Left, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        StyleShopText(statusText, TypeCardStatus, UIDesign.TrackCaption, UIDesign.TextSub,
            FontStyles.Bold, TextAlignmentOptions.Left);
        UIStyleKit.ApplySoftShadow(statusText, new Color(0.01f, 0.01f, 0.03f, 0.65f),
            new Vector2(0.4f, -0.6f), 0.28f);
        statusText.name = "ActionState";

        GameObject priceIconGo = new GameObject("PriceDiamondIcon", typeof(RectTransform));
        priceIconGo.transform.SetParent(priceRowGo.transform, false);
        Image priceIcon = priceIconGo.AddComponent<Image>();
        priceIcon.sprite = DiamondIcon();
        priceIcon.preserveAspect = true;
        priceIcon.raycastTarget = false;
        priceIconGo.SetActive(false);

        ApplyGridCardPriceRowLayout(statusText, priceIcon);

        cardViews.Add(new SkinCardView
        {
            rect = rect,
            visual = visual,
            previewRect = previewRect,
            preview = preview,
            modelPreview = prefabPreview,
            previewTexture = previewTexture,
            nameText = nameText,
            statusText = statusText,
            priceIcon = priceIcon,
        });
    }

    // ── Category tabs ────────────────────────────────────────────────────────

    void BuildCategoryTabs(Transform parent)
    {
        rocketsTabActiveSprite = LoadShopTabSprite("RocketsTab_Active");
        rocketsTabInactiveSprite = LoadShopTabSprite("RocketsTab_Inactive");
        premiumTabActiveSprite = LoadShopTabSprite("PremiumRocketsTab_Active");
        premiumTabInactiveSprite = LoadShopTabSprite("PremiumRocketsTab_Inactive");

        GameObject tabsGo = new GameObject("CategoryTabs", typeof(RectTransform));
        tabsGo.transform.SetParent(parent, false);
        RectTransform tabsRect = tabsGo.GetComponent<RectTransform>();
        tabsRect.anchorMin = tabsRect.anchorMax = new Vector2(0.5f, 1f);
        tabsRect.pivot = new Vector2(0.5f, 1f);
        tabsRect.anchoredPosition = new Vector2(0f, -ShopTabsTop);
        tabsRect.sizeDelta = new Vector2(WatchAdBannerWidth, ShopTabsRowHeight);

        HorizontalLayoutGroup layout = tabsGo.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = ShopTabGap;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        layout.childAlignment = TextAnchor.MiddleCenter;

        rocketsTabButton = BuildSpriteTabButton(tabsRect, "RocketsTab", ShowRocketsTab, out rocketsTabImage);
        premiumTabButton = BuildSpriteTabButton(tabsRect, "PremiumTab", ShowPremiumTab, out premiumTabImage);

        StyleTab(rocketsTabButton, rocketsTabImage, rocketsTabActiveSprite, rocketsTabInactiveSprite, true);
        StyleTab(premiumTabButton, premiumTabImage, premiumTabActiveSprite, premiumTabInactiveSprite, false);
    }

    RectTransform BuildSpriteTabButton(Transform parent, string name, UnityEngine.Events.UnityAction onClick,
        out Image tabImage)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        LayoutElement layoutElement = go.AddComponent<LayoutElement>();
        layoutElement.flexibleWidth = 1f;
        layoutElement.minWidth = ShopTabWidth;
        layoutElement.preferredHeight = ShopTabsRowHeight;

        Image hitImage = go.AddComponent<Image>();
        hitImage.color = Color.clear;
        hitImage.raycastTarget = true;
        KillShopTint(go);

        GameObject visualGo = new GameObject("Visual", typeof(RectTransform));
        visualGo.transform.SetParent(go.transform, false);
        RectTransform visualRect = visualGo.GetComponent<RectTransform>();
        visualRect.anchorMin = new Vector2(0.5f, 0.5f);
        visualRect.anchorMax = new Vector2(0.5f, 0.5f);
        visualRect.pivot = new Vector2(0.5f, 0.5f);
        visualRect.anchoredPosition = Vector2.zero;

        tabImage = visualGo.AddComponent<Image>();
        tabImage.type = Image.Type.Simple;
        tabImage.preserveAspect = true;
        tabImage.color = Color.white;
        tabImage.raycastTarget = false;
        KillShopTint(visualGo);

        Button button = go.AddComponent<Button>();
        button.targetGraphic = hitImage;
        button.transition = Selectable.Transition.None;
        ConfigureShopSpriteButtonColors(button);
        button.onClick.AddListener(onClick);
        go.AddComponent<UIButtonPressFeedback>();

        return go.GetComponent<RectTransform>();
    }

    void ShowRocketsTab() => SetActiveTab(ShopTab.Rockets);
    void ShowPremiumTab() => SetActiveTab(ShopTab.Premium);

    // A rocket's category is its real product association — the same
    // iapProductId that already drives its price, badge and purchase path
    // everywhere else in this file. No second classification to keep in sync.
    static bool IsPremiumSkin(SkinData skin) => !string.IsNullOrEmpty(skin.iapProductId);

    // One content area for both tabs: cards belonging to the other category
    // are deactivated (GridLayoutGroup and this file's own manual layout both
    // skip inactive children), not destroyed — so switching tabs can never
    // duplicate a card or its listener. Nothing here touches selectedSkin,
    // spends currency or writes save data.
    void SetActiveTab(ShopTab tab)
    {
        activeTab = tab;

        StyleTab(rocketsTabButton, rocketsTabImage, rocketsTabActiveSprite, rocketsTabInactiveSprite,
            tab == ShopTab.Rockets);
        StyleTab(premiumTabButton, premiumTabImage, premiumTabActiveSprite, premiumTabInactiveSprite,
            tab == ShopTab.Premium);

        LayoutVisibleCards();
        if (shopScroll != null)
            shopScroll.verticalNormalizedPosition = 1f;
        RefreshShop();
    }

    // Positions only the active tab's cards, top-down, wrapping at
    // GridColumns, left to right in real skin/product order — no centring.
    // A real card's row is completed with COMING SOON placeholders rather
    // than left with a gap or a lone card, so GOLD (or whichever real card
    // ends a row) stays the first slot of its row instead of drifting to the
    // row's centre. Recomputed every tab switch from the live skin/product
    // data, so it stays correct if more rockets are added later.
    void LayoutVisibleCards()
    {
        if (shopContent == null || skins == null) return;

        List<int> visible = new List<int>();
        for (int i = 0; i < skins.Length; i++)
            if (IsPremiumSkin(skins[i]) == (activeTab == ShopTab.Premium)) visible.Add(i);

        for (int i = 0; i < cardViews.Count; i++)
            if (cardViews[i]?.rect != null) cardViews[i].rect.gameObject.SetActive(false);
        for (int i = 0; i < placeholderCards.Count; i++)
            placeholderCards[i].gameObject.SetActive(false);

        int remainder = visible.Count % GridColumns;
        int placeholdersNeeded = visible.Count == 0 ? 0
            : (remainder == 0 ? 0 : GridColumns - remainder);

        int totalSlots = visible.Count + placeholdersNeeded;
        int placeholderCursor = 0;

        for (int slot = 0; slot < totalSlots; slot++)
        {
            RectTransform rect;
            if (slot < visible.Count)
            {
                int skinIndex = visible[slot];
                if (skinIndex >= cardViews.Count || cardViews[skinIndex]?.rect == null) continue;
                rect = cardViews[skinIndex].rect;
            }
            else
            {
                if (placeholderCursor >= placeholderCards.Count)
                    placeholderCards.Add(BuildPlaceholderCard(shopContent));
                rect = placeholderCards[placeholderCursor];
                placeholderCursor++;
            }

            int row = slot / GridColumns;
            int col = slot % GridColumns;

            rect.gameObject.SetActive(true);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(
                col * (GridCellWidth + GridColumnGap),
                -(GridVerticalContentPadding + row * (GridCellHeight + GridRowGap)));
            rect.sizeDelta = new Vector2(GridCellWidth, GridCellHeight);
        }

        if (shopContent != null)
            shopContent.sizeDelta = new Vector2(WatchAdBannerWidth, shopContent.sizeDelta.y);

        int rows = totalSlots == 0 ? 0 : Mathf.CeilToInt(totalSlots / (float)GridColumns);
        ApplyShopContentHeight(rows == 0 ? 1 : rows);

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(shopContent);
    }

    static void StyleTab(RectTransform tabRect, Image tabImage, Sprite activeSprite, Sprite inactiveSprite,
        bool active)
    {
        if (tabRect == null || tabImage == null) return;

        Sprite sprite = active ? activeSprite : inactiveSprite;
        tabImage.sprite = sprite;
        tabImage.color = active ? Color.white : ShopTabInactiveImageColor;
        FitTabVisualToRoot(tabImage, tabRect);
    }

    static void FitTabVisualToRoot(Image tabImage, RectTransform tabRoot)
    {
        if (tabImage == null || tabRoot == null) return;
        Sprite sprite = tabImage.sprite;
        if (sprite == null || sprite.rect.height <= 0f) return;

        float targetWidth = tabRoot.rect.width;
        if (targetWidth <= 1f) targetWidth = ShopTabWidth;

        RectTransform visualRect = tabImage.rectTransform;
        float aspect = sprite.rect.width / sprite.rect.height;
        float targetHeight = targetWidth / aspect;

        visualRect.anchorMin = new Vector2(0.5f, 0.5f);
        visualRect.anchorMax = new Vector2(0.5f, 0.5f);
        visualRect.pivot = new Vector2(0.5f, 0.5f);
        visualRect.anchoredPosition = Vector2.zero;
        visualRect.sizeDelta = new Vector2(targetWidth, targetHeight);
        tabImage.preserveAspect = true;
    }


    // Coming Soon row filler — same card shell, dimmed, non-interactive.
    RectTransform BuildPlaceholderCard(Transform parent)
    {
        EnsureRocketCardSprites();

        GameObject cardGo = new GameObject("PlaceholderCard", typeof(RectTransform));
        cardGo.transform.SetParent(parent, false);
        RectTransform rect = cardGo.GetComponent<RectTransform>();
        rect.localScale = Vector3.one;
        rect.sizeDelta = new Vector2(GridCellWidth, GridCellHeight);

        Image hitImage = cardGo.AddComponent<Image>();
        hitImage.color = Color.clear;
        hitImage.raycastTarget = false;

        GameObject visualGo = new GameObject("CardVisual", typeof(RectTransform));
        visualGo.transform.SetParent(cardGo.transform, false);
        RectTransform visualRect = visualGo.GetComponent<RectTransform>();
        Stretch(visualRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image visual = visualGo.AddComponent<Image>();
        visual.sprite = rocketCardNormalSprite;
        visual.type = Image.Type.Simple;
        visual.preserveAspect = false;
        visual.color = new Color(0.72f, 0.72f, 0.78f, 0.82f);
        visual.raycastTarget = false;
        KillShopTint(visualGo);

        GameObject previewGo = new GameObject("Preview", typeof(RectTransform));
        previewGo.transform.SetParent(cardGo.transform, false);
        RectTransform previewRect = previewGo.GetComponent<RectTransform>();
        ApplyGridCardPreviewLayout(previewRect);

        TextMeshProUGUI mark = UIStyleKit.AddLabel(previewGo.transform, "?", 64f, LockedLavender,
            FontStyles.Bold);
        StyleShopText(mark, 64f, 0f, new Color(LockedLavender.r, LockedLavender.g,
            LockedLavender.b, 0.55f), FontStyles.Bold);
        mark.raycastTarget = false;

        TextMeshProUGUI label = UIStyleKit.MakeLabel(cardGo.transform, string.Empty,
            TypeCardName, LockedLavender, Vector2.zero,
            new Vector2(GridCellWidth - GridTextWidthInset * 2f, 28f), FontStyles.Bold,
            TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        StyleShopText(label, TypeCardName, UIDesign.TrackButton,
            new Color(LockedLavender.r, LockedLavender.g, LockedLavender.b, 0.75f),
            FontStyles.Bold, TextAlignmentOptions.Center);
        label.name = "Name";
        label.gameObject.SetActive(false);

        TextMeshProUGUI subLabel = UIStyleKit.MakeLabel(cardGo.transform, "COMING SOON",
            TypeCardStatus, LockedLavender, Vector2.zero,
            new Vector2(GridCellWidth - GridTextWidthInset * 2f, 24f), FontStyles.Bold,
            TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        StyleShopText(subLabel, TypeCardStatus, UIDesign.TrackCaption,
            new Color(LockedLavender.r, LockedLavender.g, LockedLavender.b, 0.6f),
            FontStyles.Bold, TextAlignmentOptions.Center);
        subLabel.name = "ActionState";

        ApplyGridCardNameLayout(label);
        ApplyGridCardPlaceholderStateLayout(subLabel);

        return rect;
    }

    void RefreshShop()
    {
        if (skins == null) return;
        int balance = CoinManager.instance != null ? CoinManager.instance.GetCoins() : 0;
        if (shopBalanceText != null) shopBalanceText.text = balance.ToString();
        ApplyShopBalanceChipLayout();
        RefreshShopRewardedButton();

        EnsureRocketCardSprites();

        for (int i = 0; i < cardViews.Count && i < skins.Length; i++)
        {
            SkinCardView view = cardViews[i];
            SkinData skin = skins[i];
            bool premium = !string.IsNullOrEmpty(skin.iapProductId);
            bool owned = IsOwned(i);
            bool equipped = selectedSkin == i;

            if (view.preview != null) view.preview.color = skin.tint;
            if (view.modelPreview != null) view.modelPreview.color = Color.white;

            if (view.visual != null)
            {
                view.visual.sprite = equipped ? rocketCardSelectedSprite : rocketCardNormalSprite;
                view.visual.color = Color.white;
            }

            int coinPrice = premium ? 0 : GameEconomyConfig.Current.GetSkinPrice(i);
            bool affordable = balance >= coinPrice;

            if (equipped)
            {
                view.statusText.text = "EQUIPPED";
                view.statusText.color = UIDesign.Gold;
            }
            else if (owned)
            {
                view.statusText.text = "EQUIP";
                view.statusText.color = OwnedLime;
            }
            else if (premium)
            {
                MonetizationManager mm = MonetizationManager.instance;
                bool deferred = mm != null && mm.IsPurchaseDeferred(skin.iapProductId);
                if (deferred)
                {
                    view.statusText.text = "PENDING";
                    view.statusText.color = LockedLavender;
                }
                else if (mm == null || mm.State == MonetizationManager.IapState.Uninitialized
                    || mm.State == MonetizationManager.IapState.Connecting)
                {
                    view.statusText.text = "CONNECTING…";
                    view.statusText.color = LockedLavender;
                }
                else if (mm.State == MonetizationManager.IapState.Unavailable
                    || !mm.TryGetLocalizedPrice(skin.iapProductId, out string localizedPrice))
                {
                    view.statusText.text = "UNAVAILABLE";
                    view.statusText.color = LockedLavender;
                }
                else
                {
                    bool canBuy = mm.CanPurchase(skin.iapProductId);
                    view.statusText.text = localizedPrice;
                    view.statusText.color = canBuy ? PremiumIdentity : LockedLavender;
                }
            }
            else
            {
                view.statusText.text = coinPrice == 0 ? "FREE" : coinPrice.ToString();
                view.statusText.color = affordable ? UIDesign.Gold : LockedLavender;
            }

            bool showPriceIcon = !premium && !owned && !equipped && coinPrice > 0;
            ApplyGridPriceRowContent(view, showPriceIcon);

            if (view.rect != null)
                view.rect.localScale = Vector3.one;
        }
    }

    void OnSkinClicked(int index)
    {
        if (index < 0 || index >= skins.Length) return;

        if (index < cardViews.Count && cardViews[index]?.rect != null)
            TriggerCardScalePop(cardViews[index].rect);

        SkinData skin = skins[index];
        bool owned = IsOwned(index);

        if (!owned)
        {
            // IAP rockets equip themselves from OnIapPurchaseSucceeded once the
            // store confirms ownership — a tap here only starts the purchase.
            if (!string.IsNullOrEmpty(skin.iapProductId))
            {
                if (MonetizationManager.instance == null || !MonetizationManager.instance.Purchase(skin.iapProductId))
                    ShowFlash("STORE NOT READY", UIDesign.Danger);
                RefreshShop();
                return;
            }

            int price = GameEconomyConfig.Current.GetSkinPrice(index);
            int balance = CoinManager.instance != null ? CoinManager.instance.GetCoins() : 0;
            if (CoinManager.instance == null || balance < price || !CoinManager.instance.SpendCoins(price))
            {
                int missing = Mathf.Max(0, price - balance);
                ShowFlash("NOT ENOUGH DIAMONDS  •  NEED " + missing + " MORE", UIDesign.Danger);
                if (index < cardViews.Count && cardViews[index].rect != null)
                    StartPurchasePulse(cardViews[index].rect, false);
                RefreshShop();
                return;
            }

            PlayerPrefs.SetInt(skin.prefsKey, 1);
            ShowFlash("PURCHASED  •  EQUIPPED", UIDesign.Accent);
            if (index < cardViews.Count && cardViews[index].rect != null)
                StartPurchasePulse(cardViews[index].rect, true);
        }
        else
        {
            if (index < cardViews.Count && cardViews[index].rect != null)
                StartPurchasePulse(cardViews[index].rect, true);
        }

        selectedSkin = index;
        PlayerPrefs.SetInt(SelectedSkinKey, selectedSkin);
        PlayerPrefs.Save();
        ApplySkin(selectedSkin);
        RefreshShop();
    }

    bool IsOwned(int index)
    {
        if (index < 0 || index >= skins.Length) return false;
        SkinData skin = skins[index];

        if (!string.IsNullOrEmpty(skin.iapProductId))
            return MonetizationManager.instance != null
                && MonetizationManager.instance.IsRocketOwned(skin.iapProductId);

        return !string.IsNullOrEmpty(skin.prefsKey)
            && PlayerPrefs.GetInt(skin.prefsKey, index == 0 ? 1 : 0) == 1;
    }

    int IndexForIapProduct(string productId)
    {
        if (string.IsNullOrEmpty(productId)) return -1;
        for (int i = 0; i < skins.Length; i++)
            if (skins[i].iapProductId == productId) return i;
        return -1;
    }

    void OnIapPurchaseSucceeded(string productId)
    {
        int index = IndexForIapProduct(productId);
        if (index < 0)
        {
            RefreshShop();
            return;
        }

        // Same precedent as a coin-skin purchase: buying equips it immediately.
        selectedSkin = index;
        PlayerPrefs.SetInt(SelectedSkinKey, selectedSkin);
        PlayerPrefs.Save();
        ApplySkin(selectedSkin);
        ShowFlash("PURCHASED  •  EQUIPPED", UIDesign.Accent);
        if (index < cardViews.Count && cardViews[index].rect != null)
            StartPurchasePulse(cardViews[index].rect, true);
        RefreshShop();
    }

    void OnIapPurchaseFailed(string productId, string reason)
    {
        int index = IndexForIapProduct(productId);
        if (index >= 0)
        {
            ShowFlash("PURCHASE FAILED", UIDesign.Danger);
            if (index < cardViews.Count) StartPurchasePulse(cardViews[index].rect, false);
        }
        RefreshShop();
    }

    static Texture2D RenderModelPreview(GameObject prefab, Vector3 rotationEuler)
    {
        if (prefab == null) return null;

        Texture2D texture = null;
        RenderTexture renderTarget = null;
        GameObject previewObject = null;
        GameObject cameraObject = null;
        List<Material> previewMaterials = new List<Material>();
        try
        {
            renderTarget = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32)
            {
                name = prefab.name + "_ShopPreviewTarget",
                antiAliasing = 2,
                hideFlags = HideFlags.HideAndDontSave
            };
            renderTarget.Create();

            previewObject = Instantiate(prefab);
            previewObject.name = prefab.name + "_ShopPreviewModel";
            previewObject.hideFlags = HideFlags.HideAndDontSave;
            previewObject.transform.rotation = Quaternion.Euler(rotationEuler);
            RetroUfoVisualPivot retroPreview = previewObject.GetComponentInChildren<RetroUfoVisualPivot>(true);
            if (retroPreview != null) retroPreview.ApplyShopPreviewPose();
            SetLayerRecursively(previewObject.transform, ShopPreviewLayer);

            Renderer[] previewRenderers = previewObject.GetComponentsInChildren<Renderer>(true);
            if (previewRenderers.Length == 0) throw new InvalidOperationException("prefab has no renderers");

            // Sprite-Lit materials require a 2D Light, so a standalone preview
            // camera otherwise renders the real Cat Rocket as a black silhouette.
            // Clone only the preview instance's materials and use the matching
            // unlit 2D shader so vertex colours remain intact without touching
            // the gameplay prefab or its shared materials.
            Shader previewShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (previewShader == null) throw new InvalidOperationException("URP 2D preview shader is missing");
            for (int rendererIndex = 0; rendererIndex < previewRenderers.Length; rendererIndex++)
            {
                Renderer previewRenderer = previewRenderers[rendererIndex];
                Material[] sourceMaterials = previewRenderer.sharedMaterials;
                Material[] unlitMaterials = new Material[sourceMaterials.Length];
                for (int materialIndex = 0; materialIndex < sourceMaterials.Length; materialIndex++)
                {
                    Material sourceMaterial = sourceMaterials[materialIndex];
                    if (sourceMaterial == null) continue;
                    Material previewMaterial = new Material(sourceMaterial)
                    {
                        shader = previewShader,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    previewMaterials.Add(previewMaterial);
                    unlitMaterials[materialIndex] = previewMaterial;
                }
                previewRenderer.sharedMaterials = unlitMaterials;
            }

            Bounds bounds = previewRenderers[0].bounds;
            for (int i = 1; i < previewRenderers.Length; i++) bounds.Encapsulate(previewRenderers[i].bounds);

            cameraObject = new GameObject(prefab.name + "_ShopPreviewCamera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            Camera previewCamera = cameraObject.AddComponent<Camera>();
            previewCamera.enabled = false;
            previewCamera.orthographic = true;
            previewCamera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x) * 1.18f;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = Color.clear;
            previewCamera.cullingMask = 1 << ShopPreviewLayer;
            previewCamera.nearClipPlane = 0.01f;
            previewCamera.farClipPlane = 50f;
            previewCamera.allowHDR = false;
            previewCamera.targetTexture = renderTarget;
            cameraObject.transform.position = bounds.center + Vector3.back * 10f;
            cameraObject.transform.rotation = Quaternion.identity;

            previewCamera.Render();

            RenderTexture previousTarget = RenderTexture.active;
            try
            {
                RenderTexture.active = renderTarget;
                texture = new Texture2D(256, 256, TextureFormat.RGBA32, false)
                {
                    name = prefab.name + "_ShopPreview",
                    hideFlags = HideFlags.HideAndDontSave
                };
                texture.ReadPixels(new Rect(0f, 0f, 256f, 256f), 0, 0);
                texture.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previousTarget;
            }
            return texture;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Cat Rocket shop preview fell back to the existing preview: "
                + exception.Message);
            if (texture != null) Destroy(texture);
            texture = null;
            return null;
        }
        finally
        {
            if (renderTarget != null)
            {
                renderTarget.Release();
                Destroy(renderTarget);
            }
            if (previewObject != null) { previewObject.SetActive(false); Destroy(previewObject); }
            if (cameraObject != null) { cameraObject.SetActive(false); Destroy(cameraObject); }
            for (int i = 0; i < previewMaterials.Count; i++)
                if (previewMaterials[i] != null) Destroy(previewMaterials[i]);
        }
    }

    static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

    void OnBalanceChanged(int _) => RefreshShop();

    void OnMonetizationProductsUpdated() => RefreshShop();

    void TriggerCardScalePop(RectTransform target)
    {
        if (target == null) return;
        if (activePulseCoroutines.TryGetValue(target, out Coroutine existing) && existing != null)
        {
            StopCoroutine(existing);
            activePulseCoroutines.Remove(target);
        }
        activePulseCoroutines[target] = StartCoroutine(CardScalePopRoutine(target));
    }

    IEnumerator CardScalePopRoutine(RectTransform target)
    {
        if (target == null) yield break;
        Vector3 baseScale = GetTargetBaseScale(target);
        Vector3 popScale = baseScale * 1.06f;
        float elapsed = 0f;
        while (elapsed < 0.08f)
        {
            if (target == null) yield break;
            elapsed += Time.unscaledDeltaTime;
            target.localScale = Vector3.Lerp(baseScale, popScale, Mathf.Clamp01(elapsed / 0.08f));
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < 0.12f)
        {
            if (target == null) yield break;
            elapsed += Time.unscaledDeltaTime;
            target.localScale = Vector3.Lerp(popScale, baseScale, Mathf.Clamp01(elapsed / 0.12f));
            yield return null;
        }

        if (target != null) target.localScale = baseScale;
        if (target != null) activePulseCoroutines.Remove(target);
    }

    void StartPurchasePulse(RectTransform target, bool success)
    {
        if (target == null) return;
        if (activePulseCoroutines.TryGetValue(target, out Coroutine existing) && existing != null)
        {
            StopCoroutine(existing);
            activePulseCoroutines.Remove(target);
        }
        activePulseCoroutines[target] = StartCoroutine(PurchasePulse(target, success));
    }

    IEnumerator PurchasePulse(RectTransform target, bool success)
    {
        if (target == null) yield break;
        Vector3 baseScale = GetTargetBaseScale(target);
        Vector3 peak = baseScale * (success ? 1.08f : 0.95f);
        float elapsed = 0f;
        while (elapsed < 0.14f)
        {
            if (target == null) yield break;
            elapsed += Time.unscaledDeltaTime;
            target.localScale = Vector3.Lerp(baseScale, peak, Mathf.Clamp01(elapsed / 0.14f));
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < 0.18f)
        {
            if (target == null) yield break;
            elapsed += Time.unscaledDeltaTime;
            target.localScale = Vector3.Lerp(peak, baseScale, Mathf.Clamp01(elapsed / 0.18f));
            yield return null;
        }

        if (target != null) target.localScale = baseScale;
        if (target != null) activePulseCoroutines.Remove(target);
    }

    private Vector3 GetTargetBaseScale(RectTransform target)
    {
        return Vector3.one;
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
            PresentationGate.Release(PresentationGate.Kind.Shop);
            RestoreMainMenuForeground();
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
        StyleShopText(label, UIDesign.TypeBody, UIDesign.TrackLabel, color, FontStyles.Bold);

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

    internal static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}
