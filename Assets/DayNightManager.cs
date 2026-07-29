using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class DayNightManager : MonoBehaviour
{
    public static DayNightManager instance;

    [Header("Bağlantılar")]
    public Camera mainCamera;
    public Image  toggleButton;

    [Header("Buton İkonları")]
    public Sprite daySprite;
    public Sprite nightSprite;

    [Header("Panel Renkleri")]
    public Image gameOverPanelBg;
    public SpriteRenderer gameplayBackground;
    public Color nightPanelColor = new Color(0f, 0f, 0.03f, 0.82f);
    public Color dayPanelColor   = new Color(0.2f, 0.4f, 0.6f, 0.82f);

    [Header("Gece Renkleri")]
    public Color nightCameraColor     = new Color(0.02f, 0.02f, 0.05f, 1f);
    public Color nightBackgroundColor = new Color(1f, 1f, 1f, 1f);

    [Header("Gündüz Renkleri")]
    public Color dayCameraColor       = new Color(0.45f, 0.65f, 0.85f, 1f);
    public Color dayBackgroundColor   = new Color(0.7f, 0.85f, 1f, 1f);

    [Header("Geçiş Süresi")]
    [SerializeField] private float transitionDuration = 0.6f;

    private bool             isDayMode = false;
    private SpriteRenderer[] backgrounds;
    private Color[]          baseBackgroundColors;
    private Coroutine        transitionCoroutine;

    void Awake()
    {
        if (instance == null) instance = this;
        else { Destroy(gameObject); return; }

        isDayMode = PlayerPrefs.GetInt("DayMode", 0) == 1;

        // Eski sahnede bu renklerin alpha değeri yanlışlıkla sıfır kaydedilmiş.
        if (nightPanelColor.a <= 0.01f)
            nightPanelColor = new Color(0.03f, 0.04f, 0.10f, 0.92f);
        if (dayPanelColor.a <= 0.01f)
            dayPanelColor = new Color(0.12f, 0.28f, 0.48f, 0.92f);
    }

    IEnumerator Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;

        // Let procedural parallax stars finish their Start pass first.
        yield return null;

        if (gameplayBackground == null && mainCamera != null)
        {
            foreach (SpriteRenderer renderer in mainCamera.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.gameObject.name.Trim() == "Background")
                {
                    gameplayBackground = renderer;
                    break;
                }
            }
        }

        ParallaxBackground[] parallaxObjects = FindObjectsByType<ParallaxBackground>();
        var renderers = new List<SpriteRenderer>();
        if (gameplayBackground != null) renderers.Add(gameplayBackground);

        foreach (ParallaxBackground parallax in parallaxObjects)
        {
            foreach (SpriteRenderer renderer in parallax.GetComponentsInChildren<SpriteRenderer>(true))
                if (renderer != null && !renderers.Contains(renderer)) renderers.Add(renderer);
        }
        backgrounds = renderers.ToArray();
        baseBackgroundColors = new Color[backgrounds.Length];
        for (int i = 0; i < backgrounds.Length; i++)
            baseBackgroundColors[i] = backgrounds[i] != null ? backgrounds[i].color : Color.white;

        ApplyModeInstant();
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    public void ToggleMode() => SetMode(!isDayMode);

    public void SetMode(bool day)
    {
        isDayMode = day;
        PlayerPrefs.SetInt("DayMode", isDayMode ? 1 : 0);
        PlayerPrefs.Save();

        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        transitionCoroutine = StartCoroutine(SmoothTransition());
    }

    public bool IsDayMode() => isDayMode;

    // ─── Geçiş Animasyonu ────────────────────────────────────────────────────

    IEnumerator SmoothTransition()
    {
        Color fromCamera = mainCamera      != null ? mainCamera.backgroundColor : Color.black;
        Color fromPanel  = gameOverPanelBg != null ? gameOverPanelBg.color      : Color.black;

        Color toCamera = isDayMode ? dayCameraColor : nightCameraColor;
        Color toPanel  = isDayMode ? dayPanelColor  : nightPanelColor;
        Color[] fromBg = new Color[backgrounds != null ? backgrounds.Length : 0];
        if (backgrounds != null)
            for (int i = 0; i < backgrounds.Length; i++)
                if (backgrounds[i] != null)
                    fromBg[i] = backgrounds[i].color;

        float t = 0f;
        while (t < transitionDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, t / transitionDuration);

            if (mainCamera != null)
                mainCamera.backgroundColor = Color.Lerp(fromCamera, toCamera, p);

            if (backgrounds != null)
                for (int i = 0; i < backgrounds.Length; i++)
                    if (backgrounds[i] != null)
                        backgrounds[i].color = Color.Lerp(fromBg[i], GetBackgroundColor(i), p);

            if (gameOverPanelBg != null)
                gameOverPanelBg.color = Color.Lerp(fromPanel, toPanel, p);

            yield return null;
        }

        ApplyModeInstant();
        transitionCoroutine = null;
    }

    // ─── Anlık Uygulama ──────────────────────────────────────────────────────

    void ApplyModeInstant()
    {
        if (mainCamera != null)
            mainCamera.backgroundColor = isDayMode ? dayCameraColor : nightCameraColor;

        if (backgrounds != null)
            for (int i = 0; i < backgrounds.Length; i++)
                if (backgrounds[i] != null)
                    backgrounds[i].color = GetBackgroundColor(i);

        if (gameOverPanelBg != null)
            gameOverPanelBg.color = isDayMode ? dayPanelColor : nightPanelColor;

        // StartPanel arka planına DOKUNMAZ — kullanıcının tasarımı korunur
        UpdateIcon();
    }

    Color GetBackgroundColor(int index)
    {
        if (backgrounds == null || index < 0 || index >= backgrounds.Length)
            return Color.white;

        if (backgrounds[index] == gameplayBackground)
            return isDayMode ? dayBackgroundColor : nightBackgroundColor;

        Color baseColor = baseBackgroundColors != null && index < baseBackgroundColors.Length
            ? baseBackgroundColors[index]
            : backgrounds[index].color;
        if (!isDayMode) return baseColor;

        Color tinted = Color.Lerp(baseColor, dayBackgroundColor, 0.22f);
        tinted.a = baseColor.a * 0.48f;
        return tinted;
    }

    // Public: VisualPolishController ikon sprite'larını değiştirdikten sonra çağırır.
    public void UpdateIcon()
    {
        if (toggleButton == null) return;
        toggleButton.sprite = isDayMode ? nightSprite : daySprite;
    }
}
