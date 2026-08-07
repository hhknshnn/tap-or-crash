using System.Collections;
using System.Collections.Generic;
using GoogleMobileAds.Ump.Api;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Tutorial V2: one responsive glass panel (same language as RocketFuelPopup) with a
// short, code-driven orbit/launch demo. Two presentation modes share it — Automatic
// onboarding (locked for 3 visible seconds, opens on its own once the Main Menu has
// settled) and Manual Help (immediately dismissible, opened from the Help button).
//
// The tutorial NEVER starts a run. Closing it hands the player back to the Main Menu
// exactly as they left it, and gameplay only begins on a fresh, explicit TAP TO LAUNCH.
// That is why there is no StartGame call anywhere in this file: onboarding and the
// launch are two separate player intents, and merging them is what made the menu
// disappear behind a game nobody asked to start.
public class TutorialManager : MonoBehaviour
{
    public enum PresentationMode { Automatic, ManualHelp }

    public static TutorialManager instance;

    public GameObject tutorialPanel;

    public const int CurrentTutorialVersion = 2;
    const string LegacyShownKey = "TutorialShown";
    const string CompletedVersionKey = "Tutorial.CompletedVersion";

    const float LockDuration = 3f;

    // ── Responsive panel geometry ────────────────────────────────────────────
    // The card is a share of the SAFE area rather than a fixed rect: the canvas is
    // ScaleWithScreenSize at 1080x1920 with match 0.5, so a 20:9 phone hands this a
    // canvas that is both narrower and much taller than the reference. A fixed card
    // would overflow the short screens and float in the tall ones.
    const float CardWidthFraction = 0.90f;
    const float CardHeightFraction = 0.88f;
    const float CardMaxWidth = 950f;
    // The caps only bind on very tall or very wide canvases, where a card taken to the
    // full fraction would be a stretched sliver rather than a panel. On a 20:9 phone the
    // height cap still leaves the card at ~86% of the safe height.
    const float CardMaxHeight = 1850f;
    const float CardPaddingFraction = 0.030f;

    // Row heights as a share of the card's inner height. Whatever is left over is
    // shared evenly between the rows as gaps, so the stack always fills the card
    // exactly once and never needs a ScrollRect.
    // The instruction cells are sized to their copy — two short lines under a heading —
    // rather than to whatever is left over. Everything that frees up goes to the demo,
    // which is the part of this panel that actually teaches the game.
    const float RowTitle = 0.088f;
    const float RowDemo = 0.380f;
    const float RowDemoWithPrivacy = 0.340f;
    const float RowPrimary = 0.175f;
    const float RowEconomy = 0.120f;
    const float RowButton = 0.110f;
    const float RowPrivacy = 0.058f;

    // Type is authored against a 900pt-wide card — the widest this panel is ever
    // drawn. Narrower canvases scale it down, with a floor, so the copy stays inside
    // the sizes the design calls for instead of auto-sizing itself into nothing.
    const float TypeReferenceWidth = 900f;
    const float MinTypeScale = 0.84f;

    const float TitleSize = 54f;
    const float PrimaryHeadingSize = 32f;
    const float PrimaryBodySize = 25f;
    const float EconomyHeadingSize = 27f;
    const float EconomyBodySize = 23f;
    const float ButtonSize = 30f;
    const float CountdownSize = 26f;
    const float PrivacySize = 20f;

    // ── Demo composition ─────────────────────────────────────────────────────
    // Authored against this frame and scaled as one, so the composition survives
    // every aspect ratio rather than being re-tuned per screen.
    const float DemoDesignWidth = 800f;
    const float DemoDesignHeight = 520f;
    static readonly Vector2 PlanetADesignCenter = new Vector2(-200f, -20f);
    static readonly Vector2 PlanetBDesignCenter = new Vector2(238f, 40f);
    const float PlanetADesignDiameter = 185f;
    const float PlanetBDesignDiameter = 130f;
    const float OrbitDesignRadius = 150f;
    const float RocketDesignSize = 88f;
    const float HoldRingDesignSize = 132f;
    const int OrbitDotCount = 14;
    const float OrbitDotDesignSize = 11f;

    const float PhaseOrbitDuration = 1.2f;
    const float PhaseReverseDuration = 1.5f;
    const float PhaseLaunchDuration = 1.1f;
    const float PhaseLandDuration = 1.5f;
    const float OrbitAngularSpeed = 155f;
    const float DemoStartAngle = 100f;

    const string TitleCopy = "HOW TO PLAY";
    const string HoldTitle = "HOLD TO REVERSE";
    const string HoldBody = "Hold the screen to reverse your orbit direction.";
    const string TapTitle = "TAP TO LAUNCH";
    const string TapBody = "Tap at the right moment to jump to the next planet.";
    const string LandTitle = "LAND & EARN";
    const string LandBody = "Each successful planet earns 1 coin.";
    const string FuelTitle = "FUEL";
    const string FuelBody = "Each new run uses 1 Fuel.\n1 Fuel = 5%  •  20 Fuel = 100%";
    const string PrimaryButtonCopy = "GOT IT — LET'S FLY";

    // The Shop's own close-button convention, reused here so this popup and
    // the Shop panel read as the same design family. Cropped first, raw
    // shell as fallback — same load order ShipSkinManager itself uses.
    const float CloseButtonHitSize = 116f;
    const float CloseButtonVisualSize = 92f;
    const float CloseButtonEdgeInset = 22f;

    // Copied unmodified (crop-to-alpha-bounds only) from
    // Assets/Art/UI/Redesign/Common/Logos/05_HowToPlay_Logo.png — the
    // approved How To Play logo family already used for the Main Menu and
    // Shop titles.
    const string HowToPlayLogoResourcePath = "Menu/UI/HowToPlayLogo";

    // The pre-rendered DEFAULT rocket the shop already uses for tint skins. The live
    // gameplay rocket is a 3D model whose root SpriteRenderer holds an invisible
    // bounds proxy, so reading the runtime object is exactly how the demo ended up
    // drawing a white blob.
    const string RocketSpriteResource = "RocketPreview";

    static readonly Color OverlayColor = new Color(0.020f, 0.028f, 0.070f, 0.90f);

    PresentationMode currentMode = PresentationMode.ManualHelp;

    Coroutine panelAnimation;
    Coroutine demoRoutine;
    Coroutine autoOpenRoutine;

    // Auto-open is event driven end to end: the menu says when it has settled, the
    // presentation gate says when the screen is free. Nothing here polls, and there
    // is no timed fallback that could drop onboarding over a half-built menu.
    bool awaitingAutoOpen;
    bool watchingGate;
    bool menuSubscribed;

    Button gotItButton;
    TextMeshProUGUI gotItPrimaryLabel;
    TextMeshProUGUI gotItCountdownLabel;
    Button closeButton;
    bool closeUnlocked;
    float lockElapsed;
    int lastCountdownSeconds = -1;

    Button privacyOptionsButton;
    bool privacyOptionsRequestInProgress;

    RectTransform cardRect;
    Vector2 lastCanvasSize;

    // ── Demo state ───────────────────────────────────────────────────────────
    RectTransform demoArea;
    RectTransform demoRocket;
    Image demoRocketImage;
    Image demoHoldRing;
    TextMeshProUGUI demoCoinLabel;
    readonly List<Image> orbitDots = new List<Image>();

    float demoUnit = 1f;
    Vector2 planetACenter;
    Vector2 planetBCenter;
    float orbitRadius;
    float dotPhase;
    bool demoVisualsResolved;

    void Awake()
    {
        // A second manager would subscribe a second time to the same static events and
        // present a second panel over the first. Only one may run.
        if (instance != null && instance != this) { enabled = false; return; }
        instance = this;
    }

    void Start()
    {
        ResolveTutorialPanel();
        EnsureTutorialStructure(tutorialPanel);
        if (tutorialPanel != null) tutorialPanel.SetActive(false);

        if (!ShouldAutoOpen()) return;

        awaitingAutoOpen = true;

        // The showcase reports its verdict from its own Start, which runs after this
        // one. A scene without a showcase shows the plain start screen, which is a
        // complete menu and needs nothing waited on.
        if (MainMenuShowcase.ExistsInScene && !MainMenuShowcase.HasMenuSettled)
        {
            menuSubscribed = true;
            MainMenuShowcase.MenuSettled += OnMenuSettled;
        }
        else
        {
            TryAutoOpen();
        }
    }

    void OnDestroy()
    {
        StopMenuWatch();
        StopGateWatch();
        if (instance == this) instance = null;
    }

    void Update()
    {
        if (tutorialPanel == null || !tutorialPanel.activeSelf) return;

        // Orientation and resolution changes are rare, but a card measured against the
        // wrong canvas size would be very visible.
        Canvas canvas = tutorialPanel.GetComponentInParent<Canvas>();
        RectTransform canvasRect = canvas != null ? canvas.GetComponent<RectTransform>() : null;
        if (canvasRect != null && canvasRect.rect.size != lastCanvasSize) LayoutPanel();

        if (currentMode == PresentationMode.Automatic)
        {
            if (!closeUnlocked)
            {
                // Only counts while the app is actually on screen — backgrounding
                // the app must not let the lock elapse for free.
                if (Application.isFocused) lockElapsed += Time.unscaledDeltaTime;
                if (lockElapsed >= LockDuration) UnlockClose();
                else RefreshCountdownLabel(false);
            }
            // Escape / Android Back stay inert for the whole automatic visit: the
            // player leaves onboarding through the button and nowhere else.
            return;
        }

        // Manual Help only: Android's Back button / Escape dismiss immediately.
        if (Input.GetKeyDown(KeyCode.Escape)) CloseTutorial();
    }

    // ─── Versioned persistence ──────────────────────────────────────────────

    static int GetCompletedVersion()
    {
        if (PlayerPrefs.HasKey(CompletedVersionKey)) return PlayerPrefs.GetInt(CompletedVersionKey, 0);
        // An old, unversioned completion counts as Tutorial V1 — V2 still has to
        // be shown once, but a fresh install still starts at zero.
        return PlayerPrefs.GetInt(LegacyShownKey, 0) == 1 ? 1 : 0;
    }

    static bool NeedsAutomaticOnboarding() => GetCompletedVersion() < CurrentTutorialVersion;

    static void MarkCompleted()
    {
        PlayerPrefs.SetInt(CompletedVersionKey, CurrentTutorialVersion);
        PlayerPrefs.SetInt(LegacyShownKey, 1);
        PlayerPrefs.Save();
    }

    // ─── Auto-open ──────────────────────────────────────────────────────────

    static bool ShouldAutoOpen()
        => NeedsAutomaticOnboarding() && !GameManager.isRestart && !GameManager.isGameStarted;

    void OnMenuSettled()
    {
        StopMenuWatch();
        TryAutoOpen();
    }

    void OnPresentationGateChanged() => TryAutoOpen();

    void TryAutoOpen()
    {
        if (!awaitingAutoOpen) return;

        if (!ShouldAutoOpen())
        {
            awaitingAutoOpen = false;
            StopGateWatch();
            return;
        }

        // Something else owns the screen — the world intro, an ad, the Fuel popup.
        // Rather than test this every frame, wait to be told the screen changed.
        if (PresentationGate.IsAnyFullScreenPresentationActive)
        {
            if (!watchingGate)
            {
                watchingGate = true;
                PresentationGate.Changed += OnPresentationGateChanged;
            }
            return;
        }

        awaitingAutoOpen = false;
        StopGateWatch();
        if (autoOpenRoutine == null) autoOpenRoutine = StartCoroutine(OpenAfterLayout());
    }

    // One frame after the menu settles, so the card is measured against a canvas that
    // has already laid itself out — not against the frame the menu finished on.
    IEnumerator OpenAfterLayout()
    {
        yield return null;
        autoOpenRoutine = null;
        if (ShouldAutoOpen()) ShowTutorial(PresentationMode.Automatic);
    }

    void StopMenuWatch()
    {
        if (!menuSubscribed) return;
        menuSubscribed = false;
        MainMenuShowcase.MenuSettled -= OnMenuSettled;
    }

    void StopGateWatch()
    {
        if (!watchingGate) return;
        watchingGate = false;
        PresentationGate.Changed -= OnPresentationGateChanged;
    }

    // ─── Entry points ───────────────────────────────────────────────────────

    /// <summary>
    /// Asked by the start screen before it commits to a launch. Returns true when the
    /// launch may proceed. Returns false when onboarding claimed the tap instead — the
    /// tutorial is then open, no Fuel has been spent, no run has begun, and the Main
    /// Menu behind it is untouched, so closing returns the player to it needing a
    /// fresh, explicit TAP TO LAUNCH.
    /// </summary>
    public bool TryClaimLaunch()
    {
        if (!NeedsAutomaticOnboarding()) return true;

        // Normally the automatic path has already opened (or completed) onboarding long
        // before a player could reach the launch button. This is the fallback for the
        // case where it has not.
        awaitingAutoOpen = false;
        StopMenuWatch();
        StopGateWatch();
        if (autoOpenRoutine != null) { StopCoroutine(autoOpenRoutine); autoOpenRoutine = null; }

        ShowTutorial(PresentationMode.Automatic);
        return false;
    }

    public void OnHelpButtonClicked() => ShowTutorial(PresentationMode.ManualHelp);

    public void OnGotItClicked()
    {
        if (currentMode == PresentationMode.Automatic && !closeUnlocked) return;
        CloseTutorial();
    }

    void ShowTutorial(PresentationMode mode)
    {
        ResolveTutorialPanel();
        if (tutorialPanel == null) return;
        if (tutorialPanel.activeSelf) return; // already open — never stack a duplicate

        currentMode = mode;

        tutorialPanel.SetActive(true);
        PresentationGate.Acquire(PresentationGate.Kind.Tutorial);
        tutorialPanel.transform.SetAsLastSibling();
        RefreshPrivacyOptionsButton();
        LayoutPanel();

        lockElapsed = 0f;
        lastCountdownSeconds = -1;
        SetCloseLocked(mode == PresentationMode.Automatic);

        ResetDemo();
        if (demoRoutine != null) StopCoroutine(demoRoutine);
        demoRoutine = demoVisualsResolved ? StartCoroutine(AnimateDemoLoop()) : null;

        if (panelAnimation != null) StopCoroutine(panelAnimation);
        panelAnimation = StartCoroutine(AnimateTutorialOpen());
    }

    void CloseTutorial()
    {
        if (tutorialPanel == null || !tutorialPanel.activeSelf) return;

        // Only the automatic path ever records completion — closing a manually
        // opened Help visit must not rewrite state that was already correct.
        if (currentMode == PresentationMode.Automatic) MarkCompleted();

        if (demoRoutine != null) { StopCoroutine(demoRoutine); demoRoutine = null; }
        if (panelAnimation != null) { StopCoroutine(panelAnimation); panelAnimation = null; }

        // Deactivating the panel is what stops it blocking raycasts; the CanvasGroup is
        // cleared as well so a close taken mid-fade can never leave an invisible sheet
        // over the Main Menu's buttons.
        CanvasGroup group = tutorialPanel.GetComponent<CanvasGroup>();
        if (group != null)
        {
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
        }

        tutorialPanel.SetActive(false);
        PresentationGate.Release(PresentationGate.Kind.Tutorial);
    }

    // ─── Three-second lock ──────────────────────────────────────────────────

    void SetCloseLocked(bool locked)
    {
        closeUnlocked = !locked;
        if (gotItButton != null) gotItButton.interactable = !locked;
        if (closeButton != null) closeButton.interactable = !locked;
        if (gotItCountdownLabel != null) gotItCountdownLabel.gameObject.SetActive(locked);
        if (locked) RefreshCountdownLabel(true);
    }

    void UnlockClose()
    {
        closeUnlocked = true;
        if (gotItButton != null) gotItButton.interactable = true;
        if (closeButton != null) closeButton.interactable = true;
        if (gotItCountdownLabel != null) gotItCountdownLabel.gameObject.SetActive(false);
    }

    void RefreshCountdownLabel(bool force)
    {
        if (gotItCountdownLabel == null) return;
        int seconds = Mathf.Clamp(Mathf.CeilToInt(LockDuration - lockElapsed), 1, Mathf.CeilToInt(LockDuration));
        if (!force && seconds == lastCountdownSeconds) return;
        lastCountdownSeconds = seconds;
        gotItCountdownLabel.text = seconds.ToString();
    }

    // ─── Privacy Options (unchanged behaviour) ──────────────────────────────

    // Only ever shown when Google's UMP actually requires a re-entry point
    // (EEA/UK-style consent already collected once). Anywhere else it stays
    // hidden rather than disabled, so the tutorial does not grow a dead
    // control for the vast majority of players who will never see it.
    void RefreshPrivacyOptionsButton()
    {
        if (privacyOptionsButton == null) return;

        bool required = ConsentInformation.PrivacyOptionsRequirementStatus
            == PrivacyOptionsRequirementStatus.Required;

        privacyOptionsButton.gameObject.SetActive(required);
        if (required) privacyOptionsButton.interactable = !privacyOptionsRequestInProgress;
    }

    void OnPrivacyOptionsClicked()
    {
        if (privacyOptionsRequestInProgress || privacyOptionsButton == null) return;

        privacyOptionsRequestInProgress = true;
        privacyOptionsButton.interactable = false;

        ConsentForm.ShowPrivacyOptionsForm(formError =>
        {
            privacyOptionsRequestInProgress = false;

            if (formError != null)
                Debug.LogError("Privacy options formu gösterilemedi: " + formError.Message);

            // Re-check requirement status rather than blindly re-enabling: the
            // form itself may have just satisfied it.
            RefreshPrivacyOptionsButton();
        });
    }

    // ─── Open animation ─────────────────────────────────────────────────────

    IEnumerator AnimateTutorialOpen()
    {
        CanvasGroup group = tutorialPanel.GetComponent<CanvasGroup>();
        if (group == null) group = tutorialPanel.AddComponent<CanvasGroup>();
        group.blocksRaycasts = true;
        group.interactable = true;
        group.alpha = 0f;

        Vector3 startScale = Vector3.one * 0.9f;
        if (cardRect != null) cardRect.localScale = startScale;

        const float duration = 0.26f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            group.alpha = p;
            if (cardRect != null) cardRect.localScale = Vector3.Lerp(startScale, Vector3.one, p);
            yield return null;
        }

        group.alpha = 1f;
        if (cardRect != null) cardRect.localScale = Vector3.one;
        panelAnimation = null;
    }

    // ─── Panel construction ─────────────────────────────────────────────────

    void ResolveTutorialPanel()
    {
        if (tutorialPanel != null) return;

        Canvas canvas = UIRootCanvas.Resolve();
        if (canvas == null)
        {
            Debug.LogError("TutorialManager: no Canvas found to host the tutorial panel.", this);
            return;
        }

        Transform existing = canvas.transform.Find("TutorialPanel");
        if (existing != null)
        {
            tutorialPanel = existing.gameObject;
            return;
        }

        GameObject panelGo = new GameObject("TutorialPanel", typeof(RectTransform));
        panelGo.transform.SetParent(canvas.transform, false);
        RectTransform panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        tutorialPanel = panelGo;
    }

    void EnsureTutorialStructure(GameObject panelGo)
    {
        if (panelGo == null) return;

        RectTransform panelRect = panelGo.GetComponent<RectTransform>();
        if (panelRect != null)
        {
            // A scene-authored panel may still be a fixed rect from V1. The overlay has
            // to cover the whole screen or the Main Menu stays tappable around its edges.
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            panelRect.localScale = Vector3.one;
        }

        Image overlay = panelGo.GetComponent<Image>();
        if (overlay == null) overlay = panelGo.AddComponent<Image>();
        overlay.sprite = null;
        overlay.color = OverlayColor;
        overlay.raycastTarget = true;

        // A stale scene bake can leave a world-tinted UITinted on the overlay from
        // before this panel owned a fixed colour (RocketFuelPopup's language, not
        // Pause/GameOver's) — it would silently overwrite OverlayColor the moment
        // the panel next activates.
        UITinted staleTint = panelGo.GetComponent<UITinted>();
        if (staleTint != null) DestroyImmediate(staleTint);

        CanvasGroup group = panelGo.GetComponent<CanvasGroup>();
        if (group == null) group = panelGo.AddComponent<CanvasGroup>();
        group.blocksRaycasts = true;
        group.interactable = true;

        Transform cardTransform = panelGo.transform.Find("Card");
        GameObject cardGo;
        if (cardTransform == null)
        {
            cardGo = new GameObject("Card", typeof(RectTransform));
            cardGo.transform.SetParent(panelGo.transform, false);
        }
        else
        {
            cardGo = cardTransform.gameObject;
        }

        // Tutorial V1's own hierarchy — the old scrolling single-text layout — is not
        // reusable for V2's structure, and anything of it left beside the card would
        // keep drawing and keep eating taps. Everything that is neither the card nor a
        // glass decoration MakeGlass owns is removed once, here.
        StripLegacyPanelChildren(panelGo.transform, cardGo.transform);

        cardRect = cardGo.GetComponent<RectTransform>();
        cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.localScale = Vector3.one;

        // A stale card is rebuilt clean rather than patched piecemeal. DestroyImmediate
        // so the rebuild below never collides with a same-frame pending destroy of an
        // object sharing its name.
        bool isV2Card = cardGo.transform.Find("DemoArea") != null
            && cardGo.transform.Find("CardHold") != null;
        if (!isV2Card)
        {
            for (int i = cardGo.transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(cardGo.transform.GetChild(i).gameObject);
        }

        UIKit.MakeGlass(cardGo, UIDesign.RadiusCard, UITinted.Role.GlassDeep, 0.98f, shadow: true, interactive: true);

        BuildTitle(cardGo.transform);
        BuildDemoArea(cardGo.transform);
        BuildInstructionCards(cardGo.transform);
        BuildGotItButton(cardGo.transform);
        BuildPrivacyOptionsButton(cardGo.transform);
        BuildCloseButton(cardGo.transform);

        LayoutPanel();
    }

    // The card, and the shadow/glow siblings MakeGlass parents next to it, are the only
    // things that belong on this panel. A V1 scroll view, its viewport or a legacy close
    // button left behind would sit over the new card and still block raycasts after it
    // closes.
    static void StripLegacyPanelChildren(Transform panel, Transform card)
    {
        for (int i = panel.childCount - 1; i >= 0; i--)
        {
            Transform child = panel.GetChild(i);
            if (child == card) continue;
            if (child.name == "CardShadow" || child.name == "CardGlow" || child.name == "Rim") continue;
            DestroyImmediate(child.gameObject);
        }
    }

    // ─── Responsive layout ───────────────────────────────────────────────────

    // Everything positional lives here rather than in the builders, so a resolution or
    // orientation change is one call rather than a rebuild.
    void LayoutPanel()
    {
        if (tutorialPanel == null || cardRect == null) return;

        Canvas canvas = tutorialPanel.GetComponentInParent<Canvas>();
        RectTransform canvasRect = canvas != null ? canvas.GetComponent<RectTransform>() : null;
        if (canvasRect == null) return;

        Vector2 canvasSize = canvasRect.rect.size;
        if (canvasSize.x <= 1f || canvasSize.y <= 1f) return;
        lastCanvasSize = canvasSize;

        // The safe area in canvas units. The same conversion SafeAreaFitter uses for the
        // edge-anchored HUD, applied here to a centred card.
        Rect safe = Screen.safeArea;
        float left = 0f, right = 0f, bottom = 0f, top = 0f;
        if (Screen.width > 0 && Screen.height > 0)
        {
            left = safe.xMin / Screen.width * canvasSize.x;
            right = (Screen.width - safe.xMax) / Screen.width * canvasSize.x;
            bottom = safe.yMin / Screen.height * canvasSize.y;
            top = (Screen.height - safe.yMax) / Screen.height * canvasSize.y;
        }

        float safeWidth = Mathf.Max(120f, canvasSize.x - left - right);
        float safeHeight = Mathf.Max(200f, canvasSize.y - bottom - top);

        float cardWidth = Mathf.Min(safeWidth * CardWidthFraction, CardMaxWidth);
        float cardHeight = Mathf.Min(safeHeight * CardHeightFraction, CardMaxHeight);

        cardRect.sizeDelta = new Vector2(cardWidth, cardHeight);
        cardRect.anchoredPosition = new Vector2((left - right) * 0.5f, (bottom - top) * 0.5f);

        float typeScale = Mathf.Clamp(cardWidth / TypeReferenceWidth, MinTypeScale, 1f);
        float padding = cardHeight * CardPaddingFraction;
        float innerHeight = cardHeight - padding * 2f;
        float innerWidth = cardWidth - padding * 2f;

        bool privacyVisible = privacyOptionsButton != null
            && privacyOptionsButton.gameObject.activeSelf;

        float demoFraction = privacyVisible ? RowDemoWithPrivacy : RowDemo;
        if (!demoVisualsResolved) demoFraction = 0f;

        float rowSum = RowTitle + demoFraction + RowPrimary + RowEconomy + RowButton
            + (privacyVisible ? RowPrivacy : 0f);
        int gapCount = (privacyVisible ? 5 : 4) - (demoVisualsResolved ? 0 : 1);
        float gap = Mathf.Max(0f, (1f - rowSum) / Mathf.Max(1, gapCount)) * innerHeight;

        // Top-down cursor: each row is placed by its own height, so adding or removing
        // the privacy footer re-flows the whole stack instead of leaving a hole.
        float cursor = innerHeight * 0.5f;

        cursor = PlaceRow(cardRect, "TitleText", cursor, innerHeight * RowTitle, innerWidth, gap);
        if (demoVisualsResolved)
            cursor = PlaceDemoRow(cursor, innerHeight * demoFraction, innerWidth, gap);
        cursor = PlaceInstructionRow(cursor, innerHeight * RowPrimary, innerWidth, gap, true);
        cursor = PlaceInstructionRow(cursor, innerHeight * RowEconomy, innerWidth, gap, false);
        cursor = PlaceRow(cardRect, "GotItButton", cursor, innerHeight * RowButton,
            Mathf.Min(innerWidth, 700f), gap);
        if (privacyVisible)
            PlaceRow(cardRect, "PrivacyOptionsButton", cursor, innerHeight * RowPrivacy,
                Mathf.Min(innerWidth * 0.62f, 460f), gap);

        ApplyTypeScale(typeScale);
        LayoutDemoContents();
    }

    // Places a named child as a row at the cursor and returns the cursor moved past it
    // plus one gap.
    static float PlaceRow(RectTransform parent, string name, float cursor, float height,
        float width, float gap)
    {
        Transform child = parent.Find(name);
        if (child != null)
        {
            RectTransform rect = (RectTransform)child;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(0f, cursor - height * 0.5f);
        }
        return cursor - height - gap;
    }

    float PlaceDemoRow(float cursor, float height, float width, float gap)
    {
        if (demoArea != null)
        {
            demoArea.anchorMin = demoArea.anchorMax = demoArea.pivot = new Vector2(0.5f, 0.5f);
            demoArea.sizeDelta = new Vector2(width, height);
            demoArea.anchoredPosition = new Vector2(0f, cursor - height * 0.5f);
        }
        return cursor - height - gap;
    }

    // Two cells side by side. The primary row carries the two controls, the economy row
    // the two rewards — same construction, deliberately different weight.
    float PlaceInstructionRow(float cursor, float height, float width, float gap, bool primary)
    {
        const float columnGap = 22f;
        float cellWidth = (width - columnGap) * 0.5f;
        float centreY = cursor - height * 0.5f;
        float offsetX = (cellWidth + columnGap) * 0.5f;

        PlaceCell(primary ? "CardHold" : "CardLand", new Vector2(-offsetX, centreY),
            new Vector2(cellWidth, height));
        PlaceCell(primary ? "CardTap" : "CardFuel", new Vector2(offsetX, centreY),
            new Vector2(cellWidth, height));

        return cursor - height - gap;
    }

    void PlaceCell(string name, Vector2 position, Vector2 size)
    {
        Transform cell = cardRect.Find(name);
        if (cell == null) return;

        RectTransform rect = (RectTransform)cell;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        RectTransform heading = cell.Find("Heading") as RectTransform;
        if (heading != null)
        {
            heading.sizeDelta = new Vector2(size.x - 24f, size.y * 0.30f);
            heading.anchoredPosition = new Vector2(0f, size.y * 0.30f);
        }

        RectTransform body = cell.Find("Body") as RectTransform;
        if (body != null)
        {
            body.sizeDelta = new Vector2(size.x - 30f, size.y * 0.54f);
            body.anchoredPosition = new Vector2(0f, -size.y * 0.16f);
        }

        if (name == "CardFuel")
        {
            if (body != null)
            {
                body.sizeDelta = new Vector2(size.x - 30f, size.y * 0.43f);
                body.anchoredPosition = new Vector2(0f, -size.y * 0.10f);
            }

            RectTransform meter = cell.Find("FuelMiniMeter") as RectTransform;
            if (meter != null)
            {
                meter.anchorMin = meter.anchorMax = meter.pivot = new Vector2(0.5f, 0.5f);
                meter.anchoredPosition = new Vector2(0f, -size.y * 0.40f);
                meter.sizeDelta = new Vector2(size.x - 50f, 16f);

                for (int i = 0; i < meter.childCount; i++)
                {
                    RectTransform segment = meter.GetChild(i) as RectTransform;
                    if (segment == null) continue;
                    segment.anchorMin = new Vector2(i / 20f, 0f);
                    segment.anchorMax = new Vector2((i + 1) / 20f, 1f);
                    segment.offsetMin = new Vector2(1.5f, 0f);
                    segment.offsetMax = new Vector2(-1.5f, 0f);
                }
            }
        }
    }

    void ApplyTypeScale(float scale)
    {
        SetFixedSize(cardRect, "TitleText", TitleSize * scale);
        SetCellType("CardHold", PrimaryHeadingSize * scale, PrimaryBodySize * scale);
        SetCellType("CardTap", PrimaryHeadingSize * scale, PrimaryBodySize * scale);
        SetCellType("CardLand", EconomyHeadingSize * scale, EconomyBodySize * scale);
        SetCellType("CardFuel", EconomyHeadingSize * scale, EconomyBodySize * scale);

        if (gotItPrimaryLabel != null) SetFixedSize(gotItPrimaryLabel, ButtonSize * scale);
        if (gotItCountdownLabel != null) SetFixedSize(gotItCountdownLabel, CountdownSize * scale);
        if (demoCoinLabel != null) SetFixedSize(demoCoinLabel, PrimaryHeadingSize * scale);

        Transform privacyLabel = privacyOptionsButton != null
            ? privacyOptionsButton.transform.Find("Label") : null;
        TextMeshProUGUI privacy = privacyLabel != null
            ? privacyLabel.GetComponent<TextMeshProUGUI>() : null;
        if (privacy != null) SetFixedSize(privacy, PrivacySize * scale);
    }

    void SetCellType(string cell, float headingSize, float bodySize)
    {
        Transform root = cardRect.Find(cell);
        if (root == null) return;

        SetFixedSize((RectTransform)root, "Heading", headingSize);

        Transform body = root.Find("Body");
        TextMeshProUGUI tmp = body != null ? body.GetComponent<TextMeshProUGUI>() : null;
        if (tmp == null) return;

        // Body copy is the only text allowed to auto-size, and only inside a narrow
        // band: a long line wraps to a second row rather than shrinking to a size
        // nobody can read on a phone.
        tmp.enableAutoSizing = true;
        tmp.fontSizeMax = bodySize;
        tmp.fontSizeMin = bodySize * 0.92f;
        tmp.fontSize = bodySize;
    }

    static void SetFixedSize(RectTransform parent, string name, float size)
    {
        Transform child = parent.Find(name);
        TextMeshProUGUI tmp = child != null ? child.GetComponent<TextMeshProUGUI>() : null;
        if (tmp != null) SetFixedSize(tmp, size);
    }

    static void SetFixedSize(TextMeshProUGUI tmp, float size)
    {
        tmp.enableAutoSizing = false;
        tmp.fontSize = size;
    }

    void BuildTitle(Transform card)
    {
        // The row itself is still PlaceRow-managed (full width, RowTitle
        // height) — only its content changes: the approved logo art replaces
        // the plain TMP heading. TMP_Text and Image both derive from Graphic,
        // and a GameObject can drive only one Graphic through its
        // CanvasRenderer, so the logo lives on a child rather than on
        // TitleText itself.
        TextMeshProUGUI title = MakeLabel(card, "TitleText", string.Empty, TitleSize,
            UIDesign.Accent, UIDesign.TrackTitle, FontStyles.Bold, TextAlignmentOptions.Center);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Overflow;

        Sprite logo = Resources.Load<Sprite>(HowToPlayLogoResourcePath);
        if (logo != null)
        {
            Transform existingLogo = title.transform.Find("TitleLogo");
            GameObject logoGo = existingLogo != null ? existingLogo.gameObject : new GameObject("TitleLogo", typeof(RectTransform));
            if (existingLogo == null) logoGo.transform.SetParent(title.transform, false);
            logoGo.transform.localScale = Vector3.one;

            RectTransform logoRect = logoGo.GetComponent<RectTransform>();
            logoRect.anchorMin = Vector2.zero;
            logoRect.anchorMax = Vector2.one;
            logoRect.offsetMin = Vector2.zero;
            logoRect.offsetMax = Vector2.zero;

            Image logoImage = logoGo.GetComponent<Image>();
            if (logoImage == null) logoImage = logoGo.AddComponent<Image>();
            logoImage.sprite = logo;
            logoImage.type = Image.Type.Simple;
            logoImage.preserveAspect = true;
            logoImage.color = Color.white;
            logoImage.raycastTarget = false;
        }
        else
        {
            // Logo missing at runtime — fall back to the original heading
            // instead of leaving a blank header.
            title.text = TitleCopy;
            UITinted.Attach(title.gameObject, UITinted.Role.Accent);
        }
    }

    // Same visual family as the Shop's own close button: a hit box plus a
    // separate visual child so the sprite scales independently of the tap
    // target, pinned to the card's top-left corner rather than following
    // the row-by-row PlaceRow flow the rest of the card uses.
    void BuildCloseButton(Transform card)
    {
        Transform existing = card.Find("CloseButton");
        GameObject go = existing != null ? existing.gameObject : new GameObject("CloseButton", typeof(RectTransform));
        if (existing == null) go.transform.SetParent(card, false);
        go.transform.localScale = Vector3.one;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(CloseButtonEdgeInset, -CloseButtonEdgeInset);
        rect.sizeDelta = new Vector2(CloseButtonHitSize, CloseButtonHitSize);

        Image hitImage = go.GetComponent<Image>();
        if (hitImage == null) hitImage = go.AddComponent<Image>();
        hitImage.color = Color.clear;
        hitImage.raycastTarget = true;

        closeButton = go.GetComponent<Button>();
        if (closeButton == null) closeButton = go.AddComponent<Button>();
        closeButton.targetGraphic = hitImage;
        closeButton.transition = Selectable.Transition.None;
        closeButton.onClick.RemoveListener(OnGotItClicked);
        closeButton.onClick.AddListener(OnGotItClicked);
        UIKit.AddPressFeedback(go);

        Transform visualTransform = go.transform.Find("Visual");
        GameObject visualGo = visualTransform != null ? visualTransform.gameObject : new GameObject("Visual", typeof(RectTransform));
        if (visualTransform == null) visualGo.transform.SetParent(go.transform, false);
        visualGo.transform.localScale = Vector3.one;

        RectTransform visualRect = visualGo.GetComponent<RectTransform>();
        visualRect.anchorMin = visualRect.anchorMax = new Vector2(0.5f, 0.5f);
        visualRect.pivot = new Vector2(0.5f, 0.5f);
        visualRect.anchoredPosition = Vector2.zero;
        visualRect.sizeDelta = new Vector2(CloseButtonVisualSize, CloseButtonVisualSize);

        Image visualImage = visualGo.GetComponent<Image>();
        if (visualImage == null) visualImage = visualGo.AddComponent<Image>();
        visualImage.sprite = ShipSkinManager.LoadShopHeaderSprite("CloseButtonShell");
        visualImage.type = Image.Type.Simple;
        visualImage.preserveAspect = true;
        visualImage.color = Color.white;
        visualImage.raycastTarget = false;
    }

    void BuildInstructionCards(Transform card)
    {
        BuildInstructionCell(card, "CardHold", HoldTitle, HoldBody);
        BuildInstructionCell(card, "CardTap", TapTitle, TapBody);
        BuildInstructionCell(card, "CardLand", LandTitle, LandBody);
        BuildInstructionCell(card, "CardFuel", FuelTitle, FuelBody);
        BuildFuelMiniMeter(card.Find("CardFuel"));
    }

    void BuildFuelMiniMeter(Transform fuelCell)
    {
        if (fuelCell == null) return;

        Transform existing = fuelCell.Find("FuelMiniMeter");
        GameObject meterObject = existing != null
            ? existing.gameObject
            : new GameObject("FuelMiniMeter", typeof(RectTransform));
        if (existing == null) meterObject.transform.SetParent(fuelCell, false);

        for (int i = 0; i < RocketFuelService.Capacity; i++)
        {
            string name = "FuelUnit_" + (i + 1);
            Transform segmentTransform = meterObject.transform.Find(name);
            GameObject segmentObject = segmentTransform != null
                ? segmentTransform.gameObject
                : new GameObject(name, typeof(RectTransform));
            if (segmentTransform == null) segmentObject.transform.SetParent(meterObject.transform, false);

            Image segment = segmentObject.GetComponent<Image>();
            if (segment == null) segment = segmentObject.AddComponent<Image>();
            segment.sprite = UIGlass.Panel(3f);
            segment.type = Image.Type.Sliced;
            Color color = RocketFuelGaugeView.ColourFor((i + 1f) / RocketFuelService.Capacity);
            segment.color = new Color(color.r, color.g, color.b, (i + 1) % 5 == 0 ? 1f : 0.72f);
            segment.raycastTarget = false;
        }
    }

    void BuildInstructionCell(Transform parent, string name, string title, string body)
    {
        Transform existing = parent.Find(name);
        GameObject cellGo = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform));
        if (existing == null) cellGo.transform.SetParent(parent, false);
        cellGo.transform.localScale = Vector3.one;

        UIKit.MakeGlass(cellGo, UIDesign.RadiusChip, UITinted.Role.Glass, 0.7f, shadow: false, interactive: false);

        TextMeshProUGUI heading = MakeLabel(cellGo.transform, "Heading", title, PrimaryHeadingSize,
            UIDesign.Accent, UIDesign.TrackLabel, FontStyles.Bold, TextAlignmentOptions.Center);
        heading.textWrappingMode = TextWrappingModes.NoWrap;
        heading.overflowMode = TextOverflowModes.Overflow;

        TextMeshProUGUI bodyLabel = MakeLabel(cellGo.transform, "Body", body, PrimaryBodySize,
            UIDesign.TextSub, 0f, FontStyles.Normal, TextAlignmentOptions.Center);
        bodyLabel.textWrappingMode = TextWrappingModes.Normal;
        bodyLabel.overflowMode = TextOverflowModes.Overflow;
    }

    void BuildGotItButton(Transform card)
    {
        Transform existing = card.Find("GotItButton");
        GameObject go = existing != null ? existing.gameObject : new GameObject("GotItButton", typeof(RectTransform));
        if (existing == null) go.transform.SetParent(card, false);
        go.transform.localScale = Vector3.one;

        Image background = go.GetComponent<Image>();
        if (background == null) background = go.AddComponent<Image>();
        background.sprite = UIGlass.Panel(UIDesign.RadiusPill);
        background.type = Image.Type.Sliced;
        background.color = UIDesign.Cta;
        background.raycastTarget = true;

        gotItButton = go.GetComponent<Button>();
        if (gotItButton == null) gotItButton = go.AddComponent<Button>();
        gotItButton.targetGraphic = background;
        gotItButton.onClick.RemoveListener(OnGotItClicked);
        gotItButton.onClick.AddListener(OnGotItClicked);
        UIKit.AddPressFeedback(go);

        gotItPrimaryLabel = MakeLabel(go.transform, "PrimaryLabel", PrimaryButtonCopy,
            ButtonSize, UIDesign.CtaText, UIDesign.TrackButton, FontStyles.Bold,
            TextAlignmentOptions.Center);
        gotItPrimaryLabel.textWrappingMode = TextWrappingModes.NoWrap;
        gotItPrimaryLabel.overflowMode = TextOverflowModes.Overflow;
        // The copy stays centred in the pill whether the lock is running or not: a label
        // that shifts when the countdown disappears reads as the button changing shape.
        StretchInside(gotItPrimaryLabel.rectTransform, 96f);

        // The countdown is a badge in the pill's right margin — beside the copy rather
        // than under it, so nothing has to move when it goes away.
        gotItCountdownLabel = MakeLabel(go.transform, "CountdownLabel", "3",
            CountdownSize, UIDesign.CtaText, UIDesign.TrackLabel, FontStyles.Bold,
            TextAlignmentOptions.Center);
        RectTransform countdownRect = gotItCountdownLabel.rectTransform;
        countdownRect.anchorMin = new Vector2(1f, 0.5f);
        countdownRect.anchorMax = new Vector2(1f, 0.5f);
        countdownRect.pivot = new Vector2(1f, 0.5f);
        countdownRect.anchoredPosition = new Vector2(-26f, 0f);
        countdownRect.sizeDelta = new Vector2(64f, 64f);
        gotItCountdownLabel.gameObject.SetActive(false);
    }

    // Anchors a label to fill its parent rect with a horizontal inset, so it follows
    // every responsive resize of the button without a second set of hardcoded sizes.
    static void StretchInside(RectTransform rect, float horizontal)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(horizontal, 0f);
        rect.offsetMax = new Vector2(-horizontal, 0f);
        rect.localScale = Vector3.one;
    }

    void BuildPrivacyOptionsButton(Transform card)
    {
        Transform existing = card.Find("PrivacyOptionsButton");
        GameObject go = existing != null ? existing.gameObject : new GameObject("PrivacyOptionsButton", typeof(RectTransform));
        if (existing == null) go.transform.SetParent(card, false);
        go.transform.localScale = Vector3.one;

        UIKit.MakeGlass(go, UIDesign.RadiusPill, UITinted.Role.Glass, 0.8f, shadow: false, interactive: true);
        Image background = go.GetComponent<Image>();

        privacyOptionsButton = go.GetComponent<Button>();
        if (privacyOptionsButton == null) privacyOptionsButton = go.AddComponent<Button>();
        privacyOptionsButton.targetGraphic = background;
        privacyOptionsButton.onClick.RemoveListener(OnPrivacyOptionsClicked);
        privacyOptionsButton.onClick.AddListener(OnPrivacyOptionsClicked);
        UIKit.AddPressFeedback(go);

        TextMeshProUGUI label = MakeLabel(go.transform, "Label", "PRIVACY OPTIONS", PrivacySize,
            UIDesign.TextSub, UIDesign.TrackCaption, FontStyles.Bold, TextAlignmentOptions.Center);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        StretchInside(label.rectTransform, 18f);

        // Hidden by default; RefreshPrivacyOptionsButton() is the only thing that ever
        // turns it on, and only when UMP says a re-entry point is actually required.
        go.SetActive(false);
    }

    // Labels are created centred; LayoutPanel owns where they end up and how big the
    // type is, so there is one place to read the whole hierarchy off.
    static TextMeshProUGUI MakeLabel(Transform parent, string name, string text, float fontSize,
        Color color, float tracking, FontStyles style, TextAlignmentOptions align)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform));
        if (existing == null) go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        if (tmp == null) tmp = go.AddComponent<TextMeshProUGUI>();
        UIStyleKit.ApplyRuntimeFont(tmp, parent);
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.characterSpacing = tracking;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        tmp.enableAutoSizing = false;
        return tmp;
    }

    // ─── Demo assets ─────────────────────────────────────────────────────────

    // Both sprites come from project assets, never from live runtime objects. The
    // planets may not have spawned yet when this builds, and the live rocket's root
    // SpriteRenderer is an invisible bounds proxy for the 3D model — reading either is
    // exactly how the demo ended up drawing plain white discs.
    static Sprite LoadRocketSprite() => Resources.Load<Sprite>(RocketSpriteResource);

    // The first level's authored planet prefabs — the Natural collection. Serialized
    // data, so it resolves regardless of whether anything has spawned yet.
    static void LoadPlanetSprites(out Sprite first, out Sprite second)
    {
        first = null;
        second = null;

        PlanetSpawner spawner = FindAnyObjectByType<PlanetSpawner>();
        GameObject[] pool = spawner != null && spawner.levels != null && spawner.levels.Length > 0
            ? spawner.levels[0].prefabs
            : null;
        if (pool == null) return;

        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] == null) continue;
            SpriteRenderer renderer = pool[i].GetComponent<SpriteRenderer>();
            if (renderer == null || renderer.sprite == null) continue;

            if (first == null) first = renderer.sprite;
            else { second = renderer.sprite; break; }
        }

        // A one-planet collection is legitimate; the demo then shows the same world
        // twice rather than inventing a second one.
        if (second == null) second = first;
    }

    // ─── Animated demo ──────────────────────────────────────────────────────

    void BuildDemoArea(Transform card)
    {
        Transform existing = card.Find("DemoArea");
        GameObject areaGo = existing != null ? existing.gameObject : new GameObject("DemoArea", typeof(RectTransform));
        if (existing == null) areaGo.transform.SetParent(card, false);

        demoArea = areaGo.GetComponent<RectTransform>();
        demoArea.anchorMin = demoArea.anchorMax = demoArea.pivot = new Vector2(0.5f, 0.5f);
        demoArea.localScale = Vector3.one;

        Sprite rocketSprite = LoadRocketSprite();
        LoadPlanetSprites(out Sprite planetA, out Sprite planetB);

        demoVisualsResolved = rocketSprite != null && planetA != null && planetB != null;
        if (!demoVisualsResolved)
        {
            // One clear error, and no demo at all — a panel of plain white circles
            // teaches the player nothing and looks broken. The written instructions
            // below are a complete tutorial on their own.
            Debug.LogError("TutorialManager: the demo could not resolve its Rocket and planet " +
                           "sprites (Resources/" + RocketSpriteResource + " and the first PlanetSpawner " +
                           "level's prefabs). Showing the written tutorial without the animation.", this);
            areaGo.SetActive(false);
            return;
        }

        areaGo.SetActive(true);
        // Deeper than the instruction cells: the demo has to read as its own little
        // screen, not as a window onto the Main Menu behind the panel.
        UIKit.MakeGlass(areaGo, UIDesign.RadiusCard * 0.7f, UITinted.Role.GlassDeep, 0.96f, shadow: false, interactive: false);
        if (areaGo.GetComponent<RectMask2D>() == null) areaGo.AddComponent<RectMask2D>();

        BuildOrbitDots(demoArea);
        BuildDemoImage(demoArea, "DemoPlanetA", planetA);
        BuildDemoImage(demoArea, "DemoPlanetB", planetB);

        // The ring is built before the ship so it always draws behind it: a press
        // indicator that covers the thing being pressed communicates nothing.
        demoHoldRing = BuildDemoImage(demoArea, "DemoHoldRing", UIGlass.DiscRim);
        demoHoldRing.color = new Color(UIDesign.CtaText.r, UIDesign.CtaText.g, UIDesign.CtaText.b, 0f);
        demoHoldRing.gameObject.SetActive(false);

        demoRocketImage = BuildDemoImage(demoArea, "DemoRocket", rocketSprite);
        demoRocket = demoRocketImage.rectTransform;
        demoRocket.SetAsLastSibling();

        demoCoinLabel = MakeLabel(demoArea, "DemoCoinLabel", "+1", PrimaryHeadingSize,
            UIDesign.Gold, UIDesign.TrackButton, FontStyles.Bold, TextAlignmentOptions.Center);
        demoCoinLabel.gameObject.SetActive(false);
    }

    void BuildOrbitDots(Transform area)
    {
        orbitDots.Clear();

        Transform existing = area.Find("OrbitDots");
        GameObject dotsGo = existing != null ? existing.gameObject : new GameObject("OrbitDots", typeof(RectTransform));
        if (existing == null) dotsGo.transform.SetParent(area, false);

        RectTransform dotsRect = dotsGo.GetComponent<RectTransform>();
        dotsRect.anchorMin = dotsRect.anchorMax = dotsRect.pivot = new Vector2(0.5f, 0.5f);
        dotsRect.anchoredPosition = Vector2.zero;
        dotsRect.sizeDelta = Vector2.zero;
        dotsRect.localScale = Vector3.one;

        for (int i = 0; i < OrbitDotCount; i++)
            orbitDots.Add(BuildDemoImage(dotsRect, "Dot" + i, UIGlass.Disc));
    }

    static Image BuildDemoImage(Transform parent, string name, Sprite sprite)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform));
        if (existing == null) go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;

        Image image = go.GetComponent<Image>();
        if (image == null) image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = Color.white;
        return image;
    }

    // Scales the authored composition to whatever rect the responsive stack gave the
    // demo, so the planets and the orbit never leave the frame.
    void LayoutDemoContents()
    {
        if (!demoVisualsResolved || demoArea == null) return;

        Vector2 size = demoArea.rect.size;
        if (size.x <= 1f || size.y <= 1f) return;

        // Sprites and the orbit are scaled by the smaller axis so nothing is stretched
        // and the orbit stays a circle. Positions are spread per axis, so a demo frame
        // wider than the authored one uses that width instead of leaving it empty.
        demoUnit = Mathf.Min(size.x / DemoDesignWidth, size.y / DemoDesignHeight);
        Vector2 spread = new Vector2(size.x / DemoDesignWidth, size.y / DemoDesignHeight);
        planetACenter = Vector2.Scale(PlanetADesignCenter, spread);
        planetBCenter = Vector2.Scale(PlanetBDesignCenter, spread);
        orbitRadius = OrbitDesignRadius * demoUnit;

        SetDemoRect(demoArea, "DemoPlanetA", planetACenter, PlanetADesignDiameter * demoUnit);
        SetDemoRect(demoArea, "DemoPlanetB", planetBCenter, PlanetBDesignDiameter * demoUnit);

        if (demoRocket != null)
            demoRocket.sizeDelta = Vector2.one * (RocketDesignSize * demoUnit);
        if (demoHoldRing != null)
            demoHoldRing.rectTransform.sizeDelta = Vector2.one * (HoldRingDesignSize * demoUnit);
        if (demoCoinLabel != null)
        {
            demoCoinLabel.rectTransform.sizeDelta = new Vector2(180f * demoUnit, 64f * demoUnit);
            demoCoinLabel.rectTransform.anchoredPosition = planetBCenter + Vector2.up * (62f * demoUnit);
        }

        for (int i = 0; i < orbitDots.Count; i++)
        {
            Image dot = orbitDots[i];
            if (dot == null) continue;
            dot.rectTransform.sizeDelta = Vector2.one * (OrbitDotDesignSize * demoUnit);
            dot.rectTransform.anchoredPosition = OrbitPoint(DotAngle(i));
        }

        ResetDemo();
    }

    static void SetDemoRect(Transform parent, string name, Vector2 position, float size)
    {
        Transform child = parent.Find(name);
        if (child == null) return;
        RectTransform rect = (RectTransform)child;
        rect.anchoredPosition = position;
        rect.sizeDelta = Vector2.one * size;
    }

    static float DotAngle(int index) => index * (360f / OrbitDotCount);

    Vector2 OrbitPoint(float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        return planetACenter + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * orbitRadius;
    }

    // Where the arc ends: a point on the second planet's own orbit ring, on the side the
    // ship is arriving from.
    Vector2 LandingPoint(Vector2 from)
    {
        float ringRadius = (PlanetBDesignDiameter * 0.5f + RocketDesignSize * 0.62f) * demoUnit;
        Vector2 approach = from - planetBCenter;
        if (approach.sqrMagnitude < 0.0001f) approach = Vector2.left;
        return planetBCenter + approach.normalized * ringRadius;
    }

    static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
    }

    // The sprite's nose points up, so a heading is the travel angle turned a quarter
    // turn back. This is what stops the ship reading as another round planet.
    void PointRocketAlong(Vector2 travel)
    {
        if (demoRocket == null || travel.sqrMagnitude < 0.000001f) return;
        float degrees = Mathf.Atan2(travel.y, travel.x) * Mathf.Rad2Deg - 90f;
        demoRocket.localRotation = Quaternion.Euler(0f, 0f, degrees);
    }

    void PlaceRocketOnOrbit(float angle, int direction)
    {
        if (demoRocket == null) return;
        demoRocket.anchoredPosition = OrbitPoint(angle);

        // Orbit tangent, turned with the direction of travel.
        float rad = angle * Mathf.Deg2Rad;
        PointRocketAlong(new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad)) * direction);
    }

    // Deterministic reset so a reopen never shows one stale frame from wherever the
    // loop last stopped.
    void ResetDemo()
    {
        if (!demoVisualsResolved) return;

        dotPhase = DemoStartAngle;
        PlaceRocketOnOrbit(DemoStartAngle, 1);
        UpdateOrbitDots(1);
        SetHoldRingVisible(false);
        HideCoinReward();
    }

    // A ~5.3s loop: orbit, hold and reverse, launch, land and reward.
    IEnumerator AnimateDemoLoop()
    {
        while (true)
        {
            float angle = DemoStartAngle;
            dotPhase = DemoStartAngle;

            // Phase 1 — normal orbit, direction readable from the nose and the dots.
            float t = 0f;
            while (t < PhaseOrbitDuration)
            {
                float delta = Time.unscaledDeltaTime;
                t += delta;
                angle += OrbitAngularSpeed * delta;
                dotPhase += OrbitAngularSpeed * delta;
                PlaceRocketOnOrbit(angle, 1);
                UpdateOrbitDots(1);
                yield return null;
            }

            // Phase 2 — the hold ring presses in, then the whole orbit reverses: the
            // ship's nose flips and the dots start travelling the other way.
            SetHoldRingVisible(true);
            t = 0f;
            const float reverseAt = PhaseReverseDuration * 0.42f;
            const float reverseWindow = 0.30f;
            while (t < PhaseReverseDuration)
            {
                float delta = Time.unscaledDeltaTime;
                t += delta;
                float dirBlend = Mathf.Clamp01((t - reverseAt) / reverseWindow);
                float currentDirection = Mathf.Lerp(1f, -1f, dirBlend);
                angle += currentDirection * OrbitAngularSpeed * delta;
                dotPhase += currentDirection * OrbitAngularSpeed * delta;
                int readableDirection = currentDirection >= 0f ? 1 : -1;
                PlaceRocketOnOrbit(angle, readableDirection);
                UpdateOrbitDots(readableDirection);
                UpdateHoldRing(t);
                yield return null;
            }
            SetHoldRingVisible(false);

            // Phase 3 — release, launch on a short eased arc toward the second planet.
            // The ship lands ON the second planet's orbit, not on its centre: landing is
            // being captured into an orbit, and a ship parked over a planet's face reads
            // as a collision.
            Vector2 launchStart = OrbitPoint(angle);
            Vector2 landing = LandingPoint(launchStart);
            Vector2 arcControl = Vector2.Lerp(launchStart, landing, 0.5f)
                + Vector2.up * (86f * demoUnit);
            Vector2 previous = launchStart;
            t = 0f;
            while (t < PhaseLaunchDuration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / PhaseLaunchDuration));
                Vector2 next = Bezier(launchStart, arcControl, landing, p);
                if (demoRocket != null) demoRocket.anchoredPosition = next;
                PointRocketAlong(next - previous);
                previous = next;
                yield return null;
            }

            // Phase 4 — landing reward, then a clean reset into the next loop.
            ShowCoinReward();
            t = 0f;
            while (t < PhaseLandDuration)
            {
                t += Time.unscaledDeltaTime;
                UpdateCoinFade(t);
                yield return null;
            }
            HideCoinReward();
            ResetDemo();
        }
    }

    // The orbit path is a ring of dots with a bright head travelling along it. That
    // travelling head is the direction indicator: when the hold reverses the orbit it
    // visibly turns around, which a static ring could never show.
    void UpdateOrbitDots(int direction)
    {
        Color baseColor = UIDesign.TextMuted;
        Color headColor = UIDesign.Accent;
        Color dim = new Color(baseColor.r, baseColor.g, baseColor.b, 0.26f);
        Color bright = new Color(headColor.r, headColor.g, headColor.b, 0.95f);

        for (int i = 0; i < orbitDots.Count; i++)
        {
            Image dot = orbitDots[i];
            if (dot == null) continue;

            // Bright behind the head and fading over a third of the ring, so the trail
            // reads as travel along the path rather than as a blinking ring.
            float offset = Mathf.DeltaAngle(DotAngle(i), dotPhase) * direction;
            float lead = offset >= 0f ? Mathf.Clamp01(1f - offset / 120f) : 0f;
            dot.color = Color.Lerp(dim, bright, lead * lead);
        }
    }

    void SetHoldRingVisible(bool visible)
    {
        if (demoHoldRing == null) return;
        demoHoldRing.gameObject.SetActive(visible);
        if (!visible) return;

        Color c = demoHoldRing.color;
        c.a = 0f;
        demoHoldRing.color = c;
        demoHoldRing.rectTransform.localScale = Vector3.one;
    }

    // A ring that keeps contracting onto the ship: the standard press-and-hold
    // language, repeating for as long as the hold lasts.
    void UpdateHoldRing(float t)
    {
        if (demoHoldRing == null || demoRocket == null) return;

        demoHoldRing.rectTransform.anchoredPosition = demoRocket.anchoredPosition;

        const float period = 0.62f;
        float cycle = Mathf.Repeat(t, period) / period;
        demoHoldRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.85f, 0.80f, cycle);

        // Brightest as it lands on the ship and gone by the time it has passed through
        // it, so each pulse reads as one press rather than a ring sitting there.
        float fadeIn = Mathf.Clamp01(t / 0.2f);
        Color c = demoHoldRing.color;
        c.a = 0.95f * fadeIn * Mathf.Clamp01(1f - cycle * cycle);
        demoHoldRing.color = c;
    }

    void ShowCoinReward()
    {
        if (demoCoinLabel == null) return;
        demoCoinLabel.gameObject.SetActive(true);
        Color c = demoCoinLabel.color;
        c.a = 0f;
        demoCoinLabel.color = c;
    }

    void UpdateCoinFade(float t)
    {
        if (demoCoinLabel == null) return;

        const float fadeIn = 0.35f;
        const float holdEnd = 0.95f;
        float alpha;
        if (t < fadeIn) alpha = Mathf.Clamp01(t / fadeIn);
        else if (t < holdEnd) alpha = 1f;
        else alpha = 1f - Mathf.Clamp01((t - holdEnd) / Mathf.Max(0.01f, PhaseLandDuration - holdEnd));

        Color c = demoCoinLabel.color;
        c.a = alpha;
        demoCoinLabel.color = c;
        demoCoinLabel.rectTransform.anchoredPosition =
            planetBCenter + Vector2.up * ((62f + (1f - alpha) * 14f) * demoUnit);
    }

    void HideCoinReward()
    {
        if (demoCoinLabel == null) return;
        demoCoinLabel.gameObject.SetActive(false);
    }
}
