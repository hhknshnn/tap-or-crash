using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Existing scene UI is kept intact; this layer applies a consistent mobile presentation at runtime.
//
// Runs late enough that every runtime-built control exists (the shop pill, the fuel gauges,
// the combo label) and early enough to still be inside the first frame's Start phase — so
// the menu is never rendered in its unstyled scene state. MainMenuShowcase runs after this
// and measures the styled UI, so its order must stay higher than this one.
[DefaultExecutionOrder(100)]
public sealed class VisualPolishController : MonoBehaviour
{
    private static VisualPolishController instance;

    // The shop pill and the day/night disc are the menu's bottom row. They are
    // different heights, so only a shared centre line makes them one row.
    private const float BottomRowCentre = UIDesign.ScreenMargin + UIDesign.ButtonHeightPill * 0.5f;

    // Shop pins its currency chip and header controls to one fixed identity
    // regardless of which world the player last reached (KillShopTint disables
    // UITinted there). ShopButton/SoundButton/HelpButton/DayNightButton now match
    // that same fixed identity instead of drifting hue with world progress, so the
    // Start Panel's controls read as the same UI family as the Shop in every world.
    // Built from Crystal's registered accent (CrystalPlanetAmbience.AuraTint) —
    // the redesign's canonical "purple glass" source per UIDesign.ApprovedStartupWorld
    // — passed as a raw colour rather than looked up by world name, so this never
    // depends on PlanetAmbience theme registration having already run.
    private static readonly UIDesign.Palette ShopIdentityPalette =
        UIDesign.PaletteForAccent(new Color(0.62f, 0.42f, 1f, 1f));

    // The launch lockup, bottom up: pill at 196, its caption, then the best-score
    // chip. Lifted from 344 to open an even gap on both sides of the caption.
    private const float BestChipCentre = 376f;

    // Sound/Help/Theme's shared baked shell — pre-rendered art derived from the
    // Shop header's CloseButtonShell family, minus its baked X glyph. Replaces
    // the procedural glass disc via UIKit.ApplyBakedShell.
    private const string MainMenuIconShellPath = "Menu/UI/Buttons/MainMenuIconButtonShell";

    // Alpha-bounds crop of Resources/Icons/icon_shop.png — same pixels, no
    // asymmetric padding — see StyleShopButton.
    private const string ShopIconCroppedPath = "Icons/Cropped/icon_shop_Cropped";

    private Canvas canvas;
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

    void Start()
    {
        canvas = UIRootCanvas.Resolve();
        if (canvas == null) return;

        UIDesign.EnsureInitialised();
        ConfigureCanvas();
        AdoptIconFamily();
        StyleStartScreen();
        StyleHud();
        StyleGameOver();
        StylePause();
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

        // The title is not UI. The flat LogoText / LogoRule / SubtitleText lockup and the
        // OrbitEmblem that sat under it were the old 2D menu; MenuBrandEmblem replaces all
        // four with one lit object on the showcase stage.
        StyleLaunchCall(panel);
        StyleBestScore(panel);
        StyleControlHint(panel);
        StyleShopButton(panel);

        // Sound and Help share a compact top-right row, opposite the Coin readout.
        const float discEdge = UIDesign.ScreenMargin + UIDesign.IconButtonSize * 0.5f;
        const float discGap = 20f;
        const float discRow = -(UIDesign.ScreenMargin + UIDesign.IconButtonSize * 0.5f);

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
    }

    // The single call to action: a baked shell (glow + pill + rocket icon are all
    // pre-rendered art, not procedural glass), with a breathing rhythm slow enough
    // to invite rather than nag.
    //
    // Unlike every other glass surface this method styles, LaunchGlow/LaunchPlate
    // are placed as-is: no MakeGlass tint, no procedural rim, no shadow link. Those
    // calls repaint over baked art on every scene load, which is exactly what used
    // to happen here before the shell became real art instead of a glass primitive.
    void StyleLaunchCall(Transform panel)
    {
        // TapToLaunch's font is scene-authored (Montserrat ExtraBold), unlike every
        // other label FindTmp resolves. Looked up via FindDeep instead of FindTmp so
        // UIStyleKit.ApplyRuntimeFont never stamps the shared runtime font over it.
        Transform tapTransform = FindDeep(panel, "TapToLaunch");
        if (tapTransform == null) tapTransform = FindDeep(panel, "TAP TO START");
        if (tapTransform == null) return;
        TextMeshProUGUI tap = tapTransform.GetComponent<TextMeshProUGUI>();
        if (tap == null) return;

        int siblingIndex = tap.transform.GetSiblingIndex();

        Sprite launchGlowArt = Resources.Load<Sprite>("MenuBaked/PrimaryLaunch/ButtonGlow");
        Sprite launchShellArt = Resources.Load<Sprite>("MenuBaked/PrimaryLaunch/ButtonShell_Normal");

        // The shell, its glow and the label's font/rect are scene-authored art direction —
        // SampleScene is their source of truth. Only build fallback values here when the
        // serialized object is genuinely missing (a fresh scene, or one predating this art),
        // so Edit Mode and Play Mode never disagree on how the launch call looks.
        Transform existingGlow = FindDeep(panel, "LaunchGlow");
        if (existingGlow != null)
        {
            launchGlow = existingGlow.GetComponent<Image>();
        }
        else
        {
            launchGlow = EnsurePlate(panel, "LaunchGlow", siblingIndex, new Vector2(0.5f, 0f),
                new Vector2(0f, 208f), new Vector2(405f, 273f), new Color(1f, 1f, 1f, 0.4f), launchGlowArt, null);
        }
        UIMotion.Attach(launchGlow.gameObject, UIMotion.Mode.Pulse, 1f, 3.9f);

        Transform existingPlate = FindDeep(panel, "LaunchPlate");
        Image plate = existingPlate != null
            ? existingPlate.GetComponent<Image>()
            : EnsurePlate(panel, "LaunchPlate", siblingIndex + 1, new Vector2(0.5f, 0f),
                new Vector2(0f, 208f), new Vector2(378f, 252f), Color.white, launchShellArt, null);
        UIMotion.Attach(plate.gameObject, UIMotion.Mode.Breathe, 1f, 3.9f);

        tap.text = "TAP TO LAUNCH";
        tap.transform.SetAsLastSibling();

        // The primary CTA's type treatment — Montserrat ExtraBold, cream face, dark
        // brown-orange outline, RectTransform and size range — is scene-authored on
        // TapToLaunch. Only fall back to code-built defaults when that authoring is
        // missing (no font assigned), so a correctly configured label is left alone.
        if (tap.font == null)
        {
            UIKit.StyleText(tap, UIDesign.TypeHeading, UIDesign.TrackButton, UIDesign.CtaText,
                FontStyles.Bold);
            // Recentred off-axis to leave room for RocketIcon on the shell's left side, so the
            // icon+label group reads as one balanced unit instead of the label spanning edge-to-edge.
            SetRect(tap.rectTransform, new Vector2(0.5f, 0f), new Vector2(10f, 208f), new Vector2(180f, 66f));
            tap.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);

            TMP_FontAsset launchFont = Resources.Load<TMP_FontAsset>("Fonts/Montserrat-ExtraBold SDF");
            if (launchFont != null)
            {
                tap.font = launchFont;
                tap.fontSharedMaterial = launchFont.material;
                tap.fontMaterial = launchFont.material;
            }
            tap.fontSizeMin = 12f;
            tap.fontSizeMax = 23f;
            tap.characterSpacing = 0f;
            tap.alignment = TextAlignmentOptions.Center;
            tap.margin = new Vector4(10f, 2f, 8f, 2f);
            tap.color = new Color(0.988235f, 0.972549f, 0.905882f);
            tap.outlineColor = new Color(0.32f, 0.14f, 0.05f);
            tap.outlineWidth = 0.18f;
        }
    }

    // Best score reads as a small trophy chip instead of a line of text floating in space.
    void StyleBestScore(Transform panel)
    {
        Transform bestRoot = FindDeep(panel, "BestScoreText");
        if (bestRoot == null) return;

        int value = PlayerPrefs.GetInt("HighScore", 0);

        // Its scene parent is the tap label, which now moves with the launch pill; a chip
        // needs its own slot on the panel to stay put.
        if (bestRoot.parent != panel) bestRoot.SetParent(panel, false);
        RectTransform bestRect = bestRoot.GetComponent<RectTransform>();
        if (bestRect == null) bestRect = bestRoot.gameObject.AddComponent<RectTransform>();
        SetRect(bestRect, new Vector2(0.5f, 0f), new Vector2(0f, BestChipCentre), new Vector2(260f, 78f));
        bestRoot.gameObject.SetActive(true);

        // The old single two-line label ("102\nBEST" in one TMP block), and the
        // VerticalLayoutGroup/ContentSizeFitter pass that replaced it, are both
        // retired: box/layout-group centring aligned the RECTS correctly, but
        // TMP's line-height metrics still left the rendered GLYPHS sitting
        // off-centre within them (font ascender/descender space isn't the same
        // as visible ink). Position below is computed from measured glyph ink
        // bounds (ForceMeshUpdate + textInfo.characterInfo) instead — the only
        // thing that actually matches what's drawn on screen.
        TextMeshProUGUI legacyText = bestRoot.GetComponent<TextMeshProUGUI>();
        if (legacyText != null) DestroyComponent(legacyText);
        VerticalLayoutGroup legacyLayout = bestRoot.GetComponent<VerticalLayoutGroup>();
        if (legacyLayout != null) DestroyComponent(legacyLayout);
        ContentSizeFitter legacyFitter = bestRoot.GetComponent<ContentSizeFitter>();
        if (legacyFitter != null) DestroyComponent(legacyFitter);

        const float scoreValueSize = 29f;
        const float bestLabelSize = 14f;
        const float bestTypographyGap = 3f;

        TextMeshProUGUI scoreValue = EnsureLabel(bestRoot, "ScoreValue", 0);
        scoreValue.text = value.ToString();
        UIKit.StyleText(scoreValue, scoreValueSize, UIDesign.TrackLabel, UIDesign.TextMain, FontStyles.Bold);
        SetLocalRect(scoreValue.rectTransform, Vector2.zero, new Vector2(260f, scoreValueSize * 1.6f));

        TextMeshProUGUI bestLabel = EnsureLabel(bestRoot, "BestLabel", 1);
        bestLabel.text = "BEST";
        UIKit.StyleText(bestLabel, bestLabelSize, UIDesign.TrackLabel, UIDesign.TextSub, FontStyles.Bold);
        SetLocalRect(bestLabel.rectTransform, Vector2.zero, new Vector2(260f, bestLabelSize * 1.6f));

        PositionBestTypography(scoreValue, bestLabel, bestTypographyGap);

        // Real Shop art (BalanceChipBaseFlat_Cropped) at its own native aspect —
        // Simple + preserveAspect, not stretched/sliced into a taller box, which
        // is what read as an exaggerated capsule.
        Transform plateTransform = FindDeep(panel, "BestScorePlate");
        GameObject plateGo = plateTransform != null ? plateTransform.gameObject
            : new GameObject("BestScorePlate", typeof(RectTransform));
        if (plateTransform == null) plateGo.transform.SetParent(panel, false);
        plateGo.transform.SetSiblingIndex(bestRoot.GetSiblingIndex());

        RectTransform plateRoot = plateGo.GetComponent<RectTransform>();
        SetRect(plateRoot, new Vector2(0.5f, 0f), new Vector2(0f, BestChipCentre), new Vector2(286f, 88f));

        // A prior pass built this plate with MakeGlass — a procedural fill on
        // the root's own Image plus a UITinted (world-palette colour) and an
        // accent-tinted "Rim" sibling. Switching to the Background child below
        // never removed them, so they kept rendering as a stray rectangular
        // frame behind/around the real art. The root becomes a plain
        // invisible layout anchor; only Background is visible.
        UITinted stalePlateTint = plateGo.GetComponent<UITinted>();
        if (stalePlateTint != null)
        {
            stalePlateTint.enabled = false;
            DestroyComponent(stalePlateTint);
        }
        Image stalePlateImage = plateGo.GetComponent<Image>();
        if (stalePlateImage != null) DestroyComponent(stalePlateImage);
        Transform stalePlateRim = plateGo.transform.Find("Rim");
        if (stalePlateRim != null) DestroyGameObject(stalePlateRim.gameObject);

        Sprite bestArt = ShipSkinManager.LoadShopBalanceChipSprite();
        UIKit.ApplyNativeAspectBackground(plateGo, bestArt, 286f);

        bestRoot.SetAsLastSibling();
    }

    static TextMeshProUGUI EnsureLabel(Transform parent, string name, int siblingIndex)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform));
        if (existing == null) go.transform.SetParent(parent, false);

        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        if (text == null) text = go.AddComponent<TextMeshProUGUI>();

        go.transform.SetSiblingIndex(siblingIndex);
        return text;
    }

    // Stacks two labels using their measured glyph ink bounds rather than
    // font line-height metrics, so the combined VISIBLE block — not just the
    // two RectTransforms — is what ends up centred at the host's local origin.
    static void PositionBestTypography(TextMeshProUGUI top, TextMeshProUGUI bottom, float gap)
    {
        Rect topBounds = GetTightTextBounds(top);
        Rect bottomBounds = GetTightTextBounds(bottom);

        float totalHeight = topBounds.height + gap + bottomBounds.height;
        float halfTotal = totalHeight * 0.5f;

        float topY = halfTotal - topBounds.yMax;
        float bottomY = (topY + topBounds.yMin) - gap - bottomBounds.yMax;

        top.rectTransform.anchoredPosition = new Vector2(-topBounds.center.x, topY);
        bottom.rectTransform.anchoredPosition = new Vector2(-bottomBounds.center.x, bottomY);
    }

    // The actual rendered glyph quads (per TMP_CharacterInfo), not the font's
    // ascender/descender line metrics — "102" has no descenders and "BEST" is
    // all caps, so line-height bounds include dead space neither line uses,
    // which is exactly what made the box-centred version look off-centre.
    static Rect GetTightTextBounds(TextMeshProUGUI tmp)
    {
        tmp.ForceMeshUpdate(true);
        TMP_TextInfo info = tmp.textInfo;
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo ch = info.characterInfo[i];
            if (!ch.isVisible) continue;
            minX = Mathf.Min(minX, ch.bottomLeft.x, ch.topLeft.x);
            maxX = Mathf.Max(maxX, ch.bottomRight.x, ch.topRight.x);
            minY = Mathf.Min(minY, ch.bottomLeft.y, ch.bottomRight.y);
            maxY = Mathf.Max(maxY, ch.topLeft.y, ch.topRight.y);
        }
        if (maxX < minX) return new Rect(0f, 0f, 0f, 0f);
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    // HorizontalLayoutGroup arranges the icon (alpha-cropped, so its rect IS
    // its visible bounds) and the label's own rect box side by side — but a
    // label's rect box isn't its tight glyph bounds, so the combined group
    // can still land a few px off the background's true centre. This
    // measures the actual combined visible bounds (icon rect + label's tight
    // glyph bounds) and nudges Content by the measured delta — not a guessed
    // constant, a correction computed from what's actually on screen.
    static void CenterShopContentOnVisibleBounds(Transform shop)
    {
        RectTransform content = shop.Find("Content") as RectTransform;
        Transform iconTransform = content != null ? content.Find("LeadingIcon") : null;
        TextMeshProUGUI label = content != null ? content.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        RectTransform background = shop.Find("Background") as RectTransform;
        if (content == null || iconTransform == null || label == null || background == null) return;

        LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        RectTransform iconRect = (RectTransform)iconTransform;
        Vector3[] iconCorners = new Vector3[4];
        iconRect.GetWorldCorners(iconCorners);

        Rect labelTight = GetTightTextBounds(label);
        Vector3 labelWorldMin = label.rectTransform.TransformPoint(new Vector3(labelTight.xMin, labelTight.yMin, 0f));
        Vector3 labelWorldMax = label.rectTransform.TransformPoint(new Vector3(labelTight.xMax, labelTight.yMax, 0f));

        float groupLeft = Mathf.Min(iconCorners[0].x, labelWorldMin.x);
        float groupRight = Mathf.Max(iconCorners[2].x, labelWorldMax.x);
        float groupCenterX = (groupLeft + groupRight) * 0.5f;

        Vector3[] bgCorners = new Vector3[4];
        background.GetWorldCorners(bgCorners);
        float bgCenterX = (bgCorners[0].x + bgCorners[2].x) * 0.5f;

        float deltaWorldX = groupCenterX - bgCenterX;
        float scale = content.lossyScale.x;
        if (Mathf.Approximately(scale, 0f)) scale = 1f;
        content.anchoredPosition -= new Vector2(deltaWorldX / scale, 0f);
    }

    void StyleControlHint(Transform panel)
    {
        // FindTmp (not FindDeep) is what stamps the shared runtime font onto a
        // label. Nunito isn't in the project yet, so — same idiom StyleLaunchCall
        // uses for TapToLaunch — this one is looked up via FindDeep and the
        // changeFont: false below, so its existing scene-authored font survives.
        Transform hintTransform = FindDeep(panel, "ControlHint");
        TextMeshProUGUI hint = hintTransform != null ? hintTransform.GetComponent<TextMeshProUGUI>() : null;
        if (hint == null)
        {
            hint = UIStyleKit.MakeLabel(
                panel, string.Empty, 19f, Color.white, new Vector2(0f, 112f), new Vector2(560f, 38f),
                FontStyles.Bold, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            hint.gameObject.name = "ControlHint";
        }

        // The same separator the tagline under the logo uses, so the menu's two
        // small caption lines read as one voice instead of two conventions.
        hint.text = "TAP TO LAUNCH  •  HOLD TO REVERSE";
        Color muted = UIDesign.TextMuted;
        muted.a = 0.68f;
        // Nunito (the redesign's supporting-text font) is not in the project yet.
        // Montserrat ExtraBold is a display/button weight and reads too heavy for
        // an instructional caption, so this one label keeps its existing font
        // rather than following StyleText's usual Montserrat default.
        UIKit.StyleText(hint, 20f, UIDesign.TrackCaption, muted, FontStyles.Bold,
            TextAlignmentOptions.Center, changeFont: false);
        // Was at 112, where a 560-wide centred line ran straight through the shop
        // pill. It belongs to the launch call, so it now sits in the gap between
        // that pill and the best-score chip instead of in the bottom row.
        SetRect(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 292f), new Vector2(540f, 40f));
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
            rect.sizeDelta = new Vector2(264f, UIDesign.ButtonHeightPill);
        }

        // Real Shop art (BalanceChipBaseFlat_Cropped) at its own native aspect,
        // not stretched/sliced into the taller 92px click target — that
        // stretch is what read as an exaggerated capsule. The click area
        // stays the full 264x92 footprint the bottom row is tuned against;
        // root's own Image becomes an invisible hit target behind it.
        const float shopArtWidth = 264f;
        Sprite shopArt = ShipSkinManager.LoadShopBalanceChipSprite();
        UIKit.ApplyNativeAspectBackground(shop.gameObject, shopArt, shopArtWidth);
        UIKit.MakeHitTargetOnly(shop.gameObject);

        // UIIcons.Shop (icon_shop.png) has asymmetric transparent padding
        // baked into its 256x256 canvas (36px below the glyph, 83px above),
        // so centring the full sprite's RectTransform still reads as
        // optically low. icon_shop_Cropped is the same pixels, alpha-bounds
        // trimmed — its RectTransform bounds ARE its visible bounds, so the
        // usual proportional-to-height sizing is replaced with the measured
        // target from the visual spec (icon ~22px tall, "SHOP" ~28pt, ~10px gap).
        Sprite shopIcon = Resources.Load<Sprite>(ShopIconCroppedPath);
        const float shopIconHeight = 22f;
        float shopIconWidth = shopIcon != null
            ? shopIconHeight * (shopIcon.rect.width / shopIcon.rect.height)
            : shopIconHeight;
        const float shopIconGap = 10f;
        const float shopTextSize = 28f;
        if (shopIcon != null)
            UIKit.StyleContentGroupExplicit(shop, "SHOP", shopIcon, shopIconWidth, shopIconHeight,
                shopIconGap, shopTextSize);
        else
        {
            float shopArtHeight = shopArtWidth / (shopArt.rect.width / shopArt.rect.height);
            UIKit.StyleContentGroup(shop, "SHOP", UIIcons.Shop, shopTextSize, null, shopArtHeight);
        }
        CenterShopContentOnVisibleBounds(shop);

        // SHOP stays visually static while idle — no breathing/floating loop.
        UIMotion staleMotion = shop.GetComponent<UIMotion>();
        if (staleMotion != null) DestroyComponent(staleMotion);
    }

    // Sound, help and day/night share one silhouette: a baked glass disc with a
    // lit rim and a baked icon at one glyph size. The button's own Image
    // becomes the disc, so the sprite references the managers hold are moved
    // to the glyph.
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
        Sprite shell = Resources.Load<Sprite>(MainMenuIconShellPath);
        UIKit.StyleIconButton(button, icon, shellSprite: shell);

        Image glyph = button.Find("Glyph").GetComponent<Image>();
        RouteIconTarget(button, glyph);

        // A text "?" was standing in for the icon that now exists.
        TextMeshProUGUI legacyGlyph = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (legacyGlyph != null) legacyGlyph.gameObject.SetActive(false);
    }

    // A breathing halo behind a control. DayNightButton is pinned to the Shop
    // identity now, so its halo is pinned too — otherwise a world-accent glow
    // would sit behind a violet disc and the two would fall out of sync.
    void AddSoftGlow(Transform panel, string hostName, Vector2 anchor, Vector2 position,
        float size, float alpha)
    {
        Transform host = FindDeep(panel, hostName);
        if (host == null) return;

        Color halo = ShopIdentityPalette.Accent;
        halo.a = alpha;
        Image glow = EnsurePlate(panel, hostName + "Glow", host.GetSiblingIndex(), anchor,
            position, new Vector2(size, size), halo, UIGlass.Glow, null);
        PinToShopIdentity(glow.gameObject, UITinted.Role.Accent, alpha);
        UIMotion.Attach(glow.gameObject, UIMotion.Mode.Pulse, 1f, 6.2f);
    }

    // Opts a single control out of the per-world Planet Theme System and pins
    // it to the fixed Shop identity instead — the same fixed-colour approach
    // Shop's own header uses (KillShopTint), just via UITinted removal rather
    // than never attaching one. The Planet Theme System itself (UIDesign,
    // UITinted, world palette resolution) is untouched.
    static void PinToShopIdentity(GameObject host, UITinted.Role fillRole, float alphaScale = 1f)
    {
        if (host == null) return;

        UITinted tint = host.GetComponent<UITinted>();
        if (tint != null)
        {
            tint.enabled = false;
            DestroyComponent(tint);
        }

        Graphic graphic = host.GetComponent<Graphic>();
        if (graphic != null)
        {
            Color color = PaletteFillFor(fillRole);
            color.a *= alphaScale;
            graphic.color = color;
        }

        UIKit.OverrideRim(host, ShopIdentityPalette.GlassRim);
    }

    static Color PaletteFillFor(UITinted.Role role)
    {
        switch (role)
        {
            case UITinted.Role.GlassDeep: return ShopIdentityPalette.GlassDeep;
            case UITinted.Role.Accent: return ShopIdentityPalette.Accent;
            case UITinted.Role.Scrim: return ShopIdentityPalette.Scrim;
            case UITinted.Role.Rim: return ShopIdentityPalette.GlassRim;
            default: return ShopIdentityPalette.Glass;
        }
    }

    static void DestroyComponent(Component component)
    {
        if (component == null) return;
        if (Application.isPlaying) Destroy(component);
        else DestroyImmediate(component);
    }

    static void DestroyGameObject(GameObject go)
    {
        if (go == null) return;
        if (Application.isPlaying) Destroy(go);
        else DestroyImmediate(go);
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

    void StyleHud()
    {
        TextMeshProUGUI score = FindTmp(canvas.transform, "ScoreText");
        if (score != null)
        {
            score.outlineWidth = 0f;
            UIKit.StyleDisplay(score, UIDesign.TypeTitle, UIDesign.TrackTitle, UIDesign.TextMain);
            SetRect(score.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(180f, 66f));
            GameplayPresentationLayout.PlaceTopCentre(score.rectTransform,
                canvas.GetComponent<RectTransform>(), GameplayPresentationLayout.Lane.OrbitScore);
            score.rectTransform.anchoredPosition += Vector2.down * 10f;
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
            GameplayPresentationLayout.PlaceTopCentre(existing as RectTransform,
                canvas.GetComponent<RectTransform>(), GameplayPresentationLayout.Lane.OrbitScore);
            return;
        }

        GameObject go = new GameObject("ScorePlate");
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        SetRect(rect, new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(198f, 104f));
        GameplayPresentationLayout.PlaceTopCentre(rect, canvas.GetComponent<RectTransform>(),
            GameplayPresentationLayout.Lane.OrbitScore);

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

        // Reward is the single dominant action. Everything else steps down in
        // size and contrast so the value proposition reads in one glance.
        StyleRewardButton(panel);
        StyleMajorButton(panel, "RestartButton", "FLY AGAIN", new Vector2(0f, -150f), false,
            new Vector2(560f, UIDesign.ButtonHeightPill));
        StyleMajorButton(panel, "ShareButton", "SHARE FLIGHT", new Vector2(0f, -248f), false,
            new Vector2(560f, UIDesign.ButtonHeightPill));
        StyleMajorButton(panel, "MainMenuButton_GameOver", "MAIN MENU", new Vector2(0f, -346f), false,
            new Vector2(560f, UIDesign.ButtonHeightPill));
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

        Button pauseRestart = FindDeep(panel, "RestartButton")?.GetComponent<Button>();
        if (pauseRestart != null)
        {
            pauseRestart.onClick.RemoveListener(RestartFromPause);
            pauseRestart.onClick.AddListener(RestartFromPause);
        }

        StyleMajorButton(panel, "ResumeButton", "RESUME", new Vector2(0f, 70f), true,
            new Vector2(600f, UIDesign.ButtonHeightMajor));
        StyleMajorButton(panel, "RestartButton", "RESTART RUN", new Vector2(0f, -55f), false,
            new Vector2(600f, UIDesign.ButtonHeightPill));
        StyleMajorButton(panel, "MainMenuButton", "MAIN MENU", new Vector2(0f, -175f), false,
            new Vector2(600f, UIDesign.ButtonHeightPill));
    }

    public static void RestyleGameOver()
    {
        if (instance == null) return;
        if (instance.canvas == null) instance.canvas = UIRootCanvas.Resolve();
        instance.StyleGameOver();
        instance.RefreshGameOverCoins();
    }

    // Tutorial V2 is self-styled inside TutorialManager, the same way RocketFuelPopup
    // owns its own glass card — there is no restyle pass to run here for it.

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

    void StyleRewardButton(Transform panel)
    {
        Transform target = FindDeep(panel, "WatchAdButton");
        if (target == null) return;

        RectTransform rect = target.GetComponent<RectTransform>();
        if (rect != null) SetLocalRect(rect, new Vector2(0f, -5f), new Vector2(650f, 124f));

        UIKit.MakeGlass(target.gameObject, UIDesign.RadiusPill, UITinted.Role.Glass, 1f, true, true);
        UIKit.OverrideRim(target.gameObject,
            new Color(UIDesign.Cta.r, UIDesign.Cta.g, UIDesign.Cta.b, 0.78f));
        UIKit.AddPressFeedback(target.gameObject);
        UIMotion.Attach(target.gameObject, UIMotion.Mode.Breathe, 0.9f, 3.0f);

        Transform primaryTransform = target.Find("RewardPrimaryLabel");
        TextMeshProUGUI primary = primaryTransform != null
            ? primaryTransform.GetComponent<TextMeshProUGUI>()
            : null;
        if (primary != null)
        {
            UIKit.StyleText(primary, 34f, UIDesign.TrackButton, UIDesign.CtaText, FontStyles.Bold);
            primary.rectTransform.anchorMin = Vector2.zero;
            primary.rectTransform.anchorMax = Vector2.one;
            primary.rectTransform.offsetMin = new Vector2(18f, 25f);
            primary.rectTransform.offsetMax = new Vector2(-18f, -5f);
            primary.transform.SetAsLastSibling();
        }

        Transform previewTransform = target.Find("RewardPreview");
        TextMeshProUGUI preview = previewTransform != null
            ? previewTransform.GetComponent<TextMeshProUGUI>()
            : null;
        if (preview != null)
        {
            UIKit.StyleText(preview, UIDesign.TypeCaption, UIDesign.TrackCaption,
                UIDesign.CtaText, FontStyles.Bold);
            SetLocalRect(preview.rectTransform, new Vector2(0f, -34f), new Vector2(560f, 30f));
            preview.transform.SetAsLastSibling();
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

    // Restarting from Pause is a new run, so it asks for the same Fuel every other
    // new run asks for. The frozen clock is left to RestartGame, which resets it as
    // part of the accepted restart — unfreezing here would resume gameplay behind a
    // still-open Pause panel whenever the request is refused.
    void RestartFromPause()
    {
        if (GameManager.instance != null) GameManager.instance.RestartGame();
    }

    void RefreshGameOverCoins()
    {
        if (gameOverCoinsText == null && gameOverPanel != null)
            gameOverCoinsText = FindTmp(gameOverPanel.transform, "RunCoinsEarnedText");
        if (gameOverCoinsText == null) return;

        int earned = CoinManager.instance != null ? CoinManager.instance.GetRunCoinsEarned() : 0;
        bool doubled = CoinManager.instance != null && CoinManager.instance.HasDoubledRunCoins;
        gameOverCoinsText.text = doubled
            ? "RUN REWARD  " + earned + "  •  DOUBLED"
            : "RUN COINS  " + earned + "  •  DOUBLE TO " + (earned * 2);
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

}
