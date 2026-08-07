using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CoinManager : MonoBehaviour
{
    public static CoinManager instance;

    private const string CoinKey = "Coins";
    private const string DiamondKey = "Diamonds";
    private const string MilestoneKey = "LastMilestone";

    private sealed class RewardFx
    {
        public GameObject root;
        public RectTransform rect;
        public CanvasGroup group;
        public TextMeshProUGUI amountText;
    }

    private struct LandingRewardRequest
    {
        public Vector3 worldPosition;
        public int amount;
    }

    private int coins;
    private int diamonds;
    private int runCoinsEarned;
    private int pendingLandingCredits;

    private Canvas canvas;
    private RectTransform canvasRect;
    private TextMeshProUGUI coinText;
    private RectTransform coinCounterRt;
    private RectTransform coinPlusRt;
    private float coinChipAppliedWidth = -1f;
    private Button runDoubleButton;
    private TextMeshProUGUI runDoubleLabel;
    private TextMeshProUGUI runDoublePreview;
    private Coroutine rewardQueueCoroutine;
    private Coroutine counterBounceCoroutine;
    private bool runDoubleRewardGranted;

    private readonly HashSet<int> rewardedScores = new HashSet<int>();
    private readonly Queue<LandingRewardRequest> rewardQueue = new Queue<LandingRewardRequest>();
    private readonly List<RewardFx> rewardPool = new List<RewardFx>();

    public event Action<int> BalanceChanged;

    void Awake()
    {
        if (instance == null) instance = this;
        else { Destroy(gameObject); return; }

        coins = PlayerPrefs.GetInt(CoinKey, 0);
        diamonds = PlayerPrefs.GetInt(DiamondKey, 0);
    }

    void Start()
    {
        CreateCoinCounter();
        EnsureRewardPool(3);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void CreateCoinCounter()
    {
        canvas = UIRootCanvas.Resolve();
        if (canvas == null) return;
        canvasRect = canvas.GetComponent<RectTransform>();

        Transform existing = canvas.transform.Find("CoinCounter");
        if (existing != null)
        {
            coinCounterRt = existing.GetComponent<RectTransform>();
            if (!BindDiamondChip(existing))
            {
                RebuildDiamondChip(existing.gameObject);
                BindDiamondChip(existing);
            }
            WireDiamondChipButton(existing.gameObject);
            if (coinCounterRt == null || coinText == null || coinPlusRt == null)
                Debug.LogError("CoinManager: serialized CoinCounter is incomplete. Run the Main Menu authoring command.", this);
            RefreshCoinDisplay();
            return;
        }

        if (Application.isPlaying)
        {
            Debug.LogError("CoinManager: required serialized CoinCounter is missing. Run Tools > Tap or Crash > Author Serialized Main Menu.", this);
            return;
        }

        GameObject root = new GameObject("CoinCounter");
        root.transform.SetParent(canvas.transform, false);

        coinCounterRt = root.AddComponent<RectTransform>();
        coinCounterRt.anchorMin = new Vector2(0f, 1f);
        coinCounterRt.anchorMax = new Vector2(0f, 1f);
        coinCounterRt.pivot = new Vector2(0f, 1f);
        UIDesign.EnsureInitialised();
        coinCounterRt.anchoredPosition = new Vector2(UIDesign.ScreenMargin, -UIDesign.ScreenMargin);
        coinCounterRt.sizeDelta = new Vector2(ShipSkinManager.ShopBalanceChipMinWidth, ShipSkinManager.ShopBalanceChipHeight);

        RebuildDiamondChip(root);
        BindDiamondChip(root.transform);
        WireDiamondChipButton(root);
        RefreshCoinDisplay();
    }

    // Same visual family as the Shop header's currency chip: a sliced
    // BalanceChipBaseFlat pill, DiamondIcon, centred amount and a PlusButton
    // that opens the Shop. Reuses ShipSkinManager's own builder and layout
    // constants so the HUD and the Shop currency chip can never drift apart.
    bool BindDiamondChip(Transform root)
    {
        Transform chipBase = root.Find("BalanceChipBase");
        Transform diamond = root.Find("DiamondIcon");
        Transform amount = root.Find("BalanceAmount");
        Transform plus = root.Find("PlusButton");
        if (chipBase == null || diamond == null || amount == null || plus == null) return false;

        TextMeshProUGUI amountText = amount.GetComponent<TextMeshProUGUI>();
        if (amountText == null) return false;
        coinText = amountText;
        coinPlusRt = plus as RectTransform;

        Button plusButton = plus.GetComponent<Button>();
        if (plusButton != null)
        {
            plusButton.onClick.RemoveListener(OpenCoinShop);
            plusButton.onClick.AddListener(OpenCoinShop);
        }
        return true;
    }

    static void DestroyUnityObject(UnityEngine.Object target)
    {
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    void RebuildDiamondChip(GameObject root)
    {
        for (int i = root.transform.childCount - 1; i >= 0; --i)
            DestroyUnityObject(root.transform.GetChild(i).gameObject);

        Sprite chipSprite = ShipSkinManager.LoadShopBalanceChipSprite();
        Sprite diamondSprite = ShipSkinManager.LoadShopHeaderSprite("DiamondIcon");
        Sprite plusSprite = ShipSkinManager.LoadShopHeaderSprite("PlusButton");
        if (chipSprite == null || diamondSprite == null || plusSprite == null)
        {
            Debug.LogError("CoinManager: Shop currency sprites are missing; the main menu diamond chip cannot be built.", this);
            return;
        }

        GameObject chipGo = new GameObject("BalanceChipBase", typeof(RectTransform));
        chipGo.transform.SetParent(root.transform, false);
        chipGo.transform.SetAsFirstSibling();
        RectTransform chipRect = chipGo.GetComponent<RectTransform>();
        ShipSkinManager.Stretch(chipRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        ShipSkinManager.BuildShopBalanceChipVisual(chipGo.transform, chipSprite);

        GameObject diamondGo = new GameObject("DiamondIcon", typeof(RectTransform));
        diamondGo.transform.SetParent(root.transform, false);
        RectTransform diamondRect = diamondGo.GetComponent<RectTransform>();
        diamondRect.anchorMin = diamondRect.anchorMax = new Vector2(0f, 0.5f);
        diamondRect.pivot = new Vector2(0.5f, 0.5f);
        diamondRect.anchoredPosition = new Vector2(ShipSkinManager.ShopBalanceDiamondCenterX, 0f);
        diamondRect.sizeDelta = new Vector2(ShipSkinManager.ShopBalanceDiamondVisualSize, ShipSkinManager.ShopBalanceDiamondVisualSize);
        Image diamondImage = diamondGo.AddComponent<Image>();
        diamondImage.sprite = diamondSprite;
        diamondImage.type = Image.Type.Simple;
        diamondImage.preserveAspect = true;
        diamondImage.color = Color.white;
        diamondImage.raycastTarget = false;

        GameObject amountGo = new GameObject("BalanceAmount", typeof(RectTransform));
        amountGo.transform.SetParent(root.transform, false);
        RectTransform amountRect = amountGo.GetComponent<RectTransform>();
        amountRect.anchorMin = Vector2.zero;
        amountRect.anchorMax = Vector2.one;
        amountRect.pivot = new Vector2(0.5f, 0.5f);
        amountRect.offsetMin = new Vector2(ShipSkinManager.ShopBalanceAmountLeftInset, 0f);
        amountRect.offsetMax = new Vector2(-ShipSkinManager.ShopBalanceAmountRightInset, 0f);
        amountRect.anchoredPosition = new Vector2(0f, ShipSkinManager.ShopBalanceAmountVerticalLift);

        TextMeshProUGUI amountText = amountGo.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset balanceFont = Resources.Load<TMP_FontAsset>("Fonts/Montserrat-ExtraBold SDF");
        if (balanceFont != null) amountText.font = balanceFont;
        amountText.text = "0";
        amountText.fontSize = ShipSkinManager.ShopBalanceAmountFontSize;
        amountText.fontStyle = FontStyles.Bold;
        amountText.alignment = TextAlignmentOptions.Center;
        amountText.color = ShipSkinManager.ShopBalanceAmountColor;
        amountText.margin = Vector4.zero;
        amountText.textWrappingMode = TextWrappingModes.NoWrap;
        amountText.overflowMode = TextOverflowModes.Overflow;
        amountText.raycastTarget = false;

        GameObject plusGo = new GameObject("PlusButton", typeof(RectTransform));
        plusGo.transform.SetParent(root.transform, false);
        RectTransform plusRect = plusGo.GetComponent<RectTransform>();
        plusRect.anchorMin = plusRect.anchorMax = new Vector2(1f, 0.5f);
        plusRect.pivot = new Vector2(0.5f, 0.5f);
        plusRect.anchoredPosition = new Vector2(-ShipSkinManager.ShopBalancePlusCenterX, 0f);
        plusRect.sizeDelta = new Vector2(ShipSkinManager.ShopBalancePlusHitSize, ShipSkinManager.ShopBalancePlusHitSize);

        Image plusHit = plusGo.AddComponent<Image>();
        plusHit.color = Color.clear;
        plusHit.raycastTarget = true;

        Button plusButton = plusGo.AddComponent<Button>();
        plusButton.targetGraphic = plusHit;
        plusButton.transition = Selectable.Transition.None;
        ShipSkinManager.ConfigureShopSpriteButtonColors(plusButton);
        plusButton.onClick.AddListener(OpenCoinShop);
        plusGo.AddComponent<UIButtonPressFeedback>();

        GameObject plusVisualGo = new GameObject("Visual", typeof(RectTransform));
        plusVisualGo.transform.SetParent(plusGo.transform, false);
        RectTransform plusVisualRect = plusVisualGo.GetComponent<RectTransform>();
        plusVisualRect.anchorMin = plusVisualRect.anchorMax = new Vector2(0.5f, 0.5f);
        plusVisualRect.pivot = new Vector2(0.5f, 0.5f);
        plusVisualRect.sizeDelta = new Vector2(ShipSkinManager.ShopBalancePlusVisualSize, ShipSkinManager.ShopBalancePlusVisualSize);
        Image plusImage = plusVisualGo.AddComponent<Image>();
        plusImage.sprite = plusSprite;
        plusImage.type = Image.Type.Simple;
        plusImage.preserveAspect = true;
        plusImage.color = Color.white;
        plusImage.raycastTarget = false;
    }

    // The whole chip stays a single tap target, matching the previous strip's
    // behaviour: a transparent full-rect Image/Button behind the visible chip
    // pieces, rather than only the PlusButton reacting to taps.
    void WireDiamondChipButton(GameObject root)
    {
        if (root == null) return;

        // A previously authored root may still carry the old glass pill's
        // UITinted/UIShadowLink — left alone, they would repaint this now
        // transparent hit rect with a palette colour behind the new chip art.
        UITinted legacyTint = root.GetComponent<UITinted>();
        if (legacyTint != null) DestroyUnityObject(legacyTint);
        UIShadowLink legacyShadowLink = root.GetComponent<UIShadowLink>();
        if (legacyShadowLink != null) DestroyUnityObject(legacyShadowLink);

        Image hitArea = root.GetComponent<Image>();
        if (hitArea == null) hitArea = root.AddComponent<Image>();
        hitArea.color = Color.clear;
        hitArea.raycastTarget = true;

        Button button = root.GetComponent<Button>();
        if (button == null) button = root.AddComponent<Button>();
        button.targetGraphic = hitArea;
        button.transition = Selectable.Transition.None;
        button.onClick.RemoveListener(OpenCoinShop);
        button.onClick.AddListener(OpenCoinShop);
    }

    void OpenCoinShop()
    {
        // This HUD is shared by menu and gameplay. Its purchase affordance is a
        // menu action only; a flight tap must never put a modal over the run.
        ShipSkinManager.LogShopBlocked("CoinShop");
        if (!ShipSkinManager.CanOpenFromMainMenu) return;

        ShipSkinManager shop = FindAnyObjectByType<ShipSkinManager>();
        if (shop != null) shop.OpenShop();
    }

    public int AwardLanding(Vector3 worldPosition, int score, int combo)
    {
        if (score <= 0 || !rewardedScores.Add(score)) return 0;

        int amount = GameEconomyConfig.Current.GetLandingReward(score, combo);
        if (amount <= 0) return 0;

        coins += amount;
        runCoinsEarned += amount;
        pendingLandingCredits += amount;
        SaveCoinBalance();
        BalanceChanged?.Invoke(coins);

        rewardQueue.Enqueue(new LandingRewardRequest
        {
            worldPosition = worldPosition,
            amount = amount,
        });

        if (rewardQueueCoroutine == null)
            rewardQueueCoroutine = StartCoroutine(ProcessRewardQueue());

        return amount;
    }

    // Existing gameplay code calls this method. It now routes to the deterministic landing award.
    public void SpawnCoin(Vector3 worldPos)
    {
        int score = GameManager.instance != null
            ? GameManager.instance.GetScore()
            : rewardedScores.Count + 1;
        int combo = GameManager.instance != null ? GameManager.instance.GetCombo() : 0;
        AwardLanding(worldPos, score, combo);
    }

    IEnumerator ProcessRewardQueue()
    {
        while (rewardQueue.Count > 0)
        {
            LandingRewardRequest request = rewardQueue.Dequeue();
            RewardFx fx = GetRewardFx();

            if (fx == null || canvasRect == null || coinCounterRt == null)
            {
                CompleteLandingVisual(request.amount);
                continue;
            }

            yield return AnimateLandingReward(fx, request);
            CompleteLandingVisual(request.amount);
            ReleaseRewardFx(fx);
            yield return new WaitForSecondsRealtime(0.04f);
        }

        rewardQueueCoroutine = null;
    }

    IEnumerator AnimateLandingReward(RewardFx fx, LandingRewardRequest request)
    {
        fx.root.SetActive(true);
        fx.root.transform.SetAsLastSibling();
        fx.amountText.text = "+1";
        fx.group.alpha = 1f;

        Vector2 start = WorldToCanvasPoint(request.worldPosition);
        Vector2 risen = start + new Vector2(0f, 105f);
        fx.rect.anchoredPosition = start;
        fx.rect.localScale = Vector3.one * 0.52f;

        float elapsed = 0f;
        const float popDuration = 0.17f;
        while (elapsed < popDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(elapsed / popDuration);
            float eased = 1f - Mathf.Pow(1f - p, 3f);
            fx.rect.localScale = Vector3.one * Mathf.LerpUnclamped(0.52f, 1.20f, eased);
            fx.rect.anchoredPosition = Vector2.Lerp(start, start + new Vector2(0f, 42f), eased);
            yield return null;
        }

        elapsed = 0f;
        const float riseDuration = 0.22f;
        while (elapsed < riseDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / riseDuration));
            fx.rect.localScale = Vector3.Lerp(Vector3.one * 1.20f, Vector3.one, p);
            fx.rect.anchoredPosition = Vector2.Lerp(start + new Vector2(0f, 42f), risen, p);
            yield return null;
        }

        Vector2 target = RectToCanvasPoint(coinCounterRt);
        elapsed = 0f;
        const float flyDuration = 0.46f;
        while (elapsed < flyDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float raw = Mathf.Clamp01(elapsed / flyDuration);
            float p = Mathf.SmoothStep(0f, 1f, raw);
            Vector2 pos = Vector2.Lerp(risen, target, p);
            pos.y += Mathf.Sin(raw * Mathf.PI) * 70f;
            fx.rect.anchoredPosition = pos;
            fx.rect.localScale = Vector3.one * Mathf.Lerp(1f, 0.66f, p);
            fx.group.alpha = raw < 0.76f ? 1f : 1f - ((raw - 0.76f) / 0.24f) * 0.35f;
            yield return null;
        }

        fx.rect.anchoredPosition = target;
        fx.group.alpha = 0f;
    }

    void CompleteLandingVisual(int amount)
    {
        pendingLandingCredits = Mathf.Max(0, pendingLandingCredits - amount);
        RefreshCoinDisplay();
        PlayCounterBounce();
    }

    void EnsureRewardPool(int count)
    {
        if (canvas == null) return;
        while (rewardPool.Count < count) rewardPool.Add(BuildRewardFx(rewardPool.Count));
    }

    RewardFx GetRewardFx()
    {
        EnsureRewardPool(3);
        foreach (RewardFx fx in rewardPool)
            if (fx != null && !fx.root.activeSelf) return fx;

        RewardFx extra = BuildRewardFx(rewardPool.Count);
        rewardPool.Add(extra);
        return extra;
    }

    RewardFx BuildRewardFx(int index)
    {
        if (canvas == null) return null;

        GameObject root = new GameObject("LandingRewardFx_" + index);
        root.transform.SetParent(canvas.transform, false);
        RectTransform rect = root.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(220f, 76f);

        // Same chip, same coin, one step smaller: the reward that flies up is
        // recognisably the counter it is flying towards.
        UIDesign.EnsureInitialised();
        UIKit.MakeGlass(root, UIDesign.RadiusChip, UITinted.Role.Glass, 0.86f, false);

        GameObject iconGo = new GameObject("CoinIcon");
        iconGo.transform.SetParent(root.transform, false);
        RectTransform iconRt = iconGo.AddComponent<RectTransform>();
        iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 0.5f);
        iconRt.anchoredPosition = new Vector2(-62f, 0f);
        iconRt.sizeDelta = new Vector2(46f, 46f);
        Image icon = iconGo.AddComponent<Image>();
        icon.sprite = ShipSkinManager.DiamondIcon();
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        TextMeshProUGUI amountText = UIStyleKit.MakeLabel(root.transform, "+1",
            UIDesign.TypeHeading, UIDesign.Gold, new Vector2(30f, 0f), new Vector2(120f, 62f),
            FontStyles.Bold);
        UIKit.StyleText(amountText, UIDesign.TypeHeading, UIDesign.TrackButton, UIDesign.Gold,
            FontStyles.Bold);

        CanvasGroup group = root.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        root.SetActive(false);

        return new RewardFx { root = root, rect = rect, group = group, amountText = amountText };
    }

    static void ReleaseRewardFx(RewardFx fx)
    {
        if (fx == null || fx.root == null) return;
        fx.group.alpha = 1f;
        fx.rect.localScale = Vector3.one;
        fx.root.SetActive(false);
    }

    Vector2 WorldToCanvasPoint(Vector3 worldPosition)
    {
        Camera worldCamera = Camera.main;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(worldCamera, worldPosition);
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, uiCamera, out Vector2 local);
        return local;
    }

    Vector2 RectToCanvasPoint(RectTransform target)
    {
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(uiCamera, target.position);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, uiCamera, out Vector2 local);
        return local;
    }

    public void ShowWatchAdButton()
    {
        if (canvas == null) canvas = UIRootCanvas.Resolve();
        if (canvas == null) return;

        Transform panel = canvas.transform.Find("GameOverPanel");
        if (panel == null) return;

        Transform existing = panel.Find("WatchAdButton");
        if (existing == null)
        {
            runDoubleButton = UIStyleKit.MakeButtonAnchored(
                parent: panel,
                name: "WatchAdButton",
                label: "WATCH AD  ×2",
                pos: new Vector2(0f, -5f),
                size: new Vector2(650f, 124f),
                bgColor: UIDesign.Cta,
                onClick: RequestDoubleRunReward,
                fontSize: 34f);

            existing = runDoubleButton.transform;
            runDoubleLabel = existing.GetComponentInChildren<TextMeshProUGUI>(true);
            if (runDoubleLabel != null)
            {
                runDoubleLabel.gameObject.name = "RewardPrimaryLabel";
                runDoubleLabel.fontStyle = FontStyles.Bold;
                runDoubleLabel.rectTransform.offsetMin = new Vector2(12f, 24f);
                runDoubleLabel.rectTransform.offsetMax = new Vector2(-12f, -4f);
            }

            GameObject glowObject = new GameObject("RewardGlow");
            glowObject.transform.SetParent(existing, false);
            glowObject.transform.SetAsFirstSibling();
            RectTransform glowRect = glowObject.AddComponent<RectTransform>();
            glowRect.anchorMin = glowRect.anchorMax = glowRect.pivot = new Vector2(0.5f, 0.5f);
            glowRect.sizeDelta = new Vector2(700f, 166f);
            Image glow = glowObject.AddComponent<Image>();
            glow.sprite = UIGlass.Glow;
            glow.color = new Color(UIDesign.Cta.r, UIDesign.Cta.g, UIDesign.Cta.b, 0.22f);
            glow.raycastTarget = false;
            UIMotion.Attach(glowObject, UIMotion.Mode.Pulse, 1f, 2.7f);

            runDoublePreview = UIStyleKit.MakeLabel(existing, "DOUBLE THIS RUN",
                UIDesign.TypeCaption, UIDesign.CtaText, new Vector2(0f, -34f),
                new Vector2(560f, 30f), FontStyles.Bold);
            runDoublePreview.gameObject.name = "RewardPreview";
            UIKit.StyleText(runDoublePreview, UIDesign.TypeCaption, UIDesign.TrackCaption,
                UIDesign.CtaText, FontStyles.Bold);

            if (existing.GetComponent<UIButtonPressFeedback>() == null)
                existing.gameObject.AddComponent<UIButtonPressFeedback>();
            UIMotion.Attach(existing.gameObject, UIMotion.Mode.Breathe, 0.9f, 3.0f);
        }
        else
        {
            runDoubleButton = existing.GetComponent<Button>();
            if (runDoubleLabel == null)
            {
                Transform primary = existing.Find("RewardPrimaryLabel");
                if (primary != null) runDoubleLabel = primary.GetComponent<TextMeshProUGUI>();
            }
            if (runDoublePreview == null)
            {
                Transform preview = existing.Find("RewardPreview");
                if (preview != null) runDoublePreview = preview.GetComponent<TextMeshProUGUI>();
            }
        }

        RefreshDoubleRewardButton();
        VisualPolishController.RestyleGameOver();
    }

    void RequestDoubleRunReward()
    {
        if (runDoubleRewardGranted || runCoinsEarned <= 0 || AdManager.instance == null) return;
        int currentRunReward = runCoinsEarned;
        AdManager.instance.ShowRewardedAdForCoins(currentRunReward, OnRunDoubleRewardGranted);
    }

    void OnRunDoubleRewardGranted(int bonus)
    {
        if (runDoubleRewardGranted || bonus <= 0) return;
        runDoubleRewardGranted = true;
        runCoinsEarned += bonus;
        RefreshDoubleRewardButton();
    }

    void RefreshDoubleRewardButton()
    {
        if (runDoubleButton == null) return;
        runDoubleButton.interactable = !runDoubleRewardGranted && runCoinsEarned > 0;

        if (runDoubleLabel != null)
            runDoubleLabel.text = runDoubleRewardGranted ? "REWARD CLAIMED" : "WATCH AD  ×2";
        if (runDoublePreview != null)
        {
            runDoublePreview.text = runDoubleRewardGranted
                ? "RUN TOTAL  •  " + runCoinsEarned
                : "DOUBLE THIS RUN  •  +" + runCoinsEarned + " BONUS";
        }
    }

    public void AddCoins(int amount)
    {
        if (amount <= 0) return;
        coins += amount;
        SaveCoinBalance();
        RefreshCoinDisplay();
        BalanceChanged?.Invoke(coins);
        PlayCounterBounce();
    }

    public void AddDiamonds(int amount)
    {
        if (amount <= 0) return;
        diamonds += amount;
        PlayerPrefs.SetInt(DiamondKey, diamonds);
        PlayerPrefs.Save();
    }

    public bool SpendCoins(int amount)
    {
        if (amount < 0 || coins < amount) return false;
        coins -= amount;
        SaveCoinBalance();
        RefreshCoinDisplay();
        BalanceChanged?.Invoke(coins);
        PlayCounterBounce();
        return true;
    }

    public int GetCoins() => coins;
    public int GetDiamonds() => diamonds;
    public int GetRunCoinsEarned() => runCoinsEarned;
    public int RunCoinsEarned => runCoinsEarned;
    public bool HasDoubledRunCoins => runDoubleRewardGranted;

    public void CheckMilestones(int score)
    {
        int last = PlayerPrefs.GetInt(MilestoneKey, 0);
        GameEconomyConfig.MilestoneReward[] milestones = GameEconomyConfig.Current.milestones;
        if (milestones == null) return;

        foreach (GameEconomyConfig.MilestoneReward milestone in milestones)
        {
            if (score < milestone.score || last >= milestone.score) continue;

            last = milestone.score;
            PlayerPrefs.SetInt(MilestoneKey, last);
            if (milestone.diamonds > 0) AddDiamonds(milestone.diamonds);
            StartCoroutine(ShowMilestoneNotif(milestone.score, milestone.coins, milestone.diamonds));
            break;
        }
    }

    public static void ResetMilestones()
    {
        PlayerPrefs.DeleteKey(MilestoneKey);
        PlayerPrefs.Save();
    }

    void SaveCoinBalance()
    {
        PlayerPrefs.SetInt(CoinKey, coins);
        PlayerPrefs.Save();
    }

    void RefreshCoinDisplay()
    {
        int shownBalance = Mathf.Max(0, coins - pendingLandingCredits);
        if (coinText != null) coinText.text = shownBalance.ToString("N0", CultureInfo.InvariantCulture);
        LayoutCoinGroup();
    }

    // The diamond icon and plus button are anchored to the chip's own left/right
    // edges (same as the Shop header's balance chip), so only the chip's own
    // width needs recomputing per balance change — everything else follows.
    void LayoutCoinGroup()
    {
        if (coinText == null || coinCounterRt == null) return;

        coinText.ForceMeshUpdate(true);
        float textWidth = coinText.preferredWidth;
        float width = Mathf.Clamp(
            ShipSkinManager.ShopBalanceAmountLeftInset + textWidth + ShipSkinManager.ShopBalanceAmountRightInset,
            ShipSkinManager.ShopBalanceChipMinWidth,
            ShipSkinManager.ShopBalanceChipMaxWidth);

        if (Mathf.Abs(width - coinChipAppliedWidth) > 0.5f)
        {
            coinCounterRt.sizeDelta = new Vector2(width, ShipSkinManager.ShopBalanceChipHeight);
            coinChipAppliedWidth = width;
        }
    }

    void PlayCounterBounce()
    {
        if (coinCounterRt == null) return;
        if (counterBounceCoroutine != null) StopCoroutine(counterBounceCoroutine);
        counterBounceCoroutine = StartCoroutine(BounceCounter());
    }

    IEnumerator BounceCounter()
    {
        float elapsed = 0f;
        const float upDuration = 0.12f;
        while (elapsed < upDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(elapsed / upDuration);
            coinCounterRt.localScale = Vector3.Lerp(Vector3.one, Vector3.one * 1.17f, p);
            yield return null;
        }

        elapsed = 0f;
        const float downDuration = 0.18f;
        while (elapsed < downDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / downDuration));
            coinCounterRt.localScale = Vector3.Lerp(Vector3.one * 1.17f, Vector3.one, p);
            yield return null;
        }

        coinCounterRt.localScale = Vector3.one;
        counterBounceCoroutine = null;
    }

    IEnumerator ShowMilestoneNotif(int score, int coin, int diamond)
    {
        if (canvas == null) canvas = UIRootCanvas.Resolve();
        if (canvas == null) yield break;

        GameObject root = new GameObject("MilestoneNotif");
        root.transform.SetParent(canvas.transform, false);
        RectTransform rect = root.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 120f);
        rect.sizeDelta = new Vector2(520f, 92f);

        Image bg = root.AddComponent<Image>();
        UIStyleKit.ApplyPanel(bg, UIStyleKit.BgCard);
        bg.raycastTarget = false;

        string bonus = coin > 0 ? "+" + coin + " COINS" : string.Empty;
        if (diamond > 0) bonus += (bonus.Length > 0 ? "   " : string.Empty) + "+" + diamond + " DIAMONDS";
        TextMeshProUGUI label = UIStyleKit.AddLabel(root.transform,
            "SCORE " + score + " MILESTONE\n" + bonus, 25f, UIStyleKit.CoinColor, FontStyles.Bold);
        label.lineSpacing = 5f;

        CanvasGroup group = root.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        group.alpha = 0f;

        float elapsed = 0f;
        while (elapsed < 0.25f)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Clamp01(elapsed / 0.25f);
            yield return null;
        }

        yield return new WaitForSecondsRealtime(1.25f);

        elapsed = 0f;
        Vector2 start = rect.anchoredPosition;
        while (elapsed < 0.35f)
        {
            elapsed += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(elapsed / 0.35f);
            group.alpha = 1f - p;
            rect.anchoredPosition = start + Vector2.up * (55f * p);
            yield return null;
        }

        Destroy(root);
    }
}
