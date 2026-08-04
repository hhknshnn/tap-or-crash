using System.Collections;
using UnityEngine;

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
    private bool autoShowConsumed;
    private bool subscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInstall() => SceneInstaller.RunOnEveryScene(Install);

    private static void Install()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
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
        fuel.NewRunRejected += OnNewRunRejected;
    }

    private void OnDisable()
    {
        if (!subscribed) return;

        subscribed = false;
        fuel.FuelChanged -= OnFuelChanged;
        fuel.NewRunRejected -= OnNewRunRejected;
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
            if (menuGauge != null) menuGauge.BindExisting(canvasRect);
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

        if (menuGauge == null || gameplayGauge == null || popup == null)
        {
            Debug.LogError("RocketFuelHud: required serialized Main Menu Fuel references are missing. " +
                           "Run Tools > Tap or Crash > Author Serialized Main Menu.", this);
            enabled = false;
        }
    }

    private void OnFuelChanged()
    {
        PushLevel(true);
        if (popup != null) popup.NotifyFuelChanged();
    }

    // Every rejected new-run request routes here: Tap to Launch, Fly Again, Pause
    // Restart and the tutorial's launch all fail through the same service call.
    private void OnNewRunRejected()
    {
        if (popup == null) return;
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
