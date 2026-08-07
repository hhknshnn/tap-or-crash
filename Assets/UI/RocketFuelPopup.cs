using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The shared Fuel management modal.
//
// It never interrupts a run. Fuel is spent when a run is accepted, so reaching
// zero mid-flight is a normal state; this only ever appears where a *new* run was
// asked for and refused, or where the player has walked back into a Main Menu with
// nothing left to spend.
//
// One root, built once per scene and reused. Reopening it does not rebuild the
// hierarchy or re-add a listener, which is what keeps repeated rejections from
// stacking up duplicate popups and duplicate reward requests.
//
// It is a *modal*: the Main Menu behind it is one screen-filling StartButton, so
// anything this popup fails to absorb becomes a launch. Ownership of that is
// EnsureModalSurface + SetModalInteractive, and it is the only correct place for
// it — see the comments there.
[DisallowMultipleComponent]
public sealed class RocketFuelPopup : MonoBehaviour
{
    private const float FadeDuration = 0.22f;
    private const float ConfirmationHold = 0.8f;
    private const float CardWidth = 820f;
    private const float CardHeight = 1300f;
    private const float CardMargin = 60f;
    private const float FreeBoostButtonY = -26f;
    private const float InstantRefillButtonY = -232f;
    private const float NotNowButtonY = -466f;
    private const float FullStateNotNowButtonY = -120f;
    private const float ActionButtonHeight = 180f;
    private const float ActionButtonWidth = 700f;

    // Above every Main Menu graphic, including the fuel gauge's own nested Canvas.
    // Sibling order alone cannot express that: a nested Canvas is sorted against
    // this one by sorting order first, and both were sitting on zero.
    private const int ModalSortingOrder = 400;

    private static readonly Color OverlayColor = new Color(0.018f, 0.022f, 0.065f, 0.93f);

    // Free is filled and green/cyan; paid is a dark surface with a gold edge. The
    // two actions must never read as two copies of the same button.
    private static readonly Color FreeBoostFill = new Color(0.086f, 0.780f, 0.588f, 0.99f);
    private static readonly Color FreeBoostRim = new Color(0.560f, 1f, 0.880f, 0.85f);
    private static readonly Color FreeBoostText = new Color(0.030f, 0.140f, 0.110f);
    private static readonly Color FreeBoostKicker = new Color(0.020f, 0.220f, 0.170f);
    private static readonly Color PaidSurface = new Color(0.062f, 0.048f, 0.125f, 0.99f);
    private static readonly Color DisabledSurface = new Color(0.070f, 0.075f, 0.135f, 0.98f);

    private RocketFuelService fuel;
    private FuelRewardController rewards;

    private GameObject root;
    private RectTransform card;
    private CanvasGroup canvasGroup;
    private Button watchButton;
    private Image watchBackground;
    private TextMeshProUGUI watchKicker;
    private TextMeshProUGUI watchLabel;
    private TextMeshProUGUI watchAmount;
    private Button fullRefillButton;
    private TextMeshProUGUI fullRefillKicker;
    private TextMeshProUGUI fullRefillLabel;
    private TextMeshProUGUI fullRefillPriceLabel;
    private RectTransform notNowRect;
    private TextMeshProUGUI countdown;
    private TextMeshProUGUI title;
    private TextMeshProUGUI message;
    private TextMeshProUGUI fuelValue;
    private TextMeshProUGUI fuelPercent;
    private TextMeshProUGUI fuelUnitHint;
    private TextMeshProUGUI status;
    private TextMeshProUGUI confirmation;
    private Image accentRule;
    private Image fuelProgressFill;

    private Coroutine fadeRoutine;
    private Coroutine confirmRoutine;
    private int lastCountdownSeconds = -1;
    private Vector2Int lastScreenSize;
    private float cardShownScale = 1f;
    private bool isOpen;
    private bool gateHeld;
    private bool openedEmpty;

    public bool IsOpen => isOpen;

    /// Builds the popup under the given canvas, once.
    public static RocketFuelPopup Create(Transform canvas)
    {
        Transform existing = canvas != null ? canvas.Find("RocketFuelPopup") : null;
        RocketFuelPopup existingPopup = existing != null ? existing.GetComponent<RocketFuelPopup>() : null;
        if (existingPopup != null)
        {
            existingPopup.BindExisting();
            return existingPopup;
        }

        GameObject host = new GameObject("RocketFuelPopup", typeof(RectTransform));
        host.transform.SetParent(canvas, false);
        RocketFuelPopup popup = host.AddComponent<RocketFuelPopup>();
        popup.Build(host.transform);
        return popup;
    }

    /// Opens the popup. Returns false when it is already open, or when a fullscreen
    /// advertisement owns the screen — a modal must never land on top of an ad.
    public bool TryOpen()
    {
        if (isOpen || root == null) return false;
        if (PresentationGate.IsAdvertisementActive) return false;

        fuel.RefreshFromClock();
        isOpen = true;
        openedEmpty = fuel.CurrentFuel <= 0;
        lastCountdownSeconds = -1;
        RefreshStateView();
        RefreshActionButtons();
        SetStatus(null);
        if (confirmation != null) confirmation.gameObject.SetActive(false);

        root.transform.SetAsLastSibling();
        root.SetActive(true);

        // Canvas.overrideSorting does not stick while the GameObject is inactive,
        // and this popup is authored inactive — the value written during the bake
        // was silently dropped. Re-applying here is what makes the sorting real.
        EnsureModalSurface();
        ApplyResponsiveLayout();
        SetModalInteractive(true);
        AcquireGate();
        RefreshCountdown(true);
        StartFade(0f, 1f, false, null);
        return true;
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;

        // Raycasts stop at the frame the popup starts leaving, not at the frame it
        // finishes fading: a control that is still hittable while it fades out is a
        // control the player can press by accident on the way out. The guard covers
        // the release of the very press that got us here.
        SetModalInteractive(false);
        MenuInputGuard.SuppressLaunchUntilPointerReleased();

        if (rewards != null) rewards.DetachListeners();
        StopConfirmation();
        StartFade(canvasGroup != null ? canvasGroup.alpha : 1f, 0f, true, null);
    }

    /// Called by the Fuel presenter from its single FuelChanged subscription. A
    /// natural refill landing while the popup is open ends the popup's reason to
    /// exist — the player is shown the credit and handed back to what was behind.
    public void NotifyFuelChanged()
    {
        if (!isOpen) return;

        RefreshStateView();
        RefreshActionButtons();
        RefreshCountdown(true);

        // GrantFuel raises this event before the rewarded callback reports the
        // actual capped delta. Let that callback own the confirmation; otherwise
        // every rewarded +3 briefly (and on some devices visibly) becomes +1.
        if (rewards != null && rewards.IsRequestInProgress) return;

        if (openedEmpty && fuel != null && fuel.CanStartNewRun && confirmRoutine == null)
            ShowConfirmationAndClose("+1 FUEL");
    }

    private bool initialized;

    private void Awake() => EnsureInitialized();

    // The popup's GameObject is authored inactive and only ever activated by
    // TryOpen, so Unity never calls Awake on it on its own — a GameObject that
    // starts inactive has Awake deferred until something sets it active, and
    // nothing else did. RocketFuelHud calls this right after finding the popup
    // so fuel/rewards/BindExisting run regardless, and TryOpen no longer
    // silently no-ops on root/fuel still being null on its first-ever call.
    public void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        fuel = RocketFuelService.Instance;
        rewards = FuelRewardController.Ensure();
        BindExisting();

        if (MonetizationManager.instance != null)
        {
            MonetizationManager.instance.PurchaseSucceeded += OnIapPurchaseSucceeded;
            MonetizationManager.instance.PurchaseFailed += OnIapPurchaseFailed;
            MonetizationManager.instance.ProductsUpdated += OnMonetizationProductsUpdated;
        }
    }

    public bool BindExisting()
    {
        root = gameObject;
        canvasGroup = GetComponent<CanvasGroup>();
        Transform cardTransform = transform.Find("Card");
        card = cardTransform as RectTransform;
        if (cardTransform == null) return false;

        EnsureModalSurface();

        // Grows an older, already-authored popup up to the current layout so a
        // fresh "Author Serialized Main Menu" bake is not required just to pick
        // up the Full Refill button added below the rewarded one.
        if (card != null) card.sizeDelta = new Vector2(CardWidth, CardHeight);

        Transform watch = cardTransform.Find("WatchFuelAdButton");
        watchButton = watch != null ? watch.GetComponent<Button>() : null;
        watchBackground = watch != null ? watch.GetComponent<Image>() : null;
        watchLabel = watch != null ? watch.Find("PrimaryLabel")?.GetComponent<TextMeshProUGUI>() : null;
        watchAmount = watch != null ? watch.Find("AmountLabel")?.GetComponent<TextMeshProUGUI>() : null;
        title = cardTransform.Find("Title")?.GetComponent<TextMeshProUGUI>();
        message = cardTransform.Find("Message")?.GetComponent<TextMeshProUGUI>();
        countdown = cardTransform.Find("Countdown")?.GetComponent<TextMeshProUGUI>();
        status = cardTransform.Find("Status")?.GetComponent<TextMeshProUGUI>();
        confirmation = cardTransform.Find("Confirmation")?.GetComponent<TextMeshProUGUI>();
        accentRule = cardTransform.Find("AccentRule")?.GetComponent<Image>();

        Transform closeTransform = cardTransform.Find("CloseButton");
        if (closeTransform == null) closeTransform = BuildCloseButton(cardTransform).transform;
        Button close = closeTransform.GetComponent<Button>();

        Transform notNowTransform = cardTransform.Find("NotNowButton");
        if (notNowTransform == null) notNowTransform = BuildNotNowButton(cardTransform).transform;
        notNowRect = notNowTransform as RectTransform;
        Button notNow = notNowTransform.GetComponent<Button>();

        Transform fullRefill = cardTransform.Find("FullRefillButton");
        if (fullRefill == null) fullRefill = BuildFullRefillButton(cardTransform).transform;
        fullRefillButton = fullRefill.GetComponent<Button>();
        fullRefillLabel = fullRefill.Find("PrimaryLabel")?.GetComponent<TextMeshProUGUI>();
        fullRefillPriceLabel = fullRefill.Find("AmountLabel")?.GetComponent<TextMeshProUGUI>();

        BindOrBuildFuelStatePanel(cardTransform);
        ApplyPremiumLayout(cardTransform, watch, fullRefill, notNowTransform, closeTransform);

        // AddListener on a UnityEvent appends, and this runs again on every rebind —
        // a scene reload, a rebuild, a Play Mode restart. Removing first is what keeps
        // one tap from raising one action exactly once.
        if (watchButton != null) { watchButton.onClick.RemoveListener(RequestFuelAd); watchButton.onClick.AddListener(RequestFuelAd); }
        if (close != null) { close.onClick.RemoveListener(Close); close.onClick.AddListener(Close); }
        if (notNow != null) { notNow.onClick.RemoveListener(Close); notNow.onClick.AddListener(Close); }
        if (fullRefillButton != null)
        {
            fullRefillButton.onClick.RemoveListener(RequestFullRefillPurchase);
            fullRefillButton.onClick.AddListener(RequestFullRefillPurchase);
        }

        return canvasGroup != null && watchButton != null && countdown != null && fullRefillButton != null;
    }

    private void OnDisable() => ReleaseGate();

    private void OnDestroy()
    {
        if (rewards != null) rewards.DetachListeners();
        ReleaseGate();

        if (MonetizationManager.instance != null)
        {
            MonetizationManager.instance.PurchaseSucceeded -= OnIapPurchaseSucceeded;
            MonetizationManager.instance.PurchaseFailed -= OnIapPurchaseFailed;
            MonetizationManager.instance.ProductsUpdated -= OnMonetizationProductsUpdated;
        }
    }

    private void Update()
    {
        if (!isOpen) return;

        // Android's Back button. The popup is a modal, so Back dismisses it rather
        // than falling through to whatever is behind.
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }

        // A rotation, a split-screen resize or a notch cutout appearing changes the
        // room the card has. Re-fitting is two floats of work and only on a change.
        Vector2Int screen = new Vector2Int(Screen.width, Screen.height);
        if (screen != lastScreenSize)
        {
            lastScreenSize = screen;
            ApplyResponsiveLayout();
        }

        RefreshCountdown(false);
    }

    // ── Modal surface ────────────────────────────────────────────────────────

    // The popup is authored by MenuPresentationBaker with its CanvasGroup switched
    // off — alpha 0, interactable false, blocksRaycasts false — which is the correct
    // *hidden* state. Nothing ever turned it back on, so an open popup was a picture:
    // every tap inside it passed through to the screen-filling StartButton behind,
    // which is why tapping the card launched a run and why X and NOT NOW were dead.
    //
    // Its own sorting Canvas and GraphicRaycaster go with that. Without them the
    // fuel gauge's nested isolation Canvas — same sorting order, its own raycaster —
    // can win the hit test over a modal that is drawn above it.
    private void EnsureModalSurface()
    {
        Image overlay = GetComponent<Image>();
        if (overlay == null) overlay = gameObject.AddComponent<Image>();
        overlay.color = OverlayColor;
        overlay.raycastTarget = true;

        // A CanvasRenderer culls a fully transparent mesh, and GraphicRaycaster
        // skips culled graphics. The blocker spends the first frames of every open
        // at alpha 0 while it fades in, and a tap landing in that window went
        // straight through to the launch button. The blocker never culls.
        overlay.canvasRenderer.cullTransparentMesh = false;

        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

        Canvas modal = GetComponent<Canvas>();
        if (modal == null) modal = gameObject.AddComponent<Canvas>();
        modal.overrideSorting = true;
        modal.sortingOrder = ModalSortingOrder;

        if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
    }

    private void SetModalInteractive(bool interactive)
    {
        if (canvasGroup == null) return;
        canvasGroup.interactable = interactive;
        canvasGroup.blocksRaycasts = interactive;
    }

    // The card is a fixed layout, so a canvas that cannot hold it is answered by
    // scaling the whole card rather than by letting labels fall off its edge or
    // shrink below a readable size. On every portrait phone this resolves to 1.
    private void ApplyResponsiveLayout()
    {
        if (card == null) return;
        RectTransform canvasRect = transform.parent as RectTransform;
        if (canvasRect == null) return;

        Vector2 canvasSize = canvasRect.rect.size;
        if (canvasSize.x <= 1f || canvasSize.y <= 1f) return;
        lastScreenSize = new Vector2Int(Screen.width, Screen.height);

        Rect safe = Screen.safeArea;
        float verticalScale = Screen.height > 0 ? canvasSize.y / Screen.height : 1f;
        float horizontalScale = Screen.width > 0 ? canvasSize.x / Screen.width : 1f;
        float top = Mathf.Max(0f, Screen.height - safe.yMax) * verticalScale;
        float bottom = Mathf.Max(0f, safe.yMin) * verticalScale;
        float left = Mathf.Max(0f, safe.xMin) * horizontalScale;
        float right = Mathf.Max(0f, Screen.width - safe.xMax) * horizontalScale;

        float availableHeight = canvasSize.y - top - bottom - CardMargin * 2f;
        float availableWidth = canvasSize.x - left - right - CardMargin * 2f;

        card.sizeDelta = new Vector2(CardWidth, CardHeight);
        card.anchoredPosition = new Vector2((left - right) * 0.5f, (bottom - top) * 0.5f);
        cardShownScale = Mathf.Clamp(
            Mathf.Min(availableHeight / CardHeight, availableWidth / CardWidth), 0.5f, 1f);
        if (!isOpen || fadeRoutine == null) card.localScale = Vector3.one * cardShownScale;
    }

    // ── Fuel state panel ─────────────────────────────────────────────────────

    private void BindOrBuildFuelStatePanel(Transform cardTransform)
    {
        Transform existing = cardTransform.Find("FuelStatePanel");
        GameObject panelObject = existing != null
            ? existing.gameObject
            : new GameObject("FuelStatePanel", typeof(RectTransform));
        if (existing == null) panelObject.transform.SetParent(cardTransform, false);

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        SetRect(panelRect, new Vector2(0f, 262f), new Vector2(700f, 232f));
        UIKit.MakeGlass(panelObject, UIDesign.RadiusCard, UITinted.Role.GlassDeep,
            1f, false, false);
        PinSurfaceColor(panelObject, new Color(0.060f, 0.052f, 0.145f, 0.98f));
        UIKit.OverrideRim(panelObject, new Color(UIDesign.Accent.r, UIDesign.Accent.g,
            UIDesign.Accent.b, 0.58f));

        fuelValue = EnsureLabel(panelObject.transform, "FuelValue", "0 / 20",
            UIDesign.TypeHeading, UIDesign.TextMain, new Vector2(-170f, 62f),
            new Vector2(300f, 56f), UIDesign.TrackButton);
        fuelValue.alignment = TextAlignmentOptions.MidlineLeft;

        fuelPercent = EnsureLabel(panelObject.transform, "FuelPercent", "0%",
            UIDesign.TypeHeading, UIDesign.Accent, new Vector2(200f, 62f),
            new Vector2(220f, 56f), UIDesign.TrackButton);
        fuelPercent.alignment = TextAlignmentOptions.MidlineRight;

        fuelUnitHint = EnsureLabel(panelObject.transform, "FuelUnitHint",
            "1 FUEL = 5%  •  20 FUEL = 100%", UIDesign.TypeCaption,
            UIDesign.TextSub, new Vector2(0f, 10f), new Vector2(620f, 34f),
            UIDesign.TrackCaption);

        Transform trackTransform = panelObject.transform.Find("FuelProgressTrack");
        GameObject trackObject = trackTransform != null
            ? trackTransform.gameObject
            : new GameObject("FuelProgressTrack", typeof(RectTransform));
        if (trackTransform == null) trackObject.transform.SetParent(panelObject.transform, false);
        RectTransform trackRect = trackObject.GetComponent<RectTransform>();
        SetRect(trackRect, new Vector2(0f, -62f), new Vector2(620f, 38f));
        Image track = trackObject.GetComponent<Image>();
        if (track == null) track = trackObject.AddComponent<Image>();
        track.sprite = UIGlass.Panel(12f);
        track.type = Image.Type.Sliced;
        track.color = new Color(0.025f, 0.032f, 0.080f, 0.96f);
        track.raycastTarget = false;

        Transform fillTransform = trackObject.transform.Find("Fill");
        GameObject fillObject = fillTransform != null
            ? fillTransform.gameObject
            : new GameObject("Fill", typeof(RectTransform));
        if (fillTransform == null) fillObject.transform.SetParent(trackObject.transform, false);
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(4f, 4f);
        fillRect.offsetMax = new Vector2(-4f, -4f);
        fuelProgressFill = fillObject.GetComponent<Image>();
        if (fuelProgressFill == null) fuelProgressFill = fillObject.AddComponent<Image>();
        fuelProgressFill.sprite = UIGlass.Panel(9f);
        fuelProgressFill.type = Image.Type.Filled;
        fuelProgressFill.fillMethod = Image.FillMethod.Horizontal;
        fuelProgressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fuelProgressFill.raycastTarget = false;
        fillObject.transform.SetAsFirstSibling();

        for (int i = 1; i < RocketFuelService.Capacity; i++)
        {
            string tickName = "Tick_" + i;
            Transform tickTransform = trackObject.transform.Find(tickName);
            GameObject tickObject = tickTransform != null
                ? tickTransform.gameObject
                : new GameObject(tickName, typeof(RectTransform));
            if (tickTransform == null) tickObject.transform.SetParent(trackObject.transform, false);
            RectTransform tickRect = tickObject.GetComponent<RectTransform>();
            float x = i / (float)RocketFuelService.Capacity;
            tickRect.anchorMin = tickRect.anchorMax = new Vector2(x, 0.5f);
            tickRect.pivot = new Vector2(0.5f, 0.5f);
            tickRect.anchoredPosition = Vector2.zero;
            tickRect.sizeDelta = new Vector2(2f, 24f);
            Image tick = tickObject.GetComponent<Image>();
            if (tick == null) tick = tickObject.AddComponent<Image>();
            tick.color = new Color(0.92f, 0.95f, 1f, 0.18f);
            tick.raycastTarget = false;
        }
    }

    private void ApplyPremiumLayout(Transform cardTransform, Transform watch,
        Transform fullRefill, Transform notNow, Transform close)
    {
        UIKit.MakeGlass(cardTransform.gameObject, UIDesign.RadiusCard,
            UITinted.Role.GlassDeep, 1f, true, true);
        PinSurfaceColor(cardTransform.gameObject, new Color(0.030f, 0.026f, 0.090f, 0.99f));
        UIKit.OverrideRim(cardTransform.gameObject, new Color(UIDesign.Accent.r,
            UIDesign.Accent.g, UIDesign.Accent.b, 0.64f));

        SetLabelLayout(title, new Vector2(0f, 496f), new Vector2(700f, 76f),
            UIDesign.TypeTitle, UIDesign.TextMain);
        SetLabelLayout(message, new Vector2(0f, 428f), new Vector2(700f, 56f),
            UIDesign.TypeBody, UIDesign.TextSub);
        SetLabelLayout(countdown, new Vector2(0f, 106f), new Vector2(680f, 42f),
            UIDesign.TypeLabel, UIDesign.Accent);
        SetLabelLayout(status, new Vector2(0f, -352f), new Vector2(680f, 36f),
            UIDesign.TypeCaption, UIDesign.Danger);
        SetLabelLayout(confirmation, new Vector2(0f, -352f), new Vector2(680f, 46f),
            UIDesign.TypeHeading, RocketFuelGaugeView.ColourFor(1f));

        if (accentRule != null)
        {
            SetRect(accentRule.rectTransform, new Vector2(0f, 560f), new Vector2(160f, 8f));
            accentRule.color = UIDesign.Accent;
        }

        if (watch is RectTransform watchRect)
        {
            SetRect(watchRect, new Vector2(0f, FreeBoostButtonY),
                new Vector2(ActionButtonWidth, ActionButtonHeight));
            watchBackground = watch.GetComponent<Image>();
            watchKicker = EnsureActionLabel(watch, "KickerLabel", "FREE BOOST", 62f,
                UIDesign.TypeCaption, UIDesign.TrackCaption, FreeBoostKicker);
            watchLabel = EnsureActionLabel(watch, "PrimaryLabel", "WATCH AD", 6f,
                UIDesign.TypeButton, UIDesign.TrackButton, FreeBoostText);
            watchAmount = EnsureActionLabel(watch, "AmountLabel", "+3 FUEL", -50f,
                UIDesign.TypeLabel, UIDesign.TrackLabel, FreeBoostText);
        }

        if (fullRefill is RectTransform fullRect)
        {
            SetRect(fullRect, new Vector2(0f, InstantRefillButtonY),
                new Vector2(ActionButtonWidth, ActionButtonHeight));
            UIKit.MakeGlass(fullRefill.gameObject, UIDesign.RadiusPill,
                UITinted.Role.Glass, 0.98f, false, true);
            PinSurfaceColor(fullRefill.gameObject, PaidSurface);
            UIKit.OverrideRim(fullRefill.gameObject,
                new Color(UIDesign.Gold.r, UIDesign.Gold.g, UIDesign.Gold.b, 0.92f));
            fullRefillKicker = EnsureActionLabel(fullRefill, "KickerLabel", "INSTANT REFILL", 62f,
                UIDesign.TypeCaption, UIDesign.TrackCaption, UIDesign.Gold);
            fullRefillLabel = EnsureActionLabel(fullRefill, "PrimaryLabel", "FULL REFILL", 6f,
                UIDesign.TypeButton, UIDesign.TrackButton, UIDesign.TextMain);
            fullRefillPriceLabel = EnsureActionLabel(fullRefill, "AmountLabel", "CONNECTING…", -50f,
                UIDesign.TypeLabel, UIDesign.TrackLabel, UIDesign.Gold);
        }

        if (notNow is RectTransform notNowRect) StyleNotNowButton(notNowRect);
        if (close is RectTransform closeRect) StyleCloseButton(closeRect);
    }

    private void RefreshStateView()
    {
        if (fuel == null) return;

        int current = Mathf.Clamp(fuel.CurrentFuel, 0, RocketFuelService.Capacity);
        float normalized = current / (float)RocketFuelService.Capacity;
        int percent = current * 5;
        Color accent = RocketFuelGaugeView.ColourFor(normalized);

        // The empty message is only true at zero. A player with 14 Fuel reading
        // "your tank is empty" learns that this screen does not mean what it says.
        if (current <= 0)
        {
            if (title != null) title.text = "OUT OF FUEL";
            if (message != null) message.text = "Refill now or wait for the next Fuel.";
        }
        else if (current >= RocketFuelService.Capacity)
        {
            if (title != null) title.text = "TANK FULL";
            if (message != null) message.text = "READY FOR YOUR NEXT RUN";
        }
        else
        {
            if (title != null) title.text = "FUEL STATION";
            if (message != null) message.text = "Top up before your next run.";
        }

        if (fuelValue != null) fuelValue.text = current + " / " + RocketFuelService.Capacity;
        if (fuelPercent != null)
        {
            fuelPercent.text = percent + "%";
            fuelPercent.color = accent;
        }
        if (fuelUnitHint != null) fuelUnitHint.text = "1 FUEL = 5%  •  20 FUEL = 100%";
        if (fuelProgressFill != null)
        {
            fuelProgressFill.fillAmount = normalized;
            fuelProgressFill.color = accent;
        }
        if (accentRule != null) accentRule.color = accent;
        if (countdown != null) countdown.color = accent;
    }

    private static TextMeshProUGUI EnsureLabel(Transform parent, string name, string copy,
        float size, Color color, Vector2 position, Vector2 sizeDelta, float tracking)
    {
        Transform existing = parent.Find(name);
        TextMeshProUGUI label = existing != null ? existing.GetComponent<TextMeshProUGUI>() : null;
        if (label == null)
            label = MakeLabel(parent, name, copy, size, color, position, sizeDelta, tracking);
        else
            SetLabelLayout(label, position, sizeDelta, size, color);
        return label;
    }

    private static TextMeshProUGUI EnsureActionLabel(Transform button, string name, string copy,
        float y, float size, float tracking, Color color)
    {
        TextMeshProUGUI label = EnsureLabel(button, name, copy, size, color,
            new Vector2(0f, y), new Vector2(ActionButtonWidth - 60f, size + 18f), tracking);
        label.characterSpacing = tracking;
        return label;
    }

    private static void SetLabelLayout(TextMeshProUGUI label, Vector2 position,
        Vector2 size, float fontSize, Color color)
    {
        if (label == null) return;
        SetRect(label.rectTransform, position, size);
        label.fontSize = fontSize;
        label.color = color;
        label.fontStyle = FontStyles.Bold;
        label.raycastTarget = false;
    }

    private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        if (rect == null) return;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void PinSurfaceColor(GameObject surface, Color color)
    {
        UITinted tint = surface != null ? surface.GetComponent<UITinted>() : null;
        if (tint != null) tint.enabled = false;
        Image image = surface != null ? surface.GetComponent<Image>() : null;
        if (image != null) image.color = color;
    }

    private void RefreshCountdown(bool force)
    {
        if (countdown == null || fuel == null) return;

        if (fuel.CurrentFuel >= RocketFuelService.Capacity)
        {
            countdown.gameObject.SetActive(false);
            lastCountdownSeconds = 0;
            return;
        }

        countdown.gameObject.SetActive(true);

        TimeSpan remaining = fuel.TimeUntilNextFuel;
        int totalSeconds = Mathf.Max(0, Mathf.CeilToInt((float)remaining.TotalSeconds));

        // Once a second, not once a frame: this is the only place the popup could
        // have allocated a string per rendered frame.
        if (!force && totalSeconds == lastCountdownSeconds) return;
        lastCountdownSeconds = totalSeconds;

        countdown.text = "NEXT FUEL IN " + (totalSeconds / 60).ToString("00")
            + ":" + (totalSeconds % 60).ToString("00");
    }

    // ── Rewarded Fuel ────────────────────────────────────────────────────────

    private void RequestFuelAd()
    {
        if (!isOpen) return;
        if (rewards == null) rewards = FuelRewardController.Ensure();
        if (rewards == null)
        {
            SetStatus("AD NOT AVAILABLE");
            return;
        }
        if (rewards.IsRequestInProgress) return;

        SetWatchButtonBusy();
        if (!rewards.TryRequestFuel(OnFuelRewardGranted, OnFuelRewardUnavailable))
            OnFuelRewardUnavailable();
    }

    private void OnFuelRewardGranted(int amount)
    {
        if (!isOpen) return;

        RefreshStateView();
        RefreshActionButtons();

        if (amount <= 0)
        {
            // The tank filled up some other way while the ad played. Nothing was
            // lost, but there is no credit to celebrate either.
            RestoreWatchButton();
            SetStatus("FUEL TANK ALREADY FULL");
            return;
        }

        ShowConfirmationAndClose("+" + amount + " FUEL");
    }

    private void OnFuelRewardUnavailable()
    {
        if (!isOpen) return;
        RefreshStateView();
        RestoreWatchButton();
        SetStatus("AD NOT AVAILABLE");
    }

    private void SetWatchButtonBusy()
    {
        if (watchButton == null) return;
        watchButton.interactable = false;
        PaintFreeBoost(false);
        if (watchKicker != null) watchKicker.text = "FREE BOOST";
        if (watchLabel != null) watchLabel.text = "LOADING AD";
        if (watchAmount != null) watchAmount.text = "PLEASE WAIT";
        SetStatus(null);
    }

    private void RestoreWatchButton()
    {
        if (watchButton == null) return;
        watchButton.interactable = true;
        PaintFreeBoost(true);
        if (watchKicker != null) watchKicker.text = "FREE BOOST";
        if (watchLabel != null) watchLabel.text = "WATCH AD";
        if (watchAmount != null) watchAmount.text = RewardedAmountCopy();
    }

    // The rewarded grant is capped by the tank, so a player at 19/20 is credited
    // one Fuel, not three. Promising +3 there and delivering +1 is the kind of
    // thing a store review is written about.
    private string RewardedAmountCopy()
    {
        int current = fuel != null ? Mathf.Clamp(fuel.CurrentFuel, 0, RocketFuelService.Capacity) : 0;
        int room = RocketFuelService.Capacity - current;
        int credited = Mathf.Min(FuelRewardController.RewardAmount, room);

        if (credited >= FuelRewardController.RewardAmount) return "+" + credited + " FUEL";
        return "+" + credited + " FUEL TO FULL";
    }

    private void PaintFreeBoost(bool enabled)
    {
        if (watchBackground == null) return;
        watchBackground.sprite = UIGlass.Panel(UIDesign.RadiusPill);
        watchBackground.type = Image.Type.Sliced;
        watchBackground.color = enabled ? FreeBoostFill : DisabledSurface;
        UIKit.OverrideRim(watchBackground.gameObject, enabled
            ? FreeBoostRim
            : new Color(UIDesign.TextMuted.r, UIDesign.TextMuted.g, UIDesign.TextMuted.b, 0.45f));

        Color primary = enabled ? FreeBoostText : UIDesign.TextMuted;
        Color kicker = enabled ? FreeBoostKicker : UIDesign.TextMuted;
        if (watchKicker != null) watchKicker.color = kicker;
        if (watchLabel != null) watchLabel.color = primary;
        if (watchAmount != null) watchAmount.color = primary;
    }

    // Called on open and whenever Fuel or store state changes while the popup
    // is open. A full tank overrides everything else: neither action can do
    // anything useful, so both drop to their own disabled full-tank state — the
    // free one stays green-labelled, the paid one stays gold, so they never
    // collapse into one indistinguishable grey pair.
    private void RefreshActionButtons()
    {
        bool isFull = fuel != null && fuel.CurrentFuel >= RocketFuelService.Capacity;

        if (isFull)
        {
            if (watchButton != null) watchButton.gameObject.SetActive(false);
            if (fullRefillButton != null) fullRefillButton.gameObject.SetActive(false);
            if (notNowRect != null)
                SetRect(notNowRect, new Vector2(0f, FullStateNotNowButtonY), notNowRect.sizeDelta);
            return;
        }

        if (watchButton != null) watchButton.gameObject.SetActive(true);
        if (fullRefillButton != null) fullRefillButton.gameObject.SetActive(true);
        if (notNowRect != null)
            SetRect(notNowRect, new Vector2(0f, NotNowButtonY), notNowRect.sizeDelta);

        RestoreWatchButton();
        RefreshFullRefillButton();
    }

    // ── Paid full refill ────────────────────────────────────────────────────

    private void RequestFullRefillPurchase()
    {
        if (!isOpen) return;
        if (MonetizationManager.instance == null
            || !MonetizationManager.instance.Purchase(MonetizationProducts.FuelFullRefill))
        {
            SetStatus("STORE NOT READY");
            return;
        }

        SetStatus(null);
        RefreshFullRefillButton();
    }

    private void RefreshFullRefillButton()
    {
        if (fullRefillButton == null) return;
        if (fullRefillKicker != null) fullRefillKicker.text = "INSTANT REFILL";

        MonetizationManager mm = MonetizationManager.instance;
        if (mm != null && mm.IsPurchaseDeferred(MonetizationProducts.FuelFullRefill))
        {
            fullRefillButton.interactable = false;
            if (fullRefillLabel != null) fullRefillLabel.text = "FULL REFILL";
            if (fullRefillPriceLabel != null) fullRefillPriceLabel.text = "PURCHASE PENDING";
            return;
        }

        if (mm != null && mm.IsPurchaseInFlight)
        {
            fullRefillButton.interactable = false;
            if (fullRefillLabel != null) fullRefillLabel.text = "FULL REFILL";
            if (fullRefillPriceLabel != null) fullRefillPriceLabel.text = "PURCHASE PENDING";
            return;
        }

        if (fullRefillLabel != null) fullRefillLabel.text = "FULL REFILL";

        if (mm == null || mm.State == MonetizationManager.IapState.Uninitialized
            || mm.State == MonetizationManager.IapState.Connecting)
        {
            fullRefillButton.interactable = false;
            if (fullRefillPriceLabel != null) fullRefillPriceLabel.text = "CONNECTING…";
            return;
        }

        if (mm.State == MonetizationManager.IapState.Unavailable
            || !mm.TryGetLocalizedPrice(MonetizationProducts.FuelFullRefill, out string localizedPrice))
        {
            fullRefillButton.interactable = false;
            if (fullRefillPriceLabel != null) fullRefillPriceLabel.text = "UNAVAILABLE";
            return;
        }

        fullRefillButton.interactable = mm.CanPurchase(MonetizationProducts.FuelFullRefill);
        if (fullRefillPriceLabel != null) fullRefillPriceLabel.text = localizedPrice;
    }

    private void OnIapPurchaseSucceeded(string productId)
    {
        if (productId != MonetizationProducts.FuelFullRefill) return;
        if (!isOpen) { RefreshFullRefillButton(); return; }
        ShowConfirmationAndClose("TANK FULL");
    }

    private void OnIapPurchaseFailed(string productId, string reason)
    {
        if (productId != MonetizationProducts.FuelFullRefill) return;
        if (!isOpen) return;
        SetStatus("PURCHASE FAILED");
        RefreshActionButtons();
    }

    private void OnMonetizationProductsUpdated()
    {
        if (isOpen) RefreshActionButtons();
    }

    private void SetStatus(string message)
    {
        if (status == null) return;
        bool visible = !string.IsNullOrEmpty(message);
        if (visible) status.text = message;
        status.gameObject.SetActive(visible);
    }

    private void ShowConfirmationAndClose(string message)
    {
        StopConfirmation();
        confirmRoutine = StartCoroutine(ConfirmThenClose(message));
    }

    private IEnumerator ConfirmThenClose(string message)
    {
        SetStatus(null);
        if (watchButton != null) watchButton.interactable = false;
        if (confirmation != null)
        {
            confirmation.text = message;
            confirmation.gameObject.SetActive(true);
        }

        yield return new WaitForSecondsRealtime(ConfirmationHold);
        confirmRoutine = null;
        Close();
    }

    private void StopConfirmation()
    {
        if (confirmRoutine == null) return;
        StopCoroutine(confirmRoutine);
        confirmRoutine = null;
        if (confirmation != null) confirmation.gameObject.SetActive(false);
    }

    // ── Gate and fade ────────────────────────────────────────────────────────

    private void AcquireGate()
    {
        if (gateHeld) return;
        gateHeld = true;
        PresentationGate.Acquire(PresentationGate.Kind.FuelPopup);
    }

    private void ReleaseGate()
    {
        if (!gateHeld) return;
        gateHeld = false;
        PresentationGate.Release(PresentationGate.Kind.FuelPopup);
    }

    private void StartFade(float from, float to, bool deactivate, Action onComplete)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(Fade(from, to, deactivate, onComplete));
    }

    private IEnumerator Fade(float from, float to, bool deactivate, Action onComplete)
    {
        bool opening = to > from;
        Vector3 startScale = Vector3.one * (opening ? cardShownScale * 0.90f : cardShownScale);
        Vector3 endScale = Vector3.one * (opening ? cardShownScale : cardShownScale * 0.95f);
        canvasGroup.alpha = from;
        card.localScale = startScale;

        float elapsed = 0f;
        while (elapsed < FadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / FadeDuration));
            canvasGroup.alpha = Mathf.Lerp(from, to, progress);
            card.localScale = Vector3.LerpUnclamped(startScale, endScale, progress);
            yield return null;
        }

        canvasGroup.alpha = to;
        card.localScale = endScale;
        fadeRoutine = null;

        if (deactivate)
        {
            SetModalInteractive(false);
            root.SetActive(false);
            ReleaseGate();
        }
        onComplete?.Invoke();
    }

    // ── Construction ─────────────────────────────────────────────────────────

    private void Build(Transform host)
    {
        UIDesign.EnsureInitialised();
        Color accent = RocketFuelGaugeView.ColourFor(0f);

        root = host.gameObject;
        RectTransform rootRect = root.GetComponent<RectTransform>();
        if (rootRect == null) rootRect = root.AddComponent<RectTransform>();
        Stretch(rootRect);

        EnsureModalSurface();
        canvasGroup.alpha = 0f;
        SetModalInteractive(false);

        GameObject cardObject = new GameObject("Card", typeof(RectTransform));
        cardObject.transform.SetParent(root.transform, false);
        card = cardObject.GetComponent<RectTransform>();
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(CardWidth, CardHeight);
        UIKit.MakeGlass(cardObject, UIDesign.RadiusCard, UITinted.Role.GlassDeep, 0.98f,
            shadow: true, interactive: true);

        accentRule = MakeImage(cardObject.transform, "AccentRule", UIGlass.Panel(4f),
            Image.Type.Sliced, accent, new Vector2(0f, 560f), new Vector2(160f, 8f));

        title = MakeLabel(cardObject.transform, "Title", "FUEL STATION", UIDesign.TypeTitle,
            UIDesign.TextMain, new Vector2(0f, 496f), new Vector2(700f, 76f), UIDesign.TrackTitle);

        message = MakeLabel(cardObject.transform, "Message", "Top up before your next run.",
            UIDesign.TypeBody, UIDesign.TextSub, new Vector2(0f, 428f), new Vector2(700f, 56f), 0f);

        BindOrBuildFuelStatePanel(cardObject.transform);

        countdown = MakeLabel(cardObject.transform, "Countdown", "NEXT FUEL IN 15:00",
            UIDesign.TypeLabel, accent, new Vector2(0f, 106f), new Vector2(680f, 42f),
            UIDesign.TrackLabel);

        BuildWatchButton(cardObject.transform);
        BuildFullRefillButton(cardObject.transform);

        status = MakeLabel(cardObject.transform, "Status", "AD NOT AVAILABLE",
            UIDesign.TypeCaption, UIDesign.Danger, new Vector2(0f, -352f),
            new Vector2(680f, 36f), UIDesign.TrackCaption);
        status.gameObject.SetActive(false);

        confirmation = MakeLabel(cardObject.transform, "Confirmation", "+3 FUEL",
            UIDesign.TypeHeading, RocketFuelGaugeView.ColourFor(1f), new Vector2(0f, -352f),
            new Vector2(680f, 46f), UIDesign.TrackButton);
        confirmation.gameObject.SetActive(false);

        BuildCloseButton(cardObject.transform);
        BuildNotNowButton(cardObject.transform);

        root.SetActive(false);
    }

    private void BuildWatchButton(Transform parent)
    {
        GameObject buttonObject = new GameObject("WatchFuelAdButton");
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, FreeBoostButtonY);
        rect.sizeDelta = new Vector2(ActionButtonWidth, ActionButtonHeight);

        watchBackground = buttonObject.AddComponent<Image>();
        watchBackground.sprite = UIGlass.Panel(UIDesign.RadiusPill);
        watchBackground.type = Image.Type.Sliced;
        watchBackground.color = FreeBoostFill;
        watchBackground.raycastTarget = true;

        watchButton = buttonObject.AddComponent<Button>();
        watchButton.targetGraphic = watchBackground;
        watchButton.onClick.AddListener(RequestFuelAd);
        buttonObject.AddComponent<UIButtonPressFeedback>();
        UIKit.EnsureRim(buttonObject, UIDesign.RadiusPill);
        UIKit.OverrideRim(buttonObject, FreeBoostRim);

        watchKicker = EnsureActionLabel(buttonObject.transform, "KickerLabel", "FREE BOOST", 62f,
            UIDesign.TypeCaption, UIDesign.TrackCaption, FreeBoostKicker);
        watchLabel = EnsureActionLabel(buttonObject.transform, "PrimaryLabel", "WATCH AD", 6f,
            UIDesign.TypeButton, UIDesign.TrackButton, FreeBoostText);
        watchAmount = EnsureActionLabel(buttonObject.transform, "AmountLabel",
            "+" + FuelRewardController.RewardAmount + " FUEL", -50f,
            UIDesign.TypeLabel, UIDesign.TrackLabel, FreeBoostText);
    }

    private GameObject BuildFullRefillButton(Transform parent)
    {
        GameObject buttonObject = new GameObject("FullRefillButton");
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, InstantRefillButtonY);
        rect.sizeDelta = new Vector2(ActionButtonWidth, ActionButtonHeight);

        Image background = buttonObject.AddComponent<Image>();
        background.sprite = UIGlass.Panel(UIDesign.RadiusPill);
        background.type = Image.Type.Sliced;
        background.color = PaidSurface;
        background.raycastTarget = true;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(RequestFullRefillPurchase);
        buttonObject.AddComponent<UIButtonPressFeedback>();
        UIKit.EnsureRim(buttonObject, UIDesign.RadiusPill);
        UIKit.OverrideRim(buttonObject,
            new Color(UIDesign.Gold.r, UIDesign.Gold.g, UIDesign.Gold.b, 0.92f));

        fullRefillKicker = EnsureActionLabel(buttonObject.transform, "KickerLabel",
            "INSTANT REFILL", 62f, UIDesign.TypeCaption, UIDesign.TrackCaption, UIDesign.Gold);
        fullRefillLabel = EnsureActionLabel(buttonObject.transform, "PrimaryLabel",
            "FULL REFILL", 6f, UIDesign.TypeButton, UIDesign.TrackButton, UIDesign.TextMain);
        fullRefillPriceLabel = EnsureActionLabel(buttonObject.transform, "AmountLabel",
            "CONNECTING…", -50f, UIDesign.TypeLabel, UIDesign.TrackLabel, UIDesign.Gold);

        return buttonObject;
    }

    // A close control the size of a thumb, not the size of its glyph: the hit
    // target is the disc, and the glyph inside it stays small.
    private GameObject BuildCloseButton(Transform parent)
    {
        GameObject closeObject = new GameObject("CloseButton");
        closeObject.transform.SetParent(parent, false);
        closeObject.AddComponent<RectTransform>();

        Button button = closeObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(Close);
        closeObject.AddComponent<UIButtonPressFeedback>();

        StyleCloseButton(closeObject.GetComponent<RectTransform>());
        return closeObject;
    }

    private void StyleCloseButton(RectTransform rect)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(-74f, -74f);
        rect.sizeDelta = new Vector2(UIDesign.IconButtonSize, UIDesign.IconButtonSize);

        GameObject closeObject = rect.gameObject;
        UIKit.MakeGlassDisc(closeObject, UITinted.Role.GlassDeep, 1f, false, true);
        PinSurfaceColor(closeObject, new Color(0.085f, 0.078f, 0.165f, 0.98f));
        UIKit.OverrideRim(closeObject, new Color(UIDesign.TextSub.r, UIDesign.TextSub.g,
            UIDesign.TextSub.b, 0.55f));

        Image surface = closeObject.GetComponent<Image>();
        if (surface != null) surface.raycastTarget = true;
        Button button = closeObject.GetComponent<Button>();
        if (button != null && button.targetGraphic == null) button.targetGraphic = surface;

        Image glyph = MakeImage(rect, "CloseGlyph", UIIcons.Get(UIIcons.Close),
            Image.Type.Simple, UIDesign.TextMain, Vector2.zero, new Vector2(40f, 40f));
        glyph.preserveAspect = true;
    }

    // A tappable secondary button, not a line of grey text. It is the dismissal a
    // player reaches for when the two offers above are both a "no".
    private GameObject BuildNotNowButton(Transform parent)
    {
        GameObject notNowObject = new GameObject("NotNowButton");
        notNowObject.transform.SetParent(parent, false);
        notNowObject.AddComponent<RectTransform>();

        Button button = notNowObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(Close);
        notNowObject.AddComponent<UIButtonPressFeedback>();

        StyleNotNowButton(notNowObject.GetComponent<RectTransform>());
        return notNowObject;
    }

    private void StyleNotNowButton(RectTransform rect)
    {
        SetRect(rect, new Vector2(0f, NotNowButtonY), new Vector2(440f, 104f));

        GameObject notNowObject = rect.gameObject;
        UIKit.MakeGlass(notNowObject, UIDesign.RadiusPill, UITinted.Role.Glass, 0.96f, false, true);
        PinSurfaceColor(notNowObject, new Color(0.070f, 0.066f, 0.145f, 0.96f));
        UIKit.OverrideRim(notNowObject, new Color(UIDesign.TextMuted.r, UIDesign.TextMuted.g,
            UIDesign.TextMuted.b, 0.55f));

        Image surface = notNowObject.GetComponent<Image>();
        if (surface != null) surface.raycastTarget = true;
        Button button = notNowObject.GetComponent<Button>();
        if (button != null && button.targetGraphic == null) button.targetGraphic = surface;

        TextMeshProUGUI label = EnsureLabel(rect, "NotNowLabel", "NOT NOW", UIDesign.TypeBody,
            UIDesign.TextSub, Vector2.zero, new Vector2(400f, 48f), UIDesign.TrackLabel);
        label.text = "NOT NOW";
    }

    private static TextMeshProUGUI MakeLabel(Transform parent, string name, string text,
        float size, Color color, Vector2 position, Vector2 sizeDelta, float tracking)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = sizeDelta;

        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        UIStyleKit.ApplyRuntimeFont(label, parent);
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.characterSpacing = tracking;
        label.raycastTarget = false;
        return label;
    }

    private static Image MakeImage(Transform parent, string name, Sprite sprite, Image.Type type,
        Color color, Vector2 position, Vector2 size)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name);
        if (existing == null) go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        if (rect == null) rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = go.GetComponent<Image>();
        if (image == null) image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = type;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
