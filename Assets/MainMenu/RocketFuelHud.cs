using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The presenter that stands between RocketFuelService and every Fuel view.
//
// There is exactly one of these per scene, on the Canvas. It holds the only
// subscription to the service, owns both gauge instances — the Main Menu's and the
// in-run HUD's — and owns the empty-tank popup. Separate views exist only because
// their parents have different lifetimes: the menu gauge lives under StartPanel,
// which is switched off at launch, and the run gauge lives under GameUI, which
// PauseManager already shows for exactly the frames that count as active gameplay.
//
// Neither view reads Fuel, calculates a refill, or touches PlayerPrefs. They are
// handed a number.
[DisallowMultipleComponent]
public sealed class RocketFuelHud : MonoBehaviour
{
    private RocketFuelService fuel;
    private RocketFuelGaugeView menuGauge;
    private RocketFuelGaugeView gameplayGauge;
    private RocketFuelPopup popup;
    private Coroutine autoShowWatch;
    private Coroutine rewardReplay;
    private GameObject gainToast;
    private bool autoShowConsumed;
    private bool subscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInstall() => SceneInstaller.RunOnEveryScene(Install);

    private static void Install()
    {
        Canvas canvas = UIRootCanvas.Resolve();
        if (canvas == null || canvas.GetComponent<RocketFuelHud>() != null) return;
        canvas.gameObject.AddComponent<RocketFuelHud>();
    }

    private void Awake() => fuel = RocketFuelService.Instance;

    private void OnEnable()
    {
        if (fuel == null) fuel = RocketFuelService.Instance;
        if (subscribed) return;

        subscribed = true;
        fuel.FuelChanged += OnFuelChanged;
        fuel.FuelGranted += OnFuelGranted;
        fuel.NewRunRejected += OnNewRunRejected;
    }

    private void OnDisable()
    {
        if (!subscribed) return;

        subscribed = false;
        fuel.FuelChanged -= OnFuelChanged;
        fuel.FuelGranted -= OnFuelGranted;
        fuel.NewRunRejected -= OnNewRunRejected;

        if (rewardReplay != null) StopCoroutine(rewardReplay);
        rewardReplay = null;
        if (gainToast != null) Destroy(gainToast);
        gainToast = null;
    }

    private void Start()
    {
        Build();
        fuel.RefreshFromClock();
        PushLevel(false);

        // A Main Menu entered on an empty tank explains itself once, unprompted.
        // The watch waits for the splash, tutorial or world intro to finish rather
        // than dropping a modal into the middle of one.
        if (autoShowWatch == null) autoShowWatch = StartCoroutine(WatchForEmptyMenu());
    }

    private void Build()
    {
        RectTransform canvasRect = GetComponent<RectTransform>();

        Transform startPanel = transform.Find("StartPanel");
        if (startPanel != null)
        {
            Transform root = startPanel.Find(RocketFuelGaugeView.RootName);
            menuGauge = root != null ? root.GetComponent<RocketFuelGaugeView>() : null;
            if (menuGauge != null)
            {
                menuGauge.BindExisting(canvasRect);
                WireMenuGaugeTap();
            }
        }

        Transform gameUi = transform.Find("GameUI");
        if (gameUi != null)
        {
            Transform root = gameUi.Find(RocketFuelGaugeView.RootName);
            gameplayGauge = root != null ? root.GetComponent<RocketFuelGaugeView>() : null;
            if (gameplayGauge != null) gameplayGauge.BindExisting(canvasRect);
        }

        Transform popupRoot = transform.Find("RocketFuelPopup");
        popup = popupRoot != null ? popupRoot.GetComponent<RocketFuelPopup>() : null;
        if (popup != null) popup.EnsureInitialized();

        if (menuGauge == null || gameplayGauge == null || popup == null)
        {
            Debug.LogError("RocketFuelHud: required serialized Main Menu Fuel references are missing. " +
                           "Run Tools > Tap or Crash > Author Serialized Main Menu.", this);
            enabled = false;
        }
    }

    // The gauge itself is a pure presenter with no raycast target anywhere in
    // its hierarchy — it was never tappable. This adds a transparent hit area
    // over the Main Menu instance only; the in-run gauge stays inert so a
    // stray tap mid-flight can't summon a modal over gameplay.
    private void WireMenuGaugeTap()
    {
        if (menuGauge == null || menuGauge.Root == null) return;

        GameObject rootGo = menuGauge.Root.gameObject;
        Image hitArea = rootGo.GetComponent<Image>();
        if (hitArea == null) hitArea = rootGo.AddComponent<Image>();
        hitArea.color = Color.clear;
        hitArea.raycastTarget = true;

        Button button = rootGo.GetComponent<Button>();
        if (button == null) button = rootGo.AddComponent<Button>();
        button.targetGraphic = hitArea;
        button.transition = Selectable.Transition.None;
        button.onClick.RemoveListener(OpenFuelPopupFromBar);
        button.onClick.AddListener(OpenFuelPopupFromBar);

        // RocketFuelGaugeView isolates its liquid-wave redraws onto a nested
        // Canvas on this same GameObject (EnsureIsolationCanvas, added on its
        // first Update — after this runs). A GraphicRaycaster is scoped to the
        // exact Canvas its graphics are registered under, so without one here
        // the root Canvas's raycaster silently never finds this hit area: the
        // tap lands in screen space but no click event is ever raised.
        if (rootGo.GetComponent<GraphicRaycaster>() == null) rootGo.AddComponent<GraphicRaycaster>();
    }

    private void OpenFuelPopupFromBar()
    {
        if (popup == null) return;
        // A tap must never stack this modal on top of the Shop or an in-flight
        // IAP purchase. TryOpen already refuses a live advertisement and an
        // already-open popup, so those are not repeated here — and checking
        // only these two specific kinds (rather than any active gate) means
        // the Fuel popup's own closing-fade gate never blocks its reopen.
        if (PresentationGate.IsActive(PresentationGate.Kind.Shop)
            || PresentationGate.IsActive(PresentationGate.Kind.IapPurchase)) return;
        popup.TryOpen();
    }

    private void OnFuelChanged()
    {
        PushLevel(true);
        if (popup != null) popup.NotifyFuelChanged();
    }

    private void OnFuelGranted(int amount, FuelGrantSource source)
    {
        if (source != FuelGrantSource.RewardedAd || amount <= 0) return;
        if (rewardReplay != null) StopCoroutine(rewardReplay);
        rewardReplay = StartCoroutine(ReplayRewardAfterPopup(amount));
    }

    // The authority updates immediately while the rewarded ad is settling. The
    // gauge therefore animates once behind the modal, then replays the same honest
    // capped delta after the modal is gone so the player can actually see it.
    private IEnumerator ReplayRewardAfterPopup(int amount)
    {
        while ((popup != null && popup.gameObject.activeInHierarchy)
            || PresentationGate.IsAdvertisementActive) yield return null;
        rewardReplay = null;

        if (menuGauge == null || menuGauge.Root == null || !menuGauge.Root.gameObject.activeInHierarchy)
            yield break;

        int current = fuel.CurrentFuel;
        float before = Mathf.Clamp01((current - amount) / (float)RocketFuelService.Capacity);
        menuGauge.SetNormalized(before, false);
        yield return null;
        menuGauge.SetNormalized(fuel.NormalizedFuel, true);
        ShowFuelGainToast(amount);
    }

    private void ShowFuelGainToast(int amount)
    {
        if (gainToast != null) Destroy(gainToast);

        RectTransform gaugeRect = menuGauge.Root;
        gainToast = new GameObject("FuelGainToast", typeof(RectTransform));
        gainToast.transform.SetParent(gaugeRect.parent, false);
        RectTransform rect = gainToast.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = gaugeRect.anchorMin;
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = gaugeRect.anchoredPosition + new Vector2(88f, 0f);
        rect.sizeDelta = new Vector2(220f, 68f);
        UIKit.MakeGlass(gainToast, UIDesign.RadiusChip, UITinted.Role.GlassDeep, 0.96f, false);
        UIKit.OverrideRim(gainToast, new Color(UIDesign.Accent.r, UIDesign.Accent.g,
            UIDesign.Accent.b, 0.78f));

        GameObject labelObject = new GameObject("Label", typeof(RectTransform));
        labelObject.transform.SetParent(gainToast.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(12f, 0f);
        labelRect.offsetMax = new Vector2(-12f, 0f);
        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        UIStyleKit.ApplyRuntimeFont(label, gainToast.transform);
        label.text = "+" + amount + " FUEL";
        label.fontSize = UIDesign.TypeLabel;
        label.fontStyle = FontStyles.Bold;
        label.characterSpacing = UIDesign.TrackLabel;
        label.alignment = TextAlignmentOptions.Center;
        label.color = RocketFuelGaugeView.ColourFor(fuel.NormalizedFuel);
        label.raycastTarget = false;

        CanvasGroup group = gainToast.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        StartCoroutine(AnimateFuelGainToast(gainToast, rect, group));
    }

    private IEnumerator AnimateFuelGainToast(GameObject toast, RectTransform rect, CanvasGroup group)
    {
        Vector2 start = rect.anchoredPosition;
        float elapsed = 0f;
        const float duration = 1.05f;
        while (elapsed < duration && rect != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float appear = Mathf.Clamp01(progress / 0.16f);
            float disappear = 1f - Mathf.Clamp01((progress - 0.68f) / 0.32f);
            group.alpha = Mathf.Min(appear, disappear);
            rect.anchoredPosition = start + Vector2.up * Mathf.SmoothStep(0f, 54f, progress);
            rect.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, appear);
            yield return null;
        }

        if (toast != null) Destroy(toast);
        if (gainToast == toast) gainToast = null;
    }

    // Every rejected new-run request routes here: Tap to Launch, Fly Again, Pause
    // Restart and the tutorial's launch all fail through the same service call.
    private void OnNewRunRejected()
    {
        if (popup == null) return;
        SplashScreenController splash = FindAnyObjectByType<SplashScreenController>();
        if (splash != null) splash.CancelTransition();
        autoShowConsumed = true;
        popup.TryOpen();
    }

    private void PushLevel(bool animate)
    {
        float normalized = fuel.NormalizedFuel;
        if (menuGauge != null) menuGauge.SetNormalized(normalized, animate);
        if (gameplayGauge != null) gameplayGauge.SetNormalized(normalized, animate);
    }

    private IEnumerator WatchForEmptyMenu()
    {
        Transform startPanel = transform.Find("StartPanel");

        while (!autoShowConsumed && !GameManager.isGameStarted)
        {
            bool inMenu = startPanel != null && startPanel.gameObject.activeInHierarchy;
            bool clearOfOtherPresentation = !PresentationGate.IsAnyFullScreenPresentationActive;

            if (inMenu && clearOfOtherPresentation && !GameManager.isIntroPlaying && !fuel.CanStartNewRun)
            {
                autoShowConsumed = true;
                if (popup != null) popup.TryOpen();
                break;
            }

            yield return null;
        }

        autoShowWatch = null;
    }
}
