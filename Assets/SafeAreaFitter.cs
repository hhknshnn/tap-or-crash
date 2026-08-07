using System.Collections.Generic;
using UnityEngine;

// Kenara sabitlenen kritik HUD elemanlarını çentik ve sistem çubuklarından uzak tutar.
// Arka planları etkilemez; böylece görseller ekranı doldurmaya devam eder.
public class SafeAreaFitter : MonoBehaviour
{
    private static readonly HashSet<string> TargetNames = new HashSet<string>
    {
        "CoinCounter",
        "RocketFuelHud",
        "FirstTenProgress",
        "SoundButton",
        "SoundButton2",
        "DayNightButton",
        "HelpButton",
        "PauseButton",
        "ShopButton",
        "StreakBanner",
        "LaunchPlate",
        "TAP TO START",
        "TapToLaunch",
        "ControlHint",
        "BestScoreText"
    };

    private readonly Dictionary<RectTransform, Vector2> basePositions =
        new Dictionary<RectTransform, Vector2>();
    private readonly Dictionary<RectTransform, Vector2> appliedOffsets =
        new Dictionary<RectTransform, Vector2>();

    private Canvas canvas;
    private Rect lastSafeArea;
    private Vector2Int lastScreenSize;
    private float refreshTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstall() => SceneInstaller.RunOnEveryScene(Install);

    static void Install()
    {
        Canvas sceneCanvas = UIRootCanvas.Resolve();
        if (sceneCanvas != null && sceneCanvas.GetComponent<SafeAreaFitter>() == null)
            sceneCanvas.gameObject.AddComponent<SafeAreaFitter>();
    }

    void Awake()
    {
        canvas = GetComponent<Canvas>();
    }

    void OnEnable()
    {
        canvas = GetComponent<Canvas>();
        Rebaseline();
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
            {
                basePositions.Add(rect, rect.anchoredPosition);
                appliedOffsets.Add(rect, Vector2.zero);
            }
        }
    }

    public void Rebaseline()
    {
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
        var updatedBases = new List<KeyValuePair<RectTransform, Vector2>>();
        foreach (KeyValuePair<RectTransform, Vector2> entry in basePositions)
        {
            RectTransform rect = entry.Key;
            if (rect == null)
            {
                staleTargets.Add(rect);
                continue;
            }

            Vector2 previousOffset = appliedOffsets.TryGetValue(rect, out Vector2 recordedOffset)
                ? recordedOffset
                : Vector2.zero;
            Vector2 basePosition = entry.Value;
            Vector2 expectedPosition = basePosition + previousOffset;

            // Runtime stylers write canonical anchored positions. Preserve those writes
            // as the new baseline, but never promote our own previous safe-area offset.
            if ((rect.anchoredPosition - expectedPosition).sqrMagnitude > 0.01f)
            {
                basePosition = rect.anchoredPosition;
                updatedBases.Add(new KeyValuePair<RectTransform, Vector2>(rect, basePosition));
            }

            Vector2 anchor = (rect.anchorMin + rect.anchorMax) * 0.5f;
            Vector2 offset = Vector2.zero;

            if (anchor.x <= 0.25f) offset.x += left;
            else if (anchor.x >= 0.75f) offset.x -= right;

            if (anchor.y <= 0.25f) offset.y += bottom;
            else if (anchor.y >= 0.75f) offset.y -= top;

            rect.anchoredPosition = basePosition + offset;
            appliedOffsets[rect] = offset;
        }

        foreach (KeyValuePair<RectTransform, Vector2> update in updatedBases)
            basePositions[update.Key] = update.Value;

        foreach (RectTransform stale in staleTargets)
        {
            basePositions.Remove(stale);
            appliedOffsets.Remove(stale);
        }

        lastSafeArea = safe;
        lastScreenSize = new Vector2Int(Screen.width, Screen.height);
    }
}
