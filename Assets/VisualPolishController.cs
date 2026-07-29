using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Existing scene UI is kept intact; this layer applies a consistent mobile presentation at runtime.
public sealed class VisualPolishController : MonoBehaviour
{
    private static VisualPolishController instance;

    // The shop pill and the day/night disc are the menu's bottom row. They are
    // different heights, so only a shared centre line makes them one row.
    private const float BottomRowCentre = UIDesign.ScreenMargin + UIDesign.ButtonHeightPill * 0.5f;

    // The launch lockup, bottom up: pill at 196, its caption, then the best-score
    // chip. Lifted from 344 to open an even gap on both sides of the caption.
    private const float BestChipCentre = 376f;

    private Canvas canvas;
    private RectTransform startEmblem;
    private GameObject gameOverDim;
    private GameObject gameOverPanel;
    private TextMeshProUGUI gameOverCoinsText;
    private bool gameOverWasActive;

    private GameObject startPanel;
    private Image launchGlow;
    private GameObject scorePlate;
    private GameObject scoreDisplay;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstall() => SceneInstaller.RunOnEveryScene(Install);

    static void Install()
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

        UIDesign.EnsureInitialised();
        ConfigureCanvas();
        AdoptIconFamily();
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
        // The palette follows the world the rocket is in. Checking a handful of
        // times a second is enough — the change happens once per ten planets —
        // and Refresh() returns immediately when the world has not moved.
        if (Time.frameCount % 20 == 0) UIDesign.Refresh();

        if (startPanel != null)
        {
            bool menuUp = startPanel.activeInHierarchy;

            // The run's score counter has no business sitting on top of the menu logo.
            SetActive(scorePlate, !menuUp);
            SetActive(scoreDisplay, !menuUp);
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

    // The scene's sound and day/night sprites are hand-picked PNGs from three
    // different sources. Repointing the managers at the baked family — without
    // touching the scene or their logic — is what makes those two buttons stop
    // looking like visitors from another game.
    void AdoptIconFamily()
    {
        AudioManager audio = FindAnyObjectByType<AudioManager>();
        if (audio != null)
        {
            audio.soundOnSprite = UIIcons.Get(UIIcons.SoundOn);
            audio.soundOffSprite = UIIcons.Get(UIIcons.SoundOff);
        }

        DayNightManager dayNight = FindAnyObjectByType<DayNightManager>();
        if (dayNight != null)
        {
            // The button shows the state it switches *to*: a moon while in day.
            dayNight.nightSprite = UIIcons.Get(UIIcons.Moon);
            dayNight.daySprite = UIIcons.Get(UIIcons.Sun);
        }
    }

    // The managers write their sprite into whichever Image they were wired to.
    // Once a button becomes a glass disc, that Image is the disc itself, so the
    // references are moved onto the glyph layer instead.
    void RouteIconTarget(Transform button, Image glyph)
    {
        if (button == null || glyph == null) return;
        Image buttonImage = button.GetComponent<Image>();
        if (buttonImage == null) return;

        AudioManager audio = FindAnyObjectByType<AudioManager>();
        if (audio != null && audio.soundButtonImages != null)
        {
            for (int i = 0; i < audio.soundButtonImages.Count; i++)
            {
                if (audio.soundButtonImages[i] == buttonImage) audio.soundButtonImages[i] = glyph;
            }
            audio.UpdateIcon();
        }

        DayNightManager dayNight = FindAnyObjectByType<DayNightManager>();
        if (dayNight != null && dayNight.toggleButton == buttonImage)
        {
            dayNight.toggleButton = glyph;
            dayNight.UpdateIcon();
        }
    }

    static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active) target.SetActive(active);
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

    // The menu is a showcase now: the planet owns the middle of the frame and the UI is
    // a quiet frame around it. Everything below keeps the buttons, their handlers and
    // roughly their screen slots — only the presentation changes.
    void StyleStartScreen()
    {
        Transform panel = FindDeep(canvas.transform, "StartPanel");
        if (panel == null) return;
        startPanel = panel.gameObject;

        TextMeshProUGUI logo = FindTmp(panel, "LogoText");
        if (logo != null)
        {
            logo.text = "TAP OR CRASH";
            // The outline is gone: display type at this size needs separation
            // from the planet behind it, not thicker strokes. The shadow inside
            // StyleDisplay does that job without touching the letterforms.
            logo.outlineWidth = 0f;
            UIKit.StyleDisplay(logo, UIDesign.TypeDisplay, UIDesign.TrackDisplay, UIDesign.TextMain);
            // Below the top row of controls: a full-width title cannot share that band
            // with the coin counter and the sound button on a narrow phone.
            SetRect(logo.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(840f, 110f));
        }

        // Hairline between title and tagline: the cheapest way to make a stacked pair of
        // labels read as one designed lockup.
        EnsureAccentRule(panel, "LogoRule", new Vector2(0f, -360f), new Vector2(240f, 3f));

        TextMeshProUGUI subtitle = FindTmp(panel, "SubtitleText");
        if (subtitle != null)
        {
            subtitle.text = "ONE TAP  •  ONE ORBIT";
            Color tagline = UIDesign.Accent;
            tagline.a = 0.80f;
            UIKit.StyleText(subtitle, UIDesign.TypeLabel, UIDesign.TrackCaption, tagline,
                FontStyles.Bold);
            SetRect(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -398f), new Vector2(760f, 48f));
        }

        CreateStartEmblem(panel);
        StyleLaunchCall(panel);
        StyleBestScore(panel);
        StyleControlHint(panel);
        StyleShopButton(panel);

        // One disc size for all three. The old layout gave them 84, 116 and 86
        // pixels, which is most of why the top row never settled.
        //
        // Every disc now hangs off the same screen margin as the coin chip above
        // it and the shop pill opposite it: a disc's centre is one margin plus
        // its own radius from the edge, so all four controls share one gutter.
        const float discEdge = UIDesign.ScreenMargin + UIDesign.IconButtonSize * 0.5f;
        const float discGap = 20f;
        // One gap below the coin chip rather than a hand-picked -180 that left
        // eight pixels of air once the chip moved onto the shared margin.
        const float discRow = -(UIDesign.ScreenMargin + UIDesign.ChipHeight + 24f
                                + UIDesign.IconButtonSize * 0.5f);

        StyleIconButton(panel, "SoundButton", new Vector2(1f, 1f),
            new Vector2(-(discEdge + UIDesign.IconButtonSize + discGap), discRow));
        StyleIconButton(panel, "HelpButton", new Vector2(1f, 1f), new Vector2(-discEdge, discRow));
        // Shares the shop pill's centre line: the two bottom controls are the
        // only pair in the menu that read as a single row, so a 38px difference
        // in baseline was the loudest thing on the screen.
        StyleIconButton(panel, "DayNightButton", new Vector2(1f, 0f),
            new Vector2(-discEdge, BottomRowCentre));
        AddSoftGlow(panel, "DayNightButton", new Vector2(1f, 0f),
            new Vector2(-discEdge, BottomRowCentre), 210f, 0.09f);

        // The splash controller floats the logo around wherever it found it, so it has to
        // be told about the new layout.
        SplashScreenController splash = panel.GetComponent<SplashScreenController>();
        if (splash != null) splash.RebaselineLogo();
    }

    // The single call to action: a wide glass pill with the theme's warm accent, a soft
    // halo behind it and a breathing rhythm slow enough to invite rather than nag.
    void StyleLaunchCall(Transform panel)
    {
        TextMeshProUGUI tap = FindTmp(panel, "TAP TO START");
        if (tap == null) return;

        int siblingIndex = tap.transform.GetSiblingIndex();

        // The halo stays the thruster orange in every world. One call to action,
        // one colour: the palette inherits the world, the CTA never does.
        Color halo = UIDesign.Cta;
        halo.a = 0.11f;
        launchGlow = EnsurePlate(panel, "LaunchGlow", siblingIndex, new Vector2(0.5f, 0f),
            new Vector2(0f, 196f), new Vector2(700f, 280f), halo, UIGlass.Glow, null);
        UIMotion.Attach(launchGlow.gameObject, UIMotion.Mode.Pulse, 1f, 3.9f);

        Image plate = EnsurePlate(panel, "LaunchPlate", siblingIndex + 1, new Vector2(0.5f, 0f),
            new Vector2(0f, 196f), new Vector2(474f, UIDesign.ButtonHeightMajor),
            Color.white, null, null);
        UIKit.MakeGlass(plate.gameObject, UIDesign.RadiusPill, UITinted.Role.GlassDeep);
        // The one surface in the UI whose rim is the CTA colour rather than the
        // world's: it is how the eye finds the button before reading the label.
        UIKit.OverrideRim(plate.gameObject,
            new Color(UIDesign.Cta.r, UIDesign.Cta.g, UIDesign.Cta.b, 0.70f));
        UIMotion.Attach(plate.gameObject, UIMotion.Mode.Breathe, 1f, 3.9f);

        tap.text = "TAP TO LAUNCH";
        UIKit.StyleText(tap, UIDesign.TypeHeading, UIDesign.TrackButton, UIDesign.CtaText,
            FontStyles.Bold);
        SetRect(tap.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 196f), new Vector2(440f, 80f));
        tap.transform.SetAsLastSibling();
    }

    // Best score reads as a small trophy chip instead of a line of text floating in space.
    void StyleBestScore(Transform panel)
    {
        TextMeshProUGUI best = FindTmp(panel, "BestScoreText");
        if (best == null) return;

        int value = PlayerPrefs.GetInt("HighScore", 0);
        best.text = value > 0 ? "BEST  " + value : "FIRST FLIGHT";
        UIKit.StyleText(best, UIDesign.TypeLabel, UIDesign.TrackLabel, UIDesign.TextSub,
            FontStyles.Bold);
        best.gameObject.SetActive(true);

        // Its scene parent is the tap label, which now moves with the launch pill; a chip
        // needs its own slot on the panel to stay put.
        if (best.transform.parent != panel) best.transform.SetParent(panel, false);
        // Offset right of centre by half the star's width, so the icon and the
        // text together are optically centred rather than the text alone.
        SetRect(best.rectTransform, new Vector2(0.5f, 0f), new Vector2(19f, BestChipCentre),
            new Vector2(300f, 52f));

        Image chip = EnsurePlate(panel, "BestScorePlate", best.transform.GetSiblingIndex(),
            new Vector2(0.5f, 0f), new Vector2(0f, BestChipCentre), new Vector2(332f, 66f),
            Color.white, null, null);
        UIKit.MakeGlass(chip.gameObject, UIDesign.RadiusChip, UITinted.Role.Glass, 0.82f, false);

        // The best score earns the gold star: it is the only place in the menu
        // that reports an achievement, so it is the only place gold appears.
        Image star = UIKit.EnsureChildImage(chip.gameObject, "Star", UIIcons.Get(UIIcons.Star),
            Image.Type.Simple, Vector2.zero, UITinted.Role.Glass, 1f, 1, UIDesign.Gold);
        star.preserveAspect = true;
        RectTransform starRect = star.rectTransform;
        starRect.anchorMin = starRect.anchorMax = new Vector2(0f, 0.5f);
        starRect.pivot = new Vector2(0f, 0.5f);
        starRect.anchoredPosition = new Vector2(26f, 0f);
        starRect.sizeDelta = new Vector2(34f, 34f);

        best.transform.SetAsLastSibling();
    }

    void StyleControlHint(Transform panel)
    {
        TextMeshProUGUI hint = FindTmp(panel, "ControlHint");
        if (hint == null)
        {
            hint = UIStyleKit.MakeLabel(
                panel, string.Empty, 17f, Color.white, new Vector2(0f, 112f), new Vector2(560f, 34f),
                FontStyles.Bold, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            hint.gameObject.name = "ControlHint";
        }

        // The same separator the tagline under the logo uses, so the menu's two
        // small caption lines read as one voice instead of two conventions.
        hint.text = "TAP  LAUNCH  •  HOLD  REVERSE";
        Color muted = UIDesign.TextMuted;
        muted.a = 0.68f;
        UIKit.StyleText(hint, UIDesign.TypeCaption, UIDesign.TrackCaption, muted, FontStyles.Bold);
        // Was at 112, where a 560-wide centred line ran straight through the shop
        // pill. It belongs to the launch call, so it now sits in the gap between
        // that pill and the best-score chip instead of in the bottom row.
        SetRect(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 292f), new Vector2(480f, 34f));
    }

    void StyleShopButton(Transform panel)
    {
        Transform shop = FindDeep(panel, "ShopButton");
        if (shop == null) return;

        RectTransform rect = shop.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            // Bottom-left corner on the shared margin, which puts its centre on
            // BottomRowCentre — the same line the day/night disc sits on.
            rect.anchoredPosition = new Vector2(UIDesign.ScreenMargin, UIDesign.ScreenMargin);
            rect.sizeDelta = new Vector2(250f, UIDesign.ButtonHeightPill);
        }

        UIKit.StylePill(shop, "SHOP", UIDesign.RadiusPill, UITinted.Role.Glass, UIIcons.Shop);
        UIMotion.Attach(shop.gameObject, UIMotion.Mode.Hover, 0.85f, 5.2f);
    }

    // Sound, help and day/night share one silhouette: a glass disc with a lit rim
    // and a baked icon at one glyph size. The button's own Image becomes the
    // disc, so the sprite references the managers hold are moved to the glyph.
    void StyleIconButton(Transform panel, string name, Vector2 anchor, Vector2 position)
    {
        Transform button = FindDeep(panel, name);
        if (button == null) return;

        RectTransform rect = button.GetComponent<RectTransform>();
        if (rect != null)
            SetRect(rect, anchor, position, Vector2.one * UIDesign.IconButtonSize);

        // Help is the one disc whose icon never changes, so it can be set here.
        // The other two are driven by their managers.
        string icon = name == "HelpButton" ? UIIcons.Help : null;
        UIKit.StyleIconButton(button, icon);

        Image glyph = button.Find("Glyph").GetComponent<Image>();
        RouteIconTarget(button, glyph);

        // A text "?" was standing in for the icon that now exists.
        TextMeshProUGUI legacyGlyph = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (legacyGlyph != null) legacyGlyph.gameObject.SetActive(false);
    }

    // A breathing halo behind a control, in the world's accent. Deliberately far
    // fainter than the launch call's: this one says "lit", not "press me".
    void AddSoftGlow(Transform panel, string hostName, Vector2 anchor, Vector2 position,
        float size, float alpha)
    {
        Transform host = FindDeep(panel, hostName);
        if (host == null) return;

        Color halo = UIDesign.Accent;
        halo.a = alpha;
        Image glow = EnsurePlate(panel, hostName + "Glow", host.GetSiblingIndex(), anchor,
            position, new Vector2(size, size), halo, UIGlass.Glow, null);
        // Accent role, so the halo travels with the world like the disc it backs.
        UITinted.Attach(glow.gameObject, UITinted.Role.Accent, alpha);
        UIMotion.Attach(glow.gameObject, UIMotion.Mode.Pulse, 1f, 6.2f);
    }

    // A non-interactive backing shape placed just behind a control.
    Image EnsurePlate(Transform panel, string name, int siblingIndex, Vector2 anchor, Vector2 position,
        Vector2 size, Color color, Sprite sprite, Color? rim)
    {
        Transform existing = FindDeep(panel, name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name);
        if (existing == null) go.transform.SetParent(panel, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        if (rect == null) rect = go.AddComponent<RectTransform>();
        SetRect(rect, anchor, position, size);

        Image image = go.GetComponent<Image>();
        if (image == null) image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = sprite != null && sprite.border.sqrMagnitude > 0f
            ? Image.Type.Sliced
            : Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;

        if (rim.HasValue) AddOutline(go, rim.Value, 2f);
        go.transform.SetSiblingIndex(Mathf.Max(0, siblingIndex));
        return image;
    }

    void EnsureAccentRule(Transform panel, string name, Vector2 position, Vector2 size)
    {
        Transform existing = FindDeep(panel, name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name);
        if (existing == null) go.transform.SetParent(panel, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        if (rect == null) rect = go.AddComponent<RectTransform>();
        SetRect(rect, new Vector2(0.5f, 1f), position, size);

        Image image = go.GetComponent<Image>();
        if (image == null) image = go.AddComponent<Image>();
        image.sprite = UIGlass.Panel(1f);
        image.type = Image.Type.Sliced;
        image.raycastTarget = false;
        UITinted.Attach(go, UITinted.Role.Accent, 0.42f);
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

        // Was a bespoke sine in Update(); it now drifts on the same clock and
        // the same amplitudes as every other floating element.
        UIMotion.Attach(go, UIMotion.Mode.Hover, 1.6f, 5.4f);
    }

    void StyleHud()
    {
        TextMeshProUGUI score = FindTmp(canvas.transform, "ScoreText");
        if (score != null)
        {
            score.outlineWidth = 0f;
            UIKit.StyleDisplay(score, UIDesign.TypeTitle, UIDesign.TrackTitle, UIDesign.TextMain);
            SetRect(score.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(180f, 66f));
            CreateScorePlate(score.transform.parent, score.transform.GetSiblingIndex());
            scoreDisplay = score.gameObject;
        }

        // The in-run controls join the same disc family as the menu's, and now
        // the same gutter: the scene had them at 31 and 56 pixels from the edge,
        // which is close enough to 40 to read as a mistake rather than a choice.
        const float hudDisc = UIDesign.IconButtonSize * 0.78f;
        const float hudEdge = UIDesign.ScreenMargin + hudDisc * 0.5f;
        StyleHudDisc("PauseButton", UIIcons.Pause, new Vector2(hudEdge, hudEdge));
        StyleHudDisc("SoundButton2", null, new Vector2(hudEdge + hudDisc + 20f, hudEdge));
    }

    void StyleHudDisc(string name, string icon, Vector2 position)
    {
        Transform button = FindDeep(canvas.transform, name);
        if (button == null) return;

        // Slightly smaller than the menu's discs: in-run chrome should sit back.
        const float hudDisc = UIDesign.IconButtonSize * 0.78f;
        RectTransform rect = button.GetComponent<RectTransform>();
        if (rect != null) SetRect(rect, new Vector2(0f, 0f), position, Vector2.one * hudDisc);

        UIKit.StyleIconButton(button, icon, hudDisc);

        Image glyph = button.Find("Glyph").GetComponent<Image>();
        RouteIconTarget(button, glyph);
    }

    void CreateScorePlate(Transform parent, int siblingIndex)
    {
        Transform existing = FindDeep(parent, "ScorePlate");
        if (existing != null)
        {
            scorePlate = existing.gameObject;
            return;
        }

        GameObject go = new GameObject("ScorePlate");
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        SetRect(rect, new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(198f, 104f));

        // Lower alpha than the menu's chrome: this one sits over live gameplay
        // and must never compete with the rocket.
        UIKit.MakeGlass(go, UIDesign.RadiusChip, UITinted.Role.Glass, 0.72f, false);

        TextMeshProUGUI caption = UIStyleKit.MakeLabel(go.transform, "ORBIT", UIDesign.TypeMicro,
            UIDesign.Accent, new Vector2(0f, 32f), new Vector2(160f, 22f), FontStyles.Bold);
        UIKit.StyleText(caption, UIDesign.TypeMicro, UIDesign.TrackMicro, UIDesign.Accent,
            FontStyles.Bold);
        UITinted.Attach(caption.gameObject, UITinted.Role.Accent);

        go.transform.SetSiblingIndex(Mathf.Max(0, siblingIndex));
        scorePlate = go;
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

        UIKit.MakeGlass(panel.gameObject, UIDesign.RadiusCard, UITinted.Role.GlassDeep, 1f, true, true);
        CreatePanelDim(panel, "GameOverDim", UIDesign.Scrim);

        TextMeshProUGUI title = FindTmp(panel, "GameOverText");
        if (title != null)
        {
            title.text = "GAME OVER";
            UIKit.StyleDisplay(title, UIDesign.TypeTitle, UIDesign.TrackTitle, UIDesign.Danger);
            SetLocalRect(title.rectTransform, new Vector2(0f, 420f), new Vector2(660f, 90f));
        }

        TextMeshProUGUI eyebrow = EnsurePanelLabel(panel, "FlightReport", "FLIGHT REPORT",
            new Vector2(0f, 485f), UIDesign.TypeCaption, UIDesign.Accent, UIDesign.TrackCaption);
        if (eyebrow != null) UITinted.Attach(eyebrow.gameObject, UITinted.Role.Accent);

        EnsureDivider(panel, "ReportDivider", new Vector2(0f, 360f), new Vector2(610f, 2f));

        TextMeshProUGUI score = FindTmp(panel, "ScoreResultText");
        if (score != null)
        {
            UIKit.StyleDisplay(score, UIDesign.TypeDisplay, UIDesign.TrackDisplay, UIDesign.TextMain);
            SetLocalRect(score.rectTransform, new Vector2(0f, 280f), new Vector2(650f, 100f));
        }

        TextMeshProUGUI best = FindTmp(panel, "HighScoreText");
        if (best != null)
        {
            UIKit.StyleText(best, UIDesign.TypeHeading, UIDesign.TrackButton, UIDesign.Gold,
                FontStyles.Bold);
            SetLocalRect(best.rectTransform, new Vector2(0f, 195f), new Vector2(620f, 56f));
        }

        gameOverCoinsText = EnsurePanelLabel(panel, "RunCoinsEarnedText", "RUN COINS  +0",
            new Vector2(0f, 120f), UIDesign.TypeBody, UIDesign.Gold, UIDesign.TrackLabel);
        if (gameOverCoinsText != null)
            SetLocalRect(gameOverCoinsText.rectTransform, new Vector2(0f, 120f), new Vector2(620f, 58f));

        // One primary, two quiet: the restart is the only surface here carrying
        // the CTA, which is what makes it the obvious next tap.
        StyleMajorButton(panel, "RestartButton", "FLY AGAIN", new Vector2(0f, -15f), true,
            new Vector2(600f, UIDesign.ButtonHeightMajor));
        StyleMajorButton(panel, "ShareButton", "SHARE FLIGHT", new Vector2(0f, -140f), false,
            new Vector2(600f, UIDesign.ButtonHeightPill));
        StyleMajorButton(panel, "MainMenuButton_GameOver", "MAIN MENU", new Vector2(0f, -258f), false,
            new Vector2(600f, UIDesign.ButtonHeightPill));
    }

    void StylePause()
    {
        Transform panel = FindDeep(canvas.transform, "PausePanel");
        if (panel == null) return;

        Image overlay = panel.GetComponent<Image>();
        if (overlay != null)
        {
            overlay.color = UIDesign.Scrim;
            overlay.raycastTarget = true;
            UITinted.Attach(panel.gameObject, UITinted.Role.Scrim);
        }

        EnsurePauseCard(panel);

        TextMeshProUGUI title = EnsurePanelLabel(panel, "TitleText", "FLIGHT PAUSED",
            new Vector2(0f, 260f), UIDesign.TypeTitle, UIDesign.TextMain, UIDesign.TrackTitle);
        if (title != null)
        {
            title.text = "FLIGHT PAUSED";
            UIKit.StyleDisplay(title, UIDesign.TypeTitle, UIDesign.TrackTitle, UIDesign.TextMain);
            SetLocalRect(title.rectTransform, new Vector2(0f, 260f), new Vector2(660f, 82f));
        }

        TextMeshProUGUI description = EnsurePanelLabel(panel, "PauseDescription",
            "TAKE A BREATH  •  YOUR ORBIT IS WAITING", new Vector2(0f, 185f), UIDesign.TypeCaption,
            UIDesign.TextMuted, UIDesign.TrackCaption);
        if (description != null)
        {
            // The last sentence-case line in the game. Every other small label is
            // tracked caps, and one exception is all it takes to look borrowed.
            description.text = "TAKE A BREATH  •  YOUR ORBIT IS WAITING";
            UIKit.StyleText(description, UIDesign.TypeCaption, UIDesign.TrackCaption,
                UIDesign.TextMuted, FontStyles.Bold);
            SetLocalRect(description.rectTransform, new Vector2(0f, 185f), new Vector2(640f, 52f));
        }

        if (FindDeep(panel, "RestartButton") == null)
        {
            UIStyleKit.MakeButtonAnchored(panel, "RestartButton", "RESTART RUN",
                new Vector2(0f, -65f), new Vector2(600f, UIDesign.ButtonHeightPill),
                UIDesign.Glass, RestartFromPause, UIDesign.TypeButton);
        }

        StyleMajorButton(panel, "ResumeButton", "RESUME", new Vector2(0f, 70f), true,
            new Vector2(600f, UIDesign.ButtonHeightMajor));
        StyleMajorButton(panel, "RestartButton", "RESTART RUN", new Vector2(0f, -55f), false,
            new Vector2(600f, UIDesign.ButtonHeightPill));
        StyleMajorButton(panel, "MainMenuButton", "MAIN MENU", new Vector2(0f, -175f), false,
            new Vector2(600f, UIDesign.ButtonHeightPill));
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
            overlay.color = UIDesign.Scrim;
            overlay.raycastTarget = true;
            UITinted.Attach(panel.gameObject, UITinted.Role.Scrim);
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
            // No shadow: the card fills the screen, so there is nothing behind
            // it for a shadow to fall on.
            UIKit.MakeGlass(card.gameObject, UIDesign.RadiusCard, UITinted.Role.GlassDeep, 1f, false, true);
        }

        TextMeshProUGUI title = FindTmp(panel, "TitleText");
        if (title != null)
        {
            title.text = "ORBIT TRAINING";
            UIKit.StyleDisplay(title, UIDesign.TypeTitle, UIDesign.TrackTitle, UIDesign.Accent);
            UITinted.Attach(title.gameObject, UITinted.Role.Accent);
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
            UIStyleKit.ApplyRuntimeFont(content, panel);
            content.fontSize = UIDesign.TypeBody;
            content.enableAutoSizing = false;
            // Body copy is the one place that wraps and scrolls, so it opts out
            // of StyleText's no-wrap, ellipsised button-label defaults.
            content.lineSpacing = 12f;
            content.paragraphSpacing = 18f;
            content.characterSpacing = 0f;
            content.color = UIDesign.TextMain;
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

        // Anchored to the bottom of the card rather than its centre, so the
        // button holds its distance from the edge on a tall phone.
        Transform gotIt = FindDeep(panel, "GotItButton");
        RectTransform gotItRect = gotIt != null ? gotIt.GetComponent<RectTransform>() : null;
        if (gotItRect != null)
        {
            gotItRect.anchorMin = gotItRect.anchorMax = new Vector2(0.5f, 0f);
            gotItRect.pivot = new Vector2(0.5f, 0.5f);
            gotItRect.anchoredPosition = new Vector2(0f, 96f);
            gotItRect.sizeDelta = new Vector2(620f, UIDesign.ButtonHeightMajor);
        }

        StyleMajorButton(panel, "GotItButton", "READY TO FLY", null, true, null);
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

    // Every panel button in the game goes through here, so they share one
    // radius, one rim, one shadow, one label treatment. `primary` decides only
    // whether it wears the call-to-action colour — never its shape.
    // Pass a null position when the caller has already placed the button: the
    // shadow sibling is derived from the rect, so the rect has to be final
    // before styling runs.
    void StyleMajorButton(Transform root, string name, string label, Vector2? position,
        bool primary, Vector2? size)
    {
        Transform target = FindDeep(root, name);
        if (target == null) return;

        RectTransform rect = target.GetComponent<RectTransform>();
        if (rect != null && position.HasValue && size.HasValue)
            SetLocalRect(rect, position.Value, size.Value);

        UIKit.StylePill(target, label, UIDesign.RadiusPill, UITinted.Role.Glass, null,
            UIDesign.TypeButton, primary ? UIDesign.CtaText : UIDesign.TextMain);

        if (primary)
        {
            UIKit.OverrideRim(target.gameObject,
                new Color(UIDesign.Cta.r, UIDesign.Cta.g, UIDesign.Cta.b, 0.72f));
            UIMotion.Attach(target.gameObject, UIMotion.Mode.Breathe, 0.8f, 4.1f);
        }

        Text legacy = target.GetComponentInChildren<Text>(true);
        if (legacy != null)
        {
            legacy.text = label;
            legacy.fontSize = 28;
            legacy.fontStyle = FontStyle.Bold;
            legacy.color = UIDesign.TextMain;
        }
    }

    void EnsurePauseCard(Transform panel)
    {
        Transform existing = FindDeep(panel, "PauseCard");
        if (existing != null) return;

        GameObject go = new GameObject("PauseCard");
        go.transform.SetParent(panel, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        SetLocalRect(rect, Vector2.zero, new Vector2(760f, 820f));

        UIKit.MakeGlass(go, UIDesign.RadiusCard, UITinted.Role.GlassDeep);
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
        UIKit.StyleText(label, size, spacing, color, FontStyles.Bold);
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
        image.sprite = UIGlass.Panel(1f);
        image.type = Image.Type.Sliced;
        image.raycastTarget = false;
        // A hairline at rim strength: the same edge language as every card.
        UITinted.Attach(go, UITinted.Role.Rim, 0.9f);
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
    private float target = 1f;

    /// The current press scale. UIMotion multiplies its breathing into this so
    /// a button can be pressed while it breathes; without a UIMotion this
    /// component writes the transform itself.
    public float Press { get; private set; } = 1f;

    private UIMotion motion;

    void Awake() => motion = GetComponent<UIMotion>();

    /// Called by UIMotion when it is added after this component: Awake has
    /// already run by then and would have cached a null.
    public void BindMotion(UIMotion value) => motion = value;

    void OnEnable()
    {
        Press = 1f;
        target = 1f;
        if (motion == null) transform.localScale = Vector3.one;
    }

    void Update()
    {
        Press = Mathf.Lerp(Press, target, 22f * Time.unscaledDeltaTime);
        if (motion == null) transform.localScale = Vector3.one * Press;
    }

    public void OnPointerDown(PointerEventData eventData) => target = 0.95f;
    public void OnPointerUp(PointerEventData eventData) => target = 1f;
    public void OnPointerExit(PointerEventData eventData) => target = 1f;
}
