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

    void OnEnable()
    {
        // Fast Play Mode can keep scene objects alive between sessions.
        isTransitioning = false;
    }

    void Start()
    {
        if (fadeOverlay == null) return;

        Color c = fadeOverlay.color;
        c.a = 0f;
        fadeOverlay.color = c;
    }

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
        RocketFuelService fuel = RocketFuelService.Instance;
        fuel.RefreshFromClock();
        if (!fuel.CanStartNewRun)
        {
            fuel.NotifyNewRunRejected();
            return;
        }
        StartCoroutine(FadeOutAndStart());
    }

    IEnumerator FadeOutAndStart()
    {
        isTransitioning = true;
        if (fadeOverlay != null)
        {
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float alpha = Mathf.Clamp01(elapsed / fadeDuration);
                Color c = fadeOverlay.color;
                c.a = alpha;
                fadeOverlay.color = c;
                yield return null;
            }
        }
        TutorialManager.instance.OnTapToStart(); // ✅ Fade sonrası tutorial aç
        gameObject.SetActive(false);
    }

    void SetAlpha(TextMeshProUGUI tmp, float alpha)
    {
        if (tmp == null) return;
        Color c = tmp.color;
        c.a = alpha;
        tmp.color = c;
    }
}
