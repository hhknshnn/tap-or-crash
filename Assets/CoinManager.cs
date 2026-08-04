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

    // One horizontal group: [icon] gap [amount] gap [+]. Every step is measured
    // from the one before it, so the "+" cannot drift away from the last digit.
    private const float IconSize = 72f;
    private const float IconToValueGap = 16f;
    private const float ValueToPlusGap = 14f;
    // The visible button is large enough to advertise the shop entry while the
    // transparent hit rect still gives it the same comfortable thumb target as
    // the rest of the HUD.
    private const float PlusButtonSize = 63f;
    private const float ValueFontSize = 48f;
    private const float CounterHeight = 98f;
    private const float StripHorizontalPadding = 14f;
    private const float ValueLeft = StripHorizontalPadding + IconSize + IconToValueGap;

    private Canvas canvas;
    private RectTransform canvasRect;
    private TextMeshProUGUI coinText;
    private RectTransform coinCounterRt;
    private RectTransform coinValueRt;
    private RectTransform coinPlusRt;
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
        canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return;
        canvasRect = canvas.GetComponent<RectTransform>();

        Transform existing = canvas.transform.Find("CoinCounter");
        if (existing != null)
        {
            coinCounterRt = existing.GetComponent<RectTransform>();
            Transform value = existing.Find("Value");
            Transform shortcut = existing.Find("CoinPurchaseShortcut");
            coinValueRt = value as RectTransform;
            coinText = value != null ? value.GetComponent<TextMeshProUGUI>() : null;
            coinPlusRt = shortcut as RectTransform;
            Button shortcutButton = shortcut != null ? shortcut.GetComponent<Button>() : null;
            if (shortcutButton != null)
            {
                shortcutButton.onClick.RemoveListener(OpenCoinShop);
                shortcutButton.onClick.AddListener(OpenCoinShop);
            }
            WireCoinStripButton(existing.gameObject);
            if (coinCounterRt == null || coinText == null || coinPlusRt == null)
                Debug.LogError("CoinManager: serialized CoinCounter is incomplete. Run the Main Menu authoring command.", this);
            ApplyCoinPresentation(existing);
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
        coinCounterRt.sizeDelta = new Vector2(258f, CounterHeight);
        UIKit.MakeGlass(root, UIDesign.RadiusChip, UITinted.Role.Glass, 0.92f, true, true);
        WireCoinStripButton(root);

        GameObject iconGo = new GameObject("Icon");
        iconGo.transform.SetParent(root.transform, false);
        RectTransform iconRt = iconGo.AddComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0f, 0.5f);
        iconRt.anchorMax = new Vector2(0f, 0.5f);
        iconRt.pivot = new Vector2(0f, 0.5f);
        iconRt.anchoredPosition = new Vector2(StripHorizontalPadding, 0f);
        iconRt.sizeDelta = new Vector2(IconSize, IconSize);
        Image iconImg = iconGo.AddComponent<Image>();
        iconImg.sprite = UIIcons.Get(UIIcons.Coin);
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;
        ConfigureCoinIcon(iconGo);

        GameObject textGo = new GameObject("Value");
        textGo.transform.SetParent(root.transform, false);
        coinValueRt = textGo.AddComponent<RectTransform>();
        coinValueRt.anchorMin = coinValueRt.anchorMax = new Vector2(0f, 0.5f);
        coinValueRt.pivot = new Vector2(0f, 0.5f);
        coinValueRt.anchoredPosition = new Vector2(ValueLeft, 0f);
        coinValueRt.sizeDelta = new Vector2(0f, CounterHeight - 12f);

        coinText = textGo.AddComponent<TextMeshProUGUI>();
        UIKit.StyleText(coinText, UIDesign.TypeHeading, UIDesign.TrackButton, UIDesign.TextMain,
            FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        coinText.enableAutoSizing = false;
        coinText.fontSize = ValueFontSize;
        coinText.raycastTarget = false;
        // StyleText leaves every label wrapping-off and ellipsised. The rect here is
        // sized to the text rather than the other way round, so an ellipsis would be
        // drawn exactly at the last digit it was measured to fit.
        coinText.textWrappingMode = TextWrappingModes.NoWrap;
        coinText.overflowMode = TextOverflowModes.Overflow;

        CreateCoinShopShortcut(root.transform);
        RefreshCoinDisplay();
    }

    void ApplyCoinPresentation(Transform root)
    {
        if (root == null || coinCounterRt == null) return;
        UIKit.MakeGlass(root.gameObject, UIDesign.RadiusChip, UITinted.Role.Glass, 0.92f, true, true);
        WireCoinStripButton(root.gameObject);
        coinCounterRt.sizeDelta = new Vector2(coinCounterRt.sizeDelta.x, CounterHeight);

        RectTransform icon = root.Find("Icon") as RectTransform;
        if (icon != null)
        {
            icon.anchoredPosition = new Vector2(StripHorizontalPadding, 0f);
            icon.sizeDelta = Vector2.one * IconSize;
            ConfigureCoinIcon(icon.gameObject);
        }

        if (coinValueRt != null)
        {
            coinValueRt.anchoredPosition = new Vector2(ValueLeft, 0f);
            coinValueRt.sizeDelta = new Vector2(coinValueRt.sizeDelta.x, CounterHeight - 12f);
        }
        if (coinText != null)
        {
            coinText.enableAutoSizing = false;
            coinText.fontSize = ValueFontSize;
            coinText.fontSizeMin = ValueFontSize;
            coinText.textWrappingMode = TextWrappingModes.NoWrap;
            coinText.overflowMode = TextOverflowModes.Overflow;
        }

        Transform shortcut = root.Find("CoinPurchaseShortcut");
        RectTransform disc = shortcut != null ? shortcut.Find("PlusButton") as RectTransform : null;
        if (disc != null) disc.sizeDelta = Vector2.one * PlusButtonSize;
        RectTransform highlight = disc != null ? disc.Find("Highlight") as RectTransform : null;
        if (highlight != null)
        {
            highlight.anchoredPosition = new Vector2(0f, PlusButtonSize * 0.16f);
            highlight.sizeDelta = Vector2.one * (PlusButtonSize * 0.62f);
        }
        TextMeshProUGUI plus = disc != null ? disc.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        if (plus != null)
        {
            plus.fontSize = 40f;
            plus.color = UIDesign.TextMain;
        }

        if (disc != null) RemovePlusShadowArtifacts(disc);
    }

    static void RemovePlusShadowArtifacts(Component target)
    {
        if (target == null) return;

        foreach (Shadow legacy in target.GetComponentsInChildren<Shadow>(true))
            if (legacy != null) UnityEngine.Object.Destroy(legacy);

        foreach (Outline legacy in target.GetComponentsInChildren<Outline>(true))
            if (legacy != null) UnityEngine.Object.Destroy(legacy);

        UIShadowLink link = target.GetComponent<UIShadowLink>();
        if (link != null) UnityEngine.Object.Destroy(link);

        Transform parent = target.transform;
        if (parent != null)
        {
            for (int i = parent.childCount - 1; i >= 0; --i)
            {
                Transform child = parent.GetChild(i);
                if (child != null &&
                    child.name != null &&
                    child.name.IndexOf("Shadow", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }

            Transform hostParent = parent.parent;
            if (hostParent != null)
            {
                for (int i = hostParent.childCount - 1; i >= 0; --i)
                {
                    Transform sibling = hostParent.GetChild(i);
                    if (sibling != null &&
                        sibling.name != null &&
                        sibling.name.IndexOf("Shadow", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        UnityEngine.Object.Destroy(sibling.gameObject);
                    }
                }
            }
        }
    }

    // The shortcut is two rects, not one. The button is what the player sees, and
    // it is small enough to sit a few pixels off the last digit; the transparent
    // rect around it is what the thumb hits, and it stays at the shared 112px
    // (~44dp) touch size the rest of the HUD uses. Sizing one rect for both jobs
    // is what pushed the old "+" a chip's width away from the number it belongs to.
    //
    // The button itself is the shared glass disc with its rim pinned warm, not a
    // painted orange puck: dark translucent centre, one hairline outline, one
    // highlight catching the light from above, one small white glyph.
    void CreateCoinShopShortcut(Transform parent)
    {
        GameObject plusObject = new GameObject("CoinPurchaseShortcut");
        plusObject.transform.SetParent(parent, false);
        coinPlusRt = plusObject.AddComponent<RectTransform>();
        coinPlusRt.anchorMin = coinPlusRt.anchorMax = new Vector2(0f, 0.5f);
        coinPlusRt.pivot = new Vector2(0.5f, 0.5f);
        coinPlusRt.sizeDelta = new Vector2(UIDesign.IconButtonSize, UIDesign.IconButtonSize);

        Image touchArea = plusObject.AddComponent<Image>();
        touchArea.color = Color.clear;
        touchArea.raycastTarget = true;

        Button button = plusObject.AddComponent<Button>();
        button.targetGraphic = touchArea;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(OpenCoinShop);

        GameObject discObject = new GameObject("PlusButton");
        discObject.transform.SetParent(plusObject.transform, false);
        RectTransform discRect = discObject.AddComponent<RectTransform>();
        discRect.anchorMin = discRect.anchorMax = discRect.pivot = new Vector2(0.5f, 0.5f);
        discRect.anchoredPosition = Vector2.zero;
        discRect.sizeDelta = new Vector2(PlusButtonSize, PlusButtonSize);

        // No shadow: the shared 112px hit area is intentionally untinted.
        UIKit.MakeGlassDisc(discObject, UITinted.Role.GlassDeep, 1f, false);
        UIKit.OverrideRim(discObject, new Color(UIDesign.Gold.r, UIDesign.Gold.g,
            UIDesign.Gold.b, 0.9f));

        GameObject highlightObject = new GameObject("Highlight");
        highlightObject.transform.SetParent(discObject.transform, false);
        RectTransform highlightRect = highlightObject.AddComponent<RectTransform>();
        highlightRect.anchorMin = highlightRect.anchorMax = new Vector2(0.5f, 0.5f);
        highlightRect.pivot = new Vector2(0.5f, 0.5f);
        highlightRect.anchoredPosition = new Vector2(0f, PlusButtonSize * 0.16f);
        highlightRect.sizeDelta = Vector2.one * (PlusButtonSize * 0.62f);
        Image highlight = highlightObject.AddComponent<Image>();
        highlight.sprite = UIGlass.Glow;
        highlight.color = new Color(1f, 0.92f, 0.78f, 0.14f);
        highlight.raycastTarget = false;

        TextMeshProUGUI plus = UIStyleKit.AddLabel(discObject.transform, "+", 40f,
            UIDesign.TextMain, FontStyles.Bold);
        plus.alignment = TextAlignmentOptions.Center;
        plus.raycastTarget = false;
        plus.rectTransform.offsetMin = new Vector2(0f, -2f);
        plus.rectTransform.offsetMax = Vector2.zero;
        plus.transform.SetAsLastSibling();

        plusObject.AddComponent<UIButtonPressFeedback>();
        UIMotion.Attach(discObject, UIMotion.Mode.Breathe, 0.55f, 3.6f);
    }

    void WireCoinStripButton(GameObject root)
    {
        if (root == null) return;

        Image hitArea = root.GetComponent<Image>();
        if (hitArea == null) hitArea = root.AddComponent<Image>();
        if (hitArea.sprite == null) UIKit.MakeGlass(root, UIDesign.RadiusChip,
            UITinted.Role.Glass, 0.92f, true, true);
        hitArea.raycastTarget = true;

        Button button = root.GetComponent<Button>();
        if (button == null) button = root.AddComponent<Button>();
        button.targetGraphic = hitArea;
        button.transition = Selectable.Transition.None;
        button.onClick.RemoveListener(OpenCoinShop);
        button.onClick.AddListener(OpenCoinShop);

        // The root keeps CoinManager's balance-bounce scale. The plus badge has
        // its own press feedback, so the strip can be clickable without two
        // components competing for localScale.
    }

    void ConfigureCoinIcon(GameObject iconGo)
    {
        if (iconGo == null) return;

        Image iconImage = iconGo.GetComponent<Image>();
        if (iconImage != null)
        {
            iconImage.sprite = UIIcons.Get(UIIcons.Coin);
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
        }

        Mask mask = iconGo.GetComponent<Mask>();
        if (mask == null) mask = iconGo.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        Transform oldDot = iconGo.transform.Find("Shine");
        GameObject sweepGo;
        if (oldDot != null)
        {
            oldDot.name = "ShineSweep";
            sweepGo = oldDot.gameObject;
        }
        else
        {
            Transform existing = iconGo.transform.Find("ShineSweep");
            sweepGo = existing != null ? existing.gameObject : new GameObject("ShineSweep");
            if (existing == null) sweepGo.transform.SetParent(iconGo.transform, false);
        }

        RectTransform sweepRect = sweepGo.GetComponent<RectTransform>();
        if (sweepRect == null) sweepRect = sweepGo.AddComponent<RectTransform>();
        sweepRect.anchorMin = sweepRect.anchorMax = new Vector2(0.5f, 0.5f);
        sweepRect.pivot = new Vector2(0.5f, 0.5f);
        sweepRect.anchoredPosition = new Vector2(-IconSize * 0.76f, 0f);
        sweepRect.sizeDelta = new Vector2(IconSize * 0.17f, IconSize * 1.38f);
        sweepRect.localRotation = Quaternion.Euler(0f, 0f, -26f);

        Image sweep = sweepGo.GetComponent<Image>();
        if (sweep == null) sweep = sweepGo.AddComponent<Image>();
        sweep.sprite = UIGlass.Glow;
        sweep.type = Image.Type.Simple;
        sweep.color = new Color(1f, 0.98f, 0.84f, 0.85f);
        sweep.raycastTarget = false;
        sweepGo.transform.SetAsLastSibling();
        UIMotion.Attach(sweepGo, UIMotion.Mode.Shine, 1f, 4.2f);
    }

    void OpenCoinShop()
    {
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
        icon.sprite = UIIcons.Get(UIIcons.Coin);
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
        if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
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

    // TMP's own preferred width for the string it is about to draw, taken once per
    // balance change rather than every frame. A layout group would give the same
    // answer at the cost of a rebuild whose input is this counter's own scale — the
    // bounce animation would then feed itself, which is exactly the unstable nesting
    // this avoids.
    void LayoutCoinGroup()
    {
        if (coinText == null || coinValueRt == null || coinPlusRt == null) return;

        float valueWidth = Mathf.Max(0f, coinText.GetPreferredValues(coinText.text).x);
        coinValueRt.sizeDelta = new Vector2(valueWidth, coinValueRt.sizeDelta.y);

        float plusCentre = ValueLeft + valueWidth + ValueToPlusGap + PlusButtonSize * 0.5f;
        coinPlusRt.anchoredPosition = new Vector2(plusCentre, 0f);

        if (coinCounterRt != null)
        {
            coinCounterRt.sizeDelta = new Vector2(
                plusCentre + PlusButtonSize * 0.5f + StripHorizontalPadding, CounterHeight);
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
        if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
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
