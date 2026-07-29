using System.Collections.Generic;
using UnityEngine;

// Kenara sabitlenen kritik HUD elemanlarını çentik ve sistem çubuklarından uzak tutar.
// Arka planları etkilemez; böylece görseller ekranı doldurmaya devam eder.
public class SafeAreaFitter : MonoBehaviour
{
    private static readonly HashSet<string> TargetNames = new HashSet<string>
    {
        "ScoreText",
        "ScorePlate",
        "CoinCounter",
        "FirstTenProgress",
        "ComboText",
        "SoundButton",
        "SoundButton2",
        "DayNightButton",
        "HelpButton",
        "PauseButton",
        "ShopButton",
        "StreakBanner",
        "LaunchPlate",
        "TAP TO START",
        "ControlHint",
        "BestScoreText"
    };

    private readonly Dictionary<RectTransform, Vector2> basePositions =
        new Dictionary<RectTransform, Vector2>();

    private Canvas canvas;
    private Rect lastSafeArea;
    private Vector2Int lastScreenSize;
    private float refreshTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstall() => SceneInstaller.RunOnEveryScene(Install);

    static void Install()
    {
        Canvas sceneCanvas = FindAnyObjectByType<Canvas>();
        if (sceneCanvas != null && sceneCanvas.GetComponent<SafeAreaFitter>() == null)
            sceneCanvas.gameObject.AddComponent<SafeAreaFitter>();
    }

    void Awake()
    {
        canvas = GetComponent<Canvas>();
    }

    void Start()
    {
        RefreshTargets();
        ApplySafeArea();
    }

    void Update()
    {
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer > 0f && !ScreenChanged()) return;

        refreshTimer = 0.5f;
        RefreshTargets();
        ApplySafeArea();
    }

    bool ScreenChanged()
    {
        return lastSafeArea != Screen.safeArea
            || lastScreenSize.x != Screen.width
            || lastScreenSize.y != Screen.height;
    }

    void RefreshTargets()
    {
        if (canvas == null) return;

        RectTransform[] transforms = canvas.GetComponentsInChildren<RectTransform>(true);
        foreach (RectTransform rect in transforms)
        {
            if (rect == null || !TargetNames.Contains(rect.gameObject.name)) continue;
            if (!basePositions.ContainsKey(rect))
                basePositions.Add(rect, rect.anchoredPosition);
        }
    }

    public void Rebaseline()
    {
        basePositions.Clear();
        RefreshTargets();
        ApplySafeArea();
    }

    void ApplySafeArea()
    {
        if (canvas == null || Screen.width <= 0 || Screen.height <= 0) return;

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        Vector2 canvasSize = canvasRect.rect.size;
        Rect safe = Screen.safeArea;

        float left = safe.xMin / Screen.width * canvasSize.x;
        float right = (Screen.width - safe.xMax) / Screen.width * canvasSize.x;
        float bottom = safe.yMin / Screen.height * canvasSize.y;
        float top = (Screen.height - safe.yMax) / Screen.height * canvasSize.y;

        var staleTargets = new List<RectTransform>();
        foreach (KeyValuePair<RectTransform, Vector2> entry in basePositions)
        {
            RectTransform rect = entry.Key;
            if (rect == null)
            {
                staleTargets.Add(rect);
                continue;
            }

            Vector2 anchor = (rect.anchorMin + rect.anchorMax) * 0.5f;
            Vector2 offset = Vector2.zero;

            if (anchor.x <= 0.25f) offset.x += left;
            else if (anchor.x >= 0.75f) offset.x -= right;

            if (anchor.y <= 0.25f) offset.y += bottom;
            else if (anchor.y >= 0.75f) offset.y -= top;

            rect.anchoredPosition = entry.Value + offset;
        }

        foreach (RectTransform stale in staleTargets)
            basePositions.Remove(stale);

        lastSafeArea = safe;
        lastScreenSize = new Vector2Int(Screen.width, Screen.height);
    }
}
