using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

// Her yeni seviyeye girişte (skor PlanetSpawner.PlanetsPerLevel'in katına ulaştığında)
// bir kez oynayan "LEVEL N: TEMA" geçiş kartı. Rehberli ilk seviye bitip ikinci
// seviyeye geçilirken araya özel bir "artık yalnızsın" kartı da eklenir.
// Kart girişi/çıkışı sadece kozmetik — input'u bloklamaz (blocksRaycasts: false).
public sealed class LevelIntroUI : MonoBehaviour
{
    private struct CardData
    {
        public string Title;
        public string Subtitle;
        public Color SubtitleColor;
    }

    private static readonly Color LevelAccent = new Color(0.55f, 0.90f, 0.65f, 1f);
    private static readonly Color WarningAccent = new Color(1f, 0.60f, 0.25f, 1f);

    private static LevelIntroUI instance;

    private GameObject visualRoot;
    private CanvasGroup group;
    private RectTransform rootRect;
    private TextMeshProUGUI titleLabel;
    private TextMeshProUGUI subtitleLabel;

    private readonly Queue<CardData> cardQueue = new Queue<CardData>();
    private Coroutine animation;
    private int lastAnnouncedLevel = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstall() => SceneInstaller.RunOnEveryScene(Install);

    static void Install()
    {
        if (instance != null) return;

        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return;

        Transform gameUi = canvas.transform.Find("GameUI");
        Transform parent = gameUi != null ? gameUi : canvas.transform;

        GameObject host = new GameObject("LevelIntroUI");
        host.transform.SetParent(parent, false);
        RectTransform hostRect = host.AddComponent<RectTransform>();
        hostRect.anchorMin = Vector2.zero;
        hostRect.anchorMax = Vector2.one;
        hostRect.offsetMin = Vector2.zero;
        hostRect.offsetMax = Vector2.zero;
        host.AddComponent<LevelIntroUI>();
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        BuildVisual();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Update()
    {
        if (!GameManager.isGameStarted) return;

        int score = GameManager.instance != null ? GameManager.instance.GetScore() : 0;
        int levelIndex = PlanetSpawner.LevelIndexForScore(score);
        string[] levelNames = PlanetSpawner.LevelNames;

        if (levelIndex == lastAnnouncedLevel || levelIndex < 0 || levelIndex >= levelNames.Length)
            return;

        // Rehberli ilk seviye (index 0) bitip ikinci seviyeye geçilirken önce özel uyarı kartı.
        if (lastAnnouncedLevel == 0 && levelIndex == 1)
        {
            Enqueue("YOU'RE ON YOUR OWN NOW", "No more guidance — good luck out there.", WarningAccent);
        }

        // HUD ile aynı dil: "dünya" adı + içindeki gezegen sayısı. Vurgu rengi tema
        // kaydından gelir, yeni tema eklendiğinde kart rengi kendiliğinden uyar.
        string worldName = levelNames[levelIndex].ToUpperInvariant();
        Color accent = PlanetAmbience.AccentColorFor(levelNames[levelIndex], LevelAccent);
        Enqueue($"{worldName} WORLD", $"{PlanetSpawner.PlanetsPerLevel} PLANETS AHEAD", accent);
        lastAnnouncedLevel = levelIndex;
    }

    void Enqueue(string title, string subtitle, Color subtitleColor)
    {
        cardQueue.Enqueue(new CardData { Title = title, Subtitle = subtitle, SubtitleColor = subtitleColor });
        if (animation == null) animation = StartCoroutine(ProcessQueue());
    }

    void BuildVisual()
    {
        visualRoot = new GameObject("IntroCard");
        visualRoot.transform.SetParent(transform, false);

        rootRect = visualRoot.AddComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = new Vector2(0f, 70f);
        rootRect.sizeDelta = new Vector2(640f, 210f);
        rootRect.localScale = Vector3.one * 0.7f;

        // A card, so it takes the card radius and a shadow: unlike the progress
        // strip, this one is a moment the player is meant to look at.
        UIDesign.EnsureInitialised();
        // No shadow: the card fades and scales through a CanvasGroup, which a
        // sibling shadow cannot follow — it would hang in the air mid-fade.
        UIKit.MakeGlass(visualRoot, UIDesign.RadiusCard, UITinted.Role.GlassDeep, 1f, false);

        titleLabel = UIStyleKit.MakeLabel(visualRoot.transform, "NATURAL WORLD",
            UIDesign.TypeTitle, UIDesign.TextMain, new Vector2(0f, 38f), new Vector2(600f, 70f),
            FontStyles.Bold);
        UIKit.StyleDisplay(titleLabel, UIDesign.TypeTitle, UIDesign.TrackTitle, UIDesign.TextMain);

        subtitleLabel = UIStyleKit.MakeLabel(visualRoot.transform, "10 PLANETS AHEAD",
            UIDesign.TypeLabel, LevelAccent, new Vector2(0f, -30f), new Vector2(600f, 44f),
            FontStyles.Bold);
        UIKit.StyleText(subtitleLabel, UIDesign.TypeLabel, UIDesign.TrackLabel, LevelAccent,
            FontStyles.Bold);

        group = visualRoot.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
    }

    IEnumerator ProcessQueue()
    {
        while (cardQueue.Count > 0)
        {
            CardData card = cardQueue.Dequeue();
            titleLabel.text = card.Title;
            subtitleLabel.text = card.Subtitle;
            subtitleLabel.color = card.SubtitleColor;
            yield return PlayCard();
        }
        animation = null;
    }

    IEnumerator PlayCard()
    {
        group.alpha = 0f;
        rootRect.localScale = Vector3.one * 0.7f;

        const float inDur = 0.32f;
        float t = 0f;
        while (t < inDur)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, t / inDur);
            group.alpha = p;
            rootRect.localScale = Vector3.one * Mathf.Lerp(0.7f, 1f, p);
            yield return null;
        }
        group.alpha = 1f;
        rootRect.localScale = Vector3.one;

        yield return new WaitForSecondsRealtime(2.4f);

        const float outDur = 0.35f;
        t = 0f;
        while (t < outDur)
        {
            t += Time.unscaledDeltaTime;
            float p = t / outDur;
            group.alpha = 1f - p;
            rootRect.localScale = Vector3.one * Mathf.Lerp(1f, 1.08f, p);
            yield return null;
        }
        group.alpha = 0f;
    }
}
