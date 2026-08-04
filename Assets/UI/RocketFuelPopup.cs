using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The empty-tank modal.
//
// It never interrupts a run. Fuel is spent when a run is accepted, so reaching
// zero mid-flight is a normal state; this only ever appears where a *new* run was
// asked for and refused, or where the player has walked back into a Main Menu with
// nothing left to spend.
//
// One root, built once per scene and reused. Reopening it does not rebuild the
// hierarchy or re-add a listener, which is what keeps repeated rejections from
// stacking up duplicate popups and duplicate reward requests.
[DisallowMultipleComponent]
public sealed class RocketFuelPopup : MonoBehaviour
{
    private const float FadeDuration = 0.22f;
    private const float ConfirmationHold = 0.8f;
    private const float CardWidth = 760f;
    private const float CardHeight = 720f;

    private static readonly Color OverlayColor = new Color(0.030f, 0.035f, 0.090f, 0.86f);

    private RocketFuelService fuel;
    private FuelRewardController rewards;

    private GameObject root;
    private RectTransform card;
    private CanvasGroup canvasGroup;
    private Button watchButton;
    private TextMeshProUGUI watchLabel;
    private TextMeshProUGUI watchAmount;
    private TextMeshProUGUI countdown;
    private TextMeshProUGUI status;
    private TextMeshProUGUI confirmation;
    private Image accentRule;

    private Coroutine fadeRoutine;
    private Coroutine confirmRoutine;
    private int lastCountdownSeconds = -1;
    private bool isOpen;
    private bool gateHeld;

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

        isOpen = true;
        lastCountdownSeconds = -1;
        RestoreWatchButton();
        SetStatus(null);
        if (confirmation != null) confirmation.gameObject.SetActive(false);

        root.transform.SetAsLastSibling();
        root.SetActive(true);
        AcquireGate();
        RefreshCountdown(true);
        StartFade(0f, 1f, false, null);
        return true;
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;

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

        RefreshCountdown(true);
        if (fuel != null && fuel.CanStartNewRun && confirmRoutine == null)
            ShowConfirmationAndClose("+1 FUEL");
    }

    private void Awake()
    {
        fuel = RocketFuelService.Instance;
        rewards = FuelRewardController.Ensure();
        BindExisting();
    }

    public bool BindExisting()
    {
        root = gameObject;
        canvasGroup = GetComponent<CanvasGroup>();
        Transform cardTransform = transform.Find("Card");
        card = cardTransform as RectTransform;
        if (cardTransform == null) return false;
        Transform watch = cardTransform.Find("WatchFuelAdButton");
        watchButton = watch != null ? watch.GetComponent<Button>() : null;
        watchLabel = watch != null ? watch.Find("PrimaryLabel")?.GetComponent<TextMeshProUGUI>() : null;
        watchAmount = watch != null ? watch.Find("AmountLabel")?.GetComponent<TextMeshProUGUI>() : null;
        countdown = cardTransform.Find("Countdown")?.GetComponent<TextMeshProUGUI>();
        status = cardTransform.Find("Status")?.GetComponent<TextMeshProUGUI>();
        confirmation = cardTransform.Find("Confirmation")?.GetComponent<TextMeshProUGUI>();
        accentRule = cardTransform.Find("AccentRule")?.GetComponent<Image>();
        Button close = cardTransform.Find("CloseButton")?.GetComponent<Button>();
        Button notNow = cardTransform.Find("NotNowButton")?.GetComponent<Button>();
        if (watchButton != null) { watchButton.onClick.RemoveListener(RequestFuelAd); watchButton.onClick.AddListener(RequestFuelAd); }
        if (close != null) { close.onClick.RemoveListener(Close); close.onClick.AddListener(Close); }
        if (notNow != null) { notNow.onClick.RemoveListener(Close); notNow.onClick.AddListener(Close); }
        return canvasGroup != null && watchButton != null && countdown != null;
    }

    private void OnDisable() => ReleaseGate();

    private void OnDestroy()
    {
        if (rewards != null) rewards.DetachListeners();
        ReleaseGate();
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

        RefreshCountdown(false);
    }

    // ── Countdown ────────────────────────────────────────────────────────────

    private void RefreshCountdown(bool force)
    {
        if (countdown == null || fuel == null) return;

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
        RestoreWatchButton();
        SetStatus("AD NOT AVAILABLE");
    }

    private void SetWatchButtonBusy()
    {
        if (watchButton == null) return;
        watchButton.interactable = false;
        if (watchLabel != null) watchLabel.text = "LOADING AD";
        if (watchAmount != null) watchAmount.text = "PLEASE WAIT";
        SetStatus(null);
    }

    private void RestoreWatchButton()
    {
        if (watchButton == null) return;
        watchButton.interactable = true;
        if (watchLabel != null) watchLabel.text = "WATCH AD";
        if (watchAmount != null)
            watchAmount.text = "+" + FuelRewardController.RewardAmount + " FUEL";
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
        Vector3 startScale = to > from ? Vector3.one * 0.90f : Vector3.one;
        Vector3 endScale = to > from ? Vector3.one : Vector3.one * 0.95f;
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

        Image overlay = root.AddComponent<Image>();
        overlay.color = OverlayColor;
        overlay.raycastTarget = true;

        canvasGroup = root.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;

        GameObject cardObject = new GameObject("Card", typeof(RectTransform));
        cardObject.transform.SetParent(root.transform, false);
        card = cardObject.GetComponent<RectTransform>();
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(CardWidth, CardHeight);
        UIKit.MakeGlass(cardObject, UIDesign.RadiusCard, UITinted.Role.GlassDeep, 0.98f,
            shadow: true, interactive: true);

        accentRule = MakeImage(cardObject.transform, "AccentRule", UIGlass.Panel(4f),
            Image.Type.Sliced, accent, new Vector2(0f, 286f), new Vector2(96f, 6f));

        MakeLabel(cardObject.transform, "Title", "OUT OF FUEL", UIDesign.TypeTitle,
            UIDesign.TextMain, new Vector2(0f, 214f), new Vector2(640f, 78f), UIDesign.TrackTitle);

        MakeLabel(cardObject.transform, "Message", "Your fuel tank is empty —\nbut don't worry!",
            UIDesign.TypeBody, UIDesign.TextSub, new Vector2(0f, 118f), new Vector2(620f, 100f), 0f);

        countdown = MakeLabel(cardObject.transform, "Countdown", "NEXT FUEL IN 15:00",
            UIDesign.TypeLabel, accent, new Vector2(0f, 22f), new Vector2(600f, 40f),
            UIDesign.TrackLabel);

        BuildWatchButton(cardObject.transform);

        status = MakeLabel(cardObject.transform, "Status", "AD NOT AVAILABLE",
            UIDesign.TypeCaption, UIDesign.Danger, new Vector2(0f, -204f),
            new Vector2(600f, 32f), UIDesign.TrackCaption);
        status.gameObject.SetActive(false);

        confirmation = MakeLabel(cardObject.transform, "Confirmation", "+3 FUEL",
            UIDesign.TypeHeading, RocketFuelGaugeView.ColourFor(1f), new Vector2(0f, -204f),
            new Vector2(600f, 46f), UIDesign.TrackButton);
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
        rect.anchoredPosition = new Vector2(0f, -104f);
        rect.sizeDelta = new Vector2(620f, 148f);

        Image background = buttonObject.AddComponent<Image>();
        background.sprite = UIGlass.Panel(UIDesign.RadiusPill);
        background.type = Image.Type.Sliced;
        background.color = UIDesign.Cta;
        background.raycastTarget = true;

        watchButton = buttonObject.AddComponent<Button>();
        watchButton.targetGraphic = background;
        watchButton.onClick.AddListener(RequestFuelAd);
        buttonObject.AddComponent<UIButtonPressFeedback>();

        watchLabel = MakeLabel(buttonObject.transform, "PrimaryLabel", "WATCH AD",
            UIDesign.TypeButton, UIDesign.CtaText, new Vector2(0f, 26f),
            new Vector2(560f, 44f), UIDesign.TrackButton);
        watchAmount = MakeLabel(buttonObject.transform, "AmountLabel",
            "+" + FuelRewardController.RewardAmount + " FUEL", UIDesign.TypeLabel,
            UIDesign.CtaText, new Vector2(0f, -30f), new Vector2(560f, 34f), UIDesign.TrackLabel);
    }

    private void BuildCloseButton(Transform parent)
    {
        GameObject closeObject = new GameObject("CloseButton");
        closeObject.transform.SetParent(parent, false);
        RectTransform rect = closeObject.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(-62f, -62f);
        rect.sizeDelta = new Vector2(UIDesign.IconButtonSize, UIDesign.IconButtonSize);

        Image touchArea = closeObject.AddComponent<Image>();
        touchArea.color = Color.clear;
        touchArea.raycastTarget = true;

        Button button = closeObject.AddComponent<Button>();
        button.targetGraphic = touchArea;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(Close);
        closeObject.AddComponent<UIButtonPressFeedback>();

        Image glyph = MakeImage(closeObject.transform, "CloseGlyph", UIIcons.Get(UIIcons.Close),
            Image.Type.Simple, UIDesign.TextSub, Vector2.zero, new Vector2(34f, 34f));
        glyph.preserveAspect = true;
    }

    private void BuildNotNowButton(Transform parent)
    {
        GameObject notNowObject = new GameObject("NotNowButton");
        notNowObject.transform.SetParent(parent, false);
        RectTransform rect = notNowObject.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, -272f);
        rect.sizeDelta = new Vector2(360f, 76f);

        Image touchArea = notNowObject.AddComponent<Image>();
        touchArea.color = Color.clear;
        touchArea.raycastTarget = true;

        Button button = notNowObject.AddComponent<Button>();
        button.targetGraphic = touchArea;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(Close);

        MakeLabel(notNowObject.transform, "NotNowLabel", "NOT NOW", UIDesign.TypeLabel,
            UIDesign.TextMuted, Vector2.zero, new Vector2(340f, 40f), UIDesign.TrackLabel);
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
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = go.AddComponent<Image>();
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
