using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

// The start screen's launch call and its exit.
//
// This is UI only. Everything the player sees behind the UI — the Lava hero world, its
// light rig, the orbiting rocket and the space behind them — belongs to the serialized
// MainMenu root and MainMenuShowcase. This controller used to draw its own flat starfield,
// nebulae and asteroids into a UI rect; that presentation is gone and must not come back.
public class SplashScreenController : MonoBehaviour
{
    [Header("UI Elemanları")]
    public TextMeshProUGUI tapToStartText;
    public Image fadeOverlay;

    [Header("Ayarlar")]
    public float fadeDuration = 0.6f;

    private bool isTransitioning = false;
    private float pulseTimer = 0f;
    private Coroutine transition;

    void OnEnable()
    {
        // Fast Play Mode can keep scene objects alive between sessions.
        isTransitioning = false;
        transition = null;
    }

    void Start() => SetFadeAlpha(0f);

    void Update()
    {
        if (isTransitioning) return;
        PulseTapToStart();
    }

    void PulseTapToStart()
    {
        if (tapToStartText == null) return;
        pulseTimer += Time.deltaTime;
        float alpha = Mathf.Lerp(0.35f, 1.0f, (Mathf.Sin(pulseTimer * 2.5f) + 1f) / 2f);
        SetAlpha(tapToStartText, alpha);
    }

    public void StartTransition()
    {
        if (isTransitioning) return;

        // StartButton fills the screen, so the release that dismissed a modal lands
        // here as a launch unless the guard the modal armed is still up.
        if (MenuInputGuard.IsLaunchSuppressed) return;

        // Onboarding is decided before anything moves. An incomplete Tutorial V2 takes
        // the screen instead of the launch: no fade, no queued start, no Fuel spent, and
        // the Main Menu is left exactly as it was so it is still there when the tutorial
        // closes. The player then has to tap Launch again — which is the whole point.
        if (TutorialManager.instance != null && !TutorialManager.instance.TryClaimLaunch())
            return;

        RocketFuelService fuel = RocketFuelService.Instance;
        fuel.RefreshFromClock();
        if (!fuel.CanStartNewRun)
        {
            fuel.NotifyNewRunRejected();
            return;
        }
        transition = StartCoroutine(FadeOutAndStart());
    }

    /// <summary>
    /// Puts the start screen back the way a launch found it. The launch itself is the
    /// only thing that may retire this panel, so any path that does not reach a running
    /// game has to come back through here rather than leave the menu switched off.
    /// </summary>
    public void CancelTransition()
    {
        if (transition != null) { StopCoroutine(transition); transition = null; }
        isTransitioning = false;
        SetFadeAlpha(0f);
    }

    IEnumerator FadeOutAndStart()
    {
        isTransitioning = true;

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            SetFadeAlpha(Mathf.Clamp01(elapsed / fadeDuration));
            yield return null;
        }

        transition = null;
        if (GameManager.instance != null) GameManager.instance.StartGame();

        // StartGame is allowed to decline — an empty tank, a presentation that took the
        // screen during the fade. The panel is only retired once a run is genuinely
        // running; otherwise the menu comes straight back rather than leaving the player
        // on a blank screen with no way in.
        if (!GameManager.isGameStarted)
        {
            CancelTransition();
            yield break;
        }

        gameObject.SetActive(false);
    }

    void SetFadeAlpha(float alpha)
    {
        if (fadeOverlay == null) return;
        Color c = fadeOverlay.color;
        c.a = alpha;
        fadeOverlay.color = c;
    }

    void SetAlpha(TextMeshProUGUI tmp, float alpha)
    {
        if (tmp == null) return;
        Color c = tmp.color;
        c.a = alpha;
        tmp.color = c;
    }
}
