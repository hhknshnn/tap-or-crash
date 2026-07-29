using System;
using System.Collections;
using System.Collections.Generic;
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
    private Coroutine rewardQueueCoroutine;
    private Coroutine counterBounceCoroutine;

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

        GameObject root = new GameObject("CoinCounter");
        root.transform.SetParent(canvas.transform, false);

        coinCounterRt = root.AddComponent<RectTransform>();
        coinCounterRt.anchorMin = new Vector2(1f, 1f);
        coinCounterRt.anchorMax = new Vector2(1f, 1f);
        coinCounterRt.pivot = new Vector2(1f, 1f);
        // The same gutter as the icon discs below it and the shop pill opposite.
        UIDesign.EnsureInitialised();
        coinCounterRt.anchoredPosition = new Vector2(-UIDesign.ScreenMargin, -UIDesign.ScreenMargin);
        coinCounterRt.sizeDelta = new Vector2(248f, UIDesign.ChipHeight);

        // The same glass chip as the best-score panel, at the same radius: the
        // two readouts in this game now read as a matched pair.
        UIKit.MakeGlass(root, UIDesign.RadiusChip, UITinted.Role.Glass, 0.92f, false);

        GameObject iconGo = new GameObject("Icon");
        iconGo.transform.SetParent(root.transform, false);
        RectTransform iconRt = iconGo.AddComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0f, 0.5f);
        iconRt.anchorMax = new Vector2(0f, 0.5f);
        iconRt.pivot = new Vector2(0f, 0.5f);
        iconRt.anchoredPosition = new Vector2(20f, 0f);
        iconRt.sizeDelta = new Vector2(48f, 48f);
        Image iconImg = iconGo.AddComponent<Image>();
        iconImg.sprite = UIIcons.Get(UIIcons.Coin);
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;

        // A glint over the struck face rather than a dot drawn on a disc. It is
        // dark almost all of the time and ticks bright for a moment.
        GameObject shineGo = new GameObject("Shine");
        shineGo.transform.SetParent(iconGo.transform, false);
        RectTransform shineRt = shineGo.AddComponent<RectTransform>();
        shineRt.anchorMin = shineRt.anchorMax = new Vector2(0.42f, 0.66f);
        shineRt.sizeDelta = new Vector2(30f, 30f);
        Image shine = shineGo.AddComponent<Image>();
        shine.sprite = UIGlass.Glow;
        shine.color = new Color(1f, 0.97f, 0.84f, 0.85f);
        shine.raycastTarget = false;
        UIMotion.Attach(shineGo, UIMotion.Mode.Shine, 1f, 4.8f);

        GameObject textGo = new GameObject("Value");
        textGo.transform.SetParent(root.transform, false);
        RectTransform textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(78f, 6f);
        textRt.offsetMax = new Vector2(-18f, -6f);

        coinText = textGo.AddComponent<TextMeshProUGUI>();
        UIKit.StyleText(coinText, UIDesign.TypeHeading, UIDesign.TrackButton, UIDesign.TextMain,
            FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

        RefreshCoinDisplay();
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
        if (panel == null || panel.Find("WatchAdButton") != null) return;

        int reward = GameEconomyConfig.Current.rewardedAdCoins;
        UIStyleKit.MakeButtonAnchored(
            parent: panel,
            name: "WatchAdButton",
            label: "WATCH AD   +" + reward,
            pos: new Vector2(0f, 30f),
            size: new Vector2(520f, 82f),
            bgColor: UIStyleKit.BtnSuccess,
            onClick: () =>
            {
                if (AdManager.instance != null)
                    AdManager.instance.ShowRewardedAdForCoins();
            },
            fontSize: 27f,
            anchorMin: new Vector2(0.5f, 0f),
            anchorMax: new Vector2(0.5f, 0f),
            pivot: new Vector2(0.5f, 0f));

        Transform button = panel.Find("WatchAdButton");
        if (button == null) return;

        GameObject iconGo = new GameObject("CoinIcon");
        iconGo.transform.SetParent(button, false);
        RectTransform iconRt = iconGo.AddComponent<RectTransform>();
        iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 0.5f);
        iconRt.pivot = new Vector2(0f, 0.5f);
        iconRt.anchoredPosition = new Vector2(24f, 0f);
        iconRt.sizeDelta = new Vector2(34f, 34f);
        Image coinImg = iconGo.AddComponent<Image>();
        coinImg.sprite = UIStyleKit.Circle;
        coinImg.color = UIStyleKit.CoinColor;
        coinImg.raycastTarget = false;
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
        if (coinText != null) coinText.text = shownBalance.ToString();
        if (coinCounterRt != null)
        {
            int digits = Mathf.Max(1, shownBalance.ToString().Length);
            float width = Mathf.Clamp(170f + digits * 18f, 240f, 400f);
            coinCounterRt.sizeDelta = new Vector2(width, UIDesign.ChipHeight);
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
